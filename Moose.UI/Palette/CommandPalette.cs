using System.Globalization;
using Moose.Graphics.Elements;
using Moose.Graphics.Primitives;
using Moose.UI.Input;
using Silk.NET.Input;
using static Moose.Graphics.Elements.Ui;
using KeyModifiers = Moose.Graphics.Input.KeyModifiers;

namespace Moose.UI.Palette;

sealed class CommandPalette
{
    const int VisibleRows = 10;
    const float Width = 560;
    const float TopOffset = 72;
    const float InputHeight = 40;
    const float RowHeight = 32;
    const float ListPadding = 4;
    const float ScrollbarWidth = 4;
    const float MinimumThumb = 16;
    static readonly Color Backdrop = new(0, 0, 0, 0.35f);

    string query = "";
    int selected;
    int scroll;
    List<Command> matches = [];
    IReadOnlyList<Command> commands = Command.All;
    // set while the palette asks for free text instead of filtering commands
    Func<string, Command?>? prompt;
    string placeholder = "";

    public bool IsOpen { get; private set; }

    public void Open(IEnumerable<Command> extra)
    {
        commands = [.. Command.All, .. extra];
        prompt = null;
        placeholder = "Type a command…";
        IsOpen = true;
        query = "";
        Refilter();
    }

    /// Asks for text; the only row is the command built from what's typed so far.
    public void Prompt(string hint, Func<string, Command?> command)
    {
        prompt = command;
        placeholder = hint;
        IsOpen = true;
        query = "";
        Refilter();
    }

    public void Close() => IsOpen = false;

    public string? KeyDown(Key key, KeyModifiers modifiers)
    {
        var control = modifiers.HasFlag(KeyModifiers.Control);
        var vim = control || modifiers.HasFlag(KeyModifiers.Alt);
        switch (key)
        {
            case Key.Escape:
            case Key.LeftBracket when control:
                Close();
                break;
            case Key.Enter or Key.KeypadEnter:
                return matches.Count > 0 ? matches[selected].Action : null;
            case Key.Down or Key.Tab:
            case Key.J or Key.N when vim:
                Move(1);
                break;
            case Key.Up:
            case Key.K or Key.P when vim:
                Move(-1);
                break;
            case Key.PageDown:
            case Key.D when vim:
                Move(VisibleRows / 2, wrap: false);
                break;
            case Key.PageUp:
            case Key.U when vim:
                Move(-VisibleRows / 2, wrap: false);
                break;
            case Key.W when control:
            case Key.Backspace when control:
                SetQuery(query[..WordStart(query)]);
                break;
            case Key.Backspace:
            case Key.H when control:
                if (query.Length > 0) SetQuery(query[..StringInfo.ParseCombiningCharacters(query)[^1]]);
                break;
        }
        return null;
    }

    public void TextInput(string text) => SetQuery(query + text);

    public void Scroll(float delta) => scroll = Math.Clamp(scroll - Math.Sign(delta), 0, Math.Max(0, matches.Count - VisibleRows));

    public Element Render(UiTheme theme, IReadOnlyList<Keybinding> keybindings, Action<string> run)
    {
        var hints = new Dictionary<string, string>();
        foreach (var binding in keybindings)
        {
             var chords = binding
                .Chords
                .Select(c => c.ToString());

            hints[binding.Action] = string.Join(" ", chords);
        }

        var rows = Math.Max(1, Math.Min(VisibleRows, matches.Count));
        var listHeight = rows * RowHeight + 2 * ListPadding;
        // the border is drawn inside the panel, so the content is inset by it on every side, plus the divider below the input
        var panelHeight = InputHeight + listHeight + 3 * theme.BorderWidth;

        var panel = Column()
            .H(panelHeight)
            .Padding(theme.BorderWidth)
            .Rounded(8)
            .Bg(theme.Panel)
            .Border(theme.BorderWidth, theme.BorderVariant)
            .OnClick(() => { })
            .Children(
                Input(theme),
                Div()
                    .H(theme.BorderWidth)
                    .Bg(theme.Border),
                Row()
                    .H(listHeight)
                    .Padding(ListPadding)
                    .Gap(ListPadding)
                    .Children(List(theme, hints, run), Scrollbar(theme, listHeight - 2 * ListPadding)));

        return Column()
            .Bg(Backdrop)
            .OnClick(Close)
            .Children(
                Div().H(TopOffset),
                Row()
                    .H(panelHeight)
                    .Children(Div(), panel.W(Width), Div()),
                Div());
    }

    Element Input(UiTheme theme)
    {
        var caret = Div()
            .W(2)
            .Bg(theme.Text);
        return Row()
            .H(InputHeight)
            .Padding(10)
            .Children(query.Length > 0
                ? [Text(query).Color(theme.Text), caret, Div()]
                : [caret, Text(placeholder).Truncate().Color(theme.TextMuted)]);
    }

    Element List(UiTheme theme, Dictionary<string, string> hints, Action<string> run)
    {
        if (matches.Count == 0)
            return Column().Child(Text(prompt != null ? placeholder : "No matching commands").Padding(6).Color(theme.TextMuted));
        return Column().Children(matches.Skip(scroll).Take(VisibleRows).Select((command, offset) =>
        {
            var current = scroll + offset == selected;
            var row = Row()
                .H(RowHeight)
                .Padding(6)
                .Gap(12)
                .Rounded(4)
                .Bg(current ? theme.Selected : Color.Transparent)
                .OnClick(() => run(command.Action))
                .Hover(style =>
                {
                    if (!current) style.Bg(theme.Hover);
                })
                .Child(Text(command.Title).Truncate().Color(current ? theme.Text : theme.TextMuted));
            return hints.TryGetValue(command.Action, out var hint) ? row.Child(Text(hint).Color(theme.TextMuted)) : row;
        }));
    }

    Element Scrollbar(UiTheme theme, float trackHeight)
    {
        if (matches.Count <= VisibleRows) return Div().W(0);
        var thumb = Math.Max(MinimumThumb, trackHeight * VisibleRows / matches.Count);
        var top = (trackHeight - thumb) * scroll / (matches.Count - VisibleRows);
        return Column()
            .W(ScrollbarWidth)
            .Children(
                Div().H(top),
                Div()
                    .H(thumb)
                    .Rounded(ScrollbarWidth / 2)
                    .Bg(theme.TextMuted with { A = 0.5f }),
                Div());
    }

    void SetQuery(string text)
    {
        query = text;
        Refilter();
    }

    void Refilter()
    {
        selected = 0;
        scroll = 0;
        if (prompt != null)
        {
            matches = prompt(query.Trim()) is { } command ? [command] : [];
            return;
        }
        matches = [.. commands
            .Select((command, order) => (command, order, score: FuzzyMatcher.Score(query, command.Title) ?? FuzzyMatcher.Score(query, command.Action)))
            .Where(candidate => candidate.score != null)
            .OrderBy(candidate => candidate.score)
            .ThenBy(candidate => candidate.order)
            .Select(candidate => candidate.command)];
    }

    void Move(int delta, bool wrap = true)
    {
        if (matches.Count == 0) return;
        selected = wrap ? ((selected + delta) % matches.Count + matches.Count) % matches.Count : Math.Clamp(selected + delta, 0, matches.Count - 1);
        if (selected < scroll) scroll = selected;
        else if (selected >= scroll + VisibleRows) scroll = selected - VisibleRows + 1;
    }

    static int WordStart(string text)
    {
        var index = text.TrimEnd().Length;
        while (index > 0 && !char.IsWhiteSpace(text[index - 1])) index--;
        return index;
    }
}
