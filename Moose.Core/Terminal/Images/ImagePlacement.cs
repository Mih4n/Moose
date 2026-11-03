using Moose.Core.Terminal.Grid;

namespace Moose.Core.Terminal.Images;

/// Where an image shows. Anchored placements live on the Line they start at, so they scroll, move into
/// scrollback and get dropped together with their text; relative ones hang off their parent instead.
public sealed class ImagePlacement(KittyImage image, uint id)
{
    public KittyImage Image => image;
    public uint Id => id;
    public int SourceX { get; init; }
    public int SourceY { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    /// Pixel offset inside the first cell.
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
    /// Requested size in cells; 0 derives it from the pixel size.
    public int Columns { get; init; }
    public int Rows { get; init; }
    public int Z { get; init; }
    public bool Virtual { get; init; }

    public Line? Anchor { get; set; }
    public int Column { get; set; }
    public ImagePlacement? Parent { get; set; }
    public int ParentColumns { get; init; }
    public int ParentRows { get; init; }
    public List<ImagePlacement>? Children { get; set; }

    public (int X, int Y, int Width, int Height) Source
    {
        get
        {
            var x = Math.Clamp(SourceX, 0, image.Width);
            var y = Math.Clamp(SourceY, 0, image.Height);
            var width = SourceWidth > 0 ? Math.Min(SourceWidth, image.Width - x) : image.Width - x;
            var height = SourceHeight > 0 ? Math.Min(SourceHeight, image.Height - y) : image.Height - y;
            return (x, y, width, height);
        }
    }

    /// Displayed size in cells (fractional), offset included: explicit sizes stretch to whole cells,
    /// a single explicit side keeps the aspect ratio, none shows the pixels 1:1.
    public (float Columns, float Rows) Size(float cellWidth, float cellHeight)
    {
        var (_, _, width, height) = Source;
        var offsetColumns = OffsetX / cellWidth;
        var offsetRows = OffsetY / cellHeight;
        float columns, rows;
        if (Columns > 0 && Rows > 0) (columns, rows) = (Columns, Rows);
        else if (Columns > 0) (columns, rows) = (Columns, offsetRows + (Columns - offsetColumns) * cellWidth * height / Math.Max(1, width) / cellHeight);
        else if (Rows > 0) (columns, rows) = (offsetColumns + (Rows - offsetRows) * cellHeight * width / Math.Max(1, height) / cellWidth, Rows);
        else (columns, rows) = (offsetColumns + width / cellWidth, offsetRows + height / cellHeight);
        return (columns, rows);
    }

    /// Whole cells the placement touches.
    public (int Columns, int Rows) CellSpan(float cellWidth, float cellHeight)
    {
        var (columns, rows) = Size(cellWidth, cellHeight);
        return (Math.Max(1, (int)MathF.Ceiling(columns - 0.001f)), Math.Max(1, (int)MathF.Ceiling(rows - 0.001f)));
    }

    public IEnumerable<ImagePlacement> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children ?? [])
            foreach (var descendant in child.SelfAndDescendants())
                yield return descendant;
    }

    public void Detach()
    {
        if (Parent != null) Parent.Children?.Remove(this);
        else Anchor?.Placements?.Remove(this);
        if (Anchor?.Placements is { Count: 0 }) Anchor.Placements = null;
        Parent = null;
        Anchor = null;
    }
}
