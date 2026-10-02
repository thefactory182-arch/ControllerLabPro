using System.Drawing;
using ControllerLabPro.Models;
namespace ControllerLabPro.Vision;
public interface IObjectDetector:IDisposable { IReadOnlyList<RawDetection> Detect(Bitmap frame,float threshold); }
public interface ITargetPointEstimator { PointF Estimate(Bitmap frame, RawDetection person,AimPoint point); }
public sealed record RawDetection(RectangleF Box,float Confidence,string Label);
public sealed class ProportionalTargetPointEstimator:ITargetPointEstimator
{
    public PointF Estimate(Bitmap frame, RawDetection p,AimPoint a)=>a switch { AimPoint.Head=>new(p.Box.Left+p.Box.Width*.5f,p.Box.Top+p.Box.Height*.14f),AimPoint.UpperTorso=>new(p.Box.Left+p.Box.Width*.5f,p.Box.Top+p.Box.Height*.32f),_=>new(p.Box.Left+p.Box.Width*.5f,p.Box.Top+p.Box.Height*.5f)};
}
