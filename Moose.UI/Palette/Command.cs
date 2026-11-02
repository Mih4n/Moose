namespace Moose.UI.Palette;

sealed record Command(string Action, string Title)
{
    const string SwitchTabPrefix = "switch_tab:";

    public static Command SwitchTab(int index, string title) => new($"{SwitchTabPrefix}{index}", $"Tab: {title}");

    public static int? TabIndex(string action) =>
        action.StartsWith(SwitchTabPrefix, StringComparison.Ordinal) && int.TryParse(action[SwitchTabPrefix.Length..], out var index) ? index : null;

    public static IReadOnlyList<Command> All { get; } =
    [
        new("new_tab", "New tab"),
        new("close_tab", "Close tab"),
        new("next_tab", "Next tab"),
        new("previous_tab", "Previous tab"),
        new("new_window", "New window"),
        new("copy", "Copy selection"),
        new("paste", "Paste"),
        new("scroll_line_up", "Scroll line up"),
        new("scroll_line_down", "Scroll line down"),
        new("scroll_page_up", "Scroll page up"),
        new("scroll_page_down", "Scroll page down"),
        new("scroll_to_top", "Scroll to top"),
        new("scroll_to_bottom", "Scroll to bottom"),
        new("font_size_increase", "Increase font size"),
        new("font_size_decrease", "Decrease font size"),
        new("font_size_reset", "Reset font size"),
        new("reload_config", "Reload configuration"),
    ];
}
