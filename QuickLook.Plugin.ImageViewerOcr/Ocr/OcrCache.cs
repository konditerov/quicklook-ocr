using System.Text.Json;

namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal static class OcrCache
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
    };

    public static OcrPage? TryRead(string key)
    {
        string path = PathFor(key);
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<OcrPage>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            TryDelete(path);
            return null;
        }
    }

    public static void TryWrite(string key, OcrPage page)
    {
        try
        {
            string path = PathFor(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(page, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (IOException)
        {
        }
    }

    private static string PathFor(string key)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuickLook.Plugin.ImageViewerOcr",
            "cache");
        return Path.Combine(root, key + ".json");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
