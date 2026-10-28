namespace Moose.Core.Terminal.Grid;

internal readonly record struct SavedCursor(
    int Row,
    int Column,
    CellColor Foreground,
    CellColor Background,
    CellFlags Flags,
    bool OriginMode,
    bool WrapPending,
    bool LineDrawingG0,
    bool LineDrawingG1,
    bool ShiftOut
);
