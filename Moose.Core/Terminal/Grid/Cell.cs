namespace Moose.Core.Terminal.Grid;

public record struct Cell(int Codepoint, CellColor Foreground, CellColor Background, CellFlags Flags, byte Width, int Hyperlink)
{
    public static Cell Blank(CellColor background) => new(' ', CellColor.Default, background, CellFlags.None, 1, 0);

    public readonly bool IsWideContinuation => Width == 0;
}
