namespace Moose.Graphics.Primitives;

public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static readonly Color Transparent = new(0, 0, 0, 0);
    public static Color Hex(uint rgb, float a = 1f) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

    public static Color Parse(string hex)
    {
        var digits = hex.TrimStart('#');
        var value = Convert.ToUInt32(digits, 16);
        return digits.Length == 8 ? Hex(value >> 8, (value & 0xff) / 255f) : Hex(value);
    }
}
