namespace Moose.Core.Settings;

public sealed record FontOptions(string Family, float Size, bool Ligatures = true)
{
    public static FontOptions Load() => new(
        Configuration.String(Keys.Font.Family, "monospace"),
        Configuration.Float(Keys.Font.Size, 11),
        Configuration.Bool(Keys.Font.Ligatures, true)
    );
}
