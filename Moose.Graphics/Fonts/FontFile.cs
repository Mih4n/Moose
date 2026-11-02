using Moose.Graphics.Fonts.HarfBuzz;

namespace Moose.Graphics.Fonts;

/// One font file with its shaper and a rasterized face per size.
internal sealed class FontFile(string path, IFontRasterizer rasterizer) : IDisposable
{
    readonly Dictionary<float, IFontFace> faces = [];

    public string Path => path;
    public TextShaper Shaper { get; } = new(path);

    public IFontFace Face(float physicalSize)
    {
        if (!faces.TryGetValue(physicalSize, out var face)) faces[physicalSize] = face = rasterizer.Load(path, physicalSize);
        return face;
    }

    public void Dispose()
    {
        foreach (var face in faces.Values) face.Dispose();
        Shaper.Dispose();
    }
}
