using HidSharp;
using ControllerLabPro.Models;
namespace ControllerLabPro.Services;
public sealed class DualSenseReader : IDisposable
{
    HidStream? _stream; CancellationTokenSource? _cts;
    public event Action<ControllerState>? StateChanged;
    public string Status { get; private set; }="Disconnected";
    public bool Connect()
    {
        var d=DeviceList.Local.GetHidDevices(0x054C).FirstOrDefault(x=>x.ProductID is 0x0CE6 or 0x0DF2);
        if(d is null||!d.TryOpen(out _stream)){Status="DualSense not found";return false;}
        Status=$"Connected: {d.GetProductName()}"; _cts=new(); Task.Run(()=>Loop(_cts.Token)); return true;
    }
    void Loop(CancellationToken ct)
    {
        var b=new byte[128]; while(!ct.IsCancellationRequested&&_stream is not null) try { var n=_stream.Read(b,0,b.Length); if(n<10)continue; var o=b[0]==0x01?1:2; double ax(byte v)=>(v-127.5)/127.5; var buttons=new HashSet<string>(); var face=b[o+7]; if((face&0x10)!=0)buttons.Add("X");if((face&0x20)!=0)buttons.Add("A");if((face&0x40)!=0)buttons.Add("B");if((face&0x80)!=0)buttons.Add("Y"); StateChanged?.Invoke(new(new(ax(b[o]),ax(b[o+1])),new(ax(b[o+2]),ax(b[o+3])),b[o+4]/255d,b[o+5]/255d,buttons)); } catch { Status="Input lost"; break; }
    }
    public void Dispose(){_cts?.Cancel();_stream?.Dispose();}
}
