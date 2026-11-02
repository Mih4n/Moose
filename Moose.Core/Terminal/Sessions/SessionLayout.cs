using Moose.Core.Settings;
using Tomlyn;
using Tomlyn.Model;

namespace Moose.Core.Terminal.Sessions;

/// One tab of a layout. Unset values fall back to the [shell] settings; a program without args runs with none.
public sealed record TabLayout(string? Title = null, string? Program = null, IReadOnlyList<string>? Args = null, string? WorkingDirectory = null);

/// Tabs to open and which of them is active, read from and written to a TOML file:
///
///     active = 0
///     [[tabs]]
///     title = "server"
///     working_directory = "~/project"
///     program = "fish"
///     args = ["-c", "dotnet watch"]
public sealed record SessionLayout(IReadOnlyList<TabLayout> Tabs, int Active = 0)
{
    const string ActiveKey = "active";
    const string TabsKey = "tabs";
    const string TitleKey = "title";
    const string ProgramKey = "program";
    const string ArgsKey = "args";
    const string WorkingDirectoryKey = "working_directory";

    /// Parses a layout; `~` and relative working directories resolve against home and baseDirectory.
    /// Throws FormatException for invalid TOML.
    public static SessionLayout Parse(string toml, string? home = null, string? baseDirectory = null)
    {
        TomlTable table;
        try { table = TomlSerializer.Deserialize(toml, TomlModelContext.Default.TomlTable) ?? new TomlTable(); }
        catch (TomlException exception) { throw new FormatException(exception.Message, exception); }

        var tabs = table.TryGetValue(TabsKey, out var value) && value is TomlTableArray array
            ? array.Select(tab => new TabLayout(
                NonEmpty(tab, TitleKey),
                NonEmpty(tab, ProgramKey),
                tab.TryGetValue(ArgsKey, out var args) && args is TomlArray list ? [.. list.OfType<string>()] : null,
                NonEmpty(tab, WorkingDirectoryKey) is { } directory ? Resolve(directory, home, baseDirectory) : null)).ToList()
            : [];
        var active = table.TryGetValue(ActiveKey, out var index) && index is long number ? (int)Math.Clamp(number, 0, Math.Max(0, tabs.Count - 1)) : 0;
        return new SessionLayout(tabs, active);
    }

    public static SessionLayout Load(string path, string? home = null) =>
        Parse(File.ReadAllText(path), home, Path.GetDirectoryName(Path.GetFullPath(path)));

    public string ToToml()
    {
        var tabs = new TomlTableArray();
        foreach (var tab in Tabs)
        {
            var table = new TomlTable();
            if (tab.Title != null) table[TitleKey] = tab.Title;
            if (tab.WorkingDirectory != null) table[WorkingDirectoryKey] = tab.WorkingDirectory;
            if (tab.Program != null) table[ProgramKey] = tab.Program;
            if (tab.Args != null)
            {
                var args = new TomlArray();
                foreach (var arg in tab.Args) args.Add(arg);
                table[ArgsKey] = args;
            }
            tabs.Add(table);
        }
        return TomlSerializer.Serialize(new TomlTable { [ActiveKey] = (long)Active, [TabsKey] = tabs }, TomlModelContext.Default.TomlTable);
    }

    /// Writes through a temporary file so a crash mid-write never leaves a truncated layout behind.
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, ToToml());
        File.Move(temporary, path, overwrite: true);
    }

    static string? NonEmpty(TomlTable table, string key) => table.TryGetValue(key, out var value) && value is string { Length: > 0 } text ? text : null;

    static string Resolve(string directory, string? home, string? baseDirectory)
    {
        if (home != null && (directory == "~" || directory.StartsWith("~/", StringComparison.Ordinal))) directory = Path.Combine(home, directory.TrimStart('~').TrimStart('/'));
        return baseDirectory != null ? Path.GetFullPath(directory, baseDirectory) : directory;
    }
}
