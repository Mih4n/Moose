using System.Globalization;
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

    readonly IFontLocator locator;
    readonly IFontRasterizer rasterizer;
    readonly string family;
    readonly bool ligatures;
    // the configured font first, then the configured fallbacks, then fonts fontconfig found for missing characters
    readonly List<FontFile> fonts = [];
    readonly HashSet<string> unusable = [];
    readonly Dictionary<int, int> fallbackFonts = [];
    readonly Dictionary<(float PhysicalSize, Rune Rune), (GlyphBitmap Bitmap, Rect Uv)> glyphs = [];
    readonly Dictionary<(int Font, float PhysicalSize, uint Glyph), (GlyphBitmap Bitmap, Rect Uv)> shapedGlyphs = [];
    readonly Dictionary<(float PhysicalSize, Rune Rune), (GlyphBitmap Bitmap, Rect Uv)> cellGlyphs = [];
    readonly Dictionary<float, Vector2> cellSizes = [];

    public GlyphAtlas Atlas { get; } = new(1024);
    public ImageRegistry Images { get; } = new();
    public Vector2 Scale { get; set; } = Vector2.One;
    public float DefaultSize { get; }

    public TextSystem(FontOptions font, IFontLocator locator, IFontRasterizer rasterizer)
    {
        this.locator = locator;
        this.rasterizer = rasterizer;
        family = font.Family;
        var path = locator.Find(font.Family) ?? locator.Find("monospace") ?? throw new FileNotFoundException($"No font file found for family '{font.Family}' or 'monospace'.");
        DefaultSize = font.Size;
        ligatures = font.Ligatures;
        fonts.Add(new FontFile(path, rasterizer));
        foreach (var fallback in font.Fallback ?? [])
            if (locator.Find(fallback) is { } fallbackPath && fonts.All(candidate => candidate.Path != fallbackPath) && Open(fallbackPath) is { } file)
                fonts.Add(file);
    }

    public Vector2 Measure(string text, float size)
    {
        var physicalSize = PhysicalSize(size);
        var face = Face(physicalSize);
        var codepoints = Codepoints(text);
        var width = 0f;
        foreach (var run in Runs(codepoints))
            foreach (var glyph in fonts[run.Font].Shaper.Shape(codepoints.AsSpan(run.Start, run.Length), physicalSize))
                width += glyph.Advance;
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
        var codepoints = Codepoints(text);
        foreach (var run in Runs(codepoints))
            foreach (var shaped in fonts[run.Font].Shaper.Shape(codepoints.AsSpan(run.Start, run.Length), physicalSize))
            {
                var glyph = ShapedGlyph(run.Font, physicalSize, shaped.Glyph);
                AddGlyph(scene, glyph.Bitmap, glyph.Uv, MathF.Round(pen + shaped.OffsetX), baseline - MathF.Round(shaped.OffsetY), color);
                pen += shaped.Advance;
            }
    }

    public void PaintRune(List<Quad> scene, Rune rune, float size, Vector2 origin, Color color) => PaintCells(scene, [rune.Value], size, origin, color);

    /// Paints one terminal cell per codepoint starting at origin, shaped as a run so ligatures form.
    /// Each glyph lands in the cell of its cluster, which keeps ligature fonts (designed as cell-wide pieces) on the grid;
    /// box drawing and powerline symbols break the run and are drawn procedurally to fill their cell,
    /// and characters the font lacks are shaped with a fallback font in runs of their own.
    public void PaintCells(List<Quad> scene, ReadOnlySpan<int> codepoints, float size, Vector2 origin, Color color)
    {
        var physicalSize = PhysicalSize(size);
        var face = Face(physicalSize);
        var cellWidth = PhysicalCellSize(face, physicalSize).X;
        var left = MathF.Round(origin.X * Scale.X);
        var top = MathF.Round(origin.Y * Scale.Y);
        var start = 0;
        for (var index = 0; index <= codepoints.Length; index++)
        {
            if (index < codepoints.Length && !BoxDrawingRasterizer.Handles(new Rune(codepoints[index]))) continue;
            if (index > start) PaintShapedCells(scene, codepoints[start..index], physicalSize, left + start * cellWidth, top + face.Metrics.Ascent, cellWidth, color);
            if (index < codepoints.Length)
            {
                var cellGlyph = CellGlyph(face, physicalSize, new Rune(codepoints[index]));
                AddGlyph(scene, cellGlyph.Bitmap, cellGlyph.Uv, left + index * cellWidth, top, color);
            }
            start = index + 1;
        }
    }

    void PaintShapedCells(List<Quad> scene, ReadOnlySpan<int> codepoints, float physicalSize, float left, float baseline, float cellWidth, Color color)
    {
        foreach (var run in Runs(codepoints))
            foreach (var shaped in fonts[run.Font].Shaper.Shape(codepoints.Slice(run.Start, run.Length), physicalSize, ligatures))
            {
                var glyph = ShapedGlyph(run.Font, physicalSize, shaped.Glyph);
                AddGlyph(scene, glyph.Bitmap, glyph.Uv, left + (run.Start + shaped.Cluster) * cellWidth + MathF.Round(shaped.OffsetX), baseline - MathF.Round(shaped.OffsetY), color);
            }
    }

    /// Splits codepoints into runs that share a font; marks and joiners stay with the character they modify.
    List<(int Start, int Length, int Font)> Runs(ReadOnlySpan<int> codepoints)
    {
        var runs = new List<(int Start, int Length, int Font)>();
        var start = 0;
        var current = -1;
        for (var index = 0; index < codepoints.Length; index++)
        {
            var font = current >= 0 && Modifies(codepoints[index]) ? current : FontFor(codepoints[index]);
            if (font == current) continue;
            if (index > start) runs.Add((start, index - start, current));
            start = index;
            current = font;
        }
        if (codepoints.Length > start) runs.Add((start, codepoints.Length - start, current));
        return runs;
    }

    static bool Modifies(int codepoint) =>
        Rune.IsValid(codepoint) && Rune.GetUnicodeCategory(new Rune(codepoint)) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format;

    /// The first font with a glyph for the codepoint; the main font when none has one, so it draws its missing-glyph box.
    int FontFor(int codepoint)
    {
        if (fonts[0].Shaper.Covers(codepoint) || !Rune.IsValid(codepoint)) return 0;
        if (fallbackFonts.TryGetValue(codepoint, out var cached)) return cached;
        return fallbackFonts[codepoint] = FindFallback(new Rune(codepoint));
    }

    int FindFallback(Rune rune)
    {
        for (var index = 1; index < fonts.Count; index++)
            if (fonts[index].Shaper.Covers(rune.Value)) return index;
        foreach (var path in locator.FindFallbacks(family, rune))
        {
            if (fonts.Any(font => font.Path == path) || Open(path) is not { } font) continue;
            if (!font.Shaper.Covers(rune.Value))
            {
                font.Dispose();
                continue;
            }
            fonts.Add(font);
            return fonts.Count - 1;
        }
        return 0;
    }

    /// Opens a font file, or null when FreeType can't render it at arbitrary sizes (bitmap-only and color bitmap fonts).
    FontFile? Open(string path)
    {
        if (unusable.Contains(path)) return null;
        var font = new FontFile(path, rasterizer);
        try
        {
            font.Face(PhysicalSize(DefaultSize));
            return font;
        }
        catch (InvalidOperationException)
        {
            font.Dispose();
            unusable.Add(path);
            return null;
        }
    }

    static int[] Codepoints(string text) => [.. text.EnumerateRunes().Select(rune => rune.Value)];

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

    IFontFace Face(float physicalSize) => fonts[0].Face(physicalSize);

    (GlyphBitmap Bitmap, Rect Uv) Glyph(IFontFace face, float physicalSize, Rune rune)
    {
        if (glyphs.TryGetValue((physicalSize, rune), out var glyph)) return glyph;
        var bitmap = face.Rasterize(rune);
        var uv = bitmap.Width > 0 && bitmap.Height > 0 ? Atlas.Add(bitmap.Width, bitmap.Height, bitmap.Coverage) ?? default : default;
        return glyphs[(physicalSize, rune)] = (bitmap, uv);
    }

    (GlyphBitmap Bitmap, Rect Uv) ShapedGlyph(int font, float physicalSize, uint glyphIndex)
    {
        if (shapedGlyphs.TryGetValue((font, physicalSize, glyphIndex), out var glyph)) return glyph;
        var bitmap = fonts[font].Face(physicalSize).RasterizeGlyph(glyphIndex);
        var uv = bitmap.Width > 0 && bitmap.Height > 0 ? Atlas.Add(bitmap.Width, bitmap.Height, bitmap.Coverage) ?? default : default;
        return shapedGlyphs[(font, physicalSize, glyphIndex)] = (bitmap, uv);
    }

    public void Dispose()
    {
        foreach (var font in fonts) font.Dispose();
    }
}
