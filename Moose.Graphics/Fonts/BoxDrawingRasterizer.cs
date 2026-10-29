using System.Numerics;
using System.Text;

namespace Moose.Graphics.Fonts;

internal sealed class BoxDrawingRasterizer
{
    const int Samples = 4;
    // powerline separators overshoot the cell vertically so the rows at their flat edge are not mere slivers
    const float Overshoot = 1f;

    static readonly Dictionary<int, (int Up, int Right, int Down, int Left)> Lines = new()
    {
        [0x2500] = (0, 1, 0, 1),
        [0x2501] = (0, 2, 0, 2),
        [0x2502] = (1, 0, 1, 0),
        [0x2503] = (2, 0, 2, 0),
        [0x250C] = (0, 1, 1, 0),
        [0x250F] = (0, 2, 2, 0),
        [0x2510] = (0, 0, 1, 1),
        [0x2513] = (0, 0, 2, 2),
        [0x2514] = (1, 1, 0, 0),
        [0x2517] = (2, 2, 0, 0),
        [0x2518] = (1, 0, 0, 1),
        [0x251B] = (2, 0, 0, 2),
        [0x251C] = (1, 1, 1, 0),
        [0x2523] = (2, 2, 2, 0),
        [0x2524] = (1, 0, 1, 1),
        [0x252B] = (2, 0, 2, 2),
        [0x252C] = (0, 1, 1, 1),
        [0x2533] = (0, 2, 2, 2),
        [0x2534] = (1, 1, 0, 1),
        [0x253B] = (2, 2, 0, 2),
        [0x253C] = (1, 1, 1, 1),
        [0x254B] = (2, 2, 2, 2),
        [0x2574] = (0, 0, 0, 1),
        [0x2575] = (1, 0, 0, 0),
        [0x2576] = (0, 1, 0, 0),
        [0x2577] = (0, 0, 1, 0),
        [0x2578] = (0, 0, 0, 2),
        [0x2579] = (2, 0, 0, 0),
        [0x257A] = (0, 2, 0, 0),
        [0x257B] = (0, 0, 2, 0),
    };

    readonly int width;
    readonly int height;
    readonly int light;
    readonly byte[] coverage;

    BoxDrawingRasterizer(int width, int height)
    {
        this.width = width;
        this.height = height;
        light = Math.Max(1, (int)MathF.Round(width / 6f));
        coverage = new byte[width * height];
    }

    public static bool Handles(Rune rune) => rune.Value is >= 0x2580 and <= 0x259F or >= 0xE0B0 and <= 0xE0BF or >= 0x256D and <= 0x2570 || Lines.ContainsKey(rune.Value);

    public static GlyphBitmap Rasterize(Rune rune, int width, int height)
    {
        var rasterizer = new BoxDrawingRasterizer(width, height);
        rasterizer.Draw(rune.Value);
        return new GlyphBitmap(width, height, 0, 0, width, rasterizer.coverage);
    }

    void Draw(int codepoint)
    {
        var halfHeight = height / 2f;
        var reach = halfHeight + Overshoot;
        var thickness = (float)light;
        switch (codepoint)
        {
            case 0xE0B0:
                Sample(point => point.X <= width * (1 - MathF.Abs(point.Y - halfHeight) / reach));
                FillRect(0, 0, 1, height);
                break;
            case 0xE0B2:
                Sample(point => point.X >= width * MathF.Abs(point.Y - halfHeight) / reach);
                FillRect(width - 1, 0, width, height);
                break;
            case 0xE0B1:
                Sample(point => NearPolyline(point, thickness, new(0, -Overshoot), new(width, halfHeight), new(0, height + Overshoot)));
                break;
            case 0xE0B3:
                Sample(point => NearPolyline(point, thickness, new(width, -Overshoot), new(0, halfHeight), new(width, height + Overshoot)));
                break;
            case 0xE0B4:
                Sample(point => InEllipse(point, 0, halfHeight, width, reach));
                FillRect(0, 0, 1, height);
                break;
            case 0xE0B6:
                Sample(point => InEllipse(point, width, halfHeight, width, reach));
                FillRect(width - 1, 0, width, height);
                break;
            case 0xE0B5:
                Sample(point => InEllipse(point, 0, halfHeight, width, reach) && !InEllipse(point, 0, halfHeight, width - thickness, reach - thickness));
                break;
            case 0xE0B7:
                Sample(point => InEllipse(point, width, halfHeight, width, reach) && !InEllipse(point, width, halfHeight, width - thickness, reach - thickness));
                break;
            case 0xE0B8:
                Sample(point => point.X / width <= point.Y / height);
                break;
            case 0xE0BA:
                Sample(point => point.X / width >= 1 - point.Y / height);
                break;
            case 0xE0BC:
                Sample(point => point.X / width <= 1 - point.Y / height);
                break;
            case 0xE0BE:
                Sample(point => point.X / width >= point.Y / height);
                break;
            case 0xE0B9 or 0xE0BF:
                Sample(point => NearPolyline(point, thickness, new(0, 0), new(width, height)));
                break;
            case 0xE0BB or 0xE0BD:
                Sample(point => NearPolyline(point, thickness, new(0, height), new(width, 0)));
                break;
            case >= 0x256D and <= 0x2570:
                DrawArc(codepoint);
                break;
            case >= 0x2580 and <= 0x259F:
                DrawBlock(codepoint);
                break;
            default:
                DrawLines(Lines[codepoint]);
                break;
        }
    }

