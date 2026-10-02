using System.Drawing;
using System.IO;
using ControllerLabPro.Models;
using ControllerLabPro.Services;
using ControllerLabPro.Vision;

var checks = new List<(string Name, Action Run)>
{
    ("Head/body aim-point mapping", TestAimPoints),
    ("Nearest-to-crosshair selection", TestNearestSelection),
    ("Maximum correction cap", TestCorrectionCap),
    ("Lost-target release", TestRelease),
    ("Sensitivity preserves small input and clamps", TestStickPipeline),
    ("Jitter ADS gate, release and legacy strength cap", TestJitter),
    ("Included detector runs without external model", TestIncludedModel),
    ("USB / Bluetooth reports and all gamepad buttons", TestReports),
    ("Malformed reports are ignored", TestMalformedReports),
    ("Release clears accumulated correction", TestCorrectionReset),
    ("Display enumeration and profile persistence", TestDisplays),
    ("Dark WPF windows, worker input, bypass and stop", TestUi)
};

var failed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine($"PASS  {check.Name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL  {check.Name}: {ex}"); }
}
Console.WriteLine($"{checks.Count - failed}/{checks.Count} functional checks passed");
return failed == 0 ? 0 : 1;

static void TestAimPoints()
{
    using var frame = new Bitmap(1000, 1000);
    var box = new RawDetection(new RectangleF(400, 200, 200, 600), .9f, "person");
    var estimator = new ProportionalTargetPointEstimator();
    Near(estimator.Estimate(frame, box, AimPoint.Head).Y, 284, .01f);
    Near(estimator.Estimate(frame, box, AimPoint.UpperTorso).Y, 392, .01f);
    Near(estimator.Estimate(frame, box, AimPoint.CenterMass).Y, 500, .01f);
}

static void TestNearestSelection()
{
    using var frame = new Bitmap(1000, 1000);
    using var detector = new FakeDetector([
        new(new RectangleF(100, 100, 150, 500), .95f, "person"),
        new(new RectangleF(450, 400, 100, 200), .80f, "person")]);
    var engine = new VisionAimEngine(detector, new ProportionalTargetPointEstimator());
    var cfg = Config(AimPoint.CenterMass);
    engine.Process(frame, cfg);
    Assert(engine.CurrentTarget is not null, "No target selected");
    Near(engine.CurrentTarget!.Target.X, 500, .01f);
    Near(engine.CurrentTarget.Target.Y, 500, .01f);
}

static void TestCorrectionCap()
{
    using var frame = new Bitmap(1000, 1000);
    using var detector = new FakeDetector([new(new RectangleF(650, 400, 100, 200), .9f, "person")]);
    var engine = new VisionAimEngine(detector, new ProportionalTargetPointEstimator());
    var cfg = Config(AimPoint.CenterMass); cfg.MaxAimSpeed = .08; cfg.Smoothing = 0;
    var correction = engine.Process(frame, cfg);
    Assert(Math.Abs(correction.X) <= .08001 && Math.Abs(correction.Y) <= .08001, "Correction exceeded configured maximum");
}

static void TestRelease()
{
    using var frame = new Bitmap(1000, 1000);
    using var detector = new FakeDetector([new(new RectangleF(450, 400, 100, 200), .9f, "person")]);
    var engine = new VisionAimEngine(detector, new ProportionalTargetPointEstimator());
    var cfg = Config(AimPoint.CenterMass); cfg.ReleaseAfterMissedFrames = 1;
    engine.Process(frame, cfg);
    detector.Detections = [];
    engine.Process(frame, cfg);
    Assert(engine.CurrentTarget is null, "Target did not release after configured miss count");
}

