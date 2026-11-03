using System.Text;
using Moose.Core.Terminal.Images;
using Moose.Core.Terminal.Parsing;

namespace Moose.Core.Terminal.Grid;

public sealed partial class TerminalScreen : IVtHandler
{
    const string LineDrawing = "◆▒␉␌␍␊°±␤␋┘┐┌└┼⎺⎻─⎼⎽├┤┴┬│≤≥π≠£·";
    const int TabWidth = 8;
    const string PrimaryDeviceAttributes = "\e[?62;22c";
    const string SecondaryDeviceAttributes = "\e[>1;10;0c";

    readonly VtParser parser;
    readonly Scrollback scrollback;
    readonly Dictionary<string, int> hyperlinkIds = [];
    readonly List<string> hyperlinks = [""];
    Line[] primaryLines;
    Line[] alternateLines;
    bool[] tabStops;
    SavedCursor? primarySavedCursor;
    SavedCursor? alternateSavedCursor;
    CellColor foreground;
    CellColor background;
    CellFlags flags;
    int hyperlink;
    bool wrapPending;
    bool lineDrawingG0;
    bool lineDrawingG1;
    bool shiftOut;
    int scrollTop;
    int scrollBottom;
    int lastPrinted = ' ';

    public TerminalScreen(int columns, int rows, int scrollbackLines)
    {
        parser = new VtParser(this);
        scrollback = new Scrollback(scrollbackLines);
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        primaryLines = CreateLines(Rows);
        alternateLines = CreateLines(Rows);
        tabStops = CreateTabStops(Columns);
        scrollBottom = Rows - 1;
    }

    public event Action<string>? Reply;
    public event Action<string>? TitleChanged;
    public event Action<string>? WorkingDirectoryChanged;
    public event Action? Bell;
    public event Action<string>? ClipboardSet;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CursorRow { get; private set; }
    public int CursorColumn { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public CursorShape CursorShape { get; private set; }
    public bool? CursorBlink { get; private set; }
    public bool IsAlternateScreen { get; private set; }
    public bool AutoWrap { get; private set; } = true;
    public bool OriginMode { get; private set; }
    public bool InsertMode { get; private set; }
    public bool NewLineMode { get; private set; }
    public bool ApplicationCursorKeys { get; private set; }
    public bool ApplicationKeypad { get; private set; }
    public bool BracketedPaste { get; private set; }
    public bool FocusReporting { get; private set; }
    public MouseTracking MouseTracking { get; private set; }
    public bool SgrMouse { get; private set; }
    public string Title { get; private set; } = "";
    public string? WorkingDirectory { get; private set; }
    public long Version { get; private set; }
    public int ViewportOffset { get; private set; }
    public int ScrollTop => scrollTop;
    public int ScrollBottom => scrollBottom;
    public Scrollback Scrollback => scrollback;
    public long FirstViewportLine => scrollback.TotalPushed - ViewportOffset;

    public Line this[int row] => Lines[row];

    Line[] Lines => IsAlternateScreen ? alternateLines : primaryLines;

    public void Feed(ReadOnlySpan<byte> data)
    {
        parser.Feed(data);
        Version++;
    }

    public void Feed(string text) => Feed(Encoding.UTF8.GetBytes(text));

    public string Hyperlink(int id) => id > 0 && id < hyperlinks.Count ? hyperlinks[id] : "";

    public Line? LineAt(long absoluteLine)
    {
        var relative = absoluteLine - (scrollback.TotalPushed - scrollback.Count);
        if (relative < 0) return null;
        if (relative < scrollback.Count) return scrollback[(int)relative];
        var row = relative - scrollback.Count;
        return row < Rows ? Lines[row] : null;
    }

    public void ScrollViewport(int lines)
    {
        if (IsAlternateScreen) return;
        ViewportOffset = Math.Clamp(ViewportOffset + lines, 0, scrollback.Count);
        Version++;
    }

    public void ResetViewport()
    {
        if (ViewportOffset == 0) return;
        ViewportOffset = 0;
        Version++;
    }

    public string GetText(Selection selection)
    {
        var builder = new StringBuilder();
        for (var number = selection.Start.Line; number <= selection.End.Line; number++)
        {
            if (LineAt(number) is not { } line) continue;
            var first = number == selection.Start.Line ? selection.Start.Column : 0;
            var last = number == selection.End.Line ? selection.End.Column + 1 : line.Length;
            var text = line.Text(first, last);
            var continues = number < selection.End.Line;
            if (continues && line.Wrapped)
            {
                builder.Append(text);
                continue;
            }
            builder.Append(text.TrimEnd(' '));
            if (continues) builder.Append('\n');
        }
        return builder.ToString();
    }

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows) return;

