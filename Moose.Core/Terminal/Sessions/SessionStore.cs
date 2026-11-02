namespace Moose.Core.Terminal.Sessions;

/// Where layouts live: the session saved on exit under $XDG_STATE_HOME/moose, named ones under the config directory's sessions/.
public sealed class SessionStore(EnvironmentOptions environment)
{
    const string Extension = ".toml";

    public string LastSessionPath => Path.Combine(StateDirectory, "session" + Extension);

    public string SessionsDirectory => Path.Combine(OptionsExtender.ConfigDirectory(environment), "sessions");

    string StateDirectory => Path.Combine(environment.HomeState ?? Path.Combine(environment.Home ?? "/", ".local", "state"), "moose");

    public IReadOnlyList<string> Names() => Directory.Exists(SessionsDirectory)
        ? [.. Directory.EnumerateFiles(SessionsDirectory, "*" + Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)]
        : [];

    /// A path to an existing file (`~` expanded), or else the name of a saved session.
    public string PathFor(string nameOrPath)
    {
        var expanded = environment.Home != null && nameOrPath.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(environment.Home, nameOrPath[2..]) : nameOrPath;
        if (File.Exists(expanded) || expanded.Contains('/')) return Path.GetFullPath(expanded);
        return Path.Combine(SessionsDirectory, SafeName(expanded) + Extension);
    }

    /// Throws FileNotFoundException when there's no such layout and FormatException when it isn't valid TOML.
    public SessionLayout Load(string nameOrPath)
    {
        var path = PathFor(nameOrPath);
        if (!File.Exists(path)) throw new FileNotFoundException($"No session '{nameOrPath}' (looked for {path}).", path);
        return SessionLayout.Load(path, environment.Home);
    }

    public SessionLayout? LoadLast() => File.Exists(LastSessionPath) ? SessionLayout.Load(LastSessionPath, environment.Home) : null;

    public void SaveLast(SessionLayout layout) => layout.Save(LastSessionPath);

    /// Saves under a name; returns the file written.
    public string Save(string name, SessionLayout layout)
    {
        var path = Path.Combine(SessionsDirectory, SafeName(name) + Extension);
        layout.Save(path);
        return path;
    }

    static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. name.Trim().Select(character => invalid.Contains(character) ? '_' : character)]).TrimStart('.');
        return safe.EndsWith(Extension, StringComparison.Ordinal) ? safe[..^Extension.Length] : safe;
    }
}
