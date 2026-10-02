using ControllerLabPro.Models;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
namespace ControllerLabPro.Services;
public sealed class VirtualXboxOutput : IDisposable
{
    ViGEmClient? _client; IXbox360Controller? _pad;
    public string Status { get; private set; }="Disconnected";
    public bool Connect(){Disconnect();try{_client=new();_pad=_client.CreateXbox360Controller();_pad.AutoSubmitReport=false;_pad.Connect();Status="Game output: virtual Xbox 360 (from DualSense)";return true;}catch(Exception e){Disconnect();Status="ViGEm unavailable: "+e.Message;return false;}}
    static short Axis(double v)=>(short)Math.Clamp(v*32767,short.MinValue,short.MaxValue);
    public void Submit(ControllerState s)
    {
        if(_pad is null)return; _pad.SetAxisValue(Xbox360Axis.LeftThumbX,Axis(s.Left.X));_pad.SetAxisValue(Xbox360Axis.LeftThumbY,Axis(-s.Left.Y));_pad.SetAxisValue(Xbox360Axis.RightThumbX,Axis(s.Right.X));_pad.SetAxisValue(Xbox360Axis.RightThumbY,Axis(-s.Right.Y));_pad.SetSliderValue(Xbox360Slider.LeftTrigger,(byte)(s.L2*255));_pad.SetSliderValue(Xbox360Slider.RightTrigger,(byte)(s.R2*255));
        foreach(var p in new[]{("A",Xbox360Button.A),("B",Xbox360Button.B),("X",Xbox360Button.X),("Y",Xbox360Button.Y), ("LB",Xbox360Button.LeftShoulder),("RB",Xbox360Button.RightShoulder),("Back",Xbox360Button.Back),("Start",Xbox360Button.Start),("LS",Xbox360Button.LeftThumb),("RS",Xbox360Button.RightThumb),("Guide",Xbox360Button.Guide),("Up",Xbox360Button.Up),("Down",Xbox360Button.Down),("Left",Xbox360Button.Left),("Right",Xbox360Button.Right)})_pad.SetButtonState(p.Item2,s.Buttons.Contains(p.Item1)); _pad.SubmitReport();
    }
    public void Disconnect(){try{_pad?.Disconnect();}finally{_pad=null;_client?.Dispose();_client=null;Status="Game output: disconnected";}}
    public void Dispose()=>Disconnect();
}
