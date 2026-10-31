using System.Text;

namespace Moose.Graphics.Fonts;

public interface IFontFace : IDisposable
{
    FontMetrics Metrics { get; }

    GlyphBitmap Rasterize(Rune rune);

    /// Renders a glyph by its index in the font, as produced by shaping.
    GlyphBitmap RasterizeGlyph(uint glyph);
}
