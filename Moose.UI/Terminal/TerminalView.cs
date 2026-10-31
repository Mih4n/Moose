using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Moose.Core.Settings;
using Moose.Core.Terminal.Grid;
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

    readonly Stopwatch blinkClock = Stopwatch.StartNew();
    readonly AppWindow window;
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
        tab.EnsureStarted(Columns, Rows);
        session.Resize(Columns, Rows, (int)(Columns * cellSize.X), (int)(Rows * cellSize.Y));

        lock (session.SyncRoot)
        {
            var screen = session.Screen;
            var cursor = CursorFor(screen);
            shift = painter.Snap(new Vector2(0, tab.ScrollFraction(screen, cellSize.Y))).Y;
            // a partly scrolled viewport also shows the line above it, shifted down by the remainder
            for (var row = shift > 0 ? -1 : 0; row < Math.Min(Rows, screen.Rows); row++)
            {
                var lineNumber = screen.FirstViewportLine + row;
                if (screen.LineAt(lineNumber) is not { } line) continue;
                var y = origin.Y + row * cellSize.Y + shift;
                var blockCursorColumn = cursor is { Shape: CursorShape.Block } block && block.Row == row ? block.Column : -1;
                PaintBackgrounds(painter, tab, line, lineNumber, y, blockCursorColumn);
                PaintGlyphs(painter, tab, line, lineNumber, y, size, blockCursorColumn);
                if (cursor is { } thin && thin.Row == row && thin.Shape != CursorShape.Block) PaintThinCursor(painter, thin.Shape, thin.Column, y);
            }
            if (shift > 0) MaskOverflow(painter);
            PaintScrollbar(painter, screen);
        }
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
            var visible = cell.Codepoint != ' ' && !cell.Flags.HasFlag(CellFlags.Hidden);
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
