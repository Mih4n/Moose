using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Moose.Core.Settings;
using Moose.Core.Terminal.Grid;
using Moose.Core.Terminal.Images;
using Moose.Graphics.Elements;
using Moose.Graphics.Hosting;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.UI.Terminal;

sealed class TerminalView
{
    const float MinimumFontSize = 4;
    const float DecorationThickness = 1;
    const float BeamWidth = 2;
    const float ScrollbarWidth = 6;
    const float ScrollbarMargin = 3;
    const float MinimumThumb = 24;
    // kitty: images below this z go under cell backgrounds too, other negative ones only under the text
    const int BelowBackgrounds = -1_073_741_824;

    readonly Stopwatch blinkClock = Stopwatch.StartNew();
    readonly AppWindow window;
    readonly List<(ImagePlacement Placement, int Row, int Column)> placements = [];
    readonly List<PlaceholderCell> placeholders = [];
    long now;
    float shift;
    Rect scrollbarTrack;
    Rect scrollbarThumb;
    float? thumbGrab;
    TerminalPalette palette;
    UiSettings settings;
    Vector2 origin;
    Vector2 cellSize = Vector2.One;
    Rect bounds;
    float? fontSize;
    float defaultFontSize = 11;

    public TerminalView(UiSettings settings, AppWindow window)
    {
        this.settings = settings;
        this.window = window;
        palette = new TerminalPalette();
    }

    public int Columns { get; private set; } = 1;
    public int Rows { get; private set; } = 1;
    public Color Background => palette.Background;

    public void Configure(UiSettings settings)
    {
        this.settings = settings;
        palette = new TerminalPalette();
    }

    public void ChangeFontSize(float delta) => fontSize = Math.Max(MinimumFontSize, (fontSize ?? defaultFontSize) + delta);

    public void ResetFontSize() => fontSize = null;

    public void RestartBlink() => blinkClock.Restart();

    public bool Contains(Vector2 position) => bounds.Contains(position);

    public bool ScrollbarHovered { get; set; }
    public bool ScrollbarDragging => thumbGrab != null;

    /// The strip along the right edge that holds the scrollbar; empty while there's nothing to scroll.
    public bool ScrollbarContains(Vector2 position) => scrollbarTrack.W > 0
        && position.X >= scrollbarTrack.X - ScrollbarMargin && position.X < bounds.X + bounds.W
        && position.Y >= bounds.Y && position.Y < bounds.Y + bounds.H;

    /// Starts a drag: grabbing the thumb keeps the grab point under the pointer, clicking the track centers the thumb there.
    public void BeginScrollbarDrag(TerminalTab tab, float y)
    {
        thumbGrab = y >= scrollbarThumb.Y && y < scrollbarThumb.Y + scrollbarThumb.H ? y - scrollbarThumb.Y : scrollbarThumb.H / 2;
        DragScrollbar(tab, y);
    }

    public void DragScrollbar(TerminalTab tab, float y)
    {
        if (thumbGrab is not { } grab) return;
        lock (tab.Session.SyncRoot)
        {
            var screen = tab.Session.Screen;
            var travel = scrollbarTrack.H - scrollbarThumb.H;
            var fraction = travel > 0 ? Math.Clamp((y - grab - scrollbarTrack.Y) / travel, 0, 1) : 1;
            var target = (1 - fraction) * screen.Scrollback.Count * cellSize.Y;
            tab.ScrollPixels(target - tab.ScrollPosition(screen, cellSize.Y), cellSize.Y);
        }
    }

    public float LineHeight => cellSize.Y;

    public void EndScrollbarDrag() => thumbGrab = null;

    public (int Column, int Row) CellAt(Vector2 position)
    {
        var column = (int)MathF.Floor((position.X - origin.X) / cellSize.X);
        var row = (int)MathF.Floor((position.Y - origin.Y - shift) / cellSize.Y);
        return (Math.Clamp(column, 0, Columns - 1), Math.Clamp(row, 0, Rows - 1));
    }

    public Element Render(TerminalTab tab) => Ui.Canvas(painter => Paint(painter, tab)).Bg(palette.Background);

