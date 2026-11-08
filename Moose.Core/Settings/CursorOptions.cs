namespace Moose.Core.Settings;

public sealed class CursorOptions
{
    public string Style { get; init; } = "beam";
    public bool Blink { get; init; } = true;
    public int BlinkIntervalMs { get; init; } = 500;

    public static CursorOptions Load() => new()
    {
        Style = Configuration.String(Keys.Cursor.Style, "beam"),
        Blink = Configuration.Bool(Keys.Cursor.Blink, true),
        BlinkIntervalMs = Configuration.Int(Keys.Cursor.BlinkIntervalMs, 500),
    };
}
