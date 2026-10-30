using Moose.Core;
using Moose.Core.Settings;
using Moose.UI.Input;

namespace Moose.UI;

/// Snapshot of the settings the UI caches between reloads; anything read per frame can call `Configuration` directly.
sealed record UiSettings(
    WindowOptions Window,
    CursorOptions Cursor,
    MouseOptions Mouse,
    ScrollbackOptions Scrollback,
    UiTheme Theme,
    IReadOnlyList<Keybinding> Keybindings
)
{
    public static UiSettings Load() => new(
        WindowOptions.Load(),
        CursorOptions.Load(),
        MouseOptions.Load(),
        ScrollbackOptions.Load(),
        new UiTheme(),
        LoadKeybindings());

    // built-in bindings come first in the merged array, so a user binding for the same chord shadows them (lookups take the last match)
    static IReadOnlyList<Keybinding> LoadKeybindings() =>
    [
        .. Configuration.Children(Keys.Keybindings.Section)
            .Select(binding => (
                Chord: KeyChord.Parse(Configuration.String(Configuration.Key(binding, Keys.Keybindings.Keys))),
                Action: Configuration.String(Configuration.Key(binding, Keys.Keybindings.Action))))
            .Where(binding => binding.Chord != null)
            .Select(binding => new Keybinding(binding.Chord!.Value, binding.Action)),
    ];
}
