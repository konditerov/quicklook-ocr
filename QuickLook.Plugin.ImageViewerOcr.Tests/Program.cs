using System;
using System.Drawing;
using System.Linq;
using System.Drawing.Imaging;
using System.IO;
using QuickLook.Plugin.ImageViewerOcr.Ocr;

namespace QuickLook.Plugin.ImageViewerOcr.Tests;

internal static class Program
{
    private static int Main()
    {
        TestHandles();
        TestCacheKey();
        TestCacheRoundtrip();
        TestTextLayout();
        TestCharacterSelection();
        TestQuad();
        TestFold();
        TestSmoke();
        TestLargeImage();
        TestScenes();
        TestWaybills();
        Console.WriteLine("OK");
        return 0;
    }

    private static void TestHandles()
    {
        var plugin = new Plugin();
        if (plugin.Priority <= 0)
            throw new InvalidOperationException("Plugin priority must be above ImageViewer.");
        if (!plugin.CanHandle(@"C:\missing\photo.PNG"))
            throw new InvalidOperationException("PNG must be handled.");
        if (!plugin.CanHandle(@"C:\missing\photo.jpeg") || !plugin.CanHandle(@"C:\missing\photo.bmp") || !plugin.CanHandle(@"C:\missing\scan.tiff"))
            throw new InvalidOperationException("Still raster formats must be handled.");
        if (plugin.CanHandle(@"C:\missing\anim.gif") || plugin.CanHandle(@"C:\missing\anim.apng") || plugin.CanHandle(@"C:\missing\vector.svg"))
            throw new InvalidOperationException("GIF, APNG and SVG must stay with ImageViewer.");
    }

    private static void TestCacheKey()
    {
        string path = Path.Combine(Path.GetTempPath(), "quicklook-ocr-cache-key.txt");
        File.WriteAllText(path, "a");
        string first = CacheKey.Create(path, "paddle");
        string otherEngine = CacheKey.Create(path, "windows-ocr");
        if (first == otherEngine)
            throw new InvalidOperationException("Engine id must change the cache key.");

        File.AppendAllText(path, "b");
        string second = CacheKey.Create(path, "paddle");
        if (first == second)
            throw new InvalidOperationException("File length must change the cache key.");

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        string third = CacheKey.Create(path, "paddle");
        if (second == third)
            throw new InvalidOperationException("Modification time must change the cache key.");
    }

    private static void TestCacheRoundtrip()
    {
        var page = new OcrPage
        {
            ImageWidth = 12,
            ImageHeight = 8,
            Lines = new System.Collections.Generic.List<OcrLine>
            {
                TextLayout.LineFromText("ab", QuadMath.FromRect(10, 20, 30, 40)),
            },
        };
        string key = "unit-" + Guid.NewGuid().ToString("N");
        OcrCache.TryWrite(key, page);
        OcrPage? loaded = OcrCache.TryRead(key);
        if (loaded == null || TextLayout.JoinAll(loaded) != "ab" || loaded.ImageWidth != 12)
            throw new InvalidOperationException("Cache roundtrip failed.");
        OcrPoint origin = loaded.Lines[0].Words[0].Quad[0];
        if (Math.Abs(origin.X - 10) > 0.01 || Math.Abs(origin.Y - 20) > 0.01)
            throw new InvalidOperationException("Cache dropped word coordinates: " + origin.X + "," + origin.Y);
    }

    private static void TestTextLayout()
    {
        var quad = QuadMath.FromRect(0, 10, 100, 20);
        OcrLine line = TextLayout.LineFromText("ab cd", quad);
        if (line.Words.Count != 2)
            throw new InvalidOperationException("Expected two words.");
        if (line.Words[0].Text != "ab" || line.Words[1].Text != "cd")
            throw new InvalidOperationException("Word split failed.");
        if (line.Words[1].Quad[0].X <= line.Words[0].Quad[0].X)
            throw new InvalidOperationException("Second word should start to the right.");

        var page = new OcrPage
        {
            Lines = new System.Collections.Generic.List<OcrLine>
            {
                line,
                TextLayout.LineFromText("привет", QuadMath.FromRect(0, 40, 80, 20)),
            },
        };
        string text = TextLayout.JoinAll(page);
        if (text != "ab cd" + Environment.NewLine + "привет")
            throw new InvalidOperationException("Join failed: " + text);

        line.Words[0].IsSelected = true;
        string selected = TextLayout.Join(page.Lines, word => word.IsSelected);
        if (selected != "ab")
            throw new InvalidOperationException("Selection join failed: " + selected);
    }

