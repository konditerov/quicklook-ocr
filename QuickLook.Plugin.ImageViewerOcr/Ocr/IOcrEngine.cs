namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal interface IOcrEngine
{
    string Id { get; }

    bool IsAvailable { get; }

    OcrPage Recognize(string path, CancellationToken cancellationToken);
}