    void DrawLines((int Up, int Right, int Down, int Left) arms)
    {
        var vertical = light * Math.Max(arms.Up, arms.Down);
        var horizontal = light * Math.Max(arms.Left, arms.Right);
        if (arms.Left > 0) HorizontalArm(light * arms.Left, vertical, toRight: false);
        if (arms.Right > 0) HorizontalArm(light * arms.Right, vertical, toRight: true);
        if (arms.Up > 0) VerticalArm(light * arms.Up, horizontal, toBottom: false);
        if (arms.Down > 0) VerticalArm(light * arms.Down, horizontal, toBottom: true);
    }

    void HorizontalArm(int thickness, int crossing, bool toRight)
    {
        var cross = crossing > 0 ? crossing : thickness;
        var top = (height - thickness) / 2;
        var crossLeft = (width - cross) / 2;
        if (toRight) FillRect(crossLeft, top, width, top + thickness);
        else FillRect(0, top, crossLeft + cross, top + thickness);
    }

    void VerticalArm(int thickness, int crossing, bool toBottom)
    {
        var cross = crossing > 0 ? crossing : thickness;
        var left = (width - thickness) / 2;
        var crossTop = (height - cross) / 2;
        if (toBottom) FillRect(left, crossTop, left + thickness, height);
        else FillRect(left, 0, left + thickness, crossTop + cross);
    }

    void DrawArc(int codepoint)
    {
        var horizontalSign = codepoint is 0x256D or 0x2570 ? 1 : -1;
        var verticalSign = codepoint is 0x256D or 0x256E ? 1 : -1;
        var left = (width - light) / 2;
        var top = (height - light) / 2;
        var centerX = left + light / 2f;
        var centerY = top + light / 2f;
        var radius = Math.Min(width, height) / 2f;
        var arcX = centerX + horizontalSign * radius;
        var arcY = centerY + verticalSign * radius;
        Sample(point =>
            (point.X - arcX) * horizontalSign <= 0 && (point.Y - arcY) * verticalSign <= 0 && MathF.Abs(Vector2.Distance(point, new(arcX, arcY)) - radius) <= light / 2f
            || point.X >= left && point.X < left + light && (point.Y - arcY) * verticalSign >= 0
            || point.Y >= top && point.Y < top + light && (point.X - arcX) * horizontalSign >= 0);
    }

    void DrawBlock(int codepoint)
    {
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        switch (codepoint)
        {
            case 0x2580:
                FillRect(0, 0, width, halfHeight);
                break;
            case >= 0x2581 and <= 0x2588:
                FillRect(0, height - Eighths(height, codepoint - 0x2580), width, height);
                break;
            case >= 0x2589 and <= 0x258F:
                FillRect(0, 0, Eighths(width, 0x2590 - codepoint), height);
                break;
            case 0x2590:
                FillRect(halfWidth, 0, width, height);
                break;
            case >= 0x2591 and <= 0x2593:
                Array.Fill(coverage, (byte)(64 * (codepoint - 0x2590)));
                break;
            case 0x2594:
                FillRect(0, 0, width, Eighths(height, 1));
                break;
            case 0x2595:
                FillRect(width - Eighths(width, 1), 0, width, height);
                break;
            default:
                var quadrants = QuadrantMask(codepoint);
                if ((quadrants & 1) != 0) FillRect(0, 0, halfWidth, halfHeight);
                if ((quadrants & 2) != 0) FillRect(halfWidth, 0, width, halfHeight);
                if ((quadrants & 4) != 0) FillRect(0, halfHeight, halfWidth, height);
                if ((quadrants & 8) != 0) FillRect(halfWidth, halfHeight, width, height);
                break;
        }
    }

    static int QuadrantMask(int codepoint) => codepoint switch
    {
        0x2596 => 4,
        0x2597 => 8,
        0x2598 => 1,
        0x2599 => 1 | 4 | 8,
        0x259A => 1 | 8,
        0x259B => 1 | 2 | 4,
        0x259C => 1 | 2 | 8,
        0x259D => 2,
        0x259E => 2 | 4,
        _ => 2 | 4 | 8,
    };

    static int Eighths(int size, int eighths) => (int)MathF.Round(size * eighths / 8f);

    static bool InEllipse(Vector2 point, float centerX, float centerY, float radiusX, float radiusY)
    {
        var offsetX = (point.X - centerX) / radiusX;
        var offsetY = (point.Y - centerY) / radiusY;
        return offsetX * offsetX + offsetY * offsetY <= 1;
    }

    static bool NearPolyline(Vector2 point, float thickness, params ReadOnlySpan<Vector2> points)
    {
        for (var index = 1; index < points.Length; index++)
        {
            var start = points[index - 1];
            var segment = points[index] - start;
            var along = Math.Clamp(Vector2.Dot(point - start, segment) / segment.LengthSquared(), 0, 1);
            if (Vector2.Distance(point, start + along * segment) <= thickness / 2) return true;
        }
        return false;
    }

    void FillRect(int left, int top, int right, int bottom)
    {
        for (var row = Math.Max(0, top); row < Math.Min(height, bottom); row++)
            coverage.AsSpan(row * width + Math.Max(0, left), Math.Max(0, Math.Min(width, right) - Math.Max(0, left))).Fill(255);
    }

    void Sample(Func<Vector2, bool> inside)
    {
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var hits = 0;
            for (var sampleY = 0; sampleY < Samples; sampleY++)
            for (var sampleX = 0; sampleX < Samples; sampleX++)
                if (inside(new Vector2(column + (sampleX + 0.5f) / Samples, row + (sampleY + 0.5f) / Samples))) hits++;
            coverage[row * width + column] = (byte)Math.Max(coverage[row * width + column], hits * 255 / (Samples * Samples));
        }
    }
}
