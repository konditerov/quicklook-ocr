namespace QuickLook.Plugin.ImageViewerOcr.Ocr;

public static class QuadMath
{
    public static double AngleDegrees(OcrPoint[] quad)
    {
        if (quad.Length < 2)
            return 0;

        double radians = Math.Atan2(quad[1].Y - quad[0].Y, quad[1].X - quad[0].X);
        return radians * 180d / Math.PI;
    }

    public static bool Contains(OcrPoint[] quad, OcrPoint point)
    {
        if (quad.Length < 3)
            return false;

        bool inside = false;
        for (int i = 0, j = quad.Length - 1; i < quad.Length; j = i++)
        {
            double yi = quad[i].Y;
            double yj = quad[j].Y;
            double xi = quad[i].X;
            double xj = quad[j].X;
            bool crosses = (yi > point.Y) != (yj > point.Y);
            if (!crosses)
                continue;

            double x = (xj - xi) * (point.Y - yi) / (yj - yi) + xi;
            if (point.X < x)
                inside = !inside;
        }

        return inside;
    }

    public static bool IntersectsRect(OcrPoint[] quad, double left, double top, double right, double bottom)
    {
        if (right < left)
            (left, right) = (right, left);
        if (bottom < top)
            (top, bottom) = (bottom, top);

        foreach (OcrPoint point in quad)
        {
            if (point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom)
                return true;
        }

        var corners = new[]
        {
            new OcrPoint(left, top),
            new OcrPoint(right, top),
            new OcrPoint(right, bottom),
            new OcrPoint(left, bottom),
        };
        foreach (OcrPoint corner in corners)
        {
            if (Contains(quad, corner))
                return true;
        }

        var rectEdges = new[]
        {
            (corners[0], corners[1]),
            (corners[1], corners[2]),
            (corners[2], corners[3]),
            (corners[3], corners[0]),
        };
        for (int i = 0; i < quad.Length; i++)
        {
            OcrPoint a = quad[i];
            OcrPoint b = quad[(i + 1) % quad.Length];
            foreach ((OcrPoint c, OcrPoint d) in rectEdges)
            {
                if (SegmentsCross(a, b, c, d))
                    return true;
            }
        }

        return false;
    }

    public static OcrPoint[] OrderCorners(IReadOnlyList<OcrPoint> points)
    {
        if (points.Count < 4)
            return points.ToArray();

        OcrPoint topLeft = points[0];
        OcrPoint bottomRight = points[0];
        OcrPoint topRight = points[0];
        OcrPoint bottomLeft = points[0];
        foreach (OcrPoint point in points)
        {
            if (point.X + point.Y < topLeft.X + topLeft.Y)
                topLeft = point;
            if (point.X + point.Y > bottomRight.X + bottomRight.Y)
                bottomRight = point;
            if (point.Y - point.X < topRight.Y - topRight.X)
                topRight = point;
            if (point.Y - point.X > bottomLeft.Y - bottomLeft.X)
                bottomLeft = point;
        }

        return new[] { topLeft, topRight, bottomRight, bottomLeft };
    }

    public static OcrPoint[] Inset(OcrPoint[] quad, double left, double top, double right, double bottom)
    {
        if (quad.Length < 4)
            return quad;

        return new[]
        {
            At(quad, left, top),
            At(quad, right, top),
            At(quad, right, bottom),
            At(quad, left, bottom),
        };
    }

    public static OcrPoint[] FromRect(double x, double y, double width, double height)
    {
        return new[]
        {
            new OcrPoint(x, y),
            new OcrPoint(x + width, y),
            new OcrPoint(x + width, y + height),
            new OcrPoint(x, y + height),
        };
    }

    private static OcrPoint At(OcrPoint[] quad, double x, double y)
    {
        OcrPoint top = Lerp(quad[0], quad[1], x);
        OcrPoint bottom = Lerp(quad[3], quad[2], x);
        return Lerp(top, bottom, y);
    }

    private static OcrPoint Lerp(OcrPoint a, OcrPoint b, double t)
    {
        return new OcrPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    private static bool SegmentsCross(OcrPoint a, OcrPoint b, OcrPoint c, OcrPoint d)
    {
        double d1 = Cross(c, d, a);
        double d2 = Cross(c, d, b);
        double d3 = Cross(a, b, c);
        double d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross(OcrPoint a, OcrPoint b, OcrPoint c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }
}
