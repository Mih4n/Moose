namespace Moose.Core.Settings;

public sealed class MouseOptions
{
    public bool CopyOnSelect { get; init; }

    public static MouseOptions Load() => new()
    {
        CopyOnSelect = Configuration.Bool(Keys.Mouse.CopyOnSelect),
    };
}