    void Paint(Painter painter, TerminalTab tab)
    {
        defaultFontSize = painter.DefaultFontSize;
        var size = fontSize ?? defaultFontSize;
        bounds = painter.Bounds;
        cellSize = painter.CellSize(size);
        origin = painter.Snap(new Vector2(bounds.X + settings.Window.PaddingX, bounds.Y + settings.Window.PaddingY));
        Columns = Math.Max(1, (int)((bounds.W - 2 * settings.Window.PaddingX) / cellSize.X));
        Rows = Math.Max(1, (int)((bounds.H - 2 * settings.Window.PaddingY) / cellSize.Y));

        var session = tab.Session;
        // the pty gets physical px, like kitty reports them, so images map 1:1 onto the framebuffer
        var pixelWidth = (int)MathF.Round(Columns * cellSize.X * painter.Scale.X);
        var pixelHeight = (int)MathF.Round(Rows * cellSize.Y * painter.Scale.Y);
        tab.EnsureStarted(Columns, Rows, pixelWidth, pixelHeight);
        session.Resize(Columns, Rows, pixelWidth, pixelHeight);

        lock (session.SyncRoot)
        {
            var screen = session.Screen;
            var cursor = CursorFor(screen);
            shift = painter.Snap(new Vector2(0, tab.ScrollFraction(screen, cellSize.Y))).Y;
            now = Environment.TickCount64;
            placements.Clear();
            if (screen.HasImages)
            {
                placements.AddRange(screen.PlacementsFrom(screen.FirstViewportLine, Rows).OrderBy(entry => entry.Placement.Z));
                // without images, backgrounds and text go row by row in one pass; with them, each layer covers the whole grid
                PaintPlacements(painter, screen, z => z < BelowBackgrounds);
                PaintRows(painter, tab, screen, cursor, size, backgrounds: true, glyphs: false);
                PaintPlacements(painter, screen, z => z is >= BelowBackgrounds and < 0);
                PaintRows(painter, tab, screen, cursor, size, backgrounds: false, glyphs: true);
                PaintPlacements(painter, screen, z => z >= 0);
            }
            else PaintRows(painter, tab, screen, cursor, size, backgrounds: true, glyphs: true);
            if (shift > 0) MaskOverflow(painter);
            PaintScrollbar(painter, screen);
        }
    }

    void PaintRows(Painter painter, TerminalTab tab, TerminalScreen screen, (int Row, int Column, CursorShape Shape)? cursor, float size, bool backgrounds, bool glyphs)
    {
        // a partly scrolled viewport also shows the line above it, shifted down by the remainder
        for (var row = shift > 0 ? -1 : 0; row < Math.Min(Rows, screen.Rows); row++)
        {
            var lineNumber = screen.FirstViewportLine + row;
            if (screen.LineAt(lineNumber) is not { } line) continue;
            var y = origin.Y + row * cellSize.Y + shift;
            var blockCursorColumn = cursor is { Shape: CursorShape.Block } block && block.Row == row ? block.Column : -1;
            if (backgrounds) PaintBackgrounds(painter, tab, line, lineNumber, y, blockCursorColumn);
            if (!glyphs) continue;
            PaintGlyphs(painter, tab, line, lineNumber, y, size, blockCursorColumn);
            if (screen.HasImages) PaintPlaceholders(painter, screen, line, y);
            if (cursor is { } thin && thin.Row == row && thin.Shape != CursorShape.Block) PaintThinCursor(painter, thin.Shape, thin.Column, y);
        }
    }

    void PaintPlacements(Painter painter, TerminalScreen screen, Func<int, bool> layer)
    {
        var cellPixels = new Vector2(screen.CellPixelWidth, screen.CellPixelHeight);
        foreach (var (placement, row, column) in placements)
        {
            if (!layer(placement.Z)) continue;
            var (sourceX, sourceY, sourceWidth, sourceHeight) = placement.Source;
            if (sourceWidth <= 0 || sourceHeight <= 0) continue;
            var (columns, rows) = placement.Size(cellPixels.X, cellPixels.Y);
            var offset = new Vector2(placement.OffsetX / cellPixels.X, placement.OffsetY / cellPixels.Y);
            var corner = painter.Snap(new Vector2(origin.X + (column + offset.X) * cellSize.X, origin.Y + (row + offset.Y) * cellSize.Y + shift));
            var rect = new Rect(corner.X, corner.Y, (columns - offset.X) * cellSize.X, (rows - offset.Y) * cellSize.Y);
            var image = placement.Image;
            PaintImage(painter, image, rect, new Rect((float)sourceX / image.Width, (float)sourceY / image.Height, (float)sourceWidth / image.Width, (float)sourceHeight / image.Height));
        }
    }

