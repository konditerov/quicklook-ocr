using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal sealed class RgbImage
{
    private RgbImage(int width, int height, byte[] rgb)
    {
        Width = width;
        Height = height;
        Rgb = rgb;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgb { get; }

    public static RgbImage LoadScaled(string path, int limitSide, out int sourceWidth, out int sourceHeight)
    {
        using Bitmap source = new Bitmap(path);
        sourceWidth = source.Width;
        sourceHeight = source.Height;
        int longest = Math.Max(sourceWidth, sourceHeight);
        double ratio = longest > limitSide ? (double)limitSide / longest : 1d;
        int width = Align32(sourceWidth * ratio);
        int height = Align32(sourceHeight * ratio);
        using Bitmap canvas = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        return FromBitmap(canvas);
    }

    public static RgbImage White(int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < rgb.Length; i++)
            rgb[i] = 255;
        return new RgbImage(width, height, rgb);
    }

    public RgbImage SampleQuad(OcrPoint[] quad)
    {
        double topWidth = Distance(quad[0], quad[1]);
        double bottomWidth = Distance(quad[3], quad[2]);
        double leftHeight = Distance(quad[0], quad[3]);
        double rightHeight = Distance(quad[1], quad[2]);
        int width = Math.Max(8, (int)Math.Round(Math.Max(topWidth, bottomWidth)));
        int height = Math.Max(8, (int)Math.Round(Math.Max(leftHeight, rightHeight)));
        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            double vertical = height == 1 ? 0 : y / (double)(height - 1);
            for (int x = 0; x < width; x++)
            {
                double horizontal = width == 1 ? 0 : x / (double)(width - 1);
                OcrPoint top = Lerp(quad[0], quad[1], horizontal);
                OcrPoint bottom = Lerp(quad[3], quad[2], horizontal);
                OcrPoint point = Lerp(top, bottom, vertical);
                int target = (y * width + x) * 3;
                rgb[target] = Sample(point.X, point.Y, 0);
                rgb[target + 1] = Sample(point.X, point.Y, 1);
                rgb[target + 2] = Sample(point.X, point.Y, 2);
            }
        }

        return new RgbImage(width, height, rgb);
    }

    public RgbImage FocusLine()
    {
        if (Height < 12)
            return this;

        bool darkInk = MajorityLight();
        bool[] rows = TextRows(darkInk);

        var bands = new List<(int Top, int Bottom)>();
        int yIndex = 0;
        while (yIndex < Height)
        {
            if (!rows[yIndex])
            {
                yIndex++;
                continue;
            }

            int top = yIndex;
            int bottom = yIndex;
            while (yIndex < Height)
            {
                if (rows[yIndex])
                {
                    bottom = yIndex;
                    yIndex++;
                    continue;
                }

                int gap = 0;
                while (yIndex + gap < Height && !rows[yIndex + gap])
                    gap++;
                if (gap > 3 || yIndex + gap >= Height)
                    break;

                yIndex += gap;
            }

            if (bottom - top >= 2)
                bands.Add((top, bottom));
        }

        if (bands.Count == 0)
            return this;

        if (bands.Count == 1)
            return CropBand(bands[0].Top, bands[0].Bottom);

        int center = Height / 2;
        int chosen = 0;
        int bestDistance = Height;
        for (int i = 0; i < bands.Count; i++)
        {
            int middle = (bands[i].Top + bands[i].Bottom) / 2;
            int distance = Math.Abs(middle - center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                chosen = i;
            }
        }

        int bestTop = chosen == 0 ? 0 : bands[chosen - 1].Bottom + 1;
        int bestBottom = chosen == bands.Count - 1 ? Height - 1 : bands[chosen + 1].Top - 1;
        if (bestBottom - bestTop < 4 || bestBottom - bestTop + 1 >= Height - 2)
            return this;

        return SliceRows(bestTop, bestBottom);
    }

    public bool TryTextBand(out double top, out double bottom)
    {
        top = 0;
        bottom = 1;
        if (!TryCentralBand(MajorityLight(), out int bandTop, out int bandBottom))
            return false;

        double yDenom = Math.Max(1, Height - 1);
        int pad = Math.Max(1, (bandBottom - bandTop) / 8);
        top = Math.Max(0, bandTop - pad) / yDenom;
        bottom = Math.Min(Height - 1, bandBottom + pad) / yDenom;
        return bottom > top && bottom - top < 0.92;
    }

    private RgbImage CropBand(int bandTop, int bandBottom)
    {
        int pad = Math.Max(1, (bandBottom - bandTop) / 6);
        int top = Math.Max(0, bandTop - pad);
        int bottom = Math.Min(Height - 1, bandBottom + pad);
        if (bottom - top + 1 >= Height - 2)
            return this;

        return SliceRows(top, bottom);
    }

    public bool TryInkSpan(out double left, out double top, out double right, out double bottom)
    {
        left = 0;
        top = 0;
        right = 1;
        bottom = 1;
        if (Width < 4 || Height < 4)
            return false;

        bool darkInk = MajorityLight();
        if (!TryCentralBand(darkInk, out int bandTop, out int bandBottom))
            return false;

        if (!TryCentralRun(darkInk, bandTop, bandBottom, 0.2, out int first, out int last))
            return false;

        int bandHeight = bandBottom - bandTop + 1;
        double yDenom = Math.Max(1, Height - 1);
        double xDenom = Math.Max(1, Width - 1);
        top = bandTop / yDenom;
        bottom = bandBottom / yDenom;
        left = first / xDenom;
        right = last / xDenom;
        double verticalPad = Math.Max(1d, bandHeight * 0.12) / yDenom;
        double horizontalPad = 2d / xDenom;
        top = Math.Max(0, top - verticalPad);
        bottom = Math.Min(1, bottom + verticalPad);
        left = Math.Max(0, left - horizontalPad);
        right = Math.Min(1, right + horizontalPad);
        return bottom - top >= 0.04 && right - left >= 0.04;
    }

    public bool TryTrimSpan(double u0, double u1, double v0, double v1, out double left, out double right)
    {
        left = u0;
        right = u1;
        if (Width < 4 || u1 - u0 < 0.01)
            return false;

        int x0 = Clamp(u0);
        int x1 = Clamp(u1);
        int y0 = ClampY(v0);
        int y1 = ClampY(v1);
        if (x1 < x0)
        {
            int swap = x0;
            x0 = x1;
            x1 = swap;
        }

        if (y1 < y0)
        {
            int swap = y0;
            y0 = y1;
            y1 = swap;
        }

        bool darkInk = MajorityLight();
        int first = -1;
        int last = -1;
        int bandHeight = Math.Max(1, y1 - y0 + 1);
        for (int x = x0; x <= x1; x++)
        {
            int ink = 0;
            for (int y = y0; y <= y1; y++)
            {
                if (IsStroke(darkInk, x, y))
                    ink++;
            }

            if (IsTableRule(darkInk, x, y0, y1) || ink / (double)bandHeight < 0.2)
                continue;

            if (first < 0)
                first = x;
            last = x;
        }

        if (first < 0 || last <= first)
            return false;

        first = Math.Max(x0, first - 1);
        last = Math.Min(x1, last + 1);
        if (last - first > (x1 - x0) * 0.9)
            return false;

        double denom = Math.Max(1, Width - 1);
        left = first / denom;
        right = last / denom;
        return right - left >= 0.004;
    }

    private bool TryCentralRun(bool darkInk, int bandTop, int bandBottom, double threshold, out int first, out int last)
    {
        first = 0;
        last = Width - 1;
        int bandHeight = Math.Max(1, bandBottom - bandTop + 1);
        // A hyphen is thinner than the stroke test, so 2026-09-30 splits into
        // three runs and the center one is only "09". Bridge a gap up to one
        // line tall. A vertical table rule is a hard edge: the highlight must
        // start at the letters, and the rule must not join the next cell.
        int maxGap = Math.Max(bandHeight, Math.Max(6, (int)Math.Round(Width * 0.045)));
        var runs = new List<(int Start, int End)>();
        int runStart = -1;
        int lastInk = -1;
        int gap = 0;
        for (int x = 0; x <= Width; x++)
        {
            bool rule = x < Width && IsTableRule(darkInk, x, bandTop, bandBottom);
            bool ink = false;
            if (x < Width && !rule)
            {
                int count = 0;
                for (int y = bandTop; y <= bandBottom; y++)
                {
                    if (IsStroke(darkInk, x, y))
                        count++;
                }

                ink = count / (double)bandHeight >= threshold;
            }

            if (rule)
            {
                if (runStart >= 0 && lastInk - runStart >= 2)
                    runs.Add((runStart, lastInk));
                runStart = -1;
                gap = 0;
                continue;
            }

            if (ink)
            {
                if (runStart < 0)
                    runStart = x;
                lastInk = x;
                gap = 0;
                continue;
            }

            if (runStart < 0)
                continue;

            gap++;
            if (gap <= maxGap && x < Width)
                continue;

            if (lastInk - runStart >= 2)
                runs.Add((runStart, lastInk));
            runStart = -1;
            gap = 0;
        }

        if (runs.Count == 0)
            return false;

        int center = Width / 2;
        int chosen = 0;
        int bestScore = int.MaxValue;
        for (int i = 0; i < runs.Count; i++)
        {
            int distance = center < runs[i].Start
                ? runs[i].Start - center
                : center > runs[i].End ? center - runs[i].End : 0;
            int score = distance * 10000 - (runs[i].End - runs[i].Start);
            if (score < bestScore)
            {
                bestScore = score;
                chosen = i;
            }
        }

        first = runs[chosen].Start;
        last = runs[chosen].End;
        return last - first >= 2;
    }

    private bool[] TextRows(bool darkInk)
    {
        bool[] rules = new bool[Width];
        int edge = Math.Max(2, Height / 5);
        for (int x = 0; x < Width; x++)
        {
            rules[x] = DarkShare(darkInk, x, 0, edge - 1) >= 0.45
                && DarkShare(darkInk, x, Height - edge, Height - 1) >= 0.45;
        }

        int usable = 0;
        for (int x = 0; x < Width; x++)
        {
            if (!rules[x])
                usable++;
        }

        int span = Math.Max(1, usable);
        var rows = new bool[Height];
        for (int y = 0; y < Height; y++)
        {
            int ink = 0;
            for (int x = 0; x < Width; x++)
            {
                if (usable > 0 && rules[x])
                    continue;
                if (IsInk(darkInk, x, y))
                    ink++;
            }

            rows[y] = ink > span * 0.02 && ink < span * 0.72;
        }

        return rows;
    }

    private double DarkShare(bool darkInk, int x, int y0, int y1)
    {
        int span = y1 - y0 + 1;
        if (span <= 0)
            return 0;

        int dark = 0;
        for (int y = y0; y <= y1; y++)
        {
            if (IsStroke(darkInk, x, y))
                dark++;
        }

        return dark / (double)span;
    }

    private bool IsTableRule(bool darkInk, int x, int bandTop, int bandBottom)
    {
        int reach = Math.Max(4, bandBottom - bandTop + 1);
        return Extends(darkInk, x, Math.Max(0, bandTop - reach), bandTop - 1)
            && Extends(darkInk, x, bandBottom + 1, Math.Min(Height - 1, bandBottom + reach));
    }

    private bool Extends(bool darkInk, int x, int y0, int y1)
    {
        int span = y1 - y0 + 1;
        if (span < 3)
            return false;

        int ink = 0;
        for (int y = y0; y <= y1; y++)
        {
            if (IsStroke(darkInk, x, y))
                ink++;
        }

        return ink >= span * 0.45;
    }

    private int Clamp(double fraction)
    {
        return Math.Max(0, Math.Min(Width - 1, (int)Math.Round(fraction * Math.Max(1, Width - 1))));
    }

    private int ClampY(double fraction)
    {
        return Math.Max(0, Math.Min(Height - 1, (int)Math.Round(fraction * Math.Max(1, Height - 1))));
    }

    private bool TryCentralBand(bool darkInk, out int bandTop, out int bandBottom)
    {
        bandTop = 0;
        bandBottom = Height - 1;
        bool[] rows = TextRows(darkInk);

        var bands = new List<(int Top, int Bottom)>();
        int yIndex = 0;
        while (yIndex < Height)
        {
            if (!rows[yIndex])
            {
                yIndex++;
                continue;
            }

            int top = yIndex;
            int bottom = yIndex;
            while (yIndex < Height)
            {
                if (rows[yIndex])
                {
                    bottom = yIndex;
                    yIndex++;
                    continue;
                }

                int gap = 0;
                while (yIndex + gap < Height && !rows[yIndex + gap])
                    gap++;
                if (gap > 3 || yIndex + gap >= Height)
                    break;

                yIndex += gap;
            }

            if (bottom - top >= 2)
                bands.Add((top, bottom));
        }

        if (bands.Count == 0)
            return false;

        int center = Height / 2;
        int chosen = 0;
        int bestDistance = Height;
        for (int i = 0; i < bands.Count; i++)
        {
            int middle = (bands[i].Top + bands[i].Bottom) / 2;
            int distance = Math.Abs(middle - center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                chosen = i;
            }
        }

        bandTop = bands[chosen].Top;
        bandBottom = bands[chosen].Bottom;
        return true;
    }

    private RgbImage SliceRows(int top, int bottom)
    {
        int height = bottom - top + 1;
        byte[] rgb = new byte[Width * height * 3];
        Buffer.BlockCopy(Rgb, top * Width * 3, rgb, 0, rgb.Length);
        return new RgbImage(Width, height, rgb);
    }

    private bool MajorityLight()
    {
        int light = 0;
        int count = 0;
        for (int y = 0; y < Height; y += 2)
        {
            for (int x = 0; x < Width; x += 2)
            {
                count++;
                if (Luminance(x, y) >= 150)
                    light++;
            }
        }

        return count == 0 || light * 2 >= count;
    }

    private bool IsInk(bool darkInk, int x, int y)
    {
        int luminance = Luminance(x, y);
        return darkInk ? luminance < 200 : luminance > 170;
    }

    private bool IsStroke(bool darkInk, int x, int y)
    {
        int luminance = Luminance(x, y);
        return darkInk ? luminance < 145 : luminance > 185;
    }

    private int Luminance(int x, int y)
    {
        int offset = (y * Width + x) * 3;
        return (Rgb[offset] + Rgb[offset + 1] + Rgb[offset + 2]) / 3;
    }

    public RgbImage PadWidth(int width)
    {
        if (width <= Width)
            return this;

        byte[] rgb = new byte[width * Height * 3];
        for (int i = 0; i < rgb.Length; i++)
            rgb[i] = 255;
        int rowBytes = Width * 3;
        int paddedRow = width * 3;
        for (int y = 0; y < Height; y++)
            Buffer.BlockCopy(Rgb, y * rowBytes, rgb, y * paddedRow, rowBytes);
        return new RgbImage(width, Height, rgb);
    }

    public RgbImage Resize(int width, int height)
    {
        using Bitmap source = ToBitmap();
        using Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, width, height);
        }

        return FromBitmap(bitmap);
    }

    private byte Sample(double x, double y, int channel)
    {
        if (x < 0 || y < 0 || x >= Width - 1 || y >= Height - 1)
            return 255;

        int x0 = (int)x;
        int y0 = (int)y;
        double tx = x - x0;
        double ty = y - y0;
        double a = At(x0, y0, channel);
        double b = At(x0 + 1, y0, channel);
        double c = At(x0, y0 + 1, channel);
        double d = At(x0 + 1, y0 + 1, channel);
        double value = (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
        return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
    }

    private byte At(int x, int y, int channel)
    {
        return Rgb[(y * Width + x) * 3 + channel];
    }

    private Bitmap ToBitmap()
    {
        Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            byte[] row = new byte[Width * 3];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int source = (y * Width + x) * 3;
                    row[x * 3] = Rgb[source + 2];
                    row[x * 3 + 1] = Rgb[source + 1];
                    row[x * 3 + 2] = Rgb[source];
                }

                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static RgbImage FromBitmap(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            byte[] rgb = new byte[bitmap.Width * bitmap.Height * 3];
            byte[] row = new byte[Math.Abs(data.Stride)];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int target = (y * bitmap.Width + x) * 3;
                    rgb[target] = row[x * 3 + 2];
                    rgb[target + 1] = row[x * 3 + 1];
                    rgb[target + 2] = row[x * 3];
                }
            }

            return new RgbImage(bitmap.Width, bitmap.Height, rgb);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static int Align32(double value)
    {
        int aligned = (int)Math.Round(value / 32d) * 32;
        return Math.Max(32, aligned);
    }

    private static double Distance(OcrPoint a, OcrPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static OcrPoint Lerp(OcrPoint a, OcrPoint b, double t)
    {
        return new OcrPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }
}