    private static void TestCharacterSelection()
    {
        var page = new OcrPage
        {
            Lines = new System.Collections.Generic.List<OcrLine>
            {
                new OcrLine
                {
                    Words = new System.Collections.Generic.List<OcrWord>
                    {
                        new OcrWord { Text = "Ко", Quad = QuadMath.FromRect(0, 12, 40, 18) },
                        new OcrWord { Text = "мпания", Quad = QuadMath.FromRect(50, 8, 90, 26) },
                    },
                },
            },
        };

        if (!TextSelection.TryHit(page, new OcrPoint(30, 20), out TextCaret hit) || hit.Word != 0 || hit.Character != 1)
            throw new InvalidOperationException("Second letter was missed.");

        TextCaret start = new TextCaret(0, 0, 1);
        TextCaret end = new TextCaret(0, 1, 2);
        string text = TextSelection.Copy(page, end, start);
        if (text != "о мпа")
            throw new InvalidOperationException("Character range was copied as: " + text);

        System.Collections.Generic.List<SelectionBand> bands = TextSelection.Bands(page, start, end);
        if (bands.Count != 1)
            throw new InvalidOperationException("A letter range must be one rectangle.");
        SelectionBand band = bands[0];
        if (band.X < 19 || band.X > 21 || band.X + band.Width < 94 || band.Y < 8 || band.Y + band.Height > 35)
            throw new InvalidOperationException("Selection rectangle does not follow the letters.");
        if (band.Height < 18)
            throw new InvalidOperationException("Selection rectangle collapsed.");
    }

    private static void TestQuad()
    {
        OcrPoint[] quad = QuadMath.FromRect(10, 20, 30, 40);
        if (!QuadMath.Contains(quad, new OcrPoint(15, 25)))
            throw new InvalidOperationException("Point inside quad was missed.");
        if (QuadMath.Contains(quad, new OcrPoint(0, 0)))
            throw new InvalidOperationException("Point outside quad was hit.");
        if (!QuadMath.IntersectsRect(quad, 35, 50, 50, 70))
            throw new InvalidOperationException("Overlapping rect was missed.");
        if (QuadMath.IntersectsRect(quad, 0, 0, 5, 5))
            throw new InvalidOperationException("Disjoint rect was hit.");

        OcrPoint[] tight = QuadMath.Inset(quad, 0.1, 0.25, 0.9, 0.75);
        double tightHeight = tight.Max(point => point.Y) - tight.Min(point => point.Y);
        if (tightHeight > 22 || tight[0].X < 12 || tight[1].X > 38)
            throw new InvalidOperationException("Inset quad is still too large.");
    }