static void TestStickPipeline()
{
    var cfg = new StickSettings { Sensitivity = 1 };
    Assert(ControllerPipeline.Tune(new Stick(.005,-.005),cfg)==new Stick(.005,-.005),"Small input was changed by a deadzone");
    cfg.Sensitivity=2;
    Assert(ControllerPipeline.Tune(new Stick(.5,-.25),cfg)==new Stick(1,-.5),"Sensitivity failed");
    Assert(ControllerPipeline.Tune(new Stick(1,-1),cfg)==new Stick(1,-1),"Output was not clamped");
}

static void TestJitter()
{
    var raw=new Stick(.2,-.1);
    var cfg=new JitterSettings{Enabled=true,Horizontal=.3,Vertical=.3,FrequencyHz=10};
    foreach(var pattern in Enum.GetValues<JitterPattern>())
    {
        cfg.Pattern=pattern;
        Assert(ControllerPipeline.AddJitter(raw,cfg,.01,false)==raw,"Jitter ran with L2 released");
        for(var i=0;i<100;i++){var result=ControllerPipeline.AddJitter(raw,cfg,i/1000d,true);Assert(Math.Abs(result.X-raw.X)<=.030001&&Math.Abs(result.Y-raw.Y)<=.030001,"Legacy jitter strength exceeded 3% cap");}
        Assert(ControllerPipeline.AddJitter(raw,cfg,.02,false)==raw,"Jitter persisted after ADS release");
    }
    cfg.Enabled=false;
    Assert(ControllerPipeline.AddJitter(raw,cfg,0,true)==raw,"Disabled jitter ran");
    var legacy=System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Sticks\":{\"RightDeadzone\":0.3,\"AntiDeadzone\":0.2,\"Sensitivity\":1}}")!;
    Assert(ControllerPipeline.Tune(new(.01,.01),legacy.Sticks)==new Stick(.01,.01),"Legacy profile restored deadzone behavior");
}
static void TestIncludedModel()
{
    Assert(DetectorFactory.IsBuiltin("models/yolov8n.onnx"),"Legacy default did not migrate to included model");
    using var detector=new YoloXOnnxDetector(preferGpu:false);
    using var blank=new Bitmap(416,416);using(var graphics=Graphics.FromImage(blank))graphics.Clear(Color.Black);
    Assert(detector.Detect(blank,.55f).Count==0,"Blank frame detected a person");
    using(var defaultDetector=DetectorFactory.Create(DetectorFactory.BuiltinModel)) Assert(defaultDetector.Detect(blank,.55f).Count==0,"Default detector initialization/inference failed");
    var fixture=Environment.GetEnvironmentVariable("CONTROLLERLAB_PERSON_FIXTURE");
    if(!string.IsNullOrWhiteSpace(fixture))
    {
        using var image=new Bitmap(fixture);
        var detections=detector.Detect(image,.35f);
        Assert(detections.Count>0,"Included detector found no people in the real-image fixture");
        Console.WriteLine($"Included detector found {detections.Count} people, highest confidence {detections.Max(d=>d.Confidence):P0}");
    }
}

