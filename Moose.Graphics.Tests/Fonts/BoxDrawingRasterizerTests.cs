using System.Text;
using Moose.Graphics.Fonts;

namespace Moose.Graphics.Tests.Fonts;

public class BoxDrawingRasterizerTests
{
    const int Width = 12;
    const int Height = 26;

    static GlyphBitmap Draw(int codepoint) => BoxDrawingRasterizer.Rasterize(new Rune(codepoint), Width, Height);

    static byte At(GlyphBitmap bitmap, int column, int row) => bitmap.Coverage[row * bitmap.Width + column];

    [Theory]
    [InlineData(0xE0B0)]
    [InlineData(0x2500)]
    [InlineData(0x256D)]
    [InlineData(0x2588)]
    public void Rasterize_FillsExactlyOneCellAnchoredAtItsTopLeft(int codepoint)
    {
        var bitmap = Draw(codepoint);

        Assert.Equal((Width, Height, 0, 0), (bitmap.Width, bitmap.Height, bitmap.Left, bitmap.Top));
        Assert.Equal(Width * Height, bitmap.Coverage.Length);
    }

    [Theory]
    [InlineData(0xE0B0, 0)]
    [InlineData(0xE0B4, 0)]
    [InlineData(0xE0B2, Width - 1)]
    [InlineData(0xE0B6, Width - 1)]
    public void Rasterize_PowerlineSeparatorCoversItsFlatEdgeOnEveryRow(int codepoint, int edgeColumn)
    {
        var bitmap = Draw(codepoint);

        for (var row = 0; row < Height; row++) Assert.Equal(255, At(bitmap, edgeColumn, row));
    }

    [Fact]
    public void Rasterize_RightArrowReachesTheOppositeEdgeOnlyAtItsTip()
    {
        var bitmap = Draw(0xE0B0);

        Assert.True(At(bitmap, Width - 1, Height / 2) > 0);
        Assert.Equal(0, At(bitmap, Width - 1, 0));
        Assert.Equal(0, At(bitmap, Width - 1, Height - 1));
    }

    [Fact]
    public void Rasterize_HorizontalLineTouchesBothSidesOnTheSameRows()
    {
        var bitmap = Draw(0x2500);
        var rows = Enumerable.Range(0, Height).Where(row => At(bitmap, 0, row) == 255).ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(255, At(bitmap, Width - 1, row)));
        Assert.Equal(rows, Enumerable.Range(0, Height).Where(row => At(bitmap, Width - 1, row) > 0));
    }

    [Fact]
    public void Rasterize_CornerJoinsTheVerticalLineColumnsOfNeighbouringCells()
    {
        var vertical = Draw(0x2502);
        var corner = Draw(0x250C);
        var columns = Enumerable.Range(0, Width).Where(column => At(vertical, column, 0) == 255);

        Assert.All(columns, column => Assert.Equal(255, At(corner, column, Height - 1)));
    }

    [Fact]
    public void Rasterize_BlocksAreSolidOrEmptyPerPixel()
    {
        var full = Draw(0x2588);
        var lowerHalf = Draw(0x2584);

        Assert.All(full.Coverage, value => Assert.Equal(255, value));
        Assert.Equal(0, At(lowerHalf, 0, Height / 2 - 1));
        Assert.Equal(255, At(lowerHalf, Width - 1, Height / 2));
    }

    [Theory]
    [InlineData(0xE0B0, true)]
    [InlineData(0x2502, true)]
    [InlineData(0x2591, true)]
    [InlineData('M', false)]
    [InlineData(0x2550, false)]
    public void Handles_OnlyProceduralGlyphs(int codepoint, bool expected)
    {
        Assert.Equal(expected, BoxDrawingRasterizer.Handles(new Rune(codepoint)));
    }
}
