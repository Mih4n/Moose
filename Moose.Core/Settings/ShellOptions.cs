namespace Moose.Core.Settings;

public sealed class ShellOptions
{
    public string Program { get; init; } = "";
    public string[] Args { get; init; } = [];
    public string WorkingDirectory { get; init; } = "";
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    public static ShellOptions Load() => new()
    {
        Program = Configuration.String(Keys.Shell.Program),
        Args = Configuration.Strings(Keys.Shell.Args),
        WorkingDirectory = Configuration.String(Keys.Shell.WorkingDirectory),
        Env = Configuration.Table(Keys.Shell.Env),
    };
}
