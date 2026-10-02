using ControllerLabPro.Models;
namespace ControllerLabPro.Services;
public static class ControllerPipeline
{
    public static Stick Tune(Stick s, StickSettings cfg)
    {
        var mag=Math.Sqrt(s.X*s.X+s.Y*s.Y); if(mag<=cfg.RightDeadzone)return new(0,0);
        var scaled=Math.Min(1,(mag-cfg.RightDeadzone)/(1-cfg.RightDeadzone));
        scaled=cfg.AntiDeadzone+(1-cfg.AntiDeadzone)*scaled; var gain=scaled/mag*cfg.Sensitivity;
        return new Stick(s.X*gain,s.Y*gain).Clamp();
    }
    public static Stick AddJitter(Stick s,JitterSettings cfg,double seconds)
    {
        if(!cfg.Enabled)return s; var a=seconds*cfg.FrequencyHz*Math.PI*2;
        var (x,y)=cfg.Pattern switch { JitterPattern.Horizontal=>(Math.Sin(a)*cfg.Horizontal,0d), JitterPattern.Vertical=>(0d,Math.Sin(a)*cfg.Vertical), JitterPattern.Random=>(Random.Shared.NextDouble()*2*cfg.Horizontal-cfg.Horizontal,Random.Shared.NextDouble()*2*cfg.Vertical-cfg.Vertical), _=>(Math.Cos(a)*cfg.Horizontal,Math.Sin(a)*cfg.Vertical) };
        return new Stick(s.X+x,s.Y+y).Clamp();
    }
}
