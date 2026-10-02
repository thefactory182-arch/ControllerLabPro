using System.Drawing;
using System.Diagnostics;
using ControllerLabPro.Models;

namespace ControllerLabPro.Vision;

public sealed class VisionAimEngine(IObjectDetector detector, ITargetPointEstimator pointEstimator)
{
    RectangleF? _lockedBox;
    DateTime _lockExpires;
    int _missedFrames;
    int _nextId;
    Stick _smoothed;
    long _previousFrame;

    public IReadOnlyList<Detection> LastDetections { get; private set; } = [];
    public Detection? CurrentTarget { get; private set; }

    public Stick Process(Bitmap frame, VisionSettings cfg, double? elapsedSeconds = null)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = elapsedSeconds ?? (_previousFrame == 0 ? 1d/60 : (now-_previousFrame)/(double)Stopwatch.Frequency);
        _previousFrame = now;
        var center = new PointF(frame.Width / 2f, frame.Height / 2f);
        var radius = (float)(Math.Min(frame.Width, frame.Height) * cfg.FovRadius);
        var cropRect = Rectangle.Round(new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2));
        cropRect.Intersect(new Rectangle(0, 0, frame.Width, frame.Height));

        using var crop = frame.Clone(cropRect, frame.PixelFormat);
        var raw = detector.Detect(crop, (float)cfg.DetectionConfidence)
            .Select(d => d with { Box = new RectangleF(d.Box.X + cropRect.X, d.Box.Y + cropRect.Y, d.Box.Width, d.Box.Height) })
            .ToList();

        var allDetections = raw.Select(r => new Detection(_nextId++, r.Box, r.Confidence,
                pointEstimator.Estimate(frame, r, cfg.AimPoint))).ToList();
        var candidates = allDetections.Where(d => Distance(d.Target, center) <= radius).ToList();

        Detection? target = null;
        var lockWasActive = _lockedBox is { } && DateTime.UtcNow <= _lockExpires;
        if (lockWasActive && _lockedBox is { } previous)
        {
            var associated = allDetections.OrderByDescending(d => IoU(previous, d.Box)).FirstOrDefault(d => IoU(previous, d.Box) >= .12f);
            if (associated is not null && Distance(associated.Target, center) > radius * cfg.ReleaseFovMultiplier)
            {
                Release();
                LastDetections = candidates;
                return EaseToZero(cfg.Smoothing);
            }
            target = associated is not null && Distance(associated.Target, center) <= radius ? associated : null;
        }
        if (!lockWasActive) target = candidates.OrderBy(d => Distance(d.Target, center)).FirstOrDefault();

        if (target is null)
        {
            _missedFrames++;
            if (_missedFrames >= cfg.ReleaseAfterMissedFrames || DateTime.UtcNow > _lockExpires) Release();
            LastDetections = candidates;
            return EaseToZero(cfg.Smoothing);
        }

        if (Distance(target.Target, center) > radius * cfg.ReleaseFovMultiplier)
        {
            Release();
            LastDetections = candidates;
            return EaseToZero(cfg.Smoothing);
        }

        _missedFrames = 0;
        _lockedBox = target.Box;
        _lockExpires = DateTime.UtcNow.AddMilliseconds(cfg.LockMilliseconds);
        CurrentTarget = target with { IsCurrentTarget = true };
        LastDetections = candidates.Select(d => d.Id == target.Id ? CurrentTarget : d).ToList();

        // Keep the established default; reserve the upper slider range for stronger pull.
        var strength = Math.Clamp(cfg.AimStrength, 0, 1);
        var boost = Math.Max(0, (strength - .65) / .35);
        var gain = strength * 1.8 + 6 * boost * boost;
        var desired = new Stick(
            Math.Clamp((target.Target.X - center.X) / radius * gain, -cfg.MaxAimSpeed, cfg.MaxAimSpeed),
            Math.Clamp((target.Target.Y - center.Y) / radius * gain, -cfg.MaxAimSpeed, cfg.MaxAimSpeed));
        // Strength controls correction; smoothness controls response time, independent of FPS.
        var responseSeconds = .02 + Math.Clamp(cfg.Smoothing,0,1) * .28;
        var alpha = cfg.Smoothing <= 0 ? 1 : 1-Math.Exp(-Math.Clamp(dt,.001,.25)/responseSeconds);
        _smoothed = new Stick(_smoothed.X + (desired.X - _smoothed.X) * alpha, _smoothed.Y + (desired.Y - _smoothed.Y) * alpha);
        return _smoothed;
    }

    public void Release()
    {
        _lockedBox = null;
        CurrentTarget = null;
        _missedFrames = 0;
        LastDetections = [];
        _smoothed = new();
        _previousFrame = 0;
    }

    Stick EaseToZero(double smoothing)
    {
        var alpha = Math.Clamp(1 - smoothing, .05, 1);
        _smoothed = new Stick(_smoothed.X * (1 - alpha), _smoothed.Y * (1 - alpha));
        if (Math.Abs(_smoothed.X) < .001 && Math.Abs(_smoothed.Y) < .001) _smoothed = new();
        return _smoothed;
    }

    static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    static float IoU(RectangleF a, RectangleF b)
    {
        var i = RectangleF.Intersect(a, b);
        var area = Math.Max(0, i.Width) * Math.Max(0, i.Height);
        return area / (a.Width * a.Height + b.Width * b.Height - area + 1e-5f);
    }
}
