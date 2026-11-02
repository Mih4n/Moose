namespace Moose.UI.Palette;

sealed record Command(string Action, string Title)
{
    const string SwitchTabPrefix = "switch_tab:";
    const string OpenSessionPrefix = "open_session:";
    const string SaveSessionPrefix = "save_session_as:";
    const string RenameTabPrefix = "rename_tab:";

    public static Command SwitchTab(int index, string title) => new($"{SwitchTabPrefix}{index}", $"Tab: {title}");

    public static Command OpenSession(string name) => new($"{OpenSessionPrefix}{name}", $"Open session: {name}");

    public static Command? SaveSessionAs(string name) => name.Length > 0 ? new($"{SaveSessionPrefix}{name}", $"Save session as \"{name}\"") : null;

    /// An empty name hands the tab back to the title the shell sets.
    public static Command RenameTab(string name) => name.Length > 0 ? new($"{RenameTabPrefix}{name}", $"Rename tab to \"{name}\"") : new(RenameTabPrefix, "Reset tab name");

    /// The new name, empty to reset it, or null when the action isn't a rename.
    public static string? TabName(string action) => action.StartsWith(RenameTabPrefix, StringComparison.Ordinal) ? action[RenameTabPrefix.Length..] : null;

    public static string? SessionToOpen(string action) => Argument(action, OpenSessionPrefix);

    public static string? SessionToSave(string action) => Argument(action, SaveSessionPrefix);

    static string? Argument(string action, string prefix) =>
        action.StartsWith(prefix, StringComparison.Ordinal) && action.Length > prefix.Length ? action[prefix.Length..] : null;

    public static int? TabIndex(string action) =>
        action.StartsWith(SwitchTabPrefix, StringComparison.Ordinal) && int.TryParse(action[SwitchTabPrefix.Length..], out var index) ? index : null;

    public static IReadOnlyList<Command> All { get; } =
    [
        new("new_tab", "New tab"),
        new("close_tab", "Close tab"),
        new("rename_tab", "Rename tab…"),
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
        new("save_session", "Save session"),
        new("save_session_as", "Save session as…"),
        new("restore_session", "Restore previous session"),
    ];
}
