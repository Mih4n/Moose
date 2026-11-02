using System.Numerics;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.Graphics.Tests.Rendering;

public class PixelSnapTests
{
    static readonly Vector2 Scale = new(1.6583333f);

    static Quad Fill(Rect rect) => new(rect, new Color(1, 1, 1), Color.Transparent, 0, 0);

    static Rect Physical(Rect rect) => new(rect.X * Scale.X, rect.Y * Scale.Y, rect.W * Scale.X, rect.H * Scale.Y);

    [Theory]
    [InlineData(220f)]
    [InlineData(220.3f)]
    [InlineData(57.9f)]
    public void Apply_GivesOneLogicalPixelLinesTheSameWholeDeviceWidthAnywhere(float x)
    {
        var line = Physical(PixelSnap.Apply(Fill(new Rect(x, 0, 1, 500)), Scale).Rect);

        Assert.Equal(2, line.W, 0.001f);
        Assert.Equal(MathF.Round(line.X), line.X, 0.001f);
    }

    [Fact]
    public void Apply_NeverDropsAHairlineBelowOneDevicePixel()
    {
        var line = Physical(PixelSnap.Apply(Fill(new Rect(10, 10, 300, 0.25f)), Scale).Rect);

        Assert.Equal(1, line.H, 0.001f);
    }

    [Fact]
    public void Apply_SnapsBothEdgesOfLargeRectsSoNeighboursMeetWithoutGaps()
    {
        var left = Physical(PixelSnap.Apply(Fill(new Rect(0, 0, 220.4f, 36)), Scale).Rect);
        var right = Physical(PixelSnap.Apply(Fill(new Rect(220.4f, 0, 100, 36)), Scale).Rect);

        Assert.Equal(left.X + left.W, right.X, 0.001f);
    }

    [Fact]
    public void Apply_LeavesGlyphsAndRoundedGeometryAloneButRoundsBorders()
    {
        var glyph = new Quad(new Rect(3.3f, 4.4f, 5, 6), new Color(1, 1, 1), Color.Transparent, 0, 0, new Rect(0, 0, 0.1f, 0.1f));
        var panel = new Quad(new Rect(3.3f, 4.4f, 50, 60), new Color(1, 1, 1), new Color(1, 1, 1), 8, 1);

        var snappedPanel = PixelSnap.Apply(panel, Scale);

        Assert.Equal(glyph, PixelSnap.Apply(glyph, Scale));
        Assert.Equal(panel.Rect, snappedPanel.Rect);
        Assert.Equal(2, snappedPanel.BorderWidth * Scale.X, 0.001f);
    }
}