static void TestReports()
{
    foreach(var kind in new[]{"USB","Bluetooth","Basic"})
    {
        var report=new byte[kind=="USB"?64:kind=="Bluetooth"?78:10];
        report[0]=(byte)(kind=="Bluetooth"?0x31:0x01);
        var offset=kind=="Bluetooth"?2:1;
        report[offset]=0;report[offset+1]=255;report[offset+2]=255;report[offset+3]=0;
        var face=kind=="Basic"?5:offset+7;
        report[face]=0xF1;report[face+1]=0xF3;report[face+2]=1;
        report[kind=="Basic"?8:offset+4]=255;report[kind=="Basic"?9:offset+5]=128;
        Assert(DualSenseReader.TryParse(report,out var state),kind+" report rejected");
        Assert(state!.Left==new Stick(-1,1)&&state.Right==new Stick(1,-1),kind+" axes incorrect");
        Assert(state.L2==1&&Math.Abs(state.R2-128/255d)<.001,kind+" triggers incorrect");
        Assert(state.Buttons.SetEquals(new[]{"A","B","X","Y","Up","Right","LB","RB","Back","Start","LS","RS","Guide"}),kind+" buttons incorrect");
        report[face]=8;report[face+1]=0;report[face+2]=0;
        DualSenseReader.TryParse(report,out state);
        Assert(state!.Buttons.Count==0,kind+" released buttons remained held");
    }
}
static void TestMalformedReports()
{
    Assert(!DualSenseReader.TryParse([],out _),"Empty report accepted");
    Assert(!DualSenseReader.TryParse(new byte[]{0x31,0,0},out _),"Truncated Bluetooth accepted");
    var unknown=new byte[78];unknown[0]=0x32;
    Assert(!DualSenseReader.TryParse(unknown,out _),"Unknown report ID accepted");
    using var reader=new DualSenseReader();reader.Disconnect();reader.Disconnect();
}
static void TestCorrectionReset()
{
    using var frame=new Bitmap(1000,1000);
    using var detector=new FakeDetector([new(new RectangleF(650,400,100,200),.9f,"person")]);
    var engine=new VisionAimEngine(detector,new ProportionalTargetPointEstimator());
    var cfg=Config(AimPoint.CenterMass);cfg.Smoothing=.8;
    Assert(engine.Process(frame,cfg).X>0,"No initial correction");
    engine.Release();detector.Detections=[];
    Assert(engine.Process(frame,cfg)==new Stick(),"Old correction persisted after release");
}
static void TestDisplays()
{
    var displays=WindowCapture.GetDisplays();
    Assert(displays.Count>0&&displays.All(d=>d.Bounds.Width>0&&d.Bounds.Height>0),"No usable display found");
    Assert(new WindowCapture().Capture("missing-display") is null,"Missing display silently captured a different monitor");
    var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
    try{var cfg=new AppSettings();cfg.Vision.CaptureDisplay=displays[0].DeviceName;cfg.Save(path);Assert(AppSettings.Load(path).Vision.CaptureDisplay==displays[0].DeviceName,"Display choice did not persist");}
    finally{File.Delete(path);}
}

