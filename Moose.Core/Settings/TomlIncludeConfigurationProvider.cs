using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Tomlyn;
using Tomlyn.Model;

namespace Moose.Core.Settings;

public sealed class TomlIncludeConfigurationProvider(TomlIncludeConfigurationSource source) : ConfigurationProvider, IDisposable
{
    static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(250);

    readonly List<IDisposable> watchers = [];

    public IReadOnlyList<string> Files { get; private set; } = [];

    public override void Load()
    {
        var files = new List<string>();
        var merged = source.BaseToml is { } baseToml ? Parse(baseToml) : new TomlTable();
        merged.Remove(Keys.Include);
        if (source.Path is { } path) Merge(merged, Read(System.IO.Path.GetFullPath(path), [], files));
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Flatten(merged, null, data);
        Data = data;
        Files = files;
        if (source.ReloadOnChange) Watch(files);
    }

    static TomlTable Read(string file, HashSet<string> visiting, List<string> files)
    {
        var merged = new TomlTable();
        if (!visiting.Add(file)) return merged;
        var table = Parse(File.ReadAllText(file));
        if (table.TryGetValue(Keys.Include, out var includes))
        {
            table.Remove(Keys.Include);
            var directory = System.IO.Path.GetDirectoryName(file)!;
            foreach (var include in Patterns(includes))
            foreach (var included in Resolve(directory, include))
                Merge(merged, Read(included, visiting, files));
        }
        Merge(merged, table);
        files.Add(file);
        visiting.Remove(file);
        return merged;
    }

    static TomlTable Parse(string toml) => TomlSerializer.Deserialize<TomlTable>(toml)!;

    static IEnumerable<string> Patterns(object includes) => includes switch
    {
        string single => [single],
        TomlArray many => many.OfType<string>(),
        _ => [],
    };

    static IEnumerable<string> Resolve(string directory, string include)
    {
        var path = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(ExpandHome(include)), directory);
        var name = System.IO.Path.GetFileName(path);
        if (!name.Contains('*')) return File.Exists(path) ? [path] : [];
        var parent = System.IO.Path.GetDirectoryName(path)!;
        return Directory.Exists(parent) ? Directory.EnumerateFiles(parent, name).Order(StringComparer.Ordinal) : [];
    }

    static string ExpandHome(string path) =>
        path.StartsWith("~/") && Environment.GetEnvironmentVariable("HOME") is { } home ? System.IO.Path.Combine(home, path[2..]) : path;

    static void Merge(TomlTable target, TomlTable overlay)
    {
        foreach (var (key, value) in overlay)
        {
            if (target.TryGetValue(key, out var existing))
            {
                if (existing is TomlTable existingTable && value is TomlTable table)
                {
                    Merge(existingTable, table);
                    continue;
                }
                if (existing is TomlTableArray existingTables && value is TomlTableArray tables)
                {
                    foreach (var item in tables) existingTables.Add(item);
                    continue;
                }
            }
            target[key] = value;
        }
    }

    static void Flatten(object? value, string? prefix, Dictionary<string, string?> data)
    {
        switch (value)
        {
            case TomlTable table:
                foreach (var (key, child) in table) Flatten(child, Key(prefix, key), data);
                break;
            case TomlTableArray tables:
                for (var index = 0; index < tables.Count; index++) Flatten(tables[index], Key(prefix, index.ToString(CultureInfo.InvariantCulture)), data);
                break;
            case TomlArray array:
                for (var index = 0; index < array.Count; index++) Flatten(array[index], Key(prefix, index.ToString(CultureInfo.InvariantCulture)), data);
                break;
            case bool flag:
                data[prefix!] = flag ? "true" : "false";
                break;
            case IFormattable formattable:
                data[prefix!] = formattable.ToString(null, CultureInfo.InvariantCulture);
                break;
            default:
                data[prefix!] = value?.ToString();
                break;
        }
    }

    static string Key(string? prefix, string key) => prefix == null ? key : ConfigurationPath.Combine(prefix, key);

    void Watch(IEnumerable<string> files)
    {
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
        foreach (var group in files.GroupBy(file => System.IO.Path.GetDirectoryName(file)!))
        {
            var provider = new PhysicalFileProvider(group.Key);
            watchers.Add(provider);
            foreach (var file in group)
            {
                var name = System.IO.Path.GetFileName(file);
                watchers.Add(ChangeToken.OnChange(() => provider.Watch(name), Reload));
            }
        }
    }

    void Reload()
    {
        Thread.Sleep(ReloadDelay);
        try
        {
            Load();
            OnReload();
        }
        catch (Exception exception) when (exception is IOException or TomlException)
        {
        }
    }

    public void Dispose()
    {
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
    }
}
