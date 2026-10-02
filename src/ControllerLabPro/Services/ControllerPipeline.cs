using ControllerLabPro.Models;
namespace ControllerLabPro.Services;
public static class ControllerPipeline
{
    public static Stick Tune(Stick s, StickSettings cfg) => new Stick(s.X * cfg.Sensitivity, s.Y * cfg.Sensitivity).Clamp();
    public static Stick AddJitter(Stick s, JitterSettings cfg, double seconds, bool aiming)
    {
        if (!cfg.Enabled || !aiming) return s;
        var angle = seconds * Math.Clamp(cfg.FrequencyHz, 1, 30) * Math.PI * 2;
        // Clamp legacy profiles too: previous versions allowed 30% stick movement.
        var horizontal = Math.Clamp(cfg.Horizontal, 0, .03);
        var vertical = Math.Clamp(cfg.Vertical, 0, .03);
        var (x,y) = cfg.Pattern switch
        {
            JitterPattern.Horizontal => (Math.Sin(angle) * horizontal, 0d),
            JitterPattern.Vertical => (0d, Math.Sin(angle) * vertical),
            JitterPattern.Random => ((Random.Shared.NextDouble()*2-1) * horizontal, (Random.Shared.NextDouble()*2-1) * vertical),
            _ => (Math.Cos(angle) * horizontal, Math.Sin(angle) * vertical)
        };
        return new Stick(s.X+x,s.Y+y).Clamp();
    }
}
