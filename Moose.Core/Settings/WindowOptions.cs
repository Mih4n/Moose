namespace Moose.Core.Settings;

public sealed class WindowOptions
{
    public string Title { get; init; } = "Moose";
    public bool DynamicTitle { get; init; } = true;
    /// Space between the window edge and the terminal grid, in logical pixels.
    public float PaddingX { get; init; } = 12;
    public float PaddingY { get; init; } = 8;

    public static WindowOptions Load() => new()
    {
        Title = Configuration.String(Keys.Window.Title, "Moose"),
        DynamicTitle = Configuration.Bool(Keys.Window.DynamicTitle, true),
        PaddingX = Math.Max(0, Configuration.Float(Keys.Window.PaddingX, 12)),
        PaddingY = Math.Max(0, Configuration.Float(Keys.Window.PaddingY, 8)),
    };
}
