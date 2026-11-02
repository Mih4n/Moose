using System.Numerics;
using Moose.Graphics.Primitives;

namespace Moose.Graphics.Rendering;

internal static class PixelSnap
{
    const float ThinLine = 4;

    public static Quad Apply(Quad quad, Vector2 scale)
    {
        if (quad.Uv.W > 0) return quad;
        if (quad.BorderWidth > 0) quad = quad with { BorderWidth = Thickness(quad.BorderWidth, scale.X) };
        if (quad.Radius > 0) return quad;
        var (x, width) = Span(quad.Rect.X, quad.Rect.W, scale.X);
        var (y, height) = Span(quad.Rect.Y, quad.Rect.H, scale.Y);
        return quad with { Rect = new Rect(x, y, width, height) };
    }

    static (float Start, float Length) Span(float start, float length, float scale)
    {
        if (length <= 0) return (start, length);
        var first = MathF.Round(start * scale);
        var size = length * scale;
        var last = size < ThinLine ? first + Math.Max(1, MathF.Round(size)) : MathF.Round((start + length) * scale);
        return (first / scale, (last - first) / scale);
    }

    static float Thickness(float width, float scale) => Math.Max(1, MathF.Round(width * scale)) / scale;
}
