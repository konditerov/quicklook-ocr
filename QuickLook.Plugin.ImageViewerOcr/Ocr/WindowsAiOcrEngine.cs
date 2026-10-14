namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal sealed class WindowsAiOcrEngine : IOcrEngine
{
    private WindowsAiOcrEngine()
    {
        try
        {
            IsAvailable = Type.GetType(
                "Microsoft.Windows.AI.Imaging.TextRecognizer, Microsoft.Windows.AI.Imaging",
                throwOnError: false) != null;
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    public static WindowsAiOcrEngine Shared { get; } = new WindowsAiOcrEngine();

    public string Id => "windows-ai";

    public bool IsAvailable { get; }

    public OcrPage Recognize(string path, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Windows AI Text Recognition is not available in this process.");
    }
}
