using Moose.Core;
using Moose.Core.Settings;
using Moose.Graphics.Primitives;

namespace Moose.UI;

sealed class UiTheme
{
    public float BorderWidth { get; } = 2;
    public Color Background { get; } = Configuration.Color(Keys.Ui.Colors.Background);
    public Color Panel { get; } = Configuration.Color(Keys.Ui.Colors.Panel);
    public Color Border { get; } = Configuration.Color(Keys.Ui.Colors.Border);
    public Color BorderVariant { get; } = Configuration.Color(Keys.Ui.Colors.BorderVariant);
    public Color Hover { get; } = Configuration.Color(Keys.Ui.Colors.Hover);
    public Color Selected { get; } = Configuration.Color(Keys.Ui.Colors.Selected);
    public Color Text { get; } = Configuration.Color(Keys.Ui.Colors.Text);
    public Color TextMuted { get; } = Configuration.Color(Keys.Ui.Colors.TextMuted);
    public Color DebugBounds { get; } = Configuration.Color(Keys.Ui.Colors.DebugBounds);
}
