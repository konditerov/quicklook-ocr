namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal static class OcrRunner
{
    public static Task<OcrPage?> RunAsync(string path, PluginSettings settings, CancellationToken cancellationToken)
    {
        return Task.Run(() => Run(path, settings, cancellationToken), cancellationToken);
    }

    private static OcrPage? Run(string path, PluginSettings settings, CancellationToken cancellationToken)
    {
        Stopwatch watch = Stopwatch.StartNew();
        foreach (IOcrEngine engine in OcrEngineSelector.Create(settings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!engine.IsAvailable)
                continue;

            string key = CacheKey.Create(path, engine.Id + ":v11");
            OcrPage? cached = OcrCache.TryRead(key);
            if (cached != null)
            {
                Debug.WriteLine("OCR cache hit " + engine.Id + " " + watch.ElapsedMilliseconds + " ms");
                return cached;
            }

            try
            {
                OcrPage page = engine.Recognize(path, cancellationToken);
                OcrCache.TryWrite(key, page);
                Debug.WriteLine("OCR " + engine.Id + " " + watch.ElapsedMilliseconds + " ms " + path);
                return page;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(engine.Id + " failed: " + ex);
            }
        }

        return null;
    }
}
