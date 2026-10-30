using Microsoft.Extensions.Configuration;
using Moose.Core;
using Moose.Core.Settings;
using Moose.Graphics.Primitives;

namespace Moose.Graphics.Tests.Primitives;

[Collection(nameof(ColorConfigurationTests))]
public sealed class ColorConfigurationTests : IDisposable
{
    readonly IConfiguration previous = Configuration.Current;

    static void Use(Dictionary<string, string?> values) =>
        Configuration.Initialize(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void Color_AcceptsHexRgbFunctionsAndArrays()
    {
        Use(new()
        {
            ["a"] = "#ff0000",
            ["b"] = "rgba(0, 255, 0, 0.5)",
            ["c:0"] = "0",
            ["c:1"] = "0",
            ["c:2"] = "255",
        });

        Assert.Equal(Color.Rgb(255, 0, 0), Configuration.Color("a"));
        Assert.Equal(Color.Rgb(0, 255, 0, 0.5f), Configuration.Color("b"));
        Assert.Equal(Color.Rgb(0, 0, 255), Configuration.Color("c"));
    }

    [Fact]
    public void ColorHexAndColorRgb_OnlyAcceptTheirOwnFormat()
    {
        Use(new() { ["hex"] = "#00ff00", ["rgb"] = "rgb(0, 255, 0)" });
        var fallback = Color.Rgb(1, 2, 3);

        Assert.Equal(Color.Rgb(0, 255, 0), Configuration.ColorHex("hex"));
        Assert.Equal(fallback, Configuration.ColorHex("rgb", fallback));
        Assert.Equal(Color.Rgb(0, 255, 0), Configuration.ColorRgb("rgb"));
        Assert.Equal(fallback, Configuration.ColorRgb("hex", fallback));
    }

    [Fact]
    public void Color_FallsBackToTheBuiltInDefaultForMalformedValues()
    {
        Use(new() { [Keys.Colors.Background] = "not a color" });

        Assert.Equal(Color.Hex("#282828"), Configuration.Color(Keys.Colors.Background));
    }

    public void Dispose() => Configuration.Initialize(previous);
}
