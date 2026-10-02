using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ControllerLabPro.Vision;

public sealed class YoloOnnxDetector : IObjectDetector
{
    const int Size = 640;
    readonly InferenceSession _session;
    readonly string _input;
    readonly float[] _inputBuffer = new float[3 * Size * Size];
    readonly DenseTensor<float> _tensor;

    public YoloOnnxDetector(string path)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        try { options.AppendExecutionProvider_DML(); } catch { }
        _session = new InferenceSession(path, options);
        _input = _session.InputMetadata.Keys.First();
        _tensor = new DenseTensor<float>(_inputBuffer, [1, 3, Size, Size]);
    }

    public IReadOnlyList<RawDetection> Detect(Bitmap frame, float threshold)
    {
        using var resized = new Bitmap(Size, Size, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            graphics.DrawImage(frame, 0, 0, Size, Size);
        }
        FillTensor(resized);

        using var output = _session.Run([NamedOnnxValue.CreateFromTensor(_input, _tensor)]);
        var values = output.First().AsTensor<float>();
        var dimensions = values.Dimensions.ToArray();
        if (dimensions.Length != 3) return [];
        var attrsFirst = dimensions[1] < dimensions[2];
        var attributes = attrsFirst ? dimensions[1] : dimensions[2];
        var count = attrsFirst ? dimensions[2] : dimensions[1];
        var detections = new List<RawDetection>();

        float At(int attribute, int detection) => attrsFirst ? values[0, attribute, detection] : values[0, detection, attribute];
        for (var i = 0; i < count; i++)
        {
            float best = 0;
            var classId = -1;
            for (var c = 4; c < attributes; c++)
            {
                var score = At(c, i);
                if (score > best) { best = score; classId = c - 4; }
            }
            if (best < threshold || classId != 0) continue;
            var cx = At(0, i) * frame.Width / Size;
            var cy = At(1, i) * frame.Height / Size;
            var width = At(2, i) * frame.Width / Size;
            var height = At(3, i) * frame.Height / Size;
            detections.Add(new(new RectangleF(cx - width / 2, cy - height / 2, width, height), best, "person"));
        }
        return Nms(detections, .45f);
    }

    void FillTensor(Bitmap bitmap)
    {
        var area = Size * Size;
        var data = bitmap.LockBits(new Rectangle(0, 0, Size, Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * Size];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < Size; y++)
            {
                var row = data.Stride >= 0 ? y * stride : (Size - 1 - y) * stride;
                var pixel = y * Size;
                for (var x = 0; x < Size; x++, pixel++)
                {
                    var source = row + x * 3;
                    _inputBuffer[pixel] = bytes[source + 2] / 255f;
                    _inputBuffer[area + pixel] = bytes[source + 1] / 255f;
                    _inputBuffer[area * 2 + pixel] = bytes[source] / 255f;
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
    }

    static List<RawDetection> Nms(List<RawDetection> candidates, float threshold)
    {
        var result = new List<RawDetection>();
        foreach (var detection in candidates.OrderByDescending(x => x.Confidence))
            if (result.All(existing => IoU(existing.Box, detection.Box) < threshold)) result.Add(detection);
        return result;
    }

    static float IoU(RectangleF a, RectangleF b)
    {
        var intersection = RectangleF.Intersect(a, b);
        var area = Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
        return area / (a.Width * a.Height + b.Width * b.Height - area + 1e-5f);
    }

    public void Dispose() => _session.Dispose();
}
