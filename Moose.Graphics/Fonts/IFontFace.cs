using System.Text;

namespace Moose.Graphics.Fonts;

public interface IFontFace : IDisposable
{
    FontMetrics Metrics { get; }

    GlyphBitmap Rasterize(Rune rune);
}
