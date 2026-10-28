using Microsoft.Extensions.Configuration;
using Moose.Core.Settings;

namespace Moose.Core.Tests.Settings;

public class UiColorOptionsTests
{
    readonly IConfiguration defaults = OptionsExtender.LoadDefaults();

    [Fact]
    public void Defaults_BindUiColorsIncludingAlpha()
    {
        var colors = defaults.GetSection("ui:colors").Get<UiColorOptions>()!;

        Assert.Equal("#282828", colors.Background);
        Assert.Equal("#504945", colors.BorderVariant);
        Assert.Equal("#3c383680", colors.Hover);
        Assert.Equal("#bdae93", colors.TextMuted);
        Assert.Equal("#ff00ff", colors.DebugBounds);
    }

    [Fact]
    public void Defaults_BindGruvboxTerminalColors()
    {
        var colors = defaults.GetSection("colors").Get<ColorOptions>()!;

        Assert.Equal("#282828", colors.Background);
        Assert.Equal("#504945", colors.SelectionBackground);
        Assert.Equal("#cc241d", colors.Normal.Red);
        Assert.Equal("#ebdbb2", colors.Bright.White);
    }

    [Fact]
    public void UserSection_OverridesDefaultsKeyByKey()
    {
        var user = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ui:colors:panel"] = "#000000" })
            .Build();
        var colors = new UiColorOptions();

        defaults.GetSection("ui:colors").Bind(colors);
        user.GetSection("ui:colors").Bind(colors);

        Assert.Equal("#000000", colors.Panel);
        Assert.Equal("#282828", colors.Background);
    }
}
