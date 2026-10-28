using Moose.Graphics.Primitives;

namespace Moose.Graphics.Rendering;

internal sealed class GlyphAtlas(int size)
{
    int shelfX;
    int shelfY;
    int shelfHeight;

    public int Size => size;
    public byte[] Pixels { get; } = new byte[size * size];
    public bool Dirty { get; set; } = true;

    public Rect? Add(int width, int height, ReadOnlySpan<byte> coverage)
    {
        if (shelfX + width > size)
        {
            shelfY += shelfHeight + 1;
            shelfX = 0;
            shelfHeight = 0;
        }
        if (width > size || shelfY + height > size) return null;

        for (var row = 0; row < height; row++)
            coverage.Slice(row * width, width).CopyTo(Pixels.AsSpan((shelfY + row) * size + shelfX));
        var uv = new Rect((float)shelfX / size, (float)shelfY / size, (float)width / size, (float)height / size);
        shelfX += width + 1;
        shelfHeight = Math.Max(shelfHeight, height);
        Dirty = true;
        return uv;
    }
}
