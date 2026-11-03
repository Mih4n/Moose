using System.Text;
using Moose.Core.Terminal.Images;

namespace Moose.Core.Terminal.Grid;

/// kitty graphics protocol: https://sw.kovidgoyal.net/kitty/graphics-protocol/
public sealed partial class TerminalScreen
{
    const long MaxStoredImageBytes = 320L << 20;
    const long MaxTransmissionBytes = 256L << 20;
    const long MaxImagePixels = 64L << 20;
    const int MaxParentDepth = 8;
    // positional deletes also reach placements that start this far above the screen and hang into it
    const int ScanAbove = 1000;

    readonly Dictionary<uint, KittyImage> images = [];
    ImageTransmission? loading;
    long imageSequence;
    uint nextImageId = 0x80000000;

    /// Physical px, as reported through TIOCGWINSZ and CSI 14/16 t.
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }
    public float CellPixelWidth => PixelWidth > 0 ? (float)PixelWidth / Columns : 10;
    public float CellPixelHeight => PixelHeight > 0 ? (float)PixelHeight / Rows : 20;
    public bool HasImages => images.Count > 0;

    public KittyImage? ImageById(uint id) => images.GetValueOrDefault(id);

    public void SetPixelSize(int width, int height)
    {
        if (width == PixelWidth && height == PixelHeight) return;
        (PixelWidth, PixelHeight) = (width, height);
        Version++;
    }

    public void ApcDispatch(ReadOnlySpan<byte> payload)
    {
        if (GraphicsCommand.Parse(payload) is not { } command) return;
        if (loading is { } transmission)
        {
            loading = null;
            if (command.IsContinuation)
            {
                Run(transmission.Command, () =>
                {
                    transmission.Append(command.Payload, final: !command.More);
                    if (command.More) loading = transmission;
                    else Complete(transmission);
                });
                return;
            }
        }
        Run(command, () => Begin(command));
    }

    void Run(GraphicsCommand command, Action action)
    {
        try
        {
            action();
        }
        catch (ImageException exception)
        {
            ReplyGraphics(command, command.ImageId, $"{exception.Code}:{exception.Message}");
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentException or OverflowException or InvalidDataException)
        {
            ReplyGraphics(command, command.ImageId, $"EINVAL:{exception.Message}");
        }
    }

    void Begin(GraphicsCommand command)
    {
        switch (command.Action)
        {
            case 't' or 'T' or 'q' or 'f':
                var transmission = new ImageTransmission(command, MaxTransmissionBytes);
                transmission.Append(command.Payload, final: !command.More);
                if (command.More) loading = transmission;
                else Complete(transmission);
                break;
            case 'p':
                var placed = Put(command, Lookup(command));
                ReplyGraphics(command, placed.Image.Id, null);
                break;
            case 'd':
                DeleteImages(command);
                break;
            // like kitty, animation control and composition only ever answer with errors
            case 'a':
                Animate(command, Lookup(command));
                break;
            case 'c':
                Compose(command, Lookup(command));
                break;
            default:
                throw new ImageException("EINVAL", $"unknown action {command.Action}");
        }
    }

    void Complete(ImageTransmission transmission)
    {
        var command = transmission.Command;
        switch (command.Action)
        {
            case 'q':
                transmission.Decode(MaxImagePixels);
                ReplyGraphics(command, command.ImageId, null);
                break;
            case 'f':
                var animated = Lookup(command);
                AddFrame(command, animated, transmission);
                ReplyGraphics(command, animated.Id, null);
                break;
            default:
                var (width, height, rgba) = transmission.Decode(MaxImagePixels);
                var image = Store(command, width, height, rgba);
                // the id is known now; errors from placing it must report it too
                try
                {
                    if (command.Action == 'T') Put(command, image);
                }
                catch (ImageException exception)
                {
                    ReplyGraphics(command, image.Id, $"{exception.Code}:{exception.Message}");
                    return;
                }
                ReplyGraphics(command, image.Id, null);
                break;
        }
    }

    KittyImage Store(GraphicsCommand command, int width, int height, byte[] rgba)
    {
        if (command.ImageId != 0 && command.ImageNumber != 0) throw new ImageException("EINVAL", "both i and I given");
        var id = command.ImageId != 0 ? command.ImageId : AllocateImageId();
        if (images.Remove(id, out var previous)) RemovePlacements(placement => placement.Image == previous);
        var image = new KittyImage(id, command.ImageNumber, width, height, rgba, ++imageSequence);
        images[id] = image;
        EnforceImageQuota(image);
        return image;
    }

    uint AllocateImageId()
    {
        while (nextImageId == 0 || images.ContainsKey(nextImageId)) nextImageId++;
        return nextImageId++;
    }

    KittyImage Lookup(GraphicsCommand command)
    {
        if (command.ImageId != 0)
            return images.GetValueOrDefault(command.ImageId) ?? throw new ImageException("ENOENT", $"no image with id {command.ImageId}");
        if (command.ImageNumber != 0)
            return ImageByNumber(command.ImageNumber) ?? throw new ImageException("ENOENT", $"no image with number {command.ImageNumber}");
        throw new ImageException("EINVAL", "no image id or number");
    }

    KittyImage? ImageByNumber(uint number) => images.Values.Where(image => image.Number == number).MaxBy(image => image.Sequence);

    ImagePlacement Put(GraphicsCommand command, KittyImage image)
    {
        var id = command.PlacementId;
        var placement = new ImagePlacement(image, id)
        {
            SourceX = command.Int('x'),
            SourceY = command.Int('y'),
            SourceWidth = command.Int('w'),
            SourceHeight = command.Int('h'),
            OffsetX = Math.Max(0, command.Int('X')),
            OffsetY = Math.Max(0, command.Int('Y')),
            Columns = Math.Max(0, command.Int('c')),
            Rows = Math.Max(0, command.Int('r')),
            Z = command.Int('z'),
            Virtual = command.Int('U') == 1,
            ParentColumns = command.Int('H'),
            ParentRows = command.Int('V'),
        };
        if (placement.Virtual)
        {
            image.VirtualPlacements[id] = placement;
            return placement;
        }

        var existing = id != 0 ? AllPlacements().FirstOrDefault(candidate => candidate.Image == image && candidate.Id == id) : null;
        ImagePlacement? parent = null;
        if (command.Has('P'))
        {
            var parentImage = images.GetValueOrDefault(command.UInt('P')) ?? throw new ImageException("ENOPARENT", "parent image not found");
            var parentId = command.UInt('Q');
            if (parentImage.VirtualPlacements.ContainsKey(parentId)) throw new ImageException("ENOPARENT", "virtual placements can't be parents");
            parent = AllPlacements().FirstOrDefault(candidate => candidate.Image == parentImage && candidate.Id == parentId)
                ?? throw new ImageException("ENOPARENT", "parent placement not found");
            var depth = 1;
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent, depth++)
            {
                if (ancestor == existing) throw new ImageException("ECYCLE", "placement would be its own ancestor");
                if (depth >= MaxParentDepth) throw new ImageException("ETOODEEP", "too many nested placements");
            }
        }

        existing?.Detach();
        if (parent != null)
        {
            placement.Parent = parent;
            (parent.Children ??= []).Add(placement);
            return placement;
        }
        var line = Lines[CursorRow];
        placement.Anchor = line;
        placement.Column = CursorColumn;
        (line.Placements ??= []).Add(placement);
        if (command.Int('C') != 1) MoveCursorPast(placement);
        return placement;
    }

    void MoveCursorPast(ImagePlacement placement)
    {
        var (columns, rows) = placement.CellSpan(CellPixelWidth, CellPixelHeight);
        for (var row = 1; row < rows; row++) Index();
        var column = placement.Column + columns;
        if (column >= Columns)
        {
            CursorColumn = 0;
            Index();
        }
        else CursorColumn = column;
        wrapPending = false;
    }

    void DeleteImages(GraphicsCommand command)
    {
        var what = command.Char('d', 'a');
        var freeData = char.IsUpper(what);
        var touched = new HashSet<KittyImage>();
        int x = command.Int('x') - 1, y = command.Int('y') - 1, z = command.Int('z');

        void RemoveOnScreen(Func<ImagePlacement, int, int, bool> predicate)
        {
            foreach (var (placement, row, column) in ScreenPlacements().ToList())
            {
                if (!predicate(placement, row, column)) continue;
                touched.Add(placement.Image);
                placement.Detach();
            }
        }

        void RemoveOf(KittyImage image, uint placementId)
        {
            touched.Add(image);
            RemovePlacements(placement => placement.Image == image && (placementId == 0 || placement.Id == placementId));
            if (placementId == 0) image.VirtualPlacements.Clear();
            else image.VirtualPlacements.Remove(placementId);
        }

        switch (char.ToLowerInvariant(what))
        {
            case 'a': RemoveOnScreen((placement, row, column) => Covers(placement, row, column, null, null)); break;
            case 'i':
                if (images.GetValueOrDefault(command.ImageId) is { } byId) RemoveOf(byId, command.PlacementId);
                break;
            case 'n':
                if (ImageByNumber(command.ImageNumber) is { } byNumber) RemoveOf(byNumber, command.PlacementId);
                break;
            case 'c': RemoveOnScreen((placement, row, column) => Covers(placement, row, column, CursorRow, CursorColumn)); break;
            case 'p': RemoveOnScreen((placement, row, column) => Covers(placement, row, column, y, x)); break;
            case 'q': RemoveOnScreen((placement, row, column) => placement.Z == z && Covers(placement, row, column, y, x)); break;
            case 'x': RemoveOnScreen((placement, row, column) => Covers(placement, row, column, null, x)); break;
            case 'y': RemoveOnScreen((placement, row, column) => Covers(placement, row, column, y, null)); break;
            case 'z': RemoveOnScreen((placement, _, _) => placement.Z == z); break;
            case 'r':
                var (first, last) = (command.UInt('x'), command.UInt('y'));
                foreach (var image in images.Values.Where(image => image.Id >= first && image.Id <= last).ToList()) RemoveOf(image, 0);
                break;
            case 'f':
                if (command.ImageId != 0 || command.ImageNumber != 0) Lookup(command).DropAnimationFrames();
                break;
        }

        if (!freeData || touched.Count == 0) return;
        var referenced = ReferencedImages();
        foreach (var image in touched)
            if (!referenced.Contains(image)) images.Remove(image.Id);
    }

    /// Whether the placement at (row, column) covers the given screen row and/or column; a missing row means any row on the screen.
    bool Covers(ImagePlacement placement, int row, int column, int? targetRow, int? targetColumn)
    {
        var (columns, rows) = placement.CellSpan(CellPixelWidth, CellPixelHeight);
        var rowCovered = targetRow is { } r ? r >= row && r < row + rows : row + rows > 0 && row < Rows;
        var columnCovered = targetColumn is not { } c || (c >= column && c < column + columns);
        return rowCovered && columnCovered;
    }

    void Animate(GraphicsCommand command, KittyImage image)
    {
        var now = Environment.TickCount64;
        if (command.Has('v')) image.LoopCount = Math.Max(0, command.Int('v'));
        if (command.Has('r') && command.Has('z'))
        {
            var frame = command.Int('r') - 1;
            if (frame < 0 || frame >= image.Frames.Count) throw new ImageException("ENOENT", $"no frame {frame + 1}");
            image.Frames[frame].Gap = command.Int('z');
        }
        if (command.Has('c')) image.ShowFrame(command.Int('c') - 1, now);
        if (command.Has('s'))
        {
            image.Play(command.Int('s') switch
            {
                2 => AnimationState.Loading,
                3 => AnimationState.Running,
                _ => AnimationState.Stopped,
            }, now);
        }
        Version++;
    }

    void AddFrame(GraphicsCommand command, KittyImage image, ImageTransmission transmission)
    {
        var (width, height, rgba) = transmission.Decode(MaxImagePixels, image.Width, image.Height);
        var edited = command.Int('r') - 1;
        var background = command.Int('c') - 1;
        if (edited >= image.Frames.Count) throw new ImageException("ENOENT", $"no frame {edited + 1}");
        if (background >= image.Frames.Count) throw new ImageException("ENOENT", $"no frame {background + 1}");

        byte[] canvas;
        if (background >= 0) canvas = (byte[])image.Frames[background].Pixels.Clone();
        else if (edited >= 0) canvas = (byte[])image.Frames[edited].Pixels.Clone();
        else canvas = Filled(image.Width * image.Height, command.UInt('Y'));
        Composite(canvas, image.Width, image.Height, command.Int('x'), command.Int('y'), rgba, width, height, 0, 0, width, height, overwrite: command.Int('X') == 1);

        if (edited >= 0)
        {
            image.Frames[edited].Pixels = canvas;
            if (command.Int('z') != 0) image.Frames[edited].Gap = command.Int('z');
        }
        else image.Frames.Add(new ImageFrame(canvas, command.Int('z')));
        EnforceImageQuota(image);
        Version++;
    }

    void Compose(GraphicsCommand command, KittyImage image)
    {
        var source = command.Int('c', 1) - 1;
        var target = command.Int('r', 1) - 1;
        if (source < 0 || source >= image.Frames.Count || target < 0 || target >= image.Frames.Count) throw new ImageException("ENOENT", "no such frame");
        var width = command.Int('w') > 0 ? command.Int('w') : image.Width;
        var height = command.Int('h') > 0 ? command.Int('h') : image.Height;
        int targetX = command.Int('x'), targetY = command.Int('y'), sourceX = command.Int('X'), sourceY = command.Int('Y');
        if (source == target && sourceX < targetX + width && targetX < sourceX + width && sourceY < targetY + height && targetY < sourceY + height)
            throw new ImageException("EINVAL", "source and destination overlap");
        var canvas = (byte[])image.Frames[target].Pixels.Clone();
        Composite(canvas, image.Width, image.Height, targetX, targetY, image.Frames[source].Pixels, image.Width, image.Height, sourceX, sourceY, width, height, overwrite: command.Int('C') == 1);
        image.Frames[target].Pixels = canvas;
        Version++;
    }

    static byte[] Filled(int pixels, uint rgba)
    {
        var canvas = new byte[pixels * 4];
        if (rgba == 0) return canvas;
        byte[] color = [(byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba];
        for (var offset = 0; offset < canvas.Length; offset += 4) color.CopyTo(canvas, offset);
        return canvas;
    }

    /// Copies or alpha-blends (straight alpha, "over") a rectangle of source into target, clipped to both.
    static void Composite(byte[] target, int targetWidth, int targetHeight, int targetX, int targetY,
        byte[] source, int sourceWidth, int sourceHeight, int sourceX, int sourceY, int width, int height, bool overwrite)
    {
        for (var row = 0; row < height; row++)
        {
            int ty = targetY + row, sy = sourceY + row;
            if (ty < 0 || ty >= targetHeight || sy < 0 || sy >= sourceHeight) continue;
            for (var column = 0; column < width; column++)
            {
                int tx = targetX + column, sx = sourceX + column;
                if (tx < 0 || tx >= targetWidth || sx < 0 || sx >= sourceWidth) continue;
                var t = (ty * targetWidth + tx) * 4;
                var s = (sy * sourceWidth + sx) * 4;
                if (overwrite || source[s + 3] == 255)
                {
                    Array.Copy(source, s, target, t, 4);
                    continue;
                }
                var alpha = source[s + 3] / 255f;
                var below = target[t + 3] / 255f * (1 - alpha);
                var combined = alpha + below;
                for (var channel = 0; channel < 3; channel++)
                    target[t + channel] = combined > 0 ? (byte)MathF.Round((source[s + channel] * alpha + target[t + channel] * below) / combined) : (byte)0;
                target[t + 3] = (byte)MathF.Round(combined * 255);
            }
        }
    }

    void ReplyGraphics(GraphicsCommand command, uint imageId, string? error)
    {
        if (command.ImageId == 0 && command.ImageNumber == 0) return;
        if (command.Quiet >= 2 || (error == null && command.Quiet == 1)) return;
        var reply = new StringBuilder("\e_G");
        if (imageId != 0) reply.Append($"i={imageId}");
        if (command.ImageNumber != 0) reply.Append(imageId != 0 ? "," : "").Append($"I={command.ImageNumber}");
        if (command.PlacementId != 0) reply.Append($",p={command.PlacementId}");
        reply.Append(';').Append(error ?? "OK").Append("\e\\");
        Respond(reply.ToString());
    }

    void EnforceImageQuota(KittyImage keep)
    {
        var total = images.Values.Sum(image => image.Bytes);
        if (total <= MaxStoredImageBytes) return;
        var referenced = ReferencedImages();
        // images nothing shows go first, then the oldest
        foreach (var image in images.Values.Where(image => image != keep).OrderBy(referenced.Contains).ThenBy(image => image.Sequence).ToList())
        {
            if (total <= MaxStoredImageBytes) break;
            images.Remove(image.Id);
            if (referenced.Contains(image)) RemovePlacements(placement => placement.Image == image);
            total -= image.Bytes;
        }
        if (total > MaxStoredImageBytes) throw new ImageException("ENOSPC", "image storage quota exceeded");
    }

    HashSet<KittyImage> ReferencedImages()
    {
        var referenced = AllPlacements().Select(placement => placement.Image).ToHashSet();
        foreach (var image in images.Values)
            if (image.VirtualPlacements.Count > 0) referenced.Add(image);
        return referenced;
    }

    IEnumerable<Line> AllLines()
    {
        for (var index = 0; index < scrollback.Count; index++) yield return scrollback[index];
        foreach (var line in primaryLines) yield return line;
        foreach (var line in alternateLines) yield return line;
    }

    IEnumerable<ImagePlacement> AllPlacements()
    {
        foreach (var line in AllLines())
            if (line.Placements != null)
                foreach (var placement in line.Placements)
                    foreach (var descendant in placement.SelfAndDescendants())
                        yield return descendant;
    }

    void RemovePlacements(Func<ImagePlacement, bool> predicate)
    {
        foreach (var placement in AllPlacements().Where(predicate).ToList()) placement.Detach();
    }

    /// Placements of the active screen with the row and column of their top-left cell; rows can be negative.
    IEnumerable<(ImagePlacement Placement, int Row, int Column)> ScreenPlacements() => PlacementsFrom(scrollback.TotalPushed, Rows);

    /// Placements reaching into rows lines starting at the absolute line firstLine, positioned relative to it;
    /// ones starting up to ScanAbove lines higher are included since they may hang down into view.
    public IEnumerable<(ImagePlacement Placement, int Row, int Column)> PlacementsFrom(long firstLine, int rows)
    {
        var top = IsAlternateScreen ? firstLine : Math.Max(firstLine - ScanAbove, scrollback.TotalPushed - scrollback.Count);
        for (var number = top; number < firstLine + rows; number++)
        {
            if (LineAt(number)?.Placements is not { } placements) continue;
            var row = (int)(number - firstLine);
            foreach (var placement in placements)
                foreach (var entry in WithChildren(placement, row, placement.Column))
                    yield return entry;
        }
    }

    static IEnumerable<(ImagePlacement Placement, int Row, int Column)> WithChildren(ImagePlacement placement, int row, int column)
    {
        yield return (placement, row, column);
        foreach (var child in placement.Children ?? [])
            foreach (var entry in WithChildren(child, row + child.ParentRows, column + child.ParentColumns))
                yield return entry;
    }

    /// ED 2 / ED 3: drop what's on the screen, or everything.
    void ClearImagePlacements(bool everything)
    {
        if (everything)
        {
            foreach (var line in AllLines()) line.Placements = null;
            return;
        }
        foreach (var (placement, row, column) in ScreenPlacements().ToList())
            if (Covers(placement, row, column, null, null)) placement.Detach();
    }

    void ResetImages()
    {
        loading = null;
        images.Clear();
        foreach (var line in AllLines()) line.Placements = null;
    }

    /// A combining mark after U+10EEEE carries the placeholder's row, column or id high byte.
    void AttachPlaceholderDiacritic(int codepoint)
    {
        var value = ImagePlaceholder.ValueOf(codepoint);
        var column = wrapPending ? CursorColumn : CursorColumn - 1;
        if (value < 0 || column < 0) return;
        ref var cell = ref Lines[CursorRow].Cells[column];
        if (cell.Codepoint == ImagePlaceholder.Codepoint) cell = cell with { Hyperlink = ImagePlaceholder.Append(cell.Hyperlink, value) };
    }

    /// Resolves the placeholder cells of a line to image pieces, inferring left-out diacritics from the cell before like kitty.
    public void ResolvePlaceholders(Line line, List<PlaceholderCell> cells)
    {
        int previousRow = -1, previousColumn = -1, previousHigh = 0;
        var previousColor = CellColor.Default;
        var previousIndex = -2;
        for (var index = 0; index < line.Length; index++)
        {
            var cell = line.Cells[index];
            if (cell.Codepoint != ImagePlaceholder.Codepoint) continue;
            int row = ImagePlaceholder.Slot(cell.Hyperlink, 0), column = ImagePlaceholder.Slot(cell.Hyperlink, 1), high = ImagePlaceholder.Slot(cell.Hyperlink, 2);
            var continues = previousIndex == index - 1 && previousColor == cell.Foreground;
            if (row < 0)
            {
                (row, column, high) = continues ? (previousRow, previousColumn + 1, previousHigh) : (0, 0, 0);
            }
            else if (column < 0)
            {
                (column, high) = continues && previousRow == row ? (previousColumn + 1, previousHigh) : (0, 0);
            }
            else if (high < 0)
            {
                high = continues && previousRow == row && previousColumn + 1 == column ? previousHigh : 0;
            }
            (previousRow, previousColumn, previousHigh, previousColor, previousIndex) = (row, column, high, cell.Foreground, index);

            var low = cell.Foreground.Kind switch
            {
                ColorKind.Rgb => cell.Foreground.Rgb,
                ColorKind.Indexed => (uint)cell.Foreground.Index,
                _ => 0u,
            };
            if (images.GetValueOrDefault(low | ((uint)high << 24)) is not { } image) continue;
            var placement = image.VirtualPlacements.Values.FirstOrDefault();
            if (placement != null) cells.Add(new PlaceholderCell(index, placement, row, column));
        }
    }

    void ReportWindow(int request)
    {
        switch (request)
        {
            case 14: Respond($"\e[4;{PixelHeight};{PixelWidth}t"); break;
            case 16: Respond($"\e[6;{(int)MathF.Round(CellPixelHeight)};{(int)MathF.Round(CellPixelWidth)}t"); break;
            case 18: Respond($"\e[8;{Rows};{Columns}t"); break;
        }
    }
}

/// A placeholder cell at Index showing the piece at (Row, Column) of the placement's cell grid.
public readonly record struct PlaceholderCell(int Index, ImagePlacement Placement, int Row, int Column);
