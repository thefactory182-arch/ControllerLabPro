using System.Text.Json;
namespace ControllerLabPro.Models;
public enum AimPoint { Head, UpperTorso, CenterMass }
public enum JitterPattern { Circle, Horizontal, Vertical, Random }
public sealed class AppSettings
{
    public string ProfileName { get; set; } = "Default";
    public StickSettings Sticks { get; set; } = new();
    public JitterSettings Jitter { get; set; } = new();
    public TurboSettings Turbo { get; set; } = new();
    public VisionSettings Vision { get; set; } = new();
    public Dictionary<string,string> Remap { get; set; } = new();
    public static AppSettings Load(string path) => File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new();
    public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
}
public sealed class StickSettings { public double Sensitivity { get; set; }=1; }
public sealed class JitterSettings { public bool Enabled { get; set; } public JitterPattern Pattern { get; set; }=JitterPattern.Circle; public double Horizontal { get; set; }=.005; public double Vertical { get; set; }=.005; public double FrequencyHz { get; set; }=8; }
public sealed class TurboSettings { public bool Enabled { get; set; } public string Button { get; set; }="A"; public double FrequencyHz { get; set; }=10; }
public sealed class VisionSettings
{
    public bool Enabled { get; set; }
    public bool RequireActivation { get; set; }=true;
    public string Activation { get; set; }="L2";
    public string WindowTitleContains { get; set; }="";
    public string CaptureDisplay { get; set; }="";
    public string ModelPath { get; set; }="builtin:yolox-s";
    public double DetectionConfidence { get; set; }=.55;
    public double FovRadius { get; set; }=.20;
    public double AimStrength { get; set; }=.65;
    public double Smoothing { get; set; }=.55;
    public double MaxAimSpeed { get; set; }=.5225;
    public int LockMilliseconds { get; set; }=300;
    public int ReleaseAfterMissedFrames { get; set; }=3;
    public double ReleaseFovMultiplier { get; set; }=1.08;
    public AimPoint AimPoint { get; set; }=AimPoint.Head;
    public bool DebugOverlay { get; set; }=true;
}
public readonly record struct Stick(double X,double Y) { public Stick Clamp() => new(Math.Clamp(X,-1,1),Math.Clamp(Y,-1,1)); }
public sealed record ControllerState(Stick Left, Stick Right, double L2, double R2, HashSet<string> Buttons);
public sealed record Detection(int Id, System.Drawing.RectangleF Box, float Confidence, System.Drawing.PointF Target, bool IsCurrentTarget = false);
