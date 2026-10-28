using Microsoft.Extensions.Configuration;
using Moose.Core;
using Moose.Core.Settings;
using Moose.UI.Input;

namespace Moose.UI;

sealed record UiSettings(
    WindowOptions Window,
    CursorOptions Cursor,
    MouseOptions Mouse,
    ScrollbackOptions Scrollback,
    ColorOptions Colors,
    UiTheme Theme,
    IReadOnlyList<Keybinding> Keybindings
)
{
    static readonly IConfiguration Defaults = OptionsExtender.LoadDefaults();

    public static UiSettings Load(IConfiguration configuration) => new(
        Bind<WindowOptions>(configuration, "window"),
        Bind<CursorOptions>(configuration, "cursor"),
        Bind<MouseOptions>(configuration, "mouse"),
        Bind<ScrollbackOptions>(configuration, "scrollback"),
        BindOverDefaults<ColorOptions>(configuration, "colors"),
        new UiTheme(BindOverDefaults<UiColorOptions>(configuration, "ui:colors")),
        [.. Bind<List<KeybindingOptions>>(configuration, "keybindings")
            .Select(binding => KeyChord.Parse(binding.Keys) is { } chord ? new Keybinding(chord, binding.Action) : null)
            .OfType<Keybinding>()]);

    static T Bind<T>(IConfiguration configuration, string section) where T : new() => configuration.GetSection(section).Get<T>() ?? new T();

    static T BindOverDefaults<T>(IConfiguration configuration, string section) where T : new()
    {
        var options = new T();
        Defaults.GetSection(section).Bind(options);
        configuration.GetSection(section).Bind(options);
        return options;
    }
}
