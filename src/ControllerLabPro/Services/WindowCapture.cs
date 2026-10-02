using System.Drawing;
using System.Runtime.InteropServices;
namespace ControllerLabPro.Services;

public sealed record CaptureDisplay(string DeviceName, Rectangle Bounds, bool Primary)
{
    public override string ToString() => $"{DeviceName} — {Bounds.Width} × {Bounds.Height}{(Primary ? " (primary)" : "")}";
}

public sealed class WindowCapture
{
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MONITORINFOEX
    {
        public int Size; public RECT Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    public static IReadOnlyList<CaptureDisplay> GetDisplays()
    {
        var displays = new List<CaptureDisplay>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data) =>
        {
            var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info)) displays.Add(new(info.Device,
                Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom), (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return displays.OrderByDescending(d => d.Primary).ThenBy(d => d.DeviceName).ToArray();
    }
    public (Bitmap Image, Rectangle ScreenRect)? Capture(string displayName)
    {
        var displays = GetDisplays();
        var display = string.IsNullOrWhiteSpace(displayName) ? displays.FirstOrDefault() : displays.FirstOrDefault(d => d.DeviceName == displayName);
        if (display is null) return null;
        var rect = display.Bounds;
        var bitmap = new Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try { using var graphics = Graphics.FromImage(bitmap); graphics.CopyFromScreen(rect.Location, Point.Empty, rect.Size); return (bitmap, rect); }
        catch { bitmap.Dispose(); throw; }
    }
}
