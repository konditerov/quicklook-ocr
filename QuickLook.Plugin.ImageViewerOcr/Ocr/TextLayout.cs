namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

public static class TextLayout
{
    public static OcrLine LineFromText(string text, OcrPoint[] quad)
    {
        return LineFromText(text, quad, null);
    }

    public static OcrLine LineFromText(string text, OcrPoint[] quad, Func<double, double, (double Start, double End)>? trim)
    {
        var line = new OcrLine();
        if (string.IsNullOrWhiteSpace(text) || quad.Length < 4)
            return line;

        string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return line;

        int total = 0;
        foreach (string token in tokens)
            total += token.Length;
        total += tokens.Length - 1;

        int cursor = 0;
        foreach (string token in tokens)
        {
            double start = cursor / (double)total;
            double end = (cursor + token.Length) / (double)total;
            if (trim != null)
            {
                (double trimmedStart, double trimmedEnd) = trim(start, end);
                if (trimmedEnd - trimmedStart >= 0.008 && trimmedStart >= start - 0.001 && trimmedEnd <= end + 0.001)
                {
                    start = trimmedStart;
                    end = trimmedEnd;
                }
            }

            line.Words.Add(new OcrWord
            {
                Text = token,
                Quad = Slice(quad, start, end),
                AngleDegrees = QuadMath.AngleDegrees(quad),
            });
            cursor += token.Length + 1;
        }

        return line;
    }

    public static string Join(IEnumerable<OcrLine> lines, Func<OcrWord, bool>? include)
    {
        var rows = new List<string>();
        foreach (OcrLine line in lines)
        {
            var words = new List<string>();
            foreach (OcrWord word in line.Words)
            {
                if (include == null || include(word))
                    words.Add(word.Text);
            }

            if (words.Count > 0)
                rows.Add(string.Join(" ", words));
        }

        return string.Join(Environment.NewLine, rows);
    }

    public static string JoinAll(OcrPage page)
    {
        return Join(page.Lines, null);
    }

    private static OcrPoint[] Slice(OcrPoint[] quad, double start, double end)
    {
        return new[]
        {
            Lerp(quad[0], quad[1], start),
            Lerp(quad[0], quad[1], end),
            Lerp(quad[3], quad[2], end),
            Lerp(quad[3], quad[2], start),
        };
    }

    private static OcrPoint Lerp(OcrPoint a, OcrPoint b, double t)
    {
        return new OcrPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }
}
