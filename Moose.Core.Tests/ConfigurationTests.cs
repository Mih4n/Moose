using Microsoft.Extensions.Configuration;
using Moose.Core.Settings;

namespace Moose.Core.Tests;

[Collection(nameof(ConfigurationTests))]
public sealed class ConfigurationTests : IDisposable
{
    readonly IConfiguration previous = Configuration.Current;

    static void Use(Dictionary<string, string?> values) =>
        Configuration.Initialize(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void Getters_ParseTheUserValue()
    {
        Use(new()
        {
            [Keys.Font.Size] = "13.5",
            [Keys.Scrollback.Lines] = "42",
            [Keys.Cursor.Blink] = "false",
            [Keys.Window.Title] = "Elk",
        });

        Assert.Equal(13.5f, Configuration.Float(Keys.Font.Size));
        Assert.Equal(42, Configuration.Int(Keys.Scrollback.Lines));
        Assert.False(Configuration.Bool(Keys.Cursor.Blink, true));
        Assert.Equal("Elk", Configuration.String(Keys.Window.Title));
    }

    [Fact]
    public void Getters_FallBackToTheBuiltInDefaultWhenMissingOrMalformed()
    {
        Use(new() { [Keys.Window.PaddingX] = "wide" });

        Assert.Equal(12f, Configuration.Float(Keys.Window.PaddingX));
        Assert.Equal(8f, Configuration.Float(Keys.Window.PaddingY));
        Assert.Equal("beam", Configuration.String(Keys.Cursor.Style));
    }

    [Fact]
    public void Getters_UseTheFallbackForKeysWithoutAnyDefault()
    {
        Use([]);

        Assert.Equal(7, Configuration.Int("nowhere:at_all", 7));
        Assert.Equal("x", Configuration.String("nowhere:at_all", "x"));
    }

    [Fact]
    public void Collections_ReadArraysTablesAndChildren()
    {
        Use(new()
        {
            [Configuration.Key(Keys.Shell.Args, "0")] = "-l",
            [Configuration.Key(Keys.Shell.Env, "TERM")] = "xterm",
            [Configuration.Key(Keys.Keybindings.Section, "0", Keys.Keybindings.Action)] = "copy",
        });

        Assert.Equal(["-l"], Configuration.Strings(Keys.Shell.Args));
        Assert.Equal("xterm", Configuration.Table(Keys.Shell.Env)["TERM"]);
        Assert.Equal(["keybindings:0"], Configuration.Children(Keys.Keybindings.Section));
    }

    [Fact]
    public void Defaults_CarryEveryBuiltInKeybinding()
    {
        var actions = Configuration.Defaults.GetSection(Keys.Keybindings.Section).GetChildren().Select(binding => binding[Keys.Keybindings.Action]);

        Assert.Contains("command_palette", actions);
        Assert.Contains("copy", actions);
    }

    public void Dispose() => Configuration.Initialize(previous);
}
