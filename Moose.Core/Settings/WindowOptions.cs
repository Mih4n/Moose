namespace Moose.Core.Settings;

public sealed class WindowOptions
{
    public string Title { get; init; } = "Moose";
    public bool DynamicTitle { get; init; } = true;
    /// Space between the window edge and the terminal grid, in logical pixels.
    public float PaddingX { get; init; }
    public float PaddingY { get; init; }

    public static WindowOptions Load() => new()
    {
        Title = Configuration.String(Keys.Window.Title, "Moose"),
        DynamicTitle = Configuration.Bool(Keys.Window.DynamicTitle, true),
        PaddingX = Math.Max(0, Configuration.Float(Keys.Window.PaddingX)),
        PaddingY = Math.Max(0, Configuration.Float(Keys.Window.PaddingY)),
    };
}
