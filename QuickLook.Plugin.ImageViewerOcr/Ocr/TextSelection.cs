using System.Text;

namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal readonly struct TextCaret
{
    public TextCaret(int line, int word, int character)
    {
        Line = line;
        Word = word;
        Character = character;
    }

    public int Line { get; }

    public int Word { get; }

    public int Character { get; }

    public int CompareTo(TextCaret other)
    {
        int line = Line.CompareTo(other.Line);
        if (line != 0)
            return line;
        int word = Word.CompareTo(other.Word);
        if (word != 0)
            return word;
        return Character.CompareTo(other.Character);
    }
}

internal readonly struct SelectionBand
{
    public SelectionBand(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }
}

internal static class TextSelection
{
    public static bool TryHit(OcrPage page, OcrPoint point, out TextCaret caret)
    {
        caret = default;
        double best = double.MaxValue;
        bool found = false;
        for (int line = 0; line < page.Lines.Count; line++)
        {
            List<OcrWord> words = page.Lines[line].Words;
            for (int word = 0; word < words.Count; word++)
            {
                OcrWord item = words[word];
                if (item.Text.Length == 0 || item.Quad.Length < 4)
                    continue;
                if (!TryProject(item.Quad, point, out double across, out double down))
                    continue;

                double outsideX = across < 0 ? -across : across > 1 ? across - 1 : 0;
                double outsideY = down < 0 ? -down : down > 1 ? down - 1 : 0;
                if (outsideX > 0.35 || outsideY > 0.8)
                    continue;

                double score = outsideX * Span(item.Quad[0], item.Quad[1]) + outsideY * Span(item.Quad[0], item.Quad[3]);
                if (score >= best)
                    continue;

                best = score;
                found = true;
                int index = (int)Math.Floor(Math.Max(0, across) * item.Text.Length);
                if (index >= item.Text.Length)
                    index = item.Text.Length - 1;
                caret = new TextCaret(line, word, index);
            }
        }

        return found;
    }

    public static TextCaret WordStart(TextCaret caret)
    {
        return new TextCaret(caret.Line, caret.Word, 0);
    }

    public static TextCaret WordEnd(OcrPage page, TextCaret caret)
    {
        OcrWord word = page.Lines[caret.Line].Words[caret.Word];
        return new TextCaret(caret.Line, caret.Word, Math.Max(0, word.Text.Length - 1));
    }

    public static string Copy(OcrPage page, TextCaret anchor, TextCaret focus)
    {
        Order(anchor, focus, out TextCaret start, out TextCaret end);
        var text = new StringBuilder();
        for (int line = start.Line; line <= end.Line && line < page.Lines.Count; line++)
        {
            if (text.Length > 0)
                text.AppendLine();
            List<OcrWord> words = page.Lines[line].Words;
            if (words.Count == 0)
                continue;

            int wordFrom = line == start.Line ? start.Word : 0;
            int charFrom = line == start.Line ? start.Character : 0;
            int wordTo = line == end.Line ? end.Word : words.Count - 1;
            int charTo = line == end.Line ? end.Character : words[wordTo].Text.Length - 1;
            bool any = false;
            for (int word = wordFrom; word <= wordTo && word < words.Count; word++)
            {
                string value = words[word].Text;
                if (value.Length == 0)
                    continue;
                int from = word == wordFrom ? Math.Min(charFrom, value.Length - 1) : 0;
                int to = word == wordTo ? Math.Min(charTo, value.Length - 1) : value.Length - 1;
                if (to < from)
                    continue;
                if (any)
                    text.Append(' ');
                text.Append(value, from, to - from + 1);
                any = true;
            }
        }

        return text.ToString();
    }

    public static List<SelectionBand> Bands(OcrPage page, TextCaret anchor, TextCaret focus)
    {
        Order(anchor, focus, out TextCaret start, out TextCaret end);
        var bands = new List<SelectionBand>();
        for (int line = start.Line; line <= end.Line && line < page.Lines.Count; line++)
        {
            List<OcrWord> words = page.Lines[line].Words;
            if (words.Count == 0)
                continue;

            int wordFrom = line == start.Line ? start.Word : 0;
            int charFrom = line == start.Line ? start.Character : 0;
            int wordTo = line == end.Line ? end.Word : words.Count - 1;
            int charTo = line == end.Line ? end.Character : words[wordTo].Text.Length - 1;
            if (wordFrom >= words.Count || wordTo >= words.Count)
                continue;

            OcrPoint[] first = Slice(words[wordFrom].Quad, words[wordFrom].Text.Length, charFrom);
            OcrPoint[] last = Slice(words[wordTo].Quad, words[wordTo].Text.Length, charTo);
            double left = Math.Min(first[0].X, first[3].X);
            double right = Math.Max(last[1].X, last[2].X);
            double top = double.MaxValue;
            double bottom = double.MinValue;
            for (int word = wordFrom; word <= wordTo; word++)
            {
                OcrPoint[] quad = words[word].Quad;
                if (quad.Length < 4)
                    continue;
                top = Math.Min(top, (quad[0].Y + quad[1].Y) / 2);
                bottom = Math.Max(bottom, (quad[2].Y + quad[3].Y) / 2);
            }

            if (right - left < 1 || bottom <= top)
                continue;
            bands.Add(new SelectionBand(left, top, right - left, Math.Max(1, bottom - top)));
        }

        return bands;
    }

    private static void Order(TextCaret anchor, TextCaret focus, out TextCaret start, out TextCaret end)
    {
        if (anchor.CompareTo(focus) <= 0)
        {
            start = anchor;
            end = focus;
            return;
        }

        start = focus;
        end = anchor;
    }

    private static OcrPoint[] Slice(OcrPoint[] quad, int length, int index)
    {
        if (quad.Length < 4 || length <= 1)
            return quad;

        double start = index / (double)length;
        double end = (index + 1) / (double)length;
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

    private static bool TryProject(OcrPoint[] quad, OcrPoint point, out double across, out double down)
    {
        across = 0;
        down = 0;
        double ax = quad[1].X - quad[0].X;
        double ay = quad[1].Y - quad[0].Y;
        double bx = quad[3].X - quad[0].X;
        double by = quad[3].Y - quad[0].Y;
        double px = point.X - quad[0].X;
        double py = point.Y - quad[0].Y;
        double det = ax * by - ay * bx;
        if (Math.Abs(det) < 1e-6)
            return false;

        across = (px * by - py * bx) / det;
        down = (ax * py - ay * px) / det;
        return true;
    }

    private static double Span(OcrPoint a, OcrPoint b)
    {
        double x = b.X - a.X;
        double y = b.Y - a.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
