using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
namespace ControllerLabPro.Services;
public sealed class WindowCapture
{
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h,ref POINT p);
    struct RECT{public int Left,Top,Right,Bottom;} struct POINT{public int X,Y;}
    public (Bitmap Image,Rectangle ScreenRect)? Capture(string title)
    {
        var p=Process.GetProcesses().Where(p=>!string.IsNullOrWhiteSpace(p.MainWindowTitle)&&p.MainWindowTitle.Contains(title,StringComparison.OrdinalIgnoreCase)).OrderByDescending(p=>p.MainWindowTitle.Length).FirstOrDefault();
        if(p is null||!GetClientRect(p.MainWindowHandle,out var r))return null; var pt=new POINT();ClientToScreen(p.MainWindowHandle,ref pt);var rect=new Rectangle(pt.X,pt.Y,r.Right,r.Bottom);if(rect.Width<2||rect.Height<2)return null;
        var bmp=new Bitmap(rect.Width,rect.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb);using var g=Graphics.FromImage(bmp);g.CopyFromScreen(rect.Location,Point.Empty,rect.Size);return(bmp,rect);
    }
}
