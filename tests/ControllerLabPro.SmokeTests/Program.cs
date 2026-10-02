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
    ("Stick deadzone and clamp", TestStickPipeline),
    ("Dark WPF windows and aim choices", TestUi)
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
    var cfg = new StickSettings { RightDeadzone = .1, Sensitivity = 2, AntiDeadzone = 0 };
    Assert(ControllerPipeline.Tune(new Stick(.05, .05), cfg) == new Stick(0, 0), "Deadzone failed");
    var tuned = ControllerPipeline.Tune(new Stick(1, 1), cfg);
    Assert(Math.Abs(tuned.X) <= 1 && Math.Abs(tuned.Y) <= 1, "Stick output was not clamped");
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
    if (content is System.Windows.Controls.Panel panel && panel.Background is null)
        panel.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("BgBrush");
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
