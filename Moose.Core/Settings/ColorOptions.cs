using Microsoft.Extensions.Configuration;

namespace Moose.Core.Settings;

public sealed class ColorOptions
{
    public string Foreground { get; set; } = "";
    public string Background { get; set; } = "";
    public string Cursor { get; set; } = "";
    [ConfigurationKeyName("cursor_text")]
    public string CursorText { get; set; } = "";
    [ConfigurationKeyName("selection_foreground")]
    public string SelectionForeground { get; set; } = "";
    [ConfigurationKeyName("selection_background")]
    public string SelectionBackground { get; set; } = "";
    public AnsiColorOptions Normal { get; set; } = new();
    public AnsiColorOptions Bright { get; set; } = new();
}
