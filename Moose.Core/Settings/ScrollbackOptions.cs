namespace Moose.Core.Settings;

public sealed class ScrollbackOptions
{
    public int Lines { get; set; } = 10000;
    public int Multiplier { get; set; } = 3;
}
