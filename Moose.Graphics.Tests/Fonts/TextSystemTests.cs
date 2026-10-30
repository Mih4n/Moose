using System.Numerics;
using System.Text;
using Moose.Core.Settings;
using Moose.Graphics.Elements;
using Moose.Graphics.Fonts;
using Moose.Graphics.Fonts.FreeType;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.Graphics.Tests.Fonts;

public sealed class TextSystemTests : IDisposable
{
    const float Points = 11;

    readonly FreeTypeRasterizer rasterizer = new();
    readonly FontconfigLocator locator = new();
    readonly TextSystem textSystem;

    public TextSystemTests()
    {
        textSystem = new TextSystem(new FontOptions("monospace", Points), locator, rasterizer);
    }

    [Fact]
    public void Measure_EmptyTextHasLineHeightButNoWidth()
    {
        var size = textSystem.Measure("", textSystem.DefaultSize);

        Assert.Equal(0, size.X);
        Assert.True(size.Y > 0);
    }

    [Fact]
    public void Measure_GrowsWithText()
    {
        Assert.True(textSystem.Measure("ab", textSystem.DefaultSize).X > textSystem.Measure("a", textSystem.DefaultSize).X);
    }

    [Fact]
    public void Text_SizesItselfUnlessSizeIsSet()
    {
        var size = textSystem.Measure("ab", textSystem.DefaultSize);
        var padded = new Text("ab").Padding(2);
        var sized = new Text("ab").W(50);
        var root = new Div()
            .Row()
            .Child(padded)
            .Child(sized);

        root.Measure(textSystem);
        root.Layout(new Rect(0, 0, 500, 500));

        Assert.Equal(new Rect(0, 0, size.X + 4, size.Y + 4), padded.Bounds);
        Assert.Equal(50, sized.Bounds.W);
        Assert.Equal(size.Y, sized.Bounds.H);
    }

    [Fact]
    public void CellSize_UsesMonospaceAdvanceAndLineHeight()
    {
        var cell = textSystem.CellSize(textSystem.DefaultSize);

        Assert.True(cell.X > 0);
        Assert.Equal(textSystem.Measure("M", textSystem.DefaultSize).Y, cell.Y);
        Assert.Equal(textSystem.Measure("MMMM", textSystem.DefaultSize).X, 4 * cell.X, 0.5f);
    }

    [Fact]
    public void PaintRune_AddsOneGlyphQuadAtCellOriginAndNoneForSpace()
    {
        var scene = new List<Quad>();

        textSystem.PaintRune(scene, new Rune('M'), textSystem.DefaultSize, new Vector2(100, 50), new Color(1, 0, 0));
        textSystem.PaintRune(scene, new Rune(' '), textSystem.DefaultSize, new Vector2(100, 50), new Color(1, 0, 0));

        var quad = Assert.Single(scene);
        Assert.True(quad.Uv.W > 0);
        Assert.InRange(quad.Rect.X, 100, 100 + textSystem.CellSize(textSystem.DefaultSize).X);
        Assert.InRange(quad.Rect.Y, 50, 50 + textSystem.CellSize(textSystem.DefaultSize).Y);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(4f / 3f)]
    public void CellSize_TreatsFontSizeAsPointsAndMatchesKittyMetricsInPhysicalPixels(float scale)
    {
        textSystem.Scale = new Vector2(scale);
        using var face = rasterizer.Load(locator.Find("monospace")!, Points * 96f / 72f * scale);
        var widestAscii = Enumerable.Range(0x20, 0x5F).Max(codepoint => MathF.Ceiling(face.Rasterize(new Rune(codepoint)).Advance));

        var cell = textSystem.CellSize(Points) * scale;

        Assert.Equal(widestAscii, cell.X, 0.001f);
        Assert.Equal(face.Metrics.LineHeight, cell.Y, 0.001f);
        Assert.Equal(MathF.Round(cell.Y), cell.Y, 0.001f);
    }

    [Fact]
    public void FaceMetrics_AreWholePixels()
    {
        using var face = rasterizer.Load(locator.Find("monospace")!, 19.5f);

        Assert.Equal(MathF.Ceiling(face.Metrics.Ascent), face.Metrics.Ascent);
        Assert.Equal(MathF.Ceiling(face.Metrics.LineHeight), face.Metrics.LineHeight);
        Assert.InRange(face.Metrics.Ascent, 1, face.Metrics.LineHeight);
    }

    [Fact]
    public void PaintRune_DrawsPowerlineGlyphOverTheWholeCell()
    {
        textSystem.Scale = new Vector2(4f / 3f);
        var scene = new List<Quad>();
        var origin = new Vector2(30, 60);

        textSystem.PaintRune(scene, new Rune(0xE0B0), Points, origin, new Color(1, 0, 0));

        var quad = Assert.Single(scene);
        var cell = textSystem.CellSize(Points);
        Assert.Equal(origin.X, quad.Rect.X, 0.001f);
        Assert.Equal(origin.Y, quad.Rect.Y, 0.001f);
        Assert.Equal(cell.X, quad.Rect.W, 0.001f);
        Assert.Equal(cell.Y, quad.Rect.H, 0.001f);
    }

    [Fact]
    public void Truncate_KeepsFittingTextAndEllipsizesLongTextToTheWidth()
    {
        var title = "a rather long terminal title";
        var maxWidth = textSystem.Measure("a rather", Points).X;

        var truncated = textSystem.Truncate(title, Points, maxWidth);

        Assert.Equal(title, textSystem.Truncate(title, Points, textSystem.Measure(title, Points).X));
        Assert.EndsWith("…", truncated);
        Assert.StartsWith(truncated[..^1], title);
        Assert.InRange(textSystem.Measure(truncated, Points).X, 0, maxWidth);
        Assert.True(textSystem.Measure(title[..truncated.Length] + "…", Points).X > maxWidth);
        Assert.Equal("", textSystem.Truncate(title, Points, 0));
    }

    public void Dispose()
    {
        textSystem.Dispose();
        rasterizer.Dispose();
    }
}
