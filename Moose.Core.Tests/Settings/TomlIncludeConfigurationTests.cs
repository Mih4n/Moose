using Microsoft.Extensions.Configuration;
using Moose.Core.Settings;

namespace Moose.Core.Tests.Settings;

public sealed class TomlIncludeConfigurationTests : IDisposable
{
    readonly string directory = Directory.CreateTempSubdirectory("moose-config-").FullName;

    string Write(string name, string toml)
    {
        var path = Path.Combine(directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, toml);
        return path;
    }

    static IConfigurationRoot Load(string path) => new ConfigurationBuilder().Add(new TomlIncludeConfigurationSource(path, reloadOnChange: false)).Build();

    [Fact]
    public void Load_ReadsIncludedFileRelativeToTheIncludingOne()
    {
        Write("keys/vim.toml", """
            [[keybindings]]
            keys = "ctrl+j"
            action = "next"
            """);
        var config = Load(Write("config.toml", """
            include = ["keys/vim.toml"]
            [font]
            size = 12.5
            """));

        Assert.Equal("ctrl+j", config["keybindings:0:keys"]);
        Assert.Equal("12.5", config["font:size"]);
        Assert.Null(config["include"]);
    }

    [Fact]
    public void Load_IncludingFileOverridesIncludedTablesKeyByKey()
    {
        Write("theme.toml", """
            [colors]
            foreground = "#111111"
            background = "#222222"
            """);
        var config = Load(Write("config.toml", """
            include = "theme.toml"
            [colors]
            background = "#333333"
            """));

        Assert.Equal("#111111", config["colors:foreground"]);
        Assert.Equal("#333333", config["colors:background"]);
    }

    [Fact]
    public void Load_ConcatenatesArraysOfTablesInLoadOrder()
    {
        Write("keybindings.toml", """
            [[keybindings]]
            keys = "ctrl+a"
            action = "included"
            """);
        var config = Load(Write("config.toml", """
            include = ["keybindings.toml"]
            [[keybindings]]
            keys = "ctrl+b"
            action = "own"
            """));

        Assert.Equal(["included", "own"], config.GetSection("keybindings").GetChildren().Select(binding => binding["action"]));
    }

    [Fact]
    public void Load_ReplacesPlainArrays()
    {
        Write("shell.toml", """
            [shell]
            args = ["-l", "-i"]
            """);
        var config = Load(Write("config.toml", """
            include = ["shell.toml"]
            [shell]
            args = ["-c"]
            """));

        Assert.Equal(["-c"], config.GetSection("shell:args").GetChildren().Select(arg => arg.Value));
    }

    [Fact]
    public void Load_ExpandsWildcardsInSortedOrderAndSkipsMissingFiles()
    {
        Write("conf.d/20-b.toml", "value = \"b\"\nb = true");
        Write("conf.d/10-a.toml", "value = \"a\"\na = true");
        var config = Load(Write("config.toml", """include = ["conf.d/*.toml", "missing.toml"]"""));

        Assert.Equal("b", config["value"]);
        Assert.Equal(("true", "true"), (config["a"], config["b"]));
    }

    [Fact]
    public void Load_IgnoresIncludeCycles()
    {
        Write("other.toml", "include = [\"config.toml\"]\nother = 1");
        var config = Load(Write("config.toml", "include = [\"other.toml\"]\nown = 2"));

        Assert.Equal(("1", "2"), (config["other"], config["own"]));
    }

    [Fact]
    public void Reload_PicksUpIncludesAddedLater()
    {
        var path = Write("config.toml", "own = 1");
        var config = Load(path);
        Write("extra.toml", "extra = 1");
        File.WriteAllText(path, "include = [\"extra.toml\"]\nown = 1");

        config.Reload();

        Assert.Equal("1", config["extra"]);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