    private static void TestSmoke()
    {
        if (!PaddleOcrEngine.Shared.IsAvailable)
            throw new InvalidOperationException("PaddleOCR models were not copied next to the test.");

        string path = Path.Combine(Path.GetTempPath(), "quicklook-ocr-smoke.png");
        DrawSample(path, 900, 220, 42);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        OcrPage page = PaddleOcrEngine.Shared.Recognize(path, System.Threading.CancellationToken.None);
        watch.Stop();
        string text = TextLayout.JoinAll(page).ToUpperInvariant();
        Console.WriteLine(text + " " + watch.ElapsedMilliseconds + " ms");
        if (!text.Contains("HELLO"))
            throw new InvalidOperationException("English text was not recognized.");
        if (!text.Contains("ПРИВЕТ"))
            throw new InvalidOperationException("Russian text was not recognized.");

        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            PaddleOcrEngine.Shared.Recognize(path, cancelled.Token);
            throw new InvalidOperationException("Cancelled recognition must stop.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void TestLargeImage()
    {
        string path = Path.Combine(Path.GetTempPath(), "quicklook-ocr-large.png");
        DrawSample(path, 4200, 900, 150);
        OcrPage page = PaddleOcrEngine.Shared.Recognize(path, System.Threading.CancellationToken.None);
        if (page.ImageWidth != 4200 || page.ImageHeight != 900)
            throw new InvalidOperationException("Large image must keep source coordinates.");
        string text = TextLayout.JoinAll(page).ToUpperInvariant();
        Console.WriteLine("large " + text);
        if (!text.Contains("HELLO") || !text.Contains("ПРИВЕТ"))
            throw new InvalidOperationException("Downscaled large image was not recognized: " + text);
    }

    private static void TestFold()
    {
        string folded = CaseText.FoldWords("КОМПАНИЯ \"BAVIER\" GM7171 CчeT");
        if (!folded.Contains("BAVIER") || !folded.Contains("GM7171"))
            throw new InvalidOperationException("Latin words must stay Latin: " + folded);
        if (folded.Contains("BAVIЕR") || !folded.Contains("СчеТ"))
            throw new InvalidOperationException("Cyrillic words must absorb lookalike letters: " + folded);
        if (CaseText.FoldWords("КАΦE") != "КАФЕ")
            throw new InvalidOperationException("Greek lookalikes in a Russian word must become Cyrillic.");
    }

    private static void TestWaybills()
    {
        string? folder = FindUserTests();
        if (folder == null)
        {
            Console.WriteLine("waybills skipped");
            return;
        }

        string[] files = Directory.GetFiles(folder, "*.png");
        if (files.Length == 0)
            throw new InvalidOperationException("user tests has no PNG files.");

        foreach (string file in files)
        {
            OcrPage page = PaddleOcrEngine.Shared.Recognize(file, System.Threading.CancellationToken.None);
            string text = TextLayout.JoinAll(page);
            Console.WriteLine("waybill " + Path.GetFileName(file));
            Console.WriteLine(text);
            if (!text.Contains("BAVIER"))
                throw new InvalidOperationException("BAVIER was rewritten: " + text);
            if (!text.Contains("GM7171"))
                throw new InvalidOperationException("Tracking code was lost: " + text);
            if (!text.Contains("Садовая"))
                throw new InvalidOperationException("Street case was lost: " + text);
            if (!text.Contains("140090"))
                throw new InvalidOperationException("Postal code was lost: " + text);
            if (!text.Contains("$"))
                throw new InvalidOperationException("Currency sign was lost: " + text);
            string name = Path.GetFileName(file);
            if (name.StartsWith("603a", StringComparison.Ordinal))
                Require(text, "2882.11", "1516.9", "5.049", "181.58", "18158.48", "117", "GM7171-14-260930-75-39", "2026-09-30", "3298");
            if (name.StartsWith("758ee", StringComparison.Ordinal))
                Require(text, "2800.79", "1474.1", "4.917", "176.46", "17646.06", "111", "GM7171-15-260930-73-37", "2026-09-30", "3199");
            foreach (OcrLine line in page.Lines)
            {
                foreach (OcrWord word in line.Words)
                {
                    double width = word.Quad.Max(point => point.X) - word.Quad.Min(point => point.X);
                    double height = word.Quad.Max(point => point.Y) - word.Quad.Min(point => point.Y);
                    if (word.Text == "Садовая" && height > 1 && width > height * 4.2)
                        throw new InvalidOperationException("Street highlight is too wide: " + width.ToString("0") + "x" + height.ToString("0"));
                    if (word.Text.Length <= 2 && height > 1 && width > height * 2.4)
                        throw new InvalidOperationException("Selection box is larger than the letters: " + word.Text + " " + width.ToString("0") + "x" + height.ToString("0"));
                    if (word.Text.Contains("2026-09-30"))
                    {
                        if (height > 28)
                            throw new InvalidOperationException("Date highlight is as tall as the cell: " + height.ToString("0"));
                        RequireDateBox(file, word, width);
                    }
                }
            }
        }
    }

    private static string? FindUserTests()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "user tests");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static void RequireDateBox(string file, OcrWord word, double width)
    {
        if (width < 60)
            throw new InvalidOperationException("Date highlight collapsed to " + width.ToString("0") + " px.");

        double minX = word.Quad.Min(point => point.X);
        double maxX = word.Quad.Max(point => point.X);
        double minY = word.Quad.Min(point => point.Y);
        double maxY = word.Quad.Max(point => point.Y);
        using var bitmap = new Bitmap(file);
        int glyph = FirstDateDigit(bitmap, minX, maxX, minY, maxY);
        if (glyph < 0)
            throw new InvalidOperationException("Date digits were not found on the page.");
        if (minX < glyph - 6)
            throw new InvalidOperationException("Date highlight starts at the table line, " + (glyph - minX).ToString("0") + " px before the digits.");
    }

    private static int FirstDateDigit(Bitmap bitmap, double minX, double maxX, double minY, double maxY)
    {
        int x0 = Math.Max(0, (int)minX - 24);
        int x1 = Math.Min(bitmap.Width - 1, (int)maxX);
        int top = Math.Max(0, (int)minY + 1);
        int bottom = Math.Min(bitmap.Height - 1, (int)maxY - 1);
        for (int x = x0; x <= x1; x++)
        {
            if (RuleColumn(bitmap, x, top, bottom))
                continue;
            if (DarkColumn(bitmap, x, top, bottom, 1))
                return x;
        }

        return -1;
    }

    private static bool RuleColumn(Bitmap bitmap, int x, int top, int bottom)
    {
        int above = Math.Max(0, top - 18);
        int below = Math.Min(bitmap.Height - 1, bottom + 18);
        return DarkColumn(bitmap, x, above, top - 3, 3) && DarkColumn(bitmap, x, bottom + 3, below, 3);
    }

    private static bool DarkColumn(Bitmap bitmap, int x, int y0, int y1, int need)
    {
        if (y1 < y0)
            return false;

        int ink = 0;
        for (int y = y0; y <= y1; y++)
        {
            Color color = bitmap.GetPixel(x, y);
            if ((color.R + color.G + color.B) / 3 < 140)
                ink++;
            if (ink >= need)
                return true;
        }

        return false;
    }

    private static void TestScenes()
    {
        string document = Path.Combine(Path.GetTempPath(), "quicklook-ocr-document.png");
        using (Bitmap bitmap = new Bitmap(1100, 640))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Font font = new Font("Arial", 42, FontStyle.Bold))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            graphics.DrawString("Invoice 1042", font, Brushes.Black, 48, 40);
            graphics.DrawString("Счет 15", font, Brushes.Black, 48, 220);
            graphics.DrawString("Total 250", font, Brushes.Black, 48, 400);
            bitmap.Save(document, ImageFormat.Png);
        }

