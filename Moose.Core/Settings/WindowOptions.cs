using Microsoft.Extensions.Configuration;

namespace Moose.Core.Settings;

public sealed class WindowOptions
{
    public string Title { get; set; } = "Moose";
    [ConfigurationKeyName("dynamic_title")]
    public bool DynamicTitle { get; set; } = true;
    [ConfigurationKeyName("padding_x")]
    public float PaddingX { get; set; } = 12;
    [ConfigurationKeyName("padding_y")]
    public float PaddingY { get; set; } = 8;
}
