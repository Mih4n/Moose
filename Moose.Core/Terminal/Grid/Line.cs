using System.Text;
using Moose.Core.Terminal.Images;

namespace Moose.Core.Terminal.Grid;

public sealed class Line
{
    public Line(int columns, Cell blank)
    {
        Cells = new Cell[columns];
        Array.Fill(Cells, blank);
    }

    public Cell[] Cells { get; private set; }
    public bool Wrapped { get; set; }
    /// Images whose top-left cell is on this line; null for the vast majority of lines.
    public List<ImagePlacement>? Placements { get; set; }
    public int Length => Cells.Length;

    public void Fill(int start, int end, Cell blank)
    {
        start = Math.Max(0, start);
        end = Math.Min(Cells.Length, end);
        if (start < end) Cells.AsSpan(start, end - start).Fill(blank);
    }

    public void Resize(int columns, Cell blank)
    {
        if (columns == Cells.Length) return;
        var previous = Cells.Length;
        var cells = Cells;
        Array.Resize(ref cells, columns);
        Cells = cells;
        if (columns > previous) Fill(previous, columns, blank);
        else if (Cells[columns - 1].Width == 2) Cells[columns - 1] = blank;
    }

    public bool IsBlank()
    {
        foreach (var cell in Cells)
            if (cell.Codepoint != ' ' || cell.Background != CellColor.Default) return false;
        return true;
    }

    public string Text(int start, int end)
    {
        var builder = new StringBuilder();
        for (var column = Math.Max(0, start); column < Math.Min(end, Cells.Length); column++)
            if (!Cells[column].IsWideContinuation) builder.Append(char.ConvertFromUtf32(Cells[column].Codepoint));
        return builder.ToString();
    }
}
