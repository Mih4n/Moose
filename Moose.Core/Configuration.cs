using System.Globalization;
using Microsoft.Extensions.Configuration;
using Moose.Core.Settings;

namespace Moose.Core;

public static class Configuration
{
    public delegate bool Parser<T>(string text, out T value);

    static readonly Lazy<IConfiguration> defaults = new(() => new ConfigurationBuilder()
        .Add(new TomlIncludeConfigurationSource(null, reloadOnChange: false, OptionsExtender.DefaultToml))
        .Build());

    static IConfiguration current = new ConfigurationBuilder().Build();

    public static IConfiguration Current => current;

    public static IConfiguration Defaults => defaults.Value;

    public static void Initialize(IConfiguration configuration) => current = configuration;

    public static void Reload()
    {
        if (current is IConfigurationRoot root) root.Reload();
    }

    public static string? Raw(string key) => current[key];

    public static bool Has(string key) => current.GetSection(key).Exists();

    public static string String(string key, string fallback = "") => Get(key, (string text, out string value) =>
    {
        value = text;
        return true;
    }, fallback);

    public static int Int(string key, int fallback = 0) =>
        Get(key, (string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value), fallback);

    public static float Float(string key, float fallback = 0) =>
        Get(key, (string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value), fallback);

    public static bool Bool(string key, bool fallback = false) => Get(key, bool.TryParse, fallback);

    public static T Enum<T>(string key, T fallback) where T : struct, Enum =>
        Get(key, (string text, out T value) => System.Enum.TryParse(text.Replace("_", ""), ignoreCase: true, out value), fallback);

    public static string[] Strings(string key) => [.. Source(key).GetSection(key).GetChildren().Select(child => child.Value).OfType<string>()];

    public static IReadOnlyDictionary<string, string> Table(string key) => Source(key).GetSection(key).GetChildren()
        .Where(child => child.Value != null)
        .ToDictionary(child => child.Key, child => child.Value!);

    public static IEnumerable<string> Children(string key) => Source(key).GetSection(key).GetChildren().Select(child => child.Path);

    public static string Key(params string[] parts) => ConfigurationPath.Combine(parts);

    public static T Get<T>(string key, Parser<T> parse, T fallback)
    {
        if (current[key] is { } text && parse(text, out var value)) return value;
        if (Defaults[key] is { } builtIn && parse(builtIn, out var defaultValue)) return defaultValue;
        return fallback;
    }

    static IConfiguration Source(string key) => current.GetSection(key).Exists() ? current : Defaults;
}
