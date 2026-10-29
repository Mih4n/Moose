using System.Globalization;

namespace Moose.Graphics.Primitives;

public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static readonly Color Transparent = new(0, 0, 0, 0);

    public static Color Hex(uint rgb, float a = 1f) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

    /// "#rgb", "#rgba", "#rrggbb" or "#rrggbbaa"; the '#' is optional.
    public static Color Hex(string hex) => TryHex(hex, out var color) ? color : throw new FormatException($"'{hex}' is not a hex color.");

    /// Channels 0-255, alpha 0-1.
    public static Color Rgb(int red, int green, int blue, float alpha = 1f) =>
        new(Math.Clamp(red, 0, 255) / 255f, Math.Clamp(green, 0, 255) / 255f, Math.Clamp(blue, 0, 255) / 255f, Math.Clamp(alpha, 0, 1));

    /// "rgb(r, g, b)", "rgba(r, g, b, a)" or bare "r, g, b[, a]"; channels 0-255, alpha 0-1.
    public static Color Rgb(string rgb) => TryRgb(rgb, out var color) ? color : throw new FormatException($"'{rgb}' is not an rgb color.");

    /// Any format accepted by Hex or Rgb.
    public static Color Parse(string text) => TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a color.");

    public static bool TryParse(string text, out Color color) => TryHex(text, out color) || TryRgb(text, out color);

    public static bool TryHex(string text, out Color color)
    {
        color = default;
        var digits = text.Trim().TrimStart('#');
        if (digits.Length is 3 or 4) digits = string.Concat(digits.Select(digit => $"{digit}{digit}"));
        if (digits.Length is not (6 or 8) || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
        color = digits.Length == 8 ? Hex(value >> 8, (value & 0xff) / 255f) : Hex(value);
        return true;
    }

    public static bool TryRgb(string text, out Color color)
    {
        color = default;
        var body = text.Trim();
        var open = body.IndexOf('(');
        if (open >= 0)
        {
            var function = body[..open].Trim().ToLowerInvariant();
            if (function is not ("rgb" or "rgba") || !body.EndsWith(')')) return false;
            body = body[(open + 1)..^1];
        }
        var parts = body.Split(',', StringSplitOptions.TrimEntries);
        var channels = new float[parts.Length];
        for (var index = 0; index < parts.Length; index++)
            if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out channels[index])) return false;
        return TryRgb(channels, out color);
    }

    /// [r, g, b] or [r, g, b, a]; channels 0-255, alpha 0-1.
    public static bool TryRgb(ReadOnlySpan<float> channels, out Color color)
    {
        color = default;
        if (channels.Length is not (3 or 4)) return false;
        color = Rgb((int)MathF.Round(channels[0]), (int)MathF.Round(channels[1]), (int)MathF.Round(channels[2]), channels.Length == 4 ? channels[3] : 1f);
        return true;
    }
}
