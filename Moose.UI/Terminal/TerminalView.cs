using System.Diagnostics;
using System.Numerics;
using System.Text;
using Moose.Core.Settings;
using Moose.Core.Terminal.Grid;
using Moose.Graphics.Elements;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.UI.Terminal;

sealed class TerminalView
{
    const float MinimumFontSize = 4;
    const float DecorationThickness = 1;
    const float BeamWidth = 2;

    readonly Stopwatch blinkClock = Stopwatch.StartNew();
    TerminalPalette palette;
    UiSettings settings;
    Vector2 origin;
    Vector2 cellSize = Vector2.One;
    Rect bounds;
    float? fontSize;
    float defaultFontSize = 11;

    public TerminalView(UiSettings settings)
    {
        this.settings = settings;
        palette = new TerminalPalette(settings.Colors);
    }

    public int Columns { get; private set; } = 1;
    public int Rows { get; private set; } = 1;
    public Color Background => palette.Background;

    public void Configure(UiSettings settings)
    {
        this.settings = settings;
        palette = new TerminalPalette(settings.Colors);
    }

    public void ChangeFontSize(float delta) => fontSize = Math.Max(MinimumFontSize, (fontSize ?? defaultFontSize) + delta);

    public void ResetFontSize() => fontSize = null;

    public void RestartBlink() => blinkClock.Restart();

    public bool Contains(Vector2 position) => bounds.Contains(position);

    public (int Column, int Row) CellAt(Vector2 position)
    {
        var column = (int)MathF.Floor((position.X - origin.X) / cellSize.X);
        var row = (int)MathF.Floor((position.Y - origin.Y) / cellSize.Y);
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
            for (var row = 0; row < Math.Min(Rows, screen.Rows); row++)
            {
                var lineNumber = screen.FirstViewportLine + row;
                if (screen.LineAt(lineNumber) is not { } line) continue;
                var y = origin.Y + row * cellSize.Y;
                var blockCursorColumn = cursor is { Shape: CursorShape.Block } block && block.Row == row ? block.Column : -1;
                PaintBackgrounds(painter, tab, line, lineNumber, y, blockCursorColumn);
                PaintGlyphs(painter, tab, line, lineNumber, y, size, blockCursorColumn);
                if (cursor is { } thin && thin.Row == row && thin.Shape != CursorShape.Block) PaintThinCursor(painter, thin.Shape, thin.Column, y);
            }
        }
    }

    (int Row, int Column, CursorShape Shape)? CursorFor(TerminalScreen screen)
    {
        if (!screen.CursorVisible || screen.ViewportOffset != 0) return null;
        var blinks = screen.CursorBlink ?? settings.Cursor.Blink;
        var interval = Math.Max(1, settings.Cursor.BlinkIntervalMs);
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
        for (var column = 0; column < columns; column++)
        {
            var cell = line.Cells[column];
            if (cell.IsWideContinuation) continue;
            var foreground = CellColors(tab, line, lineNumber, column, blockCursorColumn).Foreground;
            var x = origin.X + column * cellSize.X;
            if (cell.Codepoint != ' ' && !cell.Flags.HasFlag(CellFlags.Hidden)) painter.Glyph(new Rune(cell.Codepoint), new Vector2(x, y), size, foreground);
            if (cell.Flags.HasFlag(CellFlags.Underline)) painter.Fill(new Rect(x, y + cellSize.Y - 2 * DecorationThickness, cell.Width * cellSize.X, DecorationThickness), foreground);
            if (cell.Flags.HasFlag(CellFlags.Strikethrough)) painter.Fill(new Rect(x, y + MathF.Round(cellSize.Y / 2), cell.Width * cellSize.X, DecorationThickness), foreground);
        }
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
