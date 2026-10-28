using Microsoft.Extensions.Configuration;

namespace Moose.Core.Settings;

public sealed class UiColorOptions
{
    public string Background { get; set; } = "";
    public string Panel { get; set; } = "";
    public string Border { get; set; } = "";
    [ConfigurationKeyName("border_variant")]
    public string BorderVariant { get; set; } = "";
    public string Hover { get; set; } = "";
    public string Selected { get; set; } = "";
    public string Text { get; set; } = "";
    [ConfigurationKeyName("text_muted")]
    public string TextMuted { get; set; } = "";
    [ConfigurationKeyName("debug_bounds")]
    public string DebugBounds { get; set; } = "";
}
