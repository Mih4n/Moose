using System.Globalization;
using Microsoft.Extensions.Configuration;
using Moose.Core;
using ColorValue = Moose.Graphics.Primitives.Color;

namespace Moose.Graphics.Primitives;

public static class ColorConfiguration
{
    extension(Configuration)
    {
        public static ColorValue Color(string key, ColorValue fallback = default) => Read(key, ColorValue.TryParse, fallback);

        public static ColorValue ColorHex(string key, ColorValue fallback = default) => Read(key, ColorValue.TryHex, fallback, arrays: false);

        public static ColorValue ColorRgb(string key, ColorValue fallback = default) => Read(key, ColorValue.TryRgb, fallback);
    }

    static ColorValue Read(string key, Configuration.Parser<ColorValue> parse, ColorValue fallback, bool arrays = true)
    {
        if (TryRead(Configuration.Current, key, parse, arrays, out var color)) return color;
        if (TryRead(Configuration.Defaults, key, parse, arrays, out color)) return color;
        return fallback;
    }

    static bool TryRead(IConfiguration configuration, string key, Configuration.Parser<ColorValue> parse, bool arrays, out ColorValue color)
    {
        color = default;
        var section = configuration.GetSection(key);
        if (section.Value is { } text) return parse(text, out color);
        if (!arrays) return false;
        var channels = new List<float>();
        foreach (var child in section.GetChildren())
        {
            if (!float.TryParse(child.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var channel)) return false;
            channels.Add(channel);
        }
        return channels.Count > 0 && ColorValue.TryRgb(channels.ToArray(), out color);
    }
}
