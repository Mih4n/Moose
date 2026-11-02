namespace Moose.Core.Settings;

public sealed class WindowOptions
{
    public string Title { get; init; } = "Moose";
    public bool DynamicTitle { get; init; } = true;
    /// Space between the window edge and the terminal grid, in logical pixels.
    public float PaddingX { get; init; }
    public float PaddingY { get; init; }
    public bool Decorations { get; init; } = true;
    /// Wayland client-side decorations through libdecor pull in cairo, pango and D-Bus, so they're only loaded where the compositor won't decorate.
    public bool Libdecor => Decorations && IsGnome();

    public static WindowOptions Load() => new()
    {
        Title = Configuration.String(Keys.Window.Title, "Moose"),
        DynamicTitle = Configuration.Bool(Keys.Window.DynamicTitle, true),
        PaddingX = Math.Max(0, Configuration.Float(Keys.Window.PaddingX)),
        PaddingY = Math.Max(0, Configuration.Float(Keys.Window.PaddingY)),
        Decorations = Configuration.Bool(Keys.Window.Decorations, true),
    };

    static bool IsGnome() => (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
        .Split(':')
        .Any(desktop => desktop.Equals("GNOME", StringComparison.OrdinalIgnoreCase));
}
