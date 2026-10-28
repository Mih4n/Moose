using Moose.Graphics.Primitives;

namespace Moose.Graphics.Tests.Primitives;

public class ColorTests
{
    [Fact]
    public void Parse_SixDigitHexIsOpaque()
    {
        Assert.Equal(Color.Hex(0x3c3836), Color.Parse("#3c3836"));
    }

    [Fact]
    public void Parse_EightDigitHexCarriesAlpha()
    {
        Assert.Equal(Color.Hex(0x3c3836, 0x80 / 255f), Color.Parse("#3c383680"));
    }
}
