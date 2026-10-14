using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using WinOcr = Windows.Media.Ocr;

namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal sealed class WindowsMediaOcrEngine : IOcrEngine
{
    private readonly PluginSettings _settings;

    public WindowsMediaOcrEngine(PluginSettings settings)
    {
        _settings = settings;
        try
        {
            IsAvailable = Languages().Any(IsSupported);
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    public string Id => "windows-ocr";

    public bool IsAvailable { get; }

    public OcrPage Recognize(string path, CancellationToken cancellationToken)
    {
        OcrPage? best = null;
        int bestScore = -1;
        foreach (string language in Languages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupported(language))
                continue;

            OcrPage page = RecognizeLanguage(path, language);
            int score = TextLayout.JoinAll(page).Count(character => !char.IsWhiteSpace(character));
            if (score > bestScore)
            {
                best = page;
                bestScore = score;
            }
        }

        if (best == null)
            throw new InvalidOperationException("Windows.Media.Ocr has no usable language.");

        return best;
    }

    private IEnumerable<string> Languages()
    {
        if (_settings.UseRussian)
            yield return "ru";
        if (_settings.UseEnglish)
            yield return "en";
    }

    private static bool IsSupported(string language)
    {
        return WinOcr.OcrEngine.IsLanguageSupported(new Language(language));
    }

    private static OcrPage RecognizeLanguage(string path, string language)
    {
        WinOcr.OcrEngine? engine = WinOcr.OcrEngine.TryCreateFromLanguage(new Language(language));
        if (engine == null)
            throw new InvalidOperationException("Could not create Windows OCR for " + language + ".");

        using Bitmap bitmap = new Bitmap(path);
        SoftwareBitmap software = CreateSoftwareBitmap(bitmap);
        WinOcr.OcrResult result = engine.RecognizeAsync(software).AsTask().GetAwaiter().GetResult();
        var lines = new List<OcrLine>();
        foreach (WinOcr.OcrLine windowsLine in result.Lines)
        {
            var line = new OcrLine();
            foreach (WinOcr.OcrWord windowsWord in windowsLine.Words)
            {
                Windows.Foundation.Rect rect = windowsWord.BoundingRect;
                line.Words.Add(new OcrWord
                {
                    Text = windowsWord.Text,
                    Quad = QuadMath.FromRect(rect.X, rect.Y, rect.Width, rect.Height),
                    AngleDegrees = 0,
                });
            }

            if (line.Words.Count > 0)
                lines.Add(line);
        }

        return new OcrPage
        {
            ImageWidth = bitmap.Width,
            ImageHeight = bitmap.Height,
            Lines = lines,
        };
    }

    private static SoftwareBitmap CreateSoftwareBitmap(Bitmap bitmap)
    {
        using Bitmap canvas = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
        }

        BitmapData data = canvas.LockBits(
            new Rectangle(0, 0, canvas.Width, canvas.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int packedStride = canvas.Width * 4;
            byte[] pixels = new byte[packedStride * canvas.Height];
            byte[] row = new byte[Math.Abs(data.Stride)];
            for (int y = 0; y < canvas.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                Buffer.BlockCopy(row, 0, pixels, y * packedStride, packedStride);
            }
            return SoftwareBitmap.CreateCopyFromBuffer(
                pixels.AsBuffer(),
                BitmapPixelFormat.Bgra8,
                canvas.Width,
                canvas.Height,
                BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            canvas.UnlockBits(data);
        }
    }
}
