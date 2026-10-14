using System.Windows;
using System.Windows.Media.Imaging;

namespace QuickLook.Plugin.ImageViewerOcr;

internal static class ImageProbe
{
    public static Size TryGetSize(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            BitmapFrame frame = decoder.Frames[0];
            return new Size(frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception)
        {
            return Size.Empty;
        }
    }

    public static BitmapSource LoadDisplay(string path, int sourceWidth, int sourceHeight)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        using (FileStream stream = File.OpenRead(path))
        {
            bitmap.StreamSource = stream;
            int longest = Math.Max(sourceWidth, sourceHeight);
            if (longest > 4096)
            {
                if (sourceWidth >= sourceHeight)
                    bitmap.DecodePixelWidth = 4096;
                else
                    bitmap.DecodePixelHeight = 4096;
            }

            bitmap.EndInit();
        }

        bitmap.Freeze();
        return bitmap;
    }
}
