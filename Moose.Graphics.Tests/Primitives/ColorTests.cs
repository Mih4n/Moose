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

    [Fact]
    public void Hex_ExpandsShortForms()
    {
        Assert.Equal(Color.Hex("#ffaa00"), Color.Hex("fa0"));
        Assert.Equal(Color.Hex("#ffaa0088"), Color.Hex("#fa08"));
    }

    [Fact]
    public void Rgb_ReadsFunctionsAndBareLists()
    {
        Assert.Equal(Color.Hex(0x3c3836), Color.Rgb("rgb(60, 56, 54)"));
        Assert.Equal(Color.Hex(0x3c3836, 0.5f), Color.Rgb("rgba(60, 56, 54, 0.5)"));
        Assert.Equal(Color.Hex(0x3c3836), Color.Rgb("60,56,54"));
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("rgb(1, 2)")]
    [InlineData("hsl(1, 2, 3)")]
    [InlineData("#gggggg")]
    public void TryParse_RejectsMalformedColors(string text)
    {
        Assert.False(Color.TryParse(text, out _));
    }
}
