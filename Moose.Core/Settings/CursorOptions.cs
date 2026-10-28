using Microsoft.Extensions.Configuration;

namespace Moose.Core.Settings;

public sealed class CursorOptions
{
    public string Style { get; set; } = "block";
    public bool Blink { get; set; } = true;
    [ConfigurationKeyName("blink_interval_ms")]
    public int BlinkIntervalMs { get; set; } = 500;
}
