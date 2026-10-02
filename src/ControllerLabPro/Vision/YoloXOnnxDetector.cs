using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
namespace ControllerLabPro.Vision;

public static class DetectorFactory
{
    public const string BuiltinModel = "builtin:yolox-nano";
    public static string ResolvePath(string path) => Path.GetFullPath(path, AppContext.BaseDirectory);
    public static bool IsBuiltin(string path) => string.IsNullOrWhiteSpace(path) || path == BuiltinModel ||
        (path.Replace('\\','/').Equals("models/yolov8n.onnx", StringComparison.OrdinalIgnoreCase) && !File.Exists(ResolvePath(path)));
    public static IObjectDetector Create(string path) => IsBuiltin(path) ? new YoloXOnnxDetector() : new YoloOnnxDetector(ResolvePath(path));
}

// YOLOX preprocessing and grid decoding follow Megvii's Apache-2.0 reference.
// See Models/YOLOX-LICENSE.txt and Models/README.md for upstream attribution.
public sealed class YoloXOnnxDetector : IObjectDetector
{
    const int Size = 416;
    readonly InferenceSession _session;
    readonly string _input;
    readonly float[] _buffer = new float[3 * Size * Size];
    readonly DenseTensor<float> _tensor;
    public YoloXOnnxDetector(bool preferGpu = true)
    {
        using var stream = typeof(YoloXOnnxDetector).Assembly.GetManifestResourceStream("ControllerLabPro.Models.yolox_nano.onnx")
            ?? throw new InvalidOperationException("Included detector is missing from this build. Run setup-model.ps1 and rebuild.");
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        var model = memory.ToArray();
        using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, EnableMemoryPattern = false };
        if (preferGpu)
        {
            try { options.AppendExecutionProvider_DML(); _session = new InferenceSession(model, options); }
            catch { using var cpu = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL }; _session = new InferenceSession(model, cpu); }
        }
        else _session = new InferenceSession(model, options);
        _input = _session.InputMetadata.Keys.Single();
        if (!_session.InputMetadata[_input].Dimensions.SequenceEqual(new[]{1,3,Size,Size})) { _session.Dispose(); throw new InvalidDataException("Unexpected included model input dimensions."); }
        _tensor = new DenseTensor<float>(_buffer, [1,3,Size,Size]);
    }
    public IReadOnlyList<RawDetection> Detect(Bitmap frame, float threshold)
    {
        var ratio = Math.Min((float)Size/frame.Width, (float)Size/frame.Height);
        using var resized = new Bitmap(Size,Size,PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.Clear(Color.FromArgb(114,114,114)); graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(frame, new Rectangle(0,0,(int)(frame.Width*ratio),(int)(frame.Height*ratio)),0,0,frame.Width,frame.Height,GraphicsUnit.Pixel);
        }
        var data = resized.LockBits(new Rectangle(0,0,Size,Size),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride)*Size]; Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
            var area=Size*Size;
            for(var y=0;y<Size;y++) for(var x=0;x<Size;x++)
            {
                var source=y*Math.Abs(data.Stride)+x*3;var pixel=y*Size+x;
                // YOLOX uses unnormalized BGR channels, unlike YOLOv8.
                _buffer[pixel]=bytes[source];_buffer[area+pixel]=bytes[source+1];_buffer[2*area+pixel]=bytes[source+2];
            }
        }
        finally { resized.UnlockBits(data); }
        using var output=_session.Run([NamedOnnxValue.CreateFromTensor(_input,_tensor)]);
        var values=output.First().AsTensor<float>();
        if(!values.Dimensions.ToArray().SequenceEqual(new[]{1,3549,85})) throw new InvalidDataException("Unexpected included model output dimensions.");
        var candidates=new List<RawDetection>();var index=0;
        foreach(var stride in new[]{8,16,32})
        for(var y=0;y<Size/stride;y++) for(var x=0;x<Size/stride;x++,index++)
        {
            var confidence=values[0,index,4]*values[0,index,5];
            if(confidence<threshold)continue;
            var isPerson=true;
            for(var c=6;c<85;c++)if(values[0,index,c]>values[0,index,5]){isPerson=false;break;}
            if(!isPerson)continue;
            var cx=(values[0,index,0]+x)*stride/ratio;var cy=(values[0,index,1]+y)*stride/ratio;
            var width=MathF.Exp(values[0,index,2])*stride/ratio;var height=MathF.Exp(values[0,index,3])*stride/ratio;
            if(!float.IsFinite(cx+cy+width+height))continue;
            var box=RectangleF.Intersect(new(cx-width/2,cy-height/2,width,height),new(0,0,frame.Width,frame.Height));
            if(box.Width>0&&box.Height>0)candidates.Add(new(box,confidence,"person"));
        }
        var results=new List<RawDetection>();
        foreach(var detection in candidates.OrderByDescending(d=>d.Confidence))
            if(results.All(existing=>IoU(existing.Box,detection.Box)<.45f))results.Add(detection);
        return results;
    }
    static float IoU(RectangleF a,RectangleF b)
    {
        var intersection=RectangleF.Intersect(a,b);var area=Math.Max(0,intersection.Width)*Math.Max(0,intersection.Height);
        return area/(a.Width*a.Height+b.Width*b.Height-area+1e-5f);
    }
    public void Dispose()=>_session.Dispose();
}
