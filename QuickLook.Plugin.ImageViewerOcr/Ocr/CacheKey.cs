using System.Security.Cryptography;
using System.Text;

namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

public static class CacheKey
{
    public static string Create(string path, string engineId)
    {
        var info = new FileInfo(path);
        string fullPath = info.FullName;
        long length = info.Exists ? info.Length : 0;
        long ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
        string raw = fullPath + "|" + length.ToString() + "|" + ticks.ToString() + "|" + engineId;
        byte[] hash;
        using (SHA256 sha = SHA256.Create())
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));

        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte part in hash)
            builder.Append(part.ToString("x2"));
        return builder.ToString();
    }
}
