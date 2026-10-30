using System.Numerics;
using System.Text;
using Moose.Core.Settings;
using Moose.Graphics.Elements;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.Graphics.Fonts;

internal sealed class TextSystem : IDisposable
{
    const float PixelsPerPoint = 96f / 72f;
    const string Ellipsis = "…";

    readonly IFontRasterizer rasterizer;
    readonly string path;
    readonly Dictionary<float, IFontFace> faces = [];
    readonly Dictionary<(float PhysicalSize, Rune Rune), (GlyphBitmap Bitmap, Rect Uv)> glyphs = [];
    readonly Dictionary<(float PhysicalSize, Rune Rune), (GlyphBitmap Bitmap, Rect Uv)> cellGlyphs = [];
    readonly Dictionary<float, Vector2> cellSizes = [];

    public GlyphAtlas Atlas { get; } = new(1024);
    public Vector2 Scale { get; set; } = Vector2.One;
    public float DefaultSize { get; }

    public TextSystem(FontOptions font, IFontLocator locator, IFontRasterizer rasterizer)
    {
        this.rasterizer = rasterizer;
        path = locator.Find(font.Family) ?? locator.Find("monospace") ?? throw new FileNotFoundException($"No font file found for family '{font.Family}' or 'monospace'.");
        DefaultSize = font.Size;
    }

    public Vector2 Measure(string text, float size)
    {
        var physicalSize = PhysicalSize(size);
        var face = Face(physicalSize);
        var width = 0f;
        foreach (var rune in text.EnumerateRunes()) width += Glyph(face, physicalSize, rune).Bitmap.Advance;
        return new Vector2(MathF.Ceiling(width) / Scale.X, MathF.Ceiling(face.Metrics.LineHeight) / Scale.Y);
    }

    public string Truncate(string text, float size, float maxWidth)
    {
        if (Measure(text, size).X <= maxWidth) return text;
        var runes = text.EnumerateRunes().ToList();
        while (runes.Count > 0)
        {
            runes.RemoveAt(runes.Count - 1);
            var candidate = string.Concat(runes) + Ellipsis;
            if (Measure(candidate, size).X <= maxWidth) return candidate;
        }
        return Measure(Ellipsis, size).X <= maxWidth ? Ellipsis : "";
    }

    public void Paint(List<Quad> scene, string text, float size, Vector2 origin, Color color)
    {
        var physicalSize = PhysicalSize(size);
        var face = Face(physicalSize);
        var pen = MathF.Round(origin.X * Scale.X);
        var baseline = MathF.Round(origin.Y * Scale.Y + face.Metrics.Ascent);
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = Glyph(face, physicalSize, rune);
            AddGlyph(scene, glyph.Bitmap, glyph.Uv, MathF.Round(pen), baseline, color);
            pen += glyph.Bitmap.Advance;
        }
    }

    public void PaintRune(List<Quad> scene, Rune rune, float size, Vector2 origin, Color color)
    {
        var physicalSize = PhysicalSize(size);
        var face = Face(physicalSize);
        var left = MathF.Round(origin.X * Scale.X);
        var top = MathF.Round(origin.Y * Scale.Y);
        if (BoxDrawingRasterizer.Handles(rune))
        {
            var cellGlyph = CellGlyph(face, physicalSize, rune);
            AddGlyph(scene, cellGlyph.Bitmap, cellGlyph.Uv, left, top, color);
            return;
        }
        var glyph = Glyph(face, physicalSize, rune);
        AddGlyph(scene, glyph.Bitmap, glyph.Uv, left, top + face.Metrics.Ascent, color);
    }

    public Vector2 CellSize(float size)
    {
        var physicalSize = PhysicalSize(size);
        return PhysicalCellSize(Face(physicalSize), physicalSize) / Scale;
    }

    float PhysicalSize(float points) => points * PixelsPerPoint * Scale.Y;

    Vector2 PhysicalCellSize(IFontFace face, float physicalSize)
    {
        if (cellSizes.TryGetValue(physicalSize, out var cellSize)) return cellSize;
        var advance = 0f;
        for (var codepoint = 0x20; codepoint < 0x7F; codepoint++) advance = MathF.Max(advance, MathF.Ceiling(Glyph(face, physicalSize, new Rune(codepoint)).Bitmap.Advance));
        return cellSizes[physicalSize] = new Vector2(advance, face.Metrics.LineHeight);
    }

    (GlyphBitmap Bitmap, Rect Uv) CellGlyph(IFontFace face, float physicalSize, Rune rune)
    {
        if (cellGlyphs.TryGetValue((physicalSize, rune), out var glyph)) return glyph;
        var cellSize = PhysicalCellSize(face, physicalSize);
        var bitmap = BoxDrawingRasterizer.Rasterize(rune, (int)cellSize.X, (int)cellSize.Y);
        return cellGlyphs[(physicalSize, rune)] = (bitmap, Atlas.Add(bitmap.Width, bitmap.Height, bitmap.Coverage) ?? default);
    }

    void AddGlyph(List<Quad> scene, GlyphBitmap bitmap, Rect uv, float penX, float baseline, Color color)
    {
        if (uv.W <= 0) return;
        var rect = new Rect((penX + bitmap.Left) / Scale.X, (baseline - bitmap.Top) / Scale.Y, bitmap.Width / Scale.X, bitmap.Height / Scale.Y);
        scene.Add(new Quad(rect, color, Color.Transparent, 0, 0, uv));
    }

    IFontFace Face(float physicalSize)
    {
        if (!faces.TryGetValue(physicalSize, out var face)) faces[physicalSize] = face = rasterizer.Load(path, physicalSize);
        return face;
    }

    (GlyphBitmap Bitmap, Rect Uv) Glyph(IFontFace face, float physicalSize, Rune rune)
    {
        if (glyphs.TryGetValue((physicalSize, rune), out var glyph)) return glyph;
        var bitmap = face.Rasterize(rune);
        var uv = bitmap.Width > 0 && bitmap.Height > 0 ? Atlas.Add(bitmap.Width, bitmap.Height, bitmap.Coverage) ?? default : default;
        return glyphs[(physicalSize, rune)] = (bitmap, uv);
    }

    public void Dispose()
    {
        foreach (var face in faces.Values) face.Dispose();
    }
}
