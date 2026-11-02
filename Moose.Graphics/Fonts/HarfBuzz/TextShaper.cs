using HarfBuzzSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace Moose.Graphics.Fonts.HarfBuzz;

internal sealed class TextShaper : IDisposable
{
    static readonly Feature[] WithoutLigatures = [Feature.Parse("-liga"), Feature.Parse("-calt"), Feature.Parse("-dlig")];

    readonly Blob blob;
    readonly Face face;
    readonly Dictionary<float, Font> fonts = [];
    Font? lookup;

    public TextShaper(string path)
    {
        blob = Blob.FromFile(path);
        face = new Face(blob, 0);
    }

    /// Whether the font has its own glyph for the codepoint, as opposed to .notdef.
    public bool Covers(int codepoint) => (lookup ??= new Font(face)).TryGetGlyph(codepoint, out var glyph) && glyph != 0;

    public ShapedGlyph[] Shape(string text, float size, bool ligatures = true)
    {
        using var buffer = new Buffer();
        buffer.AddUtf16(text);
        return Shape(buffer, size, ligatures);
    }

    public ShapedGlyph[] Shape(ReadOnlySpan<int> codepoints, float size, bool ligatures = true)
    {
        using var buffer = new Buffer();
        buffer.AddUtf32(codepoints);
        return Shape(buffer, size, ligatures);
    }

    ShapedGlyph[] Shape(Buffer buffer, float size, bool ligatures)
    {
        buffer.GuessSegmentProperties();
        Font(size).Shape(buffer, ligatures ? [] : WithoutLigatures);
        var infos = buffer.GetGlyphInfoSpan();
        var positions = buffer.GetGlyphPositionSpan();
        var glyphs = new ShapedGlyph[infos.Length];
        for (var index = 0; index < glyphs.Length; index++)
            glyphs[index] = new ShapedGlyph(infos[index].Codepoint, (int)infos[index].Cluster, positions[index].XAdvance / 64f, positions[index].XOffset / 64f, positions[index].YOffset / 64f);
        return glyphs;
    }

    Font Font(float size)
    {
        if (fonts.TryGetValue(size, out var font)) return font;
        font = new Font(face);
        var scale = (int)MathF.Round(size * 64);
        font.SetScale(scale, scale);
        return fonts[size] = font;
    }

    public void Dispose()
    {
        foreach (var font in fonts.Values) font.Dispose();
        lookup?.Dispose();
        face.Dispose();
        blob.Dispose();
    }
}
