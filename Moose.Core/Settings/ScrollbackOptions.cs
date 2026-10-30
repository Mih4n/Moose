namespace Moose.Core.Settings;

public sealed class ScrollbackOptions
{
    public int Lines { get; init; } = 10000;
    public int Multiplier { get; init; } = 3;

    public static ScrollbackOptions Load() => new()
    {
        Lines = Configuration.Int(Keys.Scrollback.Lines, 10000),
        Multiplier = Configuration.Int(Keys.Scrollback.Multiplier, 3),
    };
}
