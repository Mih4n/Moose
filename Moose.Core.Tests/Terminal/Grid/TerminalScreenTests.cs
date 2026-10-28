using Moose.Core.Terminal.Grid;

namespace Moose.Core.Tests.Terminal.Grid;

public class TerminalScreenTests
{
    readonly List<string> replies = [];
    readonly TerminalScreen screen;

    public TerminalScreenTests()
    {
        screen = new TerminalScreen(10, 4, scrollbackLines: 100);
        screen.Reply += replies.Add;
    }

    string Row(int row) => screen[row].Text(0, screen.Columns).TrimEnd();

    [Fact]
    public void Print_WritesAndAdvancesCursor()
    {
        screen.Feed("hello");

        Assert.Equal("hello", Row(0));
        Assert.Equal((0, 5), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void Print_AutowrapsAndMarksLineAsSoftWrapped()
    {
        screen.Feed("0123456789ab");

        Assert.Equal("0123456789", Row(0));
        Assert.Equal("ab", Row(1));
        Assert.True(screen[0].Wrapped);
        Assert.False(screen[1].Wrapped);
    }

    [Fact]
    public void Print_DefersWrapUntilNextCharacter()
    {
        screen.Feed("0123456789");

        Assert.Equal((0, 9), (screen.CursorRow, screen.CursorColumn));
        screen.Feed("\r\n");
        Assert.Equal((1, 0), (screen.CursorRow, screen.CursorColumn));
        Assert.False(screen[0].Wrapped);
    }

    [Fact]
    public void Print_WithoutAutowrapOverwritesLastColumn()
    {
        screen.Feed("\e[?7l0123456789ab");

        Assert.Equal("012345678b", Row(0));
        Assert.Equal("", Row(1));
    }

    [Fact]
    public void Print_WideCharacterTakesTwoCellsAndWrapsAsAUnit()
    {
        screen.Feed("12345678中中");

        Assert.Equal(2, screen[0].Cells[8].Width);
        Assert.True(screen[0].Cells[9].IsWideContinuation);
        Assert.Equal('中', screen[1].Cells[0].Codepoint);
        Assert.Equal((1, 2), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void Print_ZeroWidthMarksAreDropped()
    {
        screen.Feed("éx");

        Assert.Equal("ex", Row(0));
    }

    [Fact]
    public void Print_DecSpecialGraphicsTranslatesLineDrawing()
    {
        screen.Feed("\e(0lqk\e(Bq");

        Assert.Equal("┌─┐q", Row(0));
    }

    [Fact]
    public void LineFeed_AtBottomScrollsIntoScrollback()
    {
        screen.Feed("a\r\nb\r\nc\r\nd\r\ne");

        Assert.Equal(["b", "c", "d", "e"], Enumerable.Range(0, 4).Select(Row));
        Assert.Equal(1, screen.Scrollback.Count);
        Assert.Equal("a", screen.Scrollback[0].Text(0, 10).TrimEnd());
    }

    [Fact]
    public void ScrollRegion_ScrollsOnlyInsideMarginsAndSkipsScrollback()
    {
        screen.Feed("top\r\n1\r\n2\r\nbottom");
        screen.Feed("\e[2;3r");
        Assert.Equal((0, 0), (screen.CursorRow, screen.CursorColumn));

        screen.Feed("\e[3;1H\nX");

        Assert.Equal(["top", "2", "X", "bottom"], Enumerable.Range(0, 4).Select(Row));
        Assert.Equal(0, screen.Scrollback.Count);
    }

    [Fact]
    public void ReverseIndex_AtTopMarginScrollsDown()
    {
        screen.Feed("a\r\nb\r\nc\e[H\eM");

        Assert.Equal(["", "a", "b", "c"], Enumerable.Range(0, 4).Select(Row));
    }

    [Fact]
    public void InsertAndDeleteLines_RespectScrollRegion()
    {
        screen.Feed("a\r\nb\r\nc\r\nd\e[1;3r\e[2;1H\e[L");
        Assert.Equal(["a", "", "b", "d"], Enumerable.Range(0, 4).Select(Row));

        screen.Feed("\e[2M");
        Assert.Equal(["a", "", "", "d"], Enumerable.Range(0, 4).Select(Row));
    }

    [Fact]
    public void InsertDeleteAndEraseCharacters()
    {
        screen.Feed("abcdef\e[1;2H\e[2@");
        Assert.Equal("a  bcdef", Row(0));

        screen.Feed("\e[3P");
        Assert.Equal("acdef", Row(0));

        screen.Feed("\e[2X");
        Assert.Equal("a  ef", Row(0));
    }

    [Fact]
    public void EraseInLineAndDisplay()
    {
        screen.Feed("aaaa\r\nbbbb\r\ncccc\e[2;3H\e[K");
        Assert.Equal("bb", Row(1));

        screen.Feed("\e[1K");
        Assert.Equal("", Row(1));

        screen.Feed("\e[J");
        Assert.Equal(["aaaa", "", "", ""], Enumerable.Range(0, 4).Select(Row));

        screen.Feed("\e[2J");
        Assert.Equal("", Row(0));
    }

    [Fact]
    public void Erase_UsesCurrentBackground()
    {
        screen.Feed("\e[41m\e[2J");

        Assert.Equal(CellColor.Indexed(1), screen[3].Cells[9].Background);
    }

    [Fact]
    public void Sgr_SetsAttributesAndColors()
    {
        screen.Feed("\e[1;3;4;7;9;31;102mA\e[38;5;200;48;2;1;2;3mB\e[38:2::10:20:30mC\e[0mD");

        var first = screen[0].Cells[0];
        Assert.Equal(CellFlags.Bold | CellFlags.Italic | CellFlags.Underline | CellFlags.Inverse | CellFlags.Strikethrough, first.Flags);
        Assert.Equal(CellColor.Indexed(1), first.Foreground);
        Assert.Equal(CellColor.Indexed(10), first.Background);
        Assert.Equal(CellColor.Indexed(200), screen[0].Cells[1].Foreground);
        Assert.Equal(CellColor.FromRgb(1, 2, 3), screen[0].Cells[1].Background);
        Assert.Equal(CellColor.FromRgb(10, 20, 30), screen[0].Cells[2].Foreground);
        Assert.Equal(new Cell('D', CellColor.Default, CellColor.Default, CellFlags.None, 1, 0), screen[0].Cells[3]);
    }

    [Fact]
    public void Sgr_PartialResets()
    {
        screen.Feed("\e[1;2;4;8m\e[22;24mA");

        Assert.Equal(CellFlags.Hidden, screen[0].Cells[0].Flags);
    }

    [Fact]
    public void CursorMovement_ClampsToScreen()
    {
        screen.Feed("\e[10;20H");
        Assert.Equal((3, 9), (screen.CursorRow, screen.CursorColumn));

        screen.Feed("\e[2;3H\e[A\e[2C\e[D");
        Assert.Equal((0, 3), (screen.CursorRow, screen.CursorColumn));

        screen.Feed("\e[99A\e[99D");
        Assert.Equal((0, 0), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void OriginMode_AddressesRelativeToScrollRegion()
    {
        screen.Feed("\e[2;3r\e[?6h\e[1;1HX\e[5;1HY");

        Assert.Equal("X", Row(1));
        Assert.Equal("Y", Row(2));
    }

    [Fact]
    public void SaveAndRestoreCursor_IncludesAttributes()
    {
        screen.Feed("\e[2;3H\e[31m\e7\e[H\e[0m\e8A");

        Assert.Equal(CellColor.Indexed(1), screen[1].Cells[2].Foreground);
        screen.Feed("\e[4;4H\e[s\e[H\e[uB");
        Assert.Equal('B', screen[3].Cells[3].Codepoint);
    }

    [Fact]
    public void Tabs_UseDefaultAndCustomStops()
    {
        screen.Feed("\tX");
        Assert.Equal('X', screen[0].Cells[8].Codepoint);

        screen.Feed("\r\e[3g\e[1;3H\eH\r\tY");
        Assert.Equal('Y', screen[0].Cells[2].Codepoint);
    }

    [Fact]
    public void AlternateScreen1049_PreservesPrimaryAndCursor()
    {
        screen.Feed("shell$ \e[?1049h");
        Assert.True(screen.IsAlternateScreen);
        Assert.Equal("", Row(0));

        screen.Feed("\e[Hvim");
        Assert.Equal("vim", Row(0));

        screen.Feed("\e[?1049l");
        Assert.False(screen.IsAlternateScreen);
        Assert.Equal("shell$", Row(0));
        Assert.Equal((0, 7), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void AlternateScreen_DoesNotFeedScrollback()
    {
        screen.Feed("\e[?1049h1\r\n2\r\n3\r\n4\r\n5\r\n6");

        Assert.Equal(0, screen.Scrollback.Count);
        Assert.Equal("6", Row(3));
    }

    [Fact]
    public void Modes_TrackInputRelatedFlags()
    {
        screen.Feed("\e[?1h\e[?2004h\e[?1002h\e[?1006h\e[?1004h\e[?25l\e[5 q");

        Assert.True(screen.ApplicationCursorKeys);
        Assert.True(screen.BracketedPaste);
        Assert.Equal(MouseTracking.ButtonEvent, screen.MouseTracking);
        Assert.True(screen.SgrMouse);
        Assert.True(screen.FocusReporting);
        Assert.False(screen.CursorVisible);
        Assert.Equal(CursorShape.Beam, screen.CursorShape);
        Assert.True(screen.CursorBlink);

        screen.Feed("\e[?1l\e[?1002l\e[0 q");
        Assert.False(screen.ApplicationCursorKeys);
        Assert.Equal(MouseTracking.None, screen.MouseTracking);
        Assert.Equal(CursorShape.Default, screen.CursorShape);
    }

    [Fact]
    public void DeviceQueries_Reply()
    {
        screen.Feed("\e[c\e[>c\e[5n\e[2;4H\e[6n");

        Assert.Equal(["\e[?62;22c", "\e[>1;10;0c", "\e[0n", "\e[2;4R"], replies);
    }

    [Fact]
    public void Osc_UpdatesTitleCwdHyperlinksAndClipboard()
    {
        var titles = new List<string>();
        var clipboard = new List<string>();
        screen.TitleChanged += titles.Add;
        screen.ClipboardSet += clipboard.Add;

        screen.Feed("\e]2;my title\a\e]7;file://host/home/user/My%20Dir\e\\");
        screen.Feed("\e]8;;https://example.com\aL\e]8;;\aN");
        screen.Feed("\e]52;c;aGVsbG8=\a\e]52;c;?\a");

        Assert.Equal(["my title"], titles);
        Assert.Equal("/home/user/My Dir", screen.WorkingDirectory);
        Assert.Equal("https://example.com", screen.Hyperlink(screen[0].Cells[0].Hyperlink));
        Assert.Equal(0, screen[0].Cells[1].Hyperlink);
        Assert.Equal(["hello"], clipboard);
    }

    [Fact]
    public void Bell_RaisesEvent()
    {
        var rings = 0;
        screen.Bell += () => rings++;

        screen.Feed("\a");

        Assert.Equal(1, rings);
    }

    [Fact]
    public void Scrollback_RespectsCapacity()
    {
        var small = new TerminalScreen(5, 2, scrollbackLines: 3);

        small.Feed("1\r\n2\r\n3\r\n4\r\n5\r\n6");

        Assert.Equal(3, small.Scrollback.Count);
        Assert.Equal("2", small.Scrollback[0].Text(0, 5).TrimEnd());
        Assert.Equal(4, small.Scrollback.TotalPushed);
    }

    [Fact]
    public void Viewport_ScrollsBackAndResolvesLines()
    {
        screen.Feed("a\r\nb\r\nc\r\nd\r\ne\r\nf");
        screen.ScrollViewport(2);

        Assert.Equal(2, screen.ViewportOffset);
        Assert.Equal("a", screen.LineAt(screen.FirstViewportLine)!.Text(0, 10).TrimEnd());

        screen.ScrollViewport(100);
        Assert.Equal(2, screen.ViewportOffset);

        screen.ResetViewport();
        Assert.Equal("c", screen.LineAt(screen.FirstViewportLine)!.Text(0, 10).TrimEnd());
    }

    [Fact]
    public void GetText_JoinsSoftWrappedLinesAndTrimsHardLines()
    {
        screen.Feed("0123456789ab\r\nxy   ");
        var top = screen.FirstViewportLine;

        var text = screen.GetText(new Selection(new GridPoint(top, 0), new GridPoint(top + 2, 9)));

        Assert.Equal("0123456789ab\nxy", text);
    }

    [Fact]
    public void Resize_ShrinkingPushesLinesAboveCursorIntoScrollback()
    {
        screen.Feed("a\r\nb\r\nc\r\nd");

        screen.Resize(5, 2);

        Assert.Equal(["c", "d"], Enumerable.Range(0, 2).Select(Row));
        Assert.Equal(2, screen.Scrollback.Count);
        Assert.Equal((1, 1), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void Resize_ShrinkingDropsEmptyLinesBelowCursorFirst()
    {
        screen.Feed("prompt");

        screen.Resize(10, 2);

        Assert.Equal("prompt", Row(0));
        Assert.Equal(0, screen.Scrollback.Count);
    }

    [Fact]
    public void Resize_GrowingPullsLinesBackFromScrollback()
    {
        screen.Feed("a\r\nb\r\nc\r\nd\r\ne");

        screen.Resize(12, 5);

        Assert.Equal(["a", "b", "c", "d", "e"], Enumerable.Range(0, 5).Select(Row));
        Assert.Equal(12, screen[0].Length);
        Assert.Equal(4, screen.CursorRow);
    }

    [Fact]
    public void Resize_ToSmallSizesDoesNotThrow()
    {
        screen.Feed("中中中中\e[?1049hxx");

        screen.Resize(1, 1);
        screen.Feed("中abc\e[?1049l");
        screen.Resize(30, 10);

        Assert.Equal(30, screen.Columns);
    }

    [Fact]
    public void Reset_ClearsScreenAndModes()
    {
        screen.Feed("abc\e[?1h\e[31m\ecX");

        Assert.Equal("X", Row(0));
        Assert.False(screen.ApplicationCursorKeys);
        Assert.Equal(CellColor.Default, screen[0].Cells[0].Foreground);
    }
}
