using HidSharp;
using ControllerLabPro.Models;
namespace ControllerLabPro.Services;

public sealed class DualSenseReader : IDisposable
{
    HidStream? _stream;
    CancellationTokenSource? _cts;
    Task? _reader;
    public event Action<ControllerState>? StateChanged;
    public event Action<string>? StatusChanged;
    public string Status { get; private set; } = "DualSense: disconnected";
    void SetStatus(string status) { Status = status; StatusChanged?.Invoke(status); }

    public bool Connect()
    {
        Disconnect();
        foreach (var device in DeviceList.Local.GetHidDevices(0x054C).Where(d => d.ProductID is 0x0CE6 or 0x0DF2))
        {
            try
            {
                if (device.GetMaxInputReportLength() < 10 || !device.TryOpen(out var stream)) continue;
                _stream = stream;
                stream.ReadTimeout = 500;
                // Calibration feature requests enable extended Bluetooth reports.
                try { var feature = new byte[41]; feature[0] = 0x05; stream.GetFeature(feature); } catch (Exception ex) when (ex is IOException or TimeoutException) { }
                _cts = new();
                var token = _cts.Token;
                SetStatus("Physical DualSense: connected — waiting for reports");
                _reader = Task.Run(() => Loop(stream, device.GetMaxInputReportLength(), token));
                return true;
            }
            catch (Exception ex) { _stream?.Dispose(); _stream = null; SetStatus("DualSense open failed: " + ex.Message); }
        }
        SetStatus("DualSense unavailable — connect it and close other controller readers; check HidHide access if installed");
        return false;
    }

    void Loop(HidStream stream, int reportLength, CancellationToken token)
    {
        var buffer = new byte[reportLength];
        var received = false;
        var lastReport = DateTime.UtcNow;
        while (!token.IsCancellationRequested)
        {
            int count;
            try { count = stream.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException)
            {
                if (DateTime.UtcNow - lastReport < TimeSpan.FromSeconds(3)) continue;
                SetStatus("Physical DualSense: no reports for 3 seconds — reconnect controller");
                break;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                if (!token.IsCancellationRequested) SetStatus("Physical DualSense: input lost — " + ex.Message);
                break;
            }
            if (!TryParse(buffer.AsSpan(0, count), out var state)) continue;
            lastReport = DateTime.UtcNow;
            if (!received) { received = true; SetStatus("Physical DualSense: receiving " + (buffer[0] == 0x31 ? "Bluetooth" : count < 64 ? "Bluetooth basic" : "USB") + " input"); }
            // Keep processing errors separate from physical HID failures.
            try { StateChanged?.Invoke(state!); }
            catch (Exception ex) { SetStatus("Controller processing failed: " + ex.Message); break; }
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> report, out ControllerState? state)
    {
        state = null;
        if (report.Length == 0) return false;
        var offset = report[0] switch { 0x01 => 1, 0x31 => 2, _ => -1 };
        if (offset < 0) return false;
        var basic = report[0] == 0x01 && report.Length < 64;
        if (report.Length < (basic ? 10 : offset + 10)) return false;
        // Basic Bluetooth reports place buttons before triggers.
        var faceIndex = basic ? 5 : offset + 7;
        var face = report[faceIndex]; var extra = report[faceIndex + 1]; var system = report[faceIndex + 2];
        var buttons = new HashSet<string>();
        void Add(bool held, string name) { if (held) buttons.Add(name); }
        Add((face & 0x10) != 0, "X"); Add((face & 0x20) != 0, "A");
        Add((face & 0x40) != 0, "B"); Add((face & 0x80) != 0, "Y");
        var hat = face & 0x0F;
        Add(hat is 0 or 1 or 7, "Up"); Add(hat is 3 or 4 or 5, "Down");
        Add(hat is 5 or 6 or 7, "Left"); Add(hat is 1 or 2 or 3, "Right");
        Add((extra & 1) != 0, "LB"); Add((extra & 2) != 0, "RB");
        Add((extra & 0x10) != 0, "Back"); Add((extra & 0x20) != 0, "Start");
        Add((extra & 0x40) != 0, "LS"); Add((extra & 0x80) != 0, "RS"); Add((system & 1) != 0, "Guide");
        static double Axis(byte value) => (value - 127.5) / 127.5;
        state = new(new(Axis(report[offset]), Axis(report[offset + 1])), new(Axis(report[offset + 2]), Axis(report[offset + 3])),
            report[basic ? 8 : offset + 4] / 255d, report[basic ? 9 : offset + 5] / 255d, buttons);
        return true;
    }

    public void Disconnect()
    {
        _cts?.Cancel(); _stream?.Dispose();
        _reader?.GetAwaiter().GetResult();
        _reader = null; _stream = null; _cts?.Dispose(); _cts = null;
        Status = "DualSense: disconnected";
    }
    public void Dispose() => Disconnect();
}
