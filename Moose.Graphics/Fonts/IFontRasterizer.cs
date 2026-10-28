namespace Moose.Graphics.Fonts;

public interface IFontRasterizer
{
    IFontFace Load(string path, float size);
}
