namespace Moose.Core.Terminal.Grid;

public readonly record struct GridPoint(long Line, int Column) : IComparable<GridPoint>
{
    public int CompareTo(GridPoint other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);
}
