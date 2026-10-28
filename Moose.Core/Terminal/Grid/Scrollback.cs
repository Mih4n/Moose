namespace Moose.Core.Terminal.Grid;

public sealed class Scrollback(int capacity)
{
    readonly Line[] lines = new Line[Math.Max(0, capacity)];
    int start;

    public int Count { get; private set; }
    public int Capacity => lines.Length;
    public long TotalPushed { get; private set; }

    public Line this[int index] => lines[(start + index) % lines.Length];

    public void Push(Line line)
    {
        if (lines.Length == 0) return;
        TotalPushed++;
        if (Count < lines.Length)
        {
            lines[(start + Count++) % lines.Length] = line;
            return;
        }
        lines[start] = line;
        start = (start + 1) % lines.Length;
    }

    public Line? PopNewest()
    {
        if (Count == 0) return null;
        var index = (start + --Count) % lines.Length;
        var line = lines[index];
        lines[index] = null!;
        TotalPushed--;
        return line;
    }

    public void Clear()
    {
        Array.Clear(lines);
        start = 0;
        Count = 0;
    }
}
