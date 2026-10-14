using System.Text.Json.Serialization;

namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

public readonly struct OcrPoint
{
    [JsonConstructor]
    public OcrPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}

public sealed class OcrWord
{
    public string Text { get; set; } = "";

    public OcrPoint[] Quad { get; set; } = new OcrPoint[4];

    public double AngleDegrees { get; set; }

    public bool IsSelected { get; set; }
}

public sealed class OcrLine
{
    public List<OcrWord> Words { get; set; } = new List<OcrWord>();
}

public sealed class OcrPage
{
    public int ImageWidth { get; set; }

    public int ImageHeight { get; set; }

    public List<OcrLine> Lines { get; set; } = new List<OcrLine>();
}
