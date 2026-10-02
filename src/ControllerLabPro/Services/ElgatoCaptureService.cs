using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace ControllerLabPro.Services;

public sealed record VideoDevice(int Index, string Name)
{
    public override string ToString() => Name;
}

public sealed record CaptureSample(BitmapSource Preview, double ReadMilliseconds, double ObservedFps, int Width, int Height, long FrameNumber);

public sealed class ElgatoCaptureService : IDisposable
{
    VideoCapture? _capture;
    long _frames;
    long _windowFrames;
    DateTime _windowStart;
    double _observedFps;

    public static IReadOnlyList<VideoDevice> EnumerateDevices()
    {
        var names = DirectShowNames();
        return names.Select((name, index) => new VideoDevice(index, name)).ToList();
    }

    public (int Width, int Height, double Fps) Start(int index, int width, int height, double fps)
    {
        Stop();
        _capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
        if (!_capture.IsOpened()) throw new InvalidOperationException("The selected capture device could not be opened. Close Elgato 4K Capture Utility or OBS if it is using the device.");
        _capture.Set(VideoCaptureProperties.FrameWidth, width);
        _capture.Set(VideoCaptureProperties.FrameHeight, height);
        _capture.Set(VideoCaptureProperties.Fps, fps);
        _capture.Set(VideoCaptureProperties.BufferSize, 1);
        _frames = _windowFrames = 0;
        _windowStart = DateTime.UtcNow;
        return ((int)_capture.Get(VideoCaptureProperties.FrameWidth), (int)_capture.Get(VideoCaptureProperties.FrameHeight), _capture.Get(VideoCaptureProperties.Fps));
    }

    public CaptureSample Read()
    {
        if (_capture is null) throw new InvalidOperationException("Capture has not been started.");
        using var mat = new Mat();
        var start = Stopwatch.GetTimestamp();
        if (!_capture.Read(mat) || mat.Empty()) throw new IOException("The capture device stopped returning frames.");
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _frames++;
        _windowFrames++;
        var window = DateTime.UtcNow - _windowStart;
        if (window.TotalSeconds >= 1)
        {
            _observedFps = _windowFrames / window.TotalSeconds;
            _windowFrames = 0;
            _windowStart = DateTime.UtcNow;
        }
        var stride = checked((int)(mat.Cols * mat.ElemSize()));
        var bytes = new byte[stride * mat.Rows];
        Marshal.Copy(mat.Data, bytes, 0, bytes.Length);
        var preview = BitmapSource.Create(mat.Cols, mat.Rows, 96, 96, PixelFormats.Bgr24, null, bytes, stride);
        preview.Freeze();
        return new(preview, elapsed, _observedFps, mat.Cols, mat.Rows, _frames);
    }

    public void Stop()
    {
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose() => Stop();

    static List<string> DirectShowNames()
    {
        var result = new List<string>();
        object? devEnumObject = null;
        IEnumMoniker? enumMoniker = null;
        try
        {
            devEnumObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86"))!);
            var devEnum = (ICreateDevEnum)devEnumObject!;
            var category = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
            if (devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0) != 0 || enumMoniker is null) return result;
            var monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                object? bagObject = null;
                try
                {
                    var bagId = typeof(IPropertyBag).GUID;
                    monikers[0].BindToStorage(null!, null, ref bagId, out bagObject);
                    var bag = (IPropertyBag)bagObject;
                    bag.Read("FriendlyName", out var value, IntPtr.Zero);
                    result.Add(value?.ToString() ?? $"Video capture {result.Count}");
                }
                catch { result.Add($"Video capture {result.Count}"); }
                finally
                {
                    if (bagObject is not null && Marshal.IsComObject(bagObject)) Marshal.ReleaseComObject(bagObject);
                    Marshal.ReleaseComObject(monikers[0]);
                }
            }
        }
        catch { }
        finally
        {
            if (enumMoniker is not null && Marshal.IsComObject(enumMoniker)) Marshal.ReleaseComObject(enumMoniker);
            if (devEnumObject is not null && Marshal.IsComObject(devEnumObject)) Marshal.ReleaseComObject(devEnumObject);
        }
        return result;
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid category, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object? value, IntPtr errorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}
