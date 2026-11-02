namespace Moose.Core.Settings;

/// Fallback families are tried in order for characters the main font lacks, before asking fontconfig.
public sealed record FontOptions(string Family, float Size, bool Ligatures = true, IReadOnlyList<string>? Fallback = null)
{
    public static FontOptions Load() => new(
        Configuration.String(Keys.Font.Family, "monospace"),
        Configuration.Float(Keys.Font.Size, 11),
        Configuration.Bool(Keys.Font.Ligatures, true),
        Configuration.Strings(Keys.Font.Fallback)
    );
}