    /// Unicode placeholders: each U+10EEEE cell shows its piece of the virtual placement's image, fit into its cell grid and centered.
    void PaintPlaceholders(Painter painter, TerminalScreen screen, Line line, float y)
    {
        placeholders.Clear();
        screen.ResolvePlaceholders(line, placeholders);
        var cellPixels = new Vector2(screen.CellPixelWidth, screen.CellPixelHeight);
        foreach (var cell in placeholders)
        {
            var placement = cell.Placement;
            var image = placement.Image;
            var (sourceX, sourceY, sourceWidth, sourceHeight) = placement.Source;
            if (sourceWidth <= 0 || sourceHeight <= 0 || cell.Index >= Columns) continue;
            var (columns, rows) = placement.CellSpan(cellPixels.X, cellPixels.Y);
            // in px of the placement's box
            var scale = Math.Min(columns * cellPixels.X / sourceWidth, rows * cellPixels.Y / sourceHeight);
            var picture = new Vector2(sourceWidth * scale, sourceHeight * scale);
            var pictureStart = (new Vector2(columns * cellPixels.X, rows * cellPixels.Y) - picture) / 2;
            var cellStart = new Vector2(cell.Column * cellPixels.X, cell.Row * cellPixels.Y);
            var start = Vector2.Max(cellStart, pictureStart);
            var end = Vector2.Min(cellStart + cellPixels, pictureStart + picture);
            if (end.X <= start.X || end.Y <= start.Y) continue;

            var sourceStart = new Vector2(sourceX, sourceY) + (start - pictureStart) / scale;
            var sourceSize = (end - start) / scale;
            var toLogical = cellSize / cellPixels;
            var corner = new Vector2(origin.X + cell.Index * cellSize.X, y) + (start - cellStart) * toLogical;
            var size = (end - start) * toLogical;
            PaintImage(painter, image, new Rect(corner.X, corner.Y, size.X, size.Y),
                new Rect(sourceStart.X / image.Width, sourceStart.Y / image.Height, sourceSize.X / image.Width, sourceSize.Y / image.Height));
        }
    }

    /// Paints the current animation frame clipped to the grid, scheduling a repaint for the next frame.
    void PaintImage(Painter painter, KittyImage image, Rect rect, Rect uv)
    {
        var (frame, nextChange) = image.FrameAt(now);
        if (nextChange is { } delay) window.Invalidate(TimeSpan.FromMilliseconds(Math.Max(1, delay)));
        var left = Math.Max(rect.X, origin.X);
        var top = Math.Max(rect.Y, origin.Y);
        var right = Math.Min(rect.X + rect.W, origin.X + Columns * cellSize.X);
        var bottom = Math.Min(rect.Y + rect.H, origin.Y + Rows * cellSize.Y);
        if (right <= left || bottom <= top || rect.W <= 0 || rect.H <= 0) return;
        var clippedUv = new Rect(
            uv.X + (left - rect.X) / rect.W * uv.W,
            uv.Y + (top - rect.Y) / rect.H * uv.H,
            (right - left) / rect.W * uv.W,
            (bottom - top) / rect.H * uv.H);
        painter.Image(frame.Pixels, image.Width, image.Height, new Rect(left, top, right - left, bottom - top), clippedUv);
    }

    // there's no clipping in the renderer, so cover the rows that slid into the padding with the background
    void MaskOverflow(Painter painter)
    {
        var gridBottom = origin.Y + Rows * cellSize.Y;
        painter.Fill(new Rect(bounds.X, bounds.Y, bounds.W, origin.Y - bounds.Y), palette.Background);
        painter.Fill(new Rect(bounds.X, gridBottom, bounds.W, bounds.Y + bounds.H - gridBottom), palette.Background);
    }

    void PaintScrollbar(Painter painter, TerminalScreen screen)
    {
        var count = screen.Scrollback.Count;
        if (screen.IsAlternateScreen || count == 0)
        {
            scrollbarTrack = scrollbarThumb = default;
            thumbGrab = null;
            return;
        }
        scrollbarTrack = new Rect(bounds.X + bounds.W - ScrollbarMargin - ScrollbarWidth, bounds.Y + ScrollbarMargin, ScrollbarWidth, bounds.H - 2 * ScrollbarMargin);
        var thumbHeight = Math.Min(scrollbarTrack.H, Math.Max(MinimumThumb, scrollbarTrack.H * screen.Rows / (count + screen.Rows)));
        var top = scrollbarTrack.Y + (scrollbarTrack.H - thumbHeight) * Math.Clamp(count - screen.ViewportOffset - shift / cellSize.Y, 0, count) / count;
        var corner = painter.Snap(new Vector2(scrollbarTrack.X, top));
        scrollbarThumb = new Rect(corner.X, corner.Y, ScrollbarWidth, thumbHeight);
        var active = ScrollbarHovered || ScrollbarDragging;
        if (active) painter.Fill(scrollbarTrack, settings.Theme.TextMuted with { A = 0.08f });
        painter.Fill(scrollbarThumb, settings.Theme.TextMuted with { A = active ? 0.6f : screen.ViewportOffset > 0 ? 0.4f : 0.2f }, ScrollbarWidth / 2);
    }

