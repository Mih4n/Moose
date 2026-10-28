namespace Moose.Core.Terminal.Grid;

public readonly record struct Selection(GridPoint Anchor, GridPoint Head)
{
    public GridPoint Start => Anchor.CompareTo(Head) <= 0 ? Anchor : Head;
    public GridPoint End => Anchor.CompareTo(Head) <= 0 ? Head : Anchor;
    public bool IsEmpty => Anchor == Head;

    public bool Contains(long line, int column)
    {
        var point = new GridPoint(line, column);
        return !IsEmpty && Start.CompareTo(point) <= 0 && point.CompareTo(End) <= 0;
    }
}
