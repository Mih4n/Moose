using System.Text;

namespace Moose.Graphics.Fonts;

public interface IFontLocator
{
    string? Find(string family);

    /// Font files likely to have a glyph for the rune, best match first, staying close to the family where possible.
    IEnumerable<string> FindFallbacks(string family, Rune rune);
}
