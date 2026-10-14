namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal static class CaseText
{
    private const string ShortLower = "acegmnopqrsuvwxyzаеёосхэяу";

    public static string Apply(string text, RgbImage image)
    {
        text = FoldWords(text);
        char[] agreed = text.ToCharArray();
        AgreeLatinCase(agreed);
        text = new string(agreed);
        if (text.Length == 0 || image.Width < 2 || image.Height < 2)
            return text;

        bool darkInk = BorderIsLight(image);
        if (!TryTextBand(image, darkInk, out int top, out int bottom))
            return text;

        char[] chars = text.ToCharArray();
        bool[] tall = new bool[chars.Length];
        bool[] measured = new bool[chars.Length];
        int[] tops = new int[chars.Length];
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetter(chars[i]))
                continue;
            if (!TryCharTop(image, darkInk, i, chars.Length, top, bottom, out int charTop))
                continue;

            measured[i] = true;
            tops[i] = charTop;
        }

        int wordStart = 0;
        for (int i = 0; i <= chars.Length; i++)
        {
            if (i < chars.Length && chars[i] != ' ')
                continue;
            if (IsSentenceCase(chars, wordStart, i))
            {
                wordStart = i + 1;
                continue;
            }

            int cap = bottom;
            int low = top;
            bool any = false;
            for (int index = wordStart; index < i; index++)
            {
                if (!measured[index])
                    continue;
                any = true;
                if (tops[index] < cap)
                    cap = tops[index];
                if (tops[index] > low)
                    low = tops[index];
            }

            if (any && low - cap >= 3)
            {
                int limit = cap + Math.Max(1, (low - cap) / 3);
                for (int index = wordStart; index < i; index++)
                {
                    if (!measured[index] || IsLatinLetter(chars[index]))
                        continue;
                    tall[index] = tops[index] <= limit;
                    if (IsShortLower(chars[index]) && tall[index])
                        chars[index] = char.ToUpperInvariant(chars[index]);
                    else if (char.IsUpper(chars[index]) && !tall[index] && !KeepsInitialCapital(chars, index))
                        chars[index] = char.ToLowerInvariant(chars[index]);
                }
            }

            wordStart = i + 1;
        }

        ApplyWordCase(chars, measured, tall);
        AgreeLatinCase(chars);
        return new string(chars);
    }

    public static bool ContainsDistinctCyrillic(string text)
    {
        foreach (char character in text)
        {
            bool cyrillic = (character >= 'А' && character <= 'я') || character == 'Ё' || character == 'ё';
            if (cyrillic && !IsCyrillicHomoglyph(character))
                return true;
        }

        return false;
    }

    public static string FoldWords(string text)
    {
        if (text.Length == 0)
            return text;

        char[] chars = text.ToCharArray();
        int start = 0;
        for (int i = 0; i <= chars.Length; i++)
        {
            if (i < chars.Length && char.IsLetterOrDigit(chars[i]))
                continue;
            if (i > start && ContainsCyrillicCode(chars, start, i))
            {
                FoldGreekLookalikes(chars, start, i);
                if (ContainsDistinctCyrillic(new string(chars, start, i - start)))
                    FoldShortRuns(chars, start, i);
            }
            start = i + 1;
        }

        return new string(chars);
    }

    private static bool ContainsCyrillicCode(char[] chars, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            char character = chars[i];
            if ((character >= 'А' && character <= 'я') || character == 'Ё' || character == 'ё')
                return true;
        }

        return false;
    }

    private static void FoldGreekLookalikes(char[] chars, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            char folded = chars[i] switch
            {
                'Α' => 'А',
                'Β' => 'В',
                'Ε' => 'Е',
                'Η' => 'Н',
                'Ι' => 'І',
                'Κ' => 'К',
                'Μ' => 'М',
                'Ο' => 'О',
                'Ρ' => 'Р',
                'Τ' => 'Т',
                'Υ' => 'У',
                'Χ' => 'Х',
                'Φ' => 'Ф',
                'φ' => 'ф',
                'ο' => 'о',
                _ => chars[i],
            };
            chars[i] = folded;
        }
    }

    private static void FoldShortRuns(char[] chars, int start, int end)
    {
        int run = start;
        while (run < end)
        {
            if (!IsLatinLetter(chars[run]))
            {
                run++;
                continue;
            }

            int next = run;
            while (next < end && IsLatinLetter(chars[next]))
                next++;
            if (next - run <= 2)
            {
                for (int index = run; index < next; index++)
                    chars[index] = LatinToCyrillic(chars[index]);
            }

            run = next;
        }
    }

    private static bool IsLatinLetter(char character)
    {
        return (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');
    }

    private static void AgreeLatinCase(char[] chars)
    {
        int start = -1;
        for (int i = 0; i <= chars.Length; i++)
        {
            bool latin = i < chars.Length && IsLatinLetter(chars[i]);
            if (latin)
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0 && i - start >= 4)
            {
                int upper = 0;
                int lower = 0;
                for (int index = start; index < i; index++)
                {
                    if (char.IsUpper(chars[index]))
                        upper++;
                    else if (char.IsLower(chars[index]))
                        lower++;
                }

                if (upper > lower && lower > 0)
                {
                    for (int index = start; index < i; index++)
                        chars[index] = char.ToUpperInvariant(chars[index]);
                }
            }

            start = -1;
        }
    }

    private static bool IsSentenceCase(char[] chars, int start, int end)
    {
        bool sawUpper = false;
        bool sawLower = false;
        for (int i = start; i < end; i++)
        {
            if (!char.IsLetter(chars[i]))
                continue;
            if (!sawUpper)
            {
                if (!char.IsUpper(chars[i]))
                    return false;
                sawUpper = true;
                continue;
            }

            if (char.IsUpper(chars[i]))
                return false;
            sawLower = true;
        }

        return sawUpper && sawLower;
    }

    private static bool KeepsInitialCapital(char[] chars, int index)
    {
        int start = index;
        while (start > 0 && chars[start - 1] != ' ')
            start--;
        if (index != start || !char.IsLetter(chars[index]))
            return false;

        for (int i = index + 1; i < chars.Length && chars[i] != ' '; i++)
        {
            if (char.IsLetter(chars[i]) && char.IsLower(chars[i]))
                return true;
        }

        return false;
    }

    private static void ApplyWordCase(char[] chars, bool[] measured, bool[] tall)
    {
        int start = 0;
        for (int i = 0; i <= chars.Length; i++)
        {
            if (i < chars.Length && chars[i] != ' ')
                continue;
            if (IsSentenceCase(chars, start, i))
            {
                start = i + 1;
                continue;
            }

            bool sawShort = false;
            bool shortTall = true;
            bool anyTall = false;
            for (int index = start; index < i; index++)
            {
                if (!measured[index])
                    continue;
                if (tall[index])
                    anyTall = true;
                if (!IsShortLower(char.ToLowerInvariant(chars[index])))
                    continue;
                sawShort = true;
                if (!tall[index])
                    shortTall = false;
            }

            if (sawShort && shortTall)
                SetWordCase(chars, start, i, upper: true);
            else if (sawShort && !anyTall)
                SetWordCase(chars, start, i, upper: false);

            start = i + 1;
        }
    }

    private static void SetWordCase(char[] chars, int start, int end, bool upper)
    {
        for (int i = start; i < end; i++)
        {
            if (!char.IsLetter(chars[i]) || IsLatinLetter(chars[i]))
                continue;
            chars[i] = upper ? char.ToUpperInvariant(chars[i]) : char.ToLowerInvariant(chars[i]);
        }
    }

    private static bool BorderIsLight(RgbImage image)
    {
        long sum = 0;
        int count = 0;
        for (int x = 0; x < image.Width; x++)
        {
            sum += Luminance(image, x, 0);
            sum += Luminance(image, x, image.Height - 1);
            count += 2;
        }

        return count > 0 && sum / count >= 140;
    }

    private static bool TryTextBand(RgbImage image, bool darkInk, out int top, out int bottom)
    {
        top = image.Height;
        bottom = 0;
        for (int y = 0; y < image.Height; y++)
        {
            int ink = 0;
            for (int x = 0; x < image.Width; x++)
            {
                if (IsInk(image, darkInk, x, y))
                    ink++;
            }

            if (ink <= image.Width * 0.02 || ink >= image.Width * 0.75)
                continue;
            if (y < top)
                top = y;
            if (y > bottom)
                bottom = y;
        }

        return bottom > top + 3;
    }

    private static bool TryCharTop(RgbImage image, bool darkInk, int index, int length, int top, int bottom, out int charTop)
    {
        int x0 = (int)(index * (double)image.Width / length);
        int x1 = Math.Max(x0 + 1, (int)((index + 1) * (double)image.Width / length));
        x1 = Math.Min(image.Width, x1);
        charTop = image.Height;
        int count = 0;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (!IsInk(image, darkInk, x, y))
                    continue;
                count++;
                if (y < charTop)
                    charTop = y;
            }
        }

        return count >= 4;
    }

    private static int Luminance(RgbImage image, int x, int y)
    {
        int offset = (y * image.Width + x) * 3;
        return (image.Rgb[offset] + image.Rgb[offset + 1] + image.Rgb[offset + 2]) / 3;
    }

    private static bool IsInk(RgbImage image, bool darkInk, int x, int y)
    {
        int luminance = Luminance(image, x, y);
        return darkInk ? luminance < 206 : luminance > 160;
    }

    private static bool IsShortLower(char character)
    {
        return ShortLower.IndexOf(char.ToLowerInvariant(character)) >= 0;
    }

    private static char LatinToCyrillic(char character)
    {
        switch (character)
        {
            case 'A': return 'А';
            case 'a': return 'а';
            case 'B': return 'В';
            case 'C': return 'С';
            case 'c': return 'с';
            case 'E': return 'Е';
            case 'e': return 'е';
            case 'H': return 'Н';
            case 'K': return 'К';
            case 'k': return 'к';
            case 'M': return 'М';
            case 'O': return 'О';
            case 'o': return 'о';
            case 'P': return 'Р';
            case 'p': return 'р';
            case 'T': return 'Т';
            case 'X': return 'Х';
            case 'x': return 'х';
            case 'Y': return 'У';
            case 'y': return 'у';
            default: return character;
        }
    }

    private static bool IsCyrillicHomoglyph(char character)
    {
        switch (character)
        {
            case 'А':
            case 'а':
            case 'В':
            case 'С':
            case 'с':
            case 'Е':
            case 'е':
            case 'Н':
            case 'К':
            case 'к':
            case 'М':
            case 'О':
            case 'о':
            case 'Р':
            case 'р':
            case 'Т':
            case 'Х':
            case 'х':
            case 'У':
            case 'у':
                return true;
            default:
                return false;
        }
    }
}
