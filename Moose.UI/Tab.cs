using static Moose.Graphics.Elements.Ui;
using Moose.Graphics.Elements;
using Moose.Graphics.Primitives;

namespace Moose.UI;

sealed class Tab : Element<Tab>
{
    public Tab(string title, bool active, UiTheme theme, Action close)
    {
        var closeButton = Text("×")
            .Color(active ? theme.TextMuted : Color.Transparent)
            .OnClick(close)
            .Hover(style => style.Color(theme.Text));
        Column()
            .H(37)
            .Bg(active ? theme.Selected : theme.Panel)
            .Children(
                Ui.Row()
                    .H(36)
                    .Padding(8)
                    .Gap(8)
                    .Children(
                        Text(title)
                            .Truncate()
                            .Color(active ? theme.Text : theme.TextMuted),
                        closeButton),
                Div()
                    .H(1)
                    .Bg(theme.BorderVariant));
        Hover(style =>
        {
            if (!active) style.Bg(theme.Hover);
            closeButton.Color(theme.TextMuted);
        });
    }
}
