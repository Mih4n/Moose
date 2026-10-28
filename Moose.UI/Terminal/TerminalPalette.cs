using Moose.Core.Settings;
using Moose.Core.Terminal.Grid;
using Moose.Graphics.Primitives;

namespace Moose.UI.Terminal;

sealed class TerminalPalette
{
    static readonly int[] CubeLevels = [0, 95, 135, 175, 215, 255];

    readonly Color[] indexed = new Color[256];

    public TerminalPalette(ColorOptions colors)
    {
        Foreground = Color.Parse(colors.Foreground);
        Background = Color.Parse(colors.Background);
        Cursor = Color.Parse(colors.Cursor);
        CursorText = Color.Parse(colors.CursorText);
        SelectionForeground = Color.Parse(colors.SelectionForeground);
        SelectionBackground = Color.Parse(colors.SelectionBackground);

        var normal = colors.Normal.InOrder();
        var bright = colors.Bright.InOrder();
        for (var index = 0; index < 8; index++)
        {
            indexed[index] = Color.Parse(normal[index]);
            indexed[index + 8] = Color.Parse(bright[index]);
        }
        for (var index = 16; index < 232; index++)
        {
            var cube = index - 16;
            indexed[index] = Rgb(CubeLevels[cube / 36], CubeLevels[cube / 6 % 6], CubeLevels[cube % 6]);
        }
        for (var index = 232; index < 256; index++)
        {
            var gray = 8 + (index - 232) * 10;
            indexed[index] = Rgb(gray, gray, gray);
        }
    }

    public Color Foreground { get; }
    public Color Background { get; }
    public Color Cursor { get; }
    public Color CursorText { get; }
    public Color SelectionForeground { get; }
    public Color SelectionBackground { get; }

    public (Color Foreground, Color Background) Resolve(Cell cell, bool selected)
    {
        if (selected) return (SelectionForeground, SelectionBackground);
        var foregroundColor = cell.Foreground;
        if (cell.Flags.HasFlag(CellFlags.Bold) && foregroundColor.Kind == ColorKind.Indexed && foregroundColor.Index < 8)
            foregroundColor = CellColor.Indexed(foregroundColor.Index + 8);
        var foreground = Resolve(foregroundColor, Foreground);
        var background = Resolve(cell.Background, Background);
        if (cell.Flags.HasFlag(CellFlags.Inverse)) (foreground, background) = (background, foreground);
        if (cell.Flags.HasFlag(CellFlags.Dim)) foreground = Mix(foreground, background, 0.4f);
        if (cell.Flags.HasFlag(CellFlags.Hidden)) foreground = background;
        return (foreground, background);
    }

    Color Resolve(CellColor color, Color fallback) => color.Kind switch
    {
        ColorKind.Indexed => indexed[color.Index],
        ColorKind.Rgb => Color.Hex(color.Rgb),
        _ => fallback,
    };

    static Color Rgb(int red, int green, int blue) => new(red / 255f, green / 255f, blue / 255f);

    static Color Mix(Color from, Color to, float amount) =>
        new(from.R + (to.R - from.R) * amount, from.G + (to.G - from.G) * amount, from.B + (to.B - from.B) * amount, from.A);
}
