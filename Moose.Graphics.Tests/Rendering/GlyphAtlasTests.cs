using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.Graphics.Tests.Rendering;

public class GlyphAtlasTests
{
    [Fact]
    public void Add_PacksGlyphsOnShelvesWithGutter()
    {
        var atlas = new GlyphAtlas(8) { Dirty = false };

        Assert.Equal(new Rect(0, 0, 0.5f, 0.25f), atlas.Add(4, 2, new byte[8]));
        Assert.True(atlas.Dirty);
        Assert.Equal(new Rect(5 / 8f, 0, 3 / 8f, 3 / 8f), atlas.Add(3, 3, [1, 2, 3, 4, 5, 6, 7, 8, 9]));
        Assert.Equal(1, atlas.Pixels[5]);
        Assert.Equal(9, atlas.Pixels[2 * 8 + 7]);
        Assert.Equal(new Rect(0, 0.5f, 0.25f, 0.25f), atlas.Add(2, 2, new byte[4]));
    }

    [Fact]
    public void Add_ReturnsNullWhenGlyphDoesNotFit()
    {
        var atlas = new GlyphAtlas(8);
        atlas.Add(3, 3, new byte[9]);

        Assert.Null(atlas.Add(9, 1, new byte[9]));
        Assert.Null(atlas.Add(2, 5, new byte[10]));
    }
}
