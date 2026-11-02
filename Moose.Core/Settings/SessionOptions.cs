namespace Moose.Core.Settings;

public sealed class SessionOptions
{
    public bool SaveOnExit { get; init; } = true;
    public bool RestoreOnStart { get; init; }

    public static SessionOptions Load() => new()
    {
        SaveOnExit = Configuration.Bool(Keys.Session.SaveOnExit, true),
        RestoreOnStart = Configuration.Bool(Keys.Session.RestoreOnStart),
    };
}