        var blank = Cell.Blank(CellColor.Default);
        foreach (var line in primaryLines) line.Resize(columns, blank);
        foreach (var line in alternateLines) line.Resize(columns, blank);
        Columns = columns;
        primaryLines = ResizeRows(primaryLines, rows, tracksCursor: !IsAlternateScreen);
        alternateLines = ResizeRows(alternateLines, rows, tracksCursor: false);
        Rows = rows;

        var stops = CreateTabStops(columns);
        Array.Copy(tabStops, stops, Math.Min(tabStops.Length, stops.Length));
        tabStops = stops;
        scrollTop = 0;
        scrollBottom = Rows - 1;
        CursorRow = Math.Clamp(CursorRow, 0, Rows - 1);
        CursorColumn = Math.Clamp(CursorColumn, 0, Columns - 1);
        wrapPending = false;
        ViewportOffset = Math.Min(ViewportOffset, scrollback.Count);
        Version++;
    }

    public void Print(Rune rune)
    {
        var codepoint = rune.Value;
        var lineDrawing = shiftOut ? lineDrawingG1 : lineDrawingG0;
        if (lineDrawing && codepoint is >= 0x60 and <= 0x7e) codepoint = LineDrawing[codepoint - 0x60];
        var width = CharWidth.Of(codepoint);
        if (width == 0)
        {
            AttachPlaceholderDiacritic(codepoint);
            return;
        }

        if (wrapPending && AutoWrap) WrapToNextLine();
        if (width == 2 && CursorColumn == Columns - 1)
        {
            if (!AutoWrap || Columns < 2) return;
            Lines[CursorRow].Cells[CursorColumn] = Blank();
            WrapToNextLine();
        }
        if (InsertMode) InsertCells(width);

        var cells = Lines[CursorRow].Cells;
        ClearWideOverlap(cells, CursorColumn, width);
        cells[CursorColumn] = new Cell(codepoint, foreground, background, flags, (byte)width, codepoint == ImagePlaceholder.Codepoint ? 0 : hyperlink);
        if (width == 2) cells[CursorColumn + 1] = new Cell(' ', foreground, background, flags, 0, hyperlink);
        lastPrinted = codepoint;

        if (CursorColumn + width >= Columns)
        {
            CursorColumn = Columns - 1;
            wrapPending = true;
        }
        else CursorColumn += width;
    }

    public void Execute(byte control)
    {
        switch (control)
        {
            case 0x07: Bell?.Invoke(); break;
            case 0x08: if (CursorColumn > 0) CursorColumn--; wrapPending = false; break;
            case 0x09: TabForward(); break;
            case 0x0a or 0x0b or 0x0c: LineFeed(); break;
            case 0x0d: CursorColumn = 0; wrapPending = false; break;
            case 0x0e: shiftOut = true; break;
            case 0x0f: shiftOut = false; break;
        }
    }

    public void EscapeDispatch(ReadOnlySpan<byte> intermediates, byte final)
    {
        if (intermediates.Length == 1 && intermediates[0] is (byte)'(' or (byte)')')
        {
            if (intermediates[0] == '(') lineDrawingG0 = final == '0';
            else lineDrawingG1 = final == '0';
            return;
        }
        if (intermediates.Length > 0) return;
        switch ((char)final)
        {
            case '7': SaveCursor(); break;
            case '8': RestoreCursor(); break;
            case 'D': Index(); break;
            case 'E': CursorColumn = 0; Index(); break;
            case 'M': ReverseIndex(); break;
            case 'H': tabStops[CursorColumn] = true; break;
            case 'c': Reset(); break;
            case '=': ApplicationKeypad = true; break;
            case '>': ApplicationKeypad = false; break;
        }
    }

    public void CsiDispatch(CsiParameters parameters, byte privateMarker, ReadOnlySpan<byte> intermediates, byte final)
    {
        if (intermediates.Length > 0)
        {
            DispatchIntermediateCsi(parameters, privateMarker, intermediates[0], final);
            return;
        }
        if (privateMarker == '?')
        {
            if (final == 'h') SetPrivateModes(parameters, enable: true);
            else if (final == 'l') SetPrivateModes(parameters, enable: false);
            return;
        }
        if (privateMarker == '>')
        {
            if (final == 'c') Respond(SecondaryDeviceAttributes);
            return;
        }
        if (privateMarker != 0) return;

        var count = parameters.Get(0, 1);
        switch ((char)final)
        {
            case '@': InsertCells(count); break;
            case 'A': CursorUp(count); break;
            case 'B' or 'e': CursorDown(count); break;
            case 'C' or 'a': SetColumn(CursorColumn + count); break;
            case 'D': SetColumn(CursorColumn - count); break;
            case 'E': CursorDown(count); CursorColumn = 0; break;
            case 'F': CursorUp(count); CursorColumn = 0; break;
            case 'G' or '`': SetColumn(count - 1); break;
            case 'H' or 'f': MoveCursorTo(parameters.Get(0, 1) - 1, parameters.Get(1, 1) - 1); break;
            case 'I': for (var step = 0; step < count; step++) TabForward(); break;
            case 'J': EraseInDisplay(parameters[0]); break;
            case 'K': EraseInLine(parameters[0]); break;
            case 'L': InsertLines(count); break;
            case 'M': DeleteLines(count); break;
            case 'P': DeleteCells(count); break;
            case 'S': ScrollUp(count); break;
            case 'T': ScrollDown(count); break;
            case 'X': EraseCells(count); break;
            case 'Z': for (var step = 0; step < count; step++) TabBackward(); break;
            case 'b': Repeat(count); break;
            case 'c': if (parameters[0] == 0) Respond(PrimaryDeviceAttributes); break;
            case 'd': MoveCursorTo(count - 1, CursorColumn); break;
            case 'g': ClearTabStops(parameters[0]); break;
            case 'h': SetModes(parameters, enable: true); break;
            case 'l': SetModes(parameters, enable: false); break;
            case 'm': SelectGraphicRendition(parameters); break;
            case 'n': DeviceStatusReport(parameters[0]); break;
            case 'r': SetScrollRegion(parameters.Get(0, 1) - 1, parameters.Get(1, Rows) - 1); break;
            case 's': if (parameters.Count == 0) SaveCursor(); break;
            case 't': ReportWindow(parameters[0]); break;
            case 'u': RestoreCursor(); break;
        }
    }

    public void OscDispatch(ReadOnlySpan<byte> payload)
    {
        var text = Encoding.UTF8.GetString(payload);
        var separator = text.IndexOf(';');
        var command = separator < 0 ? text : text[..separator];
        var argument = separator < 0 ? "" : text[(separator + 1)..];
        switch (command)
        {
            case "0" or "2":
                Title = argument;
                TitleChanged?.Invoke(argument);
                break;
            case "7":
                if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri) || !uri.IsFile) break;
                WorkingDirectory = Uri.UnescapeDataString(uri.AbsolutePath);
                WorkingDirectoryChanged?.Invoke(WorkingDirectory);
                break;
            case "8":
                var target = argument[(argument.IndexOf(';') + 1)..];
                hyperlink = target.Length == 0 ? 0 : HyperlinkId(target);
                break;
            case "52":
                SetClipboard(argument[(argument.IndexOf(';') + 1)..]);
                break;
        }
    }

    void DispatchIntermediateCsi(CsiParameters parameters, byte privateMarker, byte intermediate, byte final)
    {
        if (privateMarker != 0) return;
        if (intermediate == ' ' && final == 'q') SetCursorStyle(parameters[0]);
        else if (intermediate == '!' && final == 'p') SoftReset();
    }

    void SetCursorStyle(int style)
    {
        (CursorShape, CursorBlink) = style switch
        {
            0 => (CursorShape.Default, (bool?)null),
            1 => (CursorShape.Block, true),
            2 => (CursorShape.Block, false),
            3 => (CursorShape.Underline, true),
            4 => (CursorShape.Underline, false),
            5 => (CursorShape.Beam, true),
            6 => (CursorShape.Beam, false),
            _ => (CursorShape, CursorBlink),
        };
    }

    void SetModes(CsiParameters parameters, bool enable)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            if (parameters[index] == 4) InsertMode = enable;
            else if (parameters[index] == 20) NewLineMode = enable;
        }
    }

    void SetPrivateModes(CsiParameters parameters, bool enable)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            switch (parameters[index])
            {
                case 1: ApplicationCursorKeys = enable; break;
                case 6: OriginMode = enable; MoveCursorTo(0, 0); break;
                case 7: AutoWrap = enable; wrapPending = false; break;
                case 12: CursorBlink = enable; break;
                case 25: CursorVisible = enable; break;
                case 47: SwitchScreen(enable, clearOnEnter: false, clearOnLeave: false, keepCursor: false); break;
                case 1047: SwitchScreen(enable, clearOnEnter: false, clearOnLeave: true, keepCursor: false); break;
                case 1048: if (enable) SaveCursor(); else RestoreCursor(); break;
                case 1049: SwitchScreen(enable, clearOnEnter: true, clearOnLeave: false, keepCursor: true); break;
                case 1000: MouseTracking = enable ? MouseTracking.Normal : MouseTracking.None; break;
                case 1002: MouseTracking = enable ? MouseTracking.ButtonEvent : MouseTracking.None; break;
                case 1003: MouseTracking = enable ? MouseTracking.AnyEvent : MouseTracking.None; break;
                case 1004: FocusReporting = enable; break;
                case 1006: SgrMouse = enable; break;
                case 2004: BracketedPaste = enable; break;
            }
        }
    }

    void SwitchScreen(bool enable, bool clearOnEnter, bool clearOnLeave, bool keepCursor)
    {
        if (enable == IsAlternateScreen) return;
        if (enable)
        {
            if (keepCursor) SaveCursor();
            IsAlternateScreen = true;
            ViewportOffset = 0;
            if (clearOnEnter) ClearLines(alternateLines);
            return;
        }
        if (clearOnLeave) ClearLines(alternateLines);
        IsAlternateScreen = false;
        if (keepCursor) RestoreCursor();
    }

    void SelectGraphicRendition(CsiParameters parameters)
    {
        if (parameters.Count == 0)
        {
            ResetPen();
            return;
        }
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            switch (parameter)
            {
                case 0: ResetPen(); break;
                case 1: flags |= CellFlags.Bold; break;
                case 2: flags |= CellFlags.Dim; break;
                case 3: flags |= CellFlags.Italic; break;
                case 4:
                    var underlined = !parameters.IsSubparameter(index + 1) || parameters[index + 1] != 0;
                    flags = underlined ? flags | CellFlags.Underline : flags & ~CellFlags.Underline;
                    while (parameters.IsSubparameter(index + 1)) index++;
                    break;
                case 5 or 6: flags |= CellFlags.Blink; break;
                case 7: flags |= CellFlags.Inverse; break;
                case 8: flags |= CellFlags.Hidden; break;
                case 9: flags |= CellFlags.Strikethrough; break;
                case 21: flags |= CellFlags.Underline; break;
                case 22: flags &= ~(CellFlags.Bold | CellFlags.Dim); break;
                case 23: flags &= ~CellFlags.Italic; break;
                case 24: flags &= ~CellFlags.Underline; break;
                case 25: flags &= ~CellFlags.Blink; break;
                case 27: flags &= ~CellFlags.Inverse; break;
                case 28: flags &= ~CellFlags.Hidden; break;
                case 29: flags &= ~CellFlags.Strikethrough; break;
                case >= 30 and <= 37: foreground = CellColor.Indexed(parameter - 30); break;
                case 38: foreground = ExtendedColor(parameters, ref index) ?? foreground; break;
                case 39: foreground = CellColor.Default; break;
                case >= 40 and <= 47: background = CellColor.Indexed(parameter - 40); break;
                case 48: background = ExtendedColor(parameters, ref index) ?? background; break;
                case 49: background = CellColor.Default; break;
                case 58: ExtendedColor(parameters, ref index); break;
                case >= 90 and <= 97: foreground = CellColor.Indexed(parameter - 90 + 8); break;
                case >= 100 and <= 107: background = CellColor.Indexed(parameter - 100 + 8); break;
            }
        }
    }

    static CellColor? ExtendedColor(CsiParameters parameters, ref int index)
    {
        if (parameters.IsSubparameter(index + 1))
        {
            var first = index + 1;
            var end = first;
            while (parameters.IsSubparameter(end)) end++;
            index = end - 1;
            var length = end - first;
            if (parameters[first] == 5 && length >= 2) return CellColor.Indexed(parameters[first + 1]);
            if (parameters[first] == 2 && length >= 4) return CellColor.FromRgb(parameters[end - 3], parameters[end - 2], parameters[end - 1]);
            return null;
        }
        if (parameters[index + 1] == 5 && index + 2 < parameters.Count)
        {
            index += 2;
            return CellColor.Indexed(parameters[index]);
        }
        if (parameters[index + 1] == 2 && index + 4 < parameters.Count)
        {
            index += 4;
            return CellColor.FromRgb(parameters[index - 2], parameters[index - 1], parameters[index]);
        }
        return null;
    }

    void DeviceStatusReport(int request)
    {
        if (request == 5) Respond("\e[0n");
        else if (request == 6) Respond($"\e[{CursorRow - (OriginMode ? scrollTop : 0) + 1};{CursorColumn + 1}R");
    }

    void SetClipboard(string data)
    {
        if (data == "?") return;
        try
        {
            ClipboardSet?.Invoke(Encoding.UTF8.GetString(Convert.FromBase64String(data)));
        }
        catch (FormatException)
        {
        }
    }

    int HyperlinkId(string target)
    {
        if (hyperlinkIds.TryGetValue(target, out var id)) return id;
        hyperlinks.Add(target);
        return hyperlinkIds[target] = hyperlinks.Count - 1;
    }

    void Respond(string response) => Reply?.Invoke(response);

    void WrapToNextLine()
    {
        Lines[CursorRow].Wrapped = true;
        CursorColumn = 0;
        Index();
    }

    void LineFeed()
    {
        if (NewLineMode) CursorColumn = 0;
        Index();
    }

    void Index()
    {
        wrapPending = false;
        if (CursorRow == scrollBottom) ScrollUp(1);
        else if (CursorRow < Rows - 1) CursorRow++;
    }

    void ReverseIndex()
    {
        wrapPending = false;
        if (CursorRow == scrollTop) ScrollDown(1);
        else if (CursorRow > 0) CursorRow--;
    }

    void ScrollUp(int count)
    {
        var lines = Lines;
        count = Math.Min(count, scrollBottom - scrollTop + 1);
        for (var step = 0; step < count; step++)
        {
            if (scrollTop == 0 && !IsAlternateScreen)
            {
                scrollback.Push(lines[0]);
                if (ViewportOffset > 0) ViewportOffset = Math.Min(ViewportOffset + 1, scrollback.Count);
            }
            Array.Copy(lines, scrollTop + 1, lines, scrollTop, scrollBottom - scrollTop);
            lines[scrollBottom] = NewLine();
        }
    }

    void ScrollDown(int count)
    {
        var lines = Lines;
        count = Math.Min(count, scrollBottom - scrollTop + 1);
        for (var step = 0; step < count; step++)
        {
            Array.Copy(lines, scrollTop, lines, scrollTop + 1, scrollBottom - scrollTop);
            lines[scrollTop] = NewLine();
        }
    }

    void InsertLines(int count)
    {
        if (CursorRow < scrollTop || CursorRow > scrollBottom) return;
        var lines = Lines;
        count = Math.Min(count, scrollBottom - CursorRow + 1);
        for (var row = scrollBottom; row >= CursorRow + count; row--) lines[row] = lines[row - count];
        for (var row = CursorRow; row < CursorRow + count; row++) lines[row] = NewLine();
        CursorColumn = 0;
        wrapPending = false;
    }

    void DeleteLines(int count)
    {
        if (CursorRow < scrollTop || CursorRow > scrollBottom) return;
        var lines = Lines;
        count = Math.Min(count, scrollBottom - CursorRow + 1);
        for (var row = CursorRow; row <= scrollBottom - count; row++) lines[row] = lines[row + count];
        for (var row = scrollBottom - count + 1; row <= scrollBottom; row++) lines[row] = NewLine();
        CursorColumn = 0;
        wrapPending = false;
    }

    void InsertCells(int count)
    {
        var cells = Lines[CursorRow].Cells;
        count = Math.Min(count, Columns - CursorColumn);
        Array.Copy(cells, CursorColumn, cells, CursorColumn + count, Columns - CursorColumn - count);
        Lines[CursorRow].Fill(CursorColumn, CursorColumn + count, Blank());
        wrapPending = false;
    }

    void DeleteCells(int count)
    {
        var cells = Lines[CursorRow].Cells;
        count = Math.Min(count, Columns - CursorColumn);
        Array.Copy(cells, CursorColumn + count, cells, CursorColumn, Columns - CursorColumn - count);
        Lines[CursorRow].Fill(Columns - count, Columns, Blank());
        wrapPending = false;
    }

    void EraseCells(int count)
    {
        Lines[CursorRow].Fill(CursorColumn, CursorColumn + count, Blank());
        wrapPending = false;
    }

    void EraseInLine(int mode)
    {
        var line = Lines[CursorRow];
        if (mode == 0) line.Fill(CursorColumn, Columns, Blank());
        else if (mode == 1) line.Fill(0, CursorColumn + 1, Blank());
        else if (mode == 2) line.Fill(0, Columns, Blank());
        if (mode != 1) line.Wrapped = false;
        wrapPending = false;
    }

    void EraseInDisplay(int mode)
    {
        var lines = Lines;
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (var row = CursorRow + 1; row < Rows; row++) ClearLine(lines[row]);
                break;
            case 1:
                for (var row = 0; row < CursorRow; row++) ClearLine(lines[row]);
                EraseInLine(1);
                break;
            case 2:
                ClearImagePlacements(everything: false);
                foreach (var line in lines) ClearLine(line);
                break;
            case 3:
                ClearImagePlacements(everything: true);
                scrollback.Clear();
                ViewportOffset = 0;
                break;
        }
    }

    void Repeat(int count)
    {
        var rune = new Rune(lastPrinted);
        for (var step = 0; step < Math.Min(count, Columns * Rows); step++) Print(rune);
    }

    void TabForward()
    {
        var column = CursorColumn + 1;
        while (column < Columns - 1 && !tabStops[column]) column++;
        CursorColumn = Math.Min(column, Columns - 1);
        wrapPending = false;
    }

    void TabBackward()
    {
        var column = CursorColumn - 1;
        while (column > 0 && !tabStops[column]) column--;
        CursorColumn = Math.Max(column, 0);
        wrapPending = false;
    }

    void ClearTabStops(int mode)
    {
        if (mode == 0) tabStops[CursorColumn] = false;
        else if (mode == 3) Array.Clear(tabStops);
    }

    void CursorUp(int count)
    {
        var top = CursorRow >= scrollTop ? scrollTop : 0;
        CursorRow = Math.Max(top, CursorRow - count);
        wrapPending = false;
    }

    void CursorDown(int count)
    {
        var bottom = CursorRow <= scrollBottom ? scrollBottom : Rows - 1;
        CursorRow = Math.Min(bottom, CursorRow + count);
        wrapPending = false;
    }

    void SetColumn(int column)
    {
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        wrapPending = false;
    }

    void MoveCursorTo(int row, int column)
    {
        CursorRow = OriginMode ? Math.Clamp(row + scrollTop, scrollTop, scrollBottom) : Math.Clamp(row, 0, Rows - 1);
        SetColumn(column);
    }

    void SetScrollRegion(int top, int bottom)
    {
        bottom = Math.Min(bottom, Rows - 1);
        if (top >= bottom) return;
        scrollTop = top;
        scrollBottom = bottom;
        MoveCursorTo(0, 0);
    }

    void SaveCursor()
    {
        var saved = new SavedCursor(CursorRow, CursorColumn, foreground, background, flags, OriginMode, wrapPending, lineDrawingG0, lineDrawingG1, shiftOut);
        if (IsAlternateScreen) alternateSavedCursor = saved;
        else primarySavedCursor = saved;
    }

    void RestoreCursor()
    {
        var saved = (IsAlternateScreen ? alternateSavedCursor : primarySavedCursor) ?? default;
        CursorRow = Math.Clamp(saved.Row, 0, Rows - 1);
        CursorColumn = Math.Clamp(saved.Column, 0, Columns - 1);
        foreground = saved.Foreground;
        background = saved.Background;
        flags = saved.Flags;
        OriginMode = saved.OriginMode;
        wrapPending = saved.WrapPending;
        lineDrawingG0 = saved.LineDrawingG0;
        lineDrawingG1 = saved.LineDrawingG1;
        shiftOut = saved.ShiftOut;
    }

    void ResetPen()
    {
        foreground = CellColor.Default;
        background = CellColor.Default;
        flags = CellFlags.None;
    }

    void SoftReset()
    {
        CursorVisible = true;
        OriginMode = false;
        AutoWrap = true;
        InsertMode = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        scrollTop = 0;
        scrollBottom = Rows - 1;
        lineDrawingG0 = false;
        lineDrawingG1 = false;
        shiftOut = false;
        primarySavedCursor = null;
        alternateSavedCursor = null;
        wrapPending = false;
        ResetPen();
    }

    void Reset()
    {
        SoftReset();
        IsAlternateScreen = false;
        NewLineMode = false;
        BracketedPaste = false;
        FocusReporting = false;
        MouseTracking = MouseTracking.None;
        SgrMouse = false;
        CursorShape = CursorShape.Default;
        CursorBlink = null;
        hyperlink = 0;
        ViewportOffset = 0;
        tabStops = CreateTabStops(Columns);
        ResetImages();
        ClearLines(primaryLines);
        ClearLines(alternateLines);
        CursorRow = 0;
        CursorColumn = 0;
    }

    void ClearWideOverlap(Cell[] cells, int column, int width)
    {
        if (cells[column].IsWideContinuation && column > 0) cells[column - 1] = Blank();
        var last = column + width - 1;
        if (cells[last].Width == 2 && last + 1 < Columns) cells[last + 1] = Blank();
    }

    Line[] ResizeRows(Line[] lines, int rows, bool tracksCursor)
    {
        var resized = new List<Line>(lines);
        if (tracksCursor)
        {
            while (resized.Count > rows && resized.Count - 1 > CursorRow && resized[^1].IsBlank()) resized.RemoveAt(resized.Count - 1);
            while (resized.Count > rows)
            {
                scrollback.Push(resized[0]);
                resized.RemoveAt(0);
                CursorRow--;
            }
            while (resized.Count < rows && scrollback.PopNewest() is { } restored)
            {
                restored.Resize(Columns, Cell.Blank(CellColor.Default));
                resized.Insert(0, restored);
                CursorRow++;
            }
        }
        while (resized.Count > rows) resized.RemoveAt(resized.Count - 1);
        while (resized.Count < rows) resized.Add(new Line(Columns, Cell.Blank(CellColor.Default)));
        return [.. resized];
    }

    void ClearLines(Line[] lines)
    {
        foreach (var line in lines)
        {
            ClearLine(line);
            line.Placements = null;
        }
    }

    void ClearLine(Line line)
    {
        line.Fill(0, line.Length, Blank());
        line.Wrapped = false;
    }

    Cell Blank() => Cell.Blank(background);

    Line NewLine() => new(Columns, Blank());

    Line[] CreateLines(int rows) => [.. Enumerable.Range(0, rows).Select(_ => new Line(Columns, Cell.Blank(CellColor.Default)))];

    static bool[] CreateTabStops(int columns) => [.. Enumerable.Range(0, columns).Select(column => column > 0 && column % TabWidth == 0)];
}