    (int Row, int Column, CursorShape Shape)? CursorFor(TerminalScreen screen)
    {
        if (!screen.CursorVisible || screen.ViewportOffset != 0) return null;
        var blinks = screen.CursorBlink ?? settings.Cursor.Blink;
        var interval = Math.Max(1, settings.Cursor.BlinkIntervalMs);
        if (blinks) window.Invalidate(TimeSpan.FromMilliseconds(interval - blinkClock.ElapsedMilliseconds % interval));
        if (blinks && blinkClock.ElapsedMilliseconds / interval % 2 == 1) return null;
        var shape = screen.CursorShape != CursorShape.Default ? screen.CursorShape : settings.Cursor.Style.ToLowerInvariant() switch
        {
            "beam" or "bar" => CursorShape.Beam,
            "underline" => CursorShape.Underline,
            _ => CursorShape.Block,
        };
        return (screen.CursorRow, screen.CursorColumn, shape);
    }

    void PaintBackgrounds(Painter painter, TerminalTab tab, Line line, long lineNumber, float y, int blockCursorColumn)
    {
        var columns = Math.Min(line.Length, Columns);
        var runStart = 0;
        var runColor = palette.Background;
        for (var column = 0; column <= columns; column++)
        {
            var color = column == columns ? palette.Background : CellColors(tab, line, lineNumber, column, blockCursorColumn).Background;
            if (color == runColor) continue;
            if (runColor != palette.Background) painter.Fill(CellRect(runStart, column - runStart, y), runColor);
            runStart = column;
            runColor = color;
        }
    }

    void PaintGlyphs(Painter painter, TerminalTab tab, Line line, long lineNumber, float y, float size, int blockCursorColumn)
    {
        var columns = Math.Min(line.Length, Columns);
        // neighbouring single-width cells of one color are shaped together so ligatures can form across them
        var run = new List<int>();
        var runStart = 0;
        var runColor = default(Color);
        void Flush()
        {
            if (run.Count > 0) painter.Glyphs(CollectionsMarshal.AsSpan(run), new Vector2(origin.X + runStart * cellSize.X, y), size, runColor);
            run.Clear();
        }

        for (var column = 0; column < columns; column++)
        {
            var cell = line.Cells[column];
            if (cell.IsWideContinuation)
            {
                Flush();
                continue;
            }
            var foreground = CellColors(tab, line, lineNumber, column, blockCursorColumn).Foreground;
            var x = origin.X + column * cellSize.X;
            var visible = cell.Codepoint is not (' ' or ImagePlaceholder.Codepoint) && !cell.Flags.HasFlag(CellFlags.Hidden);
            if (visible && cell.Width == 1)
            {
                if (run.Count > 0 && foreground != runColor) Flush();
                if (run.Count == 0) (runStart, runColor) = (column, foreground);
                run.Add(cell.Codepoint);
            }
            else
            {
                Flush();
                if (visible) painter.Glyph(new Rune(cell.Codepoint), new Vector2(x, y), size, foreground);
            }
            if (cell.Flags.HasFlag(CellFlags.Underline)) painter.Fill(new Rect(x, y + cellSize.Y - 2 * DecorationThickness, cell.Width * cellSize.X, DecorationThickness), foreground);
            if (cell.Flags.HasFlag(CellFlags.Strikethrough)) painter.Fill(new Rect(x, y + MathF.Round(cellSize.Y / 2), cell.Width * cellSize.X, DecorationThickness), foreground);
        }
        Flush();
    }

    void PaintThinCursor(Painter painter, CursorShape shape, int column, float y)
    {
        var x = origin.X + column * cellSize.X;
        var rect = shape == CursorShape.Beam
            ? new Rect(x, y, BeamWidth, cellSize.Y)
            : new Rect(x, y + cellSize.Y - BeamWidth, cellSize.X, BeamWidth);
        painter.Fill(rect, palette.Cursor);
    }

    (Color Foreground, Color Background) CellColors(TerminalTab tab, Line line, long lineNumber, int column, int blockCursorColumn)
    {
        var cell = line.Cells[column];
        var leaderColumn = cell.IsWideContinuation && column > 0 ? column - 1 : column;
        if (leaderColumn == blockCursorColumn || column == blockCursorColumn) return (palette.CursorText, palette.Cursor);
        return palette.Resolve(line.Cells[leaderColumn], tab.Selection?.Contains(lineNumber, column) == true);
    }

    Rect CellRect(int column, int width, float y) => new(origin.X + column * cellSize.X, y, width * cellSize.X, cellSize.Y);
}