static void TestUi()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = new ControllerLabPro.App();
            app.InitializeComponent();
            var main = new ControllerLabPro.MainWindow();
            Assert(main.FindName("Deadzone") is null&&main.FindName("AntiDeadzone") is null,"Deadzone UI remains");
            Assert(main.Background is System.Windows.Media.SolidColorBrush bg && bg.Color.R<32,"Actual main window background is not dark");
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var type=typeof(ControllerLabPro.MainWindow);
            void Invoke(string name,params object[] arguments)=>type.GetMethod(name,flags)!.Invoke(main,arguments);
            ((System.Windows.Controls.CheckBox)main.FindName("JitterEnabled")).IsChecked=true;
            ((System.Windows.Controls.CheckBox)main.FindName("TurboEnabled")).IsChecked=true;
            ((System.Windows.Controls.Slider)main.FindName("TurboHz")).Value=20;
            Invoke("ApplyControls");
            var cfg=(AppSettings)type.GetField("_runtimeCfg",flags)!.GetValue(main)!;
            Assert(cfg.Turbo.Enabled&&cfg.Turbo.FrequencyHz==20,"Turbo controls did not apply");
            type.GetField("_controllerRunning",flags)!.SetValue(main,true);
            var raw=new ControllerState(new(),new(.2,.1),1,0,new(){"LB","A"});
            Exception? inputFailure=null;
            var worker=new Thread(()=>{try{Invoke("OnInput",raw);}catch(Exception ex){inputFailure=ex;}});
            worker.Start();worker.Join();
            Assert(inputFailure is null,"Background controller processing accessed WPF: "+inputFailure);
            var released=raw with {L2=0};
            worker=new Thread(()=>Invoke("OnInput",released));worker.Start();worker.Join();
            Assert(((ControllerState)type.GetField("_latestCooked",flags)!.GetValue(main)!).Right==released.Right,"UI pipeline added jitter without ADS");
            Invoke("RefreshMonitor");
            Assert(((System.Windows.Controls.TextBlock)main.FindName("MonitorText")).Text.Contains("DualSense reports: 2"),"Live controller did not update");
            type.GetField("_processingEnabled",flags)!.SetValue(main,false);
            worker=new Thread(()=>Invoke("OnInput",raw));worker.Start();worker.Join();
            Assert((ControllerState)type.GetField("_latestCooked",flags)!.GetValue(main)! == raw,"Master-off did not pass raw input through");
            Invoke("StopController");Invoke("StopController");
            Assert(((System.Windows.Controls.Button)main.FindName("ControllerButton")).Content.ToString()=="START CONTROLLER","Stop did not restore button");
            Assert(type.GetField("_last",flags)!.GetValue(main) is null,"Stop retained stale input");
            var aim = (System.Windows.Controls.ComboBox)main.FindName("AimPointBox");
            var choices = aim.Items.Cast<System.Windows.Controls.ComboBoxItem>().Select(x => x.Content?.ToString()).ToArray();
            Assert(choices.SequenceEqual(new[] { "Head", "Body — Upper Torso", "Body — Center Mass" }), "PC aim choices are not clear or complete");
            Assert(app.FindResource("BgBrush") is System.Windows.Media.SolidColorBrush mainBrush && mainBrush.Color.R < 32, "Dark theme resources did not load");

            var ps5 = new ControllerLabPro.Ps5ElgatoWindow();
            Assert(ps5.Background is System.Windows.Media.SolidColorBrush ps5Brush && ps5Brush.Color.R < 32, "PS5 window is not using the dark theme");
            var snapshotDir = Environment.GetEnvironmentVariable("CONTROLLERLAB_UI_SNAPSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(snapshotDir))
            {
                Directory.CreateDirectory(snapshotDir);
                Render(main, Path.Combine(snapshotDir, "main-window.png"), 1220, 780);
                ((System.Windows.FrameworkElement)main.FindName("DashboardPage")).Visibility = System.Windows.Visibility.Collapsed;
                ((System.Windows.FrameworkElement)main.FindName("VisionPage")).Visibility = System.Windows.Visibility.Visible;
                ((System.Windows.Controls.TextBlock)main.FindName("PageTitle")).Text = "Vision Aim";
                Render(main, Path.Combine(snapshotDir, "vision-aim-window.png"), 1220, 780);
                Render(ps5, Path.Combine(snapshotDir, "ps5-elgato-window.png"), 1120, 760);
            }
            ps5.Close();
            main.Close();
        }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw new InvalidOperationException("WPF UI smoke test failed", failure);
}

static void Render(System.Windows.Window window, string path, int width, int height)
{
    var content = (System.Windows.FrameworkElement)window.Content;
    if (content is System.Windows.Controls.Panel panel) panel.Background = window.Background;
    content.Measure(new System.Windows.Size(width, height));
    content.Arrange(new System.Windows.Rect(0, 0, width, height));
    content.UpdateLayout();
    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    bitmap.Render(content);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
    using var stream = File.Create(path);
    encoder.Save(stream);
}

static VisionSettings Config(AimPoint point) => new()
{
    FovRadius = .5,
    DetectionConfidence = .5,
    AimPoint = point,
    AimStrength = 1,
    Smoothing = 0,
    MaxAimSpeed = 1,
    LockMilliseconds = 300,
    ReleaseAfterMissedFrames = 2
};

static void Near(float actual, float expected, float tolerance) => Assert(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class FakeDetector(IReadOnlyList<RawDetection> detections) : IObjectDetector
{
    public IReadOnlyList<RawDetection> Detections { get; set; } = detections;
    public IReadOnlyList<RawDetection> Detect(Bitmap frame, float threshold) => Detections.Where(x => x.Confidence >= threshold).ToList();
    public void Dispose() { }
}
