namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

internal static class BoxFinder
{
    public static List<OcrPoint[]> Find(float[] map, int width, int height, float threshold, float boxThreshold, double unclip)
    {
        var boxes = new List<OcrPoint[]>();
        bool[] visited = new bool[width * height];
        var stack = new Stack<int>();
        var component = new List<int>();

        for (int start = 0; start < map.Length; start++)
        {
            if (visited[start] || map[start] < threshold)
                continue;

            component.Clear();
            stack.Push(start);
            visited[start] = true;
            while (stack.Count > 0)
            {
                int index = stack.Pop();
                component.Add(index);
                int x = index % width;
                int y = index / width;
                TryPush(map, visited, stack, width, height, threshold, x - 1, y);
                TryPush(map, visited, stack, width, height, threshold, x + 1, y);
                TryPush(map, visited, stack, width, height, threshold, x, y - 1);
                TryPush(map, visited, stack, width, height, threshold, x, y + 1);
            }

            if (component.Count < 8)
                continue;

            var boundary = new List<OcrPoint>();
            int step = Math.Max(1, component.Count / 1500);
            for (int i = 0; i < component.Count; i += step)
            {
                int index = component[i];
                int x = index % width;
                int y = index / width;
                if (!IsBoundary(map, width, height, threshold, x, y))
                    continue;
                boundary.Add(new OcrPoint(x, y));
            }

            if (boundary.Count < 4)
                continue;

            OcrPoint[] rect = MinAreaRect(boundary);
            if (rect.Length < 4)
                continue;

            if (MeanScore(map, width, height, rect) < boxThreshold)
                continue;

            boxes.Add(Expand(rect, unclip, 4.2));
            if (boxes.Count >= 400)
                break;
        }

        return boxes;
    }

    private static void TryPush(float[] map, bool[] visited, Stack<int> stack, int width, int height, float threshold, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;

        int index = y * width + x;
        if (visited[index] || map[index] < threshold)
            return;

        visited[index] = true;
        stack.Push(index);
    }

    private static bool IsBoundary(float[] map, int width, int height, float threshold, int x, int y)
    {
        return x == 0 || y == 0 || x == width - 1 || y == height - 1
            || map[y * width + (x - 1)] < threshold
            || map[y * width + (x + 1)] < threshold
            || map[(y - 1) * width + x] < threshold
            || map[(y + 1) * width + x] < threshold;
    }

    private static float MeanScore(float[] map, int width, int height, OcrPoint[] quad)
    {
        double minX = quad.Min(point => point.X);
        double maxX = quad.Max(point => point.X);
        double minY = quad.Min(point => point.Y);
        double maxY = quad.Max(point => point.Y);
        int left = Math.Max(0, (int)minX);
        int right = Math.Min(width - 1, (int)Math.Ceiling(maxX));
        int top = Math.Max(0, (int)minY);
        int bottom = Math.Min(height - 1, (int)Math.Ceiling(maxY));
        double sum = 0;
        int count = 0;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (!QuadMath.Contains(quad, new OcrPoint(x, y)))
                    continue;
                sum += map[y * width + x];
                count++;
            }
        }

        return count == 0 ? 0 : (float)(sum / count);
    }

    private static OcrPoint[] Expand(OcrPoint[] quad, double horizontal, double vertical)
    {
        double cx = quad.Average(point => point.X);
        double cy = quad.Average(point => point.Y);
        var expanded = new OcrPoint[quad.Length];
        for (int i = 0; i < quad.Length; i++)
        {
            expanded[i] = new OcrPoint(
                cx + (quad[i].X - cx) * horizontal,
                cy + (quad[i].Y - cy) * vertical);
        }

        return expanded;
    }

    private static OcrPoint[] MinAreaRect(IReadOnlyList<OcrPoint> points)
    {
        OcrPoint[] hull = ConvexHull(points);
        if (hull.Length < 3)
            return Array.Empty<OcrPoint>();

        double bestArea = double.MaxValue;
        OcrPoint[] best = Array.Empty<OcrPoint>();
        for (int i = 0; i < hull.Length; i++)
        {
            OcrPoint a = hull[i];
            OcrPoint b = hull[(i + 1) % hull.Length];
            double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
            double cosine = Math.Cos(-angle);
            double sine = Math.Sin(-angle);
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (OcrPoint point in hull)
            {
                double x = point.X * cosine - point.Y * sine;
                double y = point.X * sine + point.Y * cosine;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            double area = (maxX - minX) * (maxY - minY);
            if (area >= bestArea || area <= 1)
                continue;

            bestArea = area;
            double undoCos = Math.Cos(angle);
            double undoSin = Math.Sin(angle);
            var corners = new[]
            {
                Unrotate(minX, minY, undoCos, undoSin),
                Unrotate(maxX, minY, undoCos, undoSin),
                Unrotate(maxX, maxY, undoCos, undoSin),
                Unrotate(minX, maxY, undoCos, undoSin),
            };
            best = QuadMath.OrderCorners(corners);
        }

        return best;
    }

    private static OcrPoint Unrotate(double x, double y, double cosine, double sine)
    {
        return new OcrPoint(x * cosine - y * sine, x * sine + y * cosine);
    }

    private static OcrPoint[] ConvexHull(IReadOnlyList<OcrPoint> points)
    {
        OcrPoint[] ordered = points
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToArray();
        if (ordered.Length <= 2)
            return ordered;

        var lower = new List<OcrPoint>();
        foreach (OcrPoint point in ordered)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }

        var upper = new List<OcrPoint>();
        for (int i = ordered.Length - 1; i >= 0; i--)
        {
            OcrPoint point = ordered[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower.ToArray();
    }

    private static double Cross(OcrPoint a, OcrPoint b, OcrPoint c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }
}