        OcrPage documentPage = PaddleOcrEngine.Shared.Recognize(document, System.Threading.CancellationToken.None);
        string documentText = TextLayout.JoinAll(documentPage).ToUpperInvariant();
        Console.WriteLine("document " + documentText);
        double tallest = 0;
        foreach (OcrLine line in documentPage.Lines)
        {
            foreach (OcrWord word in line.Words)
            {
                double height = word.Quad.Max(point => point.Y) - word.Quad.Min(point => point.Y);
                if (height > tallest)
                    tallest = height;
            }
        }

        if (tallest > 110)
            throw new InvalidOperationException("Selection box is taller than the text: " + tallest);
        Require(documentText, "INVOICE", "1042", "TOTAL", "250");
        if (!documentText.Contains("СЧЁТ") && !documentText.Contains("СЧЕТ"))
            throw new InvalidOperationException("Document missed the Russian line: " + documentText);

        string sign = Path.Combine(Path.GetTempPath(), "quicklook-ocr-sign.png");
        using (Bitmap bitmap = new Bitmap(900, 420))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Font font = new Font("Arial", 72, FontStyle.Bold))
        {
            graphics.Clear(Color.FromArgb(18, 42, 84));
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            graphics.DrawString("КАФЕ", font, Brushes.White, 48, 40);
            graphics.DrawString("OPEN", font, Brushes.White, 48, 200);
            bitmap.Save(sign, ImageFormat.Png);
        }

        string signText = Recognize(sign);
        Console.WriteLine("sign " + signText);
        Require(signText, "КАФЕ", "OPEN");
    }

    private static string Recognize(string path)
    {
        OcrPage page = PaddleOcrEngine.Shared.Recognize(path, System.Threading.CancellationToken.None);
        return TextLayout.JoinAll(page).ToUpperInvariant();
    }

    private static void Require(string text, params string[] parts)
    {
        foreach (string part in parts)
        {
            if (!text.Contains(part))
                throw new InvalidOperationException("Missing '" + part + "' in: " + text);
        }
    }

    private static void DrawSample(string path, int width, int height, float fontSize)
    {
        using Bitmap bitmap = new Bitmap(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = new Font("Arial", fontSize, FontStyle.Bold);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.DrawString("HELLO", font, Brushes.Black, width * 0.04f, height * 0.08f);
        graphics.DrawString("ПРИВЕТ", font, Brushes.Black, width * 0.04f, height * 0.48f);
        bitmap.Save(path, ImageFormat.Png);
    }
}
