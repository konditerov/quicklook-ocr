namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal static class OcrEngineSelector
{
    public static IReadOnlyList<IOcrEngine> Create(PluginSettings settings)
    {
        if (!settings.IsEnabled)
            return Array.Empty<IOcrEngine>();

        return settings.Engine switch
        {
            "Paddle" => Only(PaddleOcrEngine.Shared),
            "WindowsAi" => Only(WindowsAiOcrEngine.Shared),
            "WindowsOcr" => Only(TryWindows(settings)),
            _ => new IOcrEngine?[]
            {
                PaddleOcrEngine.Shared,
                WindowsAiOcrEngine.Shared,
                TryWindows(settings),
            }.Where(engine => engine != null).Cast<IOcrEngine>().ToArray(),
        };
    }

    private static IOcrEngine[] Only(IOcrEngine? engine)
    {
        return engine == null ? Array.Empty<IOcrEngine>() : new[] { engine };
    }

    private static IOcrEngine? TryWindows(PluginSettings settings)
    {
        try
        {
            return new WindowsMediaOcrEngine(settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }
}
