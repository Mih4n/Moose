using Moose.Core.Settings;
using Moose.Graphics.Primitives;

namespace Moose.UI;

sealed class UiTheme(UiColorOptions colors)
{
    public Color Background { get; } = Color.Parse(colors.Background);
    public Color Panel { get; } = Color.Parse(colors.Panel);
    public Color Border { get; } = Color.Parse(colors.Border);
    public Color BorderVariant { get; } = Color.Parse(colors.BorderVariant);
    public Color Hover { get; } = Color.Parse(colors.Hover);
    public Color Selected { get; } = Color.Parse(colors.Selected);
    public Color Text { get; } = Color.Parse(colors.Text);
    public Color TextMuted { get; } = Color.Parse(colors.TextMuted);
    public Color DebugBounds { get; } = Color.Parse(colors.DebugBounds);
}
