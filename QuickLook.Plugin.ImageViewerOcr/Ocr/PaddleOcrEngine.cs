using System.Runtime.InteropServices;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal sealed class PaddleOcrEngine : IOcrEngine
{
    private const int LimitSide = 960;
    private const int MinTextSide = 11;
    private static readonly float[] DetMean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] DetStd = { 0.229f, 0.224f, 0.225f };
    private static readonly float[] RecMean = { 0.5f, 0.5f, 0.5f };
    private static readonly float[] RecStd = { 0.5f, 0.5f, 0.5f };

    private readonly object _gate = new object();
    private InferenceSession? _detection;
    private InferenceSession? _slavic;
    private string[]? _slavicDict;
    private bool _warmupAttempted;

    public static PaddleOcrEngine Shared { get; } = new PaddleOcrEngine();

    public string Id => "paddle";

    public bool IsAvailable
    {
        get
        {
            string root = ModelRoot;
            return File.Exists(Path.Combine(root, "det.onnx"))
                && File.Exists(Path.Combine(root, "rec_eslav.onnx"))
                && File.Exists(Path.Combine(root, "dict_eslav.txt"));
        }
    }

    public OcrPage Recognize(string path, CancellationToken cancellationToken)
    {
        EnsureLoaded();
        cancellationToken.ThrowIfCancellationRequested();
        RgbImage image = RgbImage.LoadScaled(path, LimitSide, out int sourceWidth, out int sourceHeight);
        Matrix detection = Run(_detection!, image, DetMean, DetStd, 0);
        float[] map = ToProbabilityMap(detection);
        List<OcrPoint[]> boxes = BoxFinder.Find(map, detection.Columns, detection.Rows, 0.3f, 0.5f, 1.5);
        double scaleX = sourceWidth / (double)detection.Columns;
        double scaleY = sourceHeight / (double)detection.Rows;
        double imageScaleX = image.Width / (double)detection.Columns;
        double imageScaleY = image.Height / (double)detection.Rows;
        var prepared = new List<PreparedLine>();
        foreach (OcrPoint[] box in boxes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OcrPoint[] sourceQuad = Scale(box, scaleX, scaleY);
            if (ShortSide(sourceQuad) < MinTextSide)
                continue;

            RgbImage sampled = image.SampleQuad(Scale(box, imageScaleX, imageScaleY));
            RgbImage crop = sampled.FocusLine();
            int width = Math.Max(8, (int)Math.Ceiling(48d * crop.Width / Math.Max(1, crop.Height)));
            width = Math.Min(1280, Math.Max(8, width / 8 * 8));
            int widerWidth = Math.Min(1280, Math.Max(width + 8, (int)(width * 1.3) / 8 * 8));
            var item = new PreparedLine
            {
                Crop = crop,
                Sampled = sampled,
                Sized = FitRecognition(crop, width),
                Display = sourceQuad,
                MapRight = 1,
                MapBottom = 1,
                RecWidth = width,
            };
            if (BucketWidth(widerWidth) != item.Sized.Width)
                item.Wider = FitRecognition(crop, widerWidth);
            bool horizontal = sampled.TryInkSpan(out double left, out double top, out double right, out double bottom);
            bool vertical = sampled.TryTextBand(out double bandTop, out double bandBottom);
            if (horizontal || vertical)
            {
                if (!horizontal)
                {
                    left = 0;
                    right = 1;
                }

                if (vertical)
                {
                    top = bandTop;
                    bottom = bandBottom;
                }

                item.Display = QuadMath.Inset(sourceQuad, left, top, right, bottom);
                item.MapLeft = left;
                item.MapRight = right;
                item.MapTop = top;
                item.MapBottom = bottom;
            }

            prepared.Add(item);
        }

        foreach (IGrouping<int, PreparedLine> group in prepared.GroupBy(item => item.Sized.Width))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreparedLine[] items = group.ToArray();
            Matrix[] slavic = RunMany(_slavic!, items.Select(item => item.Sized).ToArray(), RecMean, RecStd, 48);
            for (int i = 0; i < items.Length; i++)
                items[i].Slavic = slavic[i];
        }

        foreach (PreparedLine item in prepared)
        {
            (string first, float firstScore) = Decode(item.Slavic, _slavicDict!);
            if (IsPlainNumber(first))
            {
                int twice = Math.Min(1280, Math.Max(item.RecWidth + 8, item.RecWidth * 2 / 8 * 8));
                if (BucketWidth(twice) != item.Sized.Width)
                {
                    item.Wider = FitRecognition(item.Crop, twice);
                    item.NeedsWider = true;
                    continue;
                }
            }

            item.NeedsWider = item.Wider != null && NeedsAnotherLook(first, firstScore);
        }

        foreach (IGrouping<int, PreparedLine> group in prepared.Where(item => item.NeedsWider).GroupBy(item => item.Wider!.Width))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreparedLine[] items = group.ToArray();
            Matrix[] wider = RunMany(_slavic!, items.Select(item => item.Wider!).ToArray(), RecMean, RecStd, 48);
            for (int i = 0; i < items.Length; i++)
                items[i].SlavicWide = wider[i];
        }

        var lines = new List<OcrLine>();
        foreach (PreparedLine item in prepared)
        {
            (string text, float confidence) = FinishLine(item);
            if (string.IsNullOrWhiteSpace(text) || confidence < 0.35f || BoxTooWide(item.Display, text))
                continue;

            OcrLine line = TextLayout.LineFromText(text, item.Display, (start, end) =>
                TrimWord(item.Sampled, item.MapLeft, item.MapRight, item.MapTop, item.MapBottom, start, end));
            if (line.Words.Count > 0)
                lines.Add(line);
        }

        lines.Sort(CompareLines);
        return new OcrPage
        {
            ImageWidth = sourceWidth,
            ImageHeight = sourceHeight,
            Lines = lines,
        };
    }

    private (string Text, float Score) SlavicText(PreparedLine item)
    {
        (string slavic, float slavicScore) = Decode(item.Slavic, _slavicDict!);
        if (!item.NeedsWider)
            return (slavic, slavicScore);

        (string wider, float widerScore) = Decode(item.SlavicWide, _slavicDict!);
        if (widerScore + 0.12f < slavicScore || !KeepsLongLatin(slavic, wider))
            return (slavic, slavicScore);
        if (DistinctCount(wider) > DistinctCount(slavic))
            return (wider, widerScore);
        if (SameDigits(wider, slavic) && slavic.Contains(".") && !wider.Contains("."))
            return (slavic, slavicScore);
        if (SameDigits(wider, slavic) && wider.Contains(".") && !slavic.Contains(".") && widerScore + 0.08f >= slavicScore)
            return (wider, widerScore);
        if (DigitCount(wider) > DigitCount(slavic) || (DigitCount(slavic) >= 4 && widerScore >= slavicScore))
            return (wider, widerScore);

        return (slavic, slavicScore);
    }

    private static bool NeedsAnotherLook(string text, float score)
    {
        return score < 0.55f || HasBrokenNumber(text) || DigitCount(text) >= 4;
    }

    private static string NormalizeDates(string text)
    {
        if (text.Length < 10)
            return text;

        char[] chars = text.ToCharArray();
        for (int i = 0; i <= chars.Length - 10; i++)
        {
            if (!IsDateShape(chars, i))
                continue;
            chars[i + 4] = '-';
            chars[i + 7] = '-';
            i += 9;
        }

        return new string(chars);
    }

    private static bool IsDateShape(char[] chars, int i)
    {
        bool bounded = (i == 0 || !char.IsDigit(chars[i - 1]))
            && (i + 10 >= chars.Length || !char.IsDigit(chars[i + 10]));
        return bounded
            && FourDigits(chars, i)
            && IsDateSeparator(chars[i + 4])
            && TwoDigits(chars, i + 5)
            && IsDateSeparator(chars[i + 7])
            && TwoDigits(chars, i + 8);
    }

    private static bool FourDigits(char[] chars, int i)
    {
        return char.IsDigit(chars[i]) && char.IsDigit(chars[i + 1]) && char.IsDigit(chars[i + 2]) && char.IsDigit(chars[i + 3]);
    }

    private static bool TwoDigits(char[] chars, int i)
    {
        return char.IsDigit(chars[i]) && char.IsDigit(chars[i + 1]);
    }

    private static bool IsDateSeparator(char character)
    {
        return character == '-' || character == '.' || character == '/';
    }

    private static bool IsPlainNumber(string text)
    {
        if (DigitCount(text) < 3)
            return false;

        foreach (char character in text)
        {
            if (char.IsLetter(character))
                return false;
        }

        return true;
    }

    private static bool SameDigits(string left, string right)
    {
        return DigitsOf(left) == DigitsOf(right) && DigitsOf(left).Length > 0;
    }

    private static string DigitsOf(string text)
    {
        var digits = new StringBuilder();
        foreach (char character in text)
        {
            if (char.IsDigit(character))
                digits.Append(character);
        }

        return digits.ToString();
    }

    private static bool HasBrokenNumber(string text)
    {
        int tokenStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && !char.IsWhiteSpace(text[i]))
                continue;
            if (i > tokenStart && BrokenNumber(text, tokenStart, i))
                return true;
            tokenStart = i + 1;
        }

        return false;
    }

    private static int DigitCount(string text)
    {
        int count = 0;
        foreach (char character in text)
        {
            if (char.IsDigit(character))
                count++;
        }

        return count;
    }

    private (string Text, float Confidence) FinishLine(PreparedLine item)
    {
        (string text, float score) = SlavicText(item);
        return (CaseText.Apply(NormalizeDates(text), item.Crop), score);
    }

    private static int DistinctCount(string text)
    {
        int count = 0;
        foreach (char character in text)
        {
            if (CaseText.ContainsDistinctCyrillic(character.ToString()))
                count++;
        }

        return count;
    }

    private static bool KeepsLongLatin(string natural, string wide)
    {
        int start = -1;
        for (int i = 0; i <= natural.Length; i++)
        {
            bool latin = i < natural.Length && IsLatinLetter(natural[i]);
            if (latin)
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0 && i - start >= 5 && !wide.Contains(natural.Substring(start, i - start)))
                return false;
            start = -1;
        }

        return true;
    }

    private static bool IsLatinLetter(char character)
    {
        return (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');
    }

    private static bool BrokenNumber(string text, int start, int end)
    {
        int weird = 0;
        int digits = 0;
        int others = 0;
        for (int i = start; i < end; i++)
        {
            char character = text[i];
            if (char.IsDigit(character))
                digits++;
            else if ("оОэЭзЗoO".IndexOf(character) >= 0)
                weird++;
            else if (char.IsLetter(character))
                others++;
        }

        return others == 0 && weird > 0 && digits + weird >= 4;
    }

    private static float[] ToProbabilityMap(Matrix detection)
    {
        bool logits = false;
        foreach (float value in detection.Values)
        {
            if (value < -0.1f || value > 1.2f)
            {
                logits = true;
                break;
            }
        }

        if (!logits)
            return detection.Values;

        float[] map = new float[detection.Values.Length];
        for (int i = 0; i < map.Length; i++)
            map[i] = 1f / (1f + (float)Math.Exp(-detection.Values[i]));
        return map;
    }

    private static (string Text, float Confidence) Decode(Matrix output, string[] dictionary)
    {
        int classes = output.Columns;
        int steps = output.Rows;
        string[] alphabet = BuildAlphabet(dictionary, classes);
        var chars = new StringBuilder();
        double score = 0;
        int chosen = 0;
        int previous = -1;
        var row = new float[classes];
        for (int step = 0; step < steps; step++)
        {
            int best = 0;
            float bestValue = float.MinValue;
            double rawSum = 0;
            bool probabilities = true;
            for (int cls = 0; cls < classes; cls++)
            {
                float value = output.Values[step * classes + cls];
                row[cls] = value;
                rawSum += value;
                if (value < -0.01f || value > 1.01f)
                    probabilities = false;
                if (value > bestValue)
                {
                    bestValue = value;
                    best = cls;
                }
            }

            if (best == previous)
                continue;
            previous = best;
            if (best <= 0 || best >= alphabet.Length)
                continue;

            string token = alphabet[best];
            if (token.Length == 0)
                continue;

            chars.Append(token);
            bool normalized = probabilities && rawSum > 0.5 && rawSum < 1.5;
            score += normalized ? bestValue : Softmax(row, best);
            chosen++;
        }

        float confidence = chosen == 0 ? 0 : (float)(score / chosen);
        return (chars.ToString(), confidence);
    }

    private static string[] BuildAlphabet(string[] dictionary, int classes)
    {
        var alphabet = new List<string> { "" };
        alphabet.AddRange(dictionary);
        while (alphabet.Count < classes)
            alphabet.Add(" ");
        return alphabet.ToArray();
    }

    private static Matrix Run(InferenceSession session, RgbImage image, float[] mean, float[] std, int expectedHeight)
    {
        return RunMany(session, new[] { image }, mean, std, expectedHeight)[0];
    }

    private static Matrix[] RunMany(InferenceSession session, RgbImage[] images, float[] mean, float[] std, int expectedHeight)
    {
        if (images.Length == 0)
            return Array.Empty<Matrix>();

        string inputName = session.InputMetadata.Keys.First();
        int[] dims = session.InputMetadata[inputName].Dimensions.ToArray();
        bool batchable = dims.Length == 4 && dims[0] <= 0;
        const int chunkSize = 8;
        var matrices = new List<Matrix>(images.Length);
        for (int offset = 0; offset < images.Length; offset += chunkSize)
        {
            int count = Math.Min(chunkSize, images.Length - offset);
            var chunk = new RgbImage[count];
            Array.Copy(images, offset, chunk, 0, count);
            if (batchable && count > 1)
                matrices.AddRange(RunChunk(session, inputName, dims, chunk, mean, std, expectedHeight));
            else
            {
                foreach (RgbImage image in chunk)
                    matrices.Add(RunSingle(session, inputName, dims, image, mean, std, expectedHeight));
            }
        }

        return matrices.ToArray();
    }

    private static Matrix[] RunChunk(InferenceSession session, string inputName, int[] dims, RgbImage[] images, float[] mean, float[] std, int expectedHeight)
    {
        bool channelsFirst = dims.Length < 4 || dims[1] == 3 || dims[3] != 3;
        RgbImage[] prepared = new RgbImage[images.Length];
        for (int i = 0; i < images.Length; i++)
            prepared[i] = FitInput(images[i], dims, channelsFirst, expectedHeight);

        int plane = prepared[0].Width * prepared[0].Height;
        float[] data = new float[images.Length * 3 * plane];
        for (int i = 0; i < prepared.Length; i++)
        {
            float[] one = channelsFirst ? ToNchw(prepared[i], mean, std) : ToNhwc(prepared[i], mean, std);
            Array.Copy(one, 0, data, i * one.Length, one.Length);
        }

        int[] shape = channelsFirst
            ? new[] { images.Length, 3, prepared[0].Height, prepared[0].Width }
            : new[] { images.Length, prepared[0].Height, prepared[0].Width, 3 };
        return Split(Execute(session, inputName, data, shape), images.Length);
    }

    private static Matrix RunSingle(InferenceSession session, string inputName, int[] dims, RgbImage image, float[] mean, float[] std, int expectedHeight)
    {
        bool channelsFirst = dims.Length < 4 || dims[1] == 3 || dims[3] != 3;
        RgbImage prepared = FitInput(image, dims, channelsFirst, expectedHeight);
        float[] data = channelsFirst ? ToNchw(prepared, mean, std) : ToNhwc(prepared, mean, std);
        int[] shape = channelsFirst
            ? new[] { 1, 3, prepared.Height, prepared.Width }
            : new[] { 1, prepared.Height, prepared.Width, 3 };
        return Split(Execute(session, inputName, data, shape), 1)[0];
    }

    private static RgbImage FitInput(RgbImage image, int[] dims, bool channelsFirst, int expectedHeight)
    {
        int width = image.Width;
        if (expectedHeight > 0 && dims.Length == 4)
        {
            int widthIndex = channelsFirst ? 3 : 2;
            if (dims[widthIndex] > 0)
                width = dims[widthIndex];
        }

        return width == image.Width ? image : image.Resize(width, image.Height);
    }

    private static Executed Execute(InferenceSession session, string inputName, float[] data, int[] shape)
    {
        var tensor = new DenseTensor<float>(data, shape);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);
        DenseTensor<float> output = results.First().AsTensor<float>().ToDenseTensor();
        float[] values = new float[output.Length];
        output.Buffer.Span.CopyTo(values);
        int[] dimensions = new int[output.Dimensions.Length];
        for (int i = 0; i < dimensions.Length; i++)
            dimensions[i] = output.Dimensions[i];
        return new Executed(values, dimensions);
    }

    private static Matrix[] Split(Executed output, int batch)
    {
        int columns = output.Dimensions[output.Dimensions.Length - 1];
        int rows = output.Dimensions.Length >= 2 ? output.Dimensions[output.Dimensions.Length - 2] : 1;
        int cell = rows * columns;
        if (cell == 0 || output.Values.Length != cell * batch)
            throw new InvalidOperationException("Recognition output does not match the batch.");

        var matrices = new Matrix[batch];
        for (int i = 0; i < batch; i++)
        {
            float[] slice = new float[cell];
            Array.Copy(output.Values, i * cell, slice, 0, cell);
            matrices[i] = new Matrix(slice, rows, columns);
        }

        return matrices;
    }

    private readonly struct Executed
    {
        public Executed(float[] values, int[] dimensions)
        {
            Values = values;
            Dimensions = dimensions;
        }

        public float[] Values { get; }

        public int[] Dimensions { get; }
    }

    private sealed class PreparedLine
    {
        public RgbImage Crop { get; set; } = null!;

        public RgbImage Sampled { get; set; } = null!;

        public RgbImage Sized { get; set; } = null!;

        public RgbImage? Wider { get; set; }

        public OcrPoint[] Display { get; set; } = null!;

        public double MapLeft { get; set; }

        public double MapRight { get; set; }

        public double MapTop { get; set; }

        public double MapBottom { get; set; }

        public Matrix Slavic { get; set; }

        public Matrix SlavicWide { get; set; }

        public bool NeedsWider { get; set; }

        public int RecWidth { get; set; }
    }

    private readonly struct Matrix
    {
        public Matrix(float[] values, int rows, int columns)
        {
            Values = values;
            Rows = rows;
            Columns = columns;
        }

        public float[] Values { get; }

        public int Rows { get; }

        public int Columns { get; }
    }

    public void Warmup()
    {
        try
        {
            EnsureLoaded();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("OCR warmup failed: " + ex.Message);
        }
    }

    private void EnsureLoaded()
    {
        if (_detection != null && _warmupAttempted)
            return;

        lock (_gate)
        {
            if (_detection == null)
            {
                if (!IsAvailable)
                    throw new FileNotFoundException("PaddleOCR models are missing.");

                LoadNative();
                string root = ModelRoot;
                string[] dictionary = LoadDict(Path.Combine(root, "dict_eslav.txt"));
                InferenceSession detection = Open(Path.Combine(root, "det.onnx"));
                InferenceSession slavic = Open(Path.Combine(root, "rec_eslav.onnx"));
                _slavicDict = dictionary;
                _detection = detection;
                _slavic = slavic;
            }

            if (_warmupAttempted)
                return;

            _warmupAttempted = true;
            try
            {
                Run(_detection, RgbImage.White(960, 640), DetMean, DetStd, 0);
                foreach (int width in new[] { 128, 192, 256, 320 })
                    Run(_slavic!, RgbImage.White(width, 48), RecMean, RecStd, 48);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OCR warmup run failed: " + ex.Message);
            }
        }
    }

    private static RgbImage FitRecognition(RgbImage crop, int width)
    {
        return crop.Resize(width, 48).PadWidth(BucketWidth(width));
    }

    private static int BucketWidth(int width)
    {
        int bucket = Math.Max(32, (width + 31) / 32 * 32);
        return Math.Min(1280, bucket);
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    private static void LoadNative()
    {
        string folder = Path.GetDirectoryName(typeof(PaddleOcrEngine).Assembly.Location) ?? "";
        LoadLibrary(Path.Combine(folder, "DirectML.dll"));
    }

    private static InferenceSession Open(string path)
    {
        try
        {
            var gpu = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            };
            gpu.AppendExecutionProvider_DML(0);
            return new InferenceSession(path, gpu);
        }
        catch (Exception)
        {
            var cpu = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                IntraOpNumThreads = Math.Max(4, Environment.ProcessorCount / 2),
                InterOpNumThreads = 1,
            };
            return new InferenceSession(path, cpu);
        }
    }

    private static string[] LoadDict(string path)
    {
        return File.ReadAllLines(path, Encoding.UTF8).Where(line => line.Length > 0).ToArray();
    }

    private static float[] ToNchw(RgbImage image, float[] mean, float[] std)
    {
        int plane = image.Width * image.Height;
        float[] data = new float[3 * plane];
        for (int index = 0; index < plane; index++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                int sourceChannel = channel == 0 ? 2 : channel == 2 ? 0 : 1;
                float value = image.Rgb[index * 3 + sourceChannel] / 255f;
                data[channel * plane + index] = (value - mean[channel]) / std[channel];
            }
        }

        return data;
    }

    private static float[] ToNhwc(RgbImage image, float[] mean, float[] std)
    {
        float[] data = new float[image.Rgb.Length];
        for (int index = 0; index < image.Width * image.Height; index++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                int sourceChannel = channel == 0 ? 2 : channel == 2 ? 0 : 1;
                float value = image.Rgb[index * 3 + sourceChannel] / 255f;
                data[index * 3 + channel] = (value - mean[channel]) / std[channel];
            }
        }

        return data;
    }

    private static double Softmax(float[] row, int chosen)
    {
        double max = row.Max();
        double sum = 0;
        foreach (float value in row)
            sum += Math.Exp(value - max);
        return Math.Exp(row[chosen] - max) / sum;
    }

    private static (double Start, double End) TrimWord(RgbImage image, double mapLeft, double mapRight, double mapTop, double mapBottom, double start, double end)
    {
        double span = Math.Max(1e-6, mapRight - mapLeft);
        double u0 = mapLeft + span * start;
        double u1 = mapLeft + span * end;
        if (!image.TryTrimSpan(u0, u1, mapTop, mapBottom, out double left, out double right))
            return (start, end);

        double trimmedStart = Math.Max(start, Math.Min(end, (left - mapLeft) / span));
        double trimmedEnd = Math.Max(trimmedStart, Math.Min(end, (right - mapLeft) / span));
        if (trimmedEnd - trimmedStart < 0.008)
            return (start, end);

        return (trimmedStart, trimmedEnd);
    }

    private static OcrPoint[] Scale(OcrPoint[] quad, double scaleX, double scaleY)
    {
        var scaled = new OcrPoint[quad.Length];
        for (int i = 0; i < quad.Length; i++)
            scaled[i] = new OcrPoint(quad[i].X * scaleX, quad[i].Y * scaleY);
        return scaled;
    }

    private static bool BoxTooWide(OcrPoint[] quad, string text)
    {
        double width = Math.Max(Distance(quad[0], quad[1]), Distance(quad[3], quad[2]));
        double height = Math.Max(Distance(quad[0], quad[3]), Distance(quad[1], quad[2]));
        if (height < 1 || text.Length == 0)
            return false;

        double limit = text.Length <= 2 ? 2.4 : Math.Max(3.5, text.Length * 1.15);
        return width > height * limit;
    }

    private static double ShortSide(OcrPoint[] quad)
    {
        double width = Math.Max(Distance(quad[0], quad[1]), Distance(quad[3], quad[2]));
        double height = Math.Max(Distance(quad[0], quad[3]), Distance(quad[1], quad[2]));
        return Math.Min(width, height);
    }

    private static double Distance(OcrPoint a, OcrPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int CompareLines(OcrLine left, OcrLine right)
    {
        double leftTop = left.Words.Count == 0 ? 0 : left.Words.Min(word => word.Quad.Min(point => point.Y));
        double rightTop = right.Words.Count == 0 ? 0 : right.Words.Min(word => word.Quad.Min(point => point.Y));
        int byTop = leftTop.CompareTo(rightTop);
        if (Math.Abs(leftTop - rightTop) > 8)
            return byTop;

        double leftX = left.Words.Count == 0 ? 0 : left.Words.Min(word => word.Quad.Min(point => point.X));
        double rightX = right.Words.Count == 0 ? 0 : right.Words.Min(word => word.Quad.Min(point => point.X));
        return leftX.CompareTo(rightX);
    }

    private static string ModelRoot
    {
        get
        {
            string directory = Path.GetDirectoryName(typeof(PaddleOcrEngine).Assembly.Location) ?? ".";
            return Path.Combine(directory, "models");
        }
    }

}
