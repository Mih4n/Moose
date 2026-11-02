using Microsoft.Extensions.Hosting;
using Moose.Core.Settings;

namespace Moose.Core;

public static class OptionsExtender
{
    static readonly Lazy<string> defaultToml = new(() =>
    {
        using var reader = new StreamReader(OpenDefaultConfig());
        return reader.ReadToEnd();
    });

    /// The built-in config.toml shipped as a resource.
    internal static string DefaultToml => defaultToml.Value;

    /// $XDG_CONFIG_HOME/moose, or ~/.config/moose.
    public static string ConfigDirectory(EnvironmentOptions options)
    {
        var home = options.HomeConfig ?? options.Home;
        return Path.Combine(home!, options.HomeConfig == null ? ".config" : "", "moose");
    }

    private static string ResolveConfigurationFile(EnvironmentOptions options)
    {
        var directory = Directory.CreateDirectory(ConfigDirectory(options));
        var file = CreateDefaultConfig(directory.FullName);

        return file;
    }

    private static Stream OpenDefaultConfig()
    {
        var assembly = typeof(MooseCoreAssamblyPointer).Assembly;
        return assembly.GetManifestResourceStream("Moose.config.toml") ?? throw new Exception("No config resource found!");
    }

    private static string CreateDefaultConfig(string directory)
    {
        var file = Path.Combine(directory, "config.toml");
        if (File.Exists(file)) return file;

        using var stream = OpenDefaultConfig();
        using var fileStream = new FileStream(file, FileMode.Create, FileAccess.Write);

        stream.CopyTo(fileStream);

        return file;
    }

    extension(IHostApplicationBuilder builder)
    {

        /// Loads the built-in defaults with the user's config.toml (and its includes) on top, and points `Configuration` at the result.
        public IHostApplicationBuilder AddTomlConfiguration()
        {
            Configuration.Initialize(builder.Configuration);
            var file = ResolveConfigurationFile(EnvironmentOptions.Load());
            builder.Configuration.Add(new TomlIncludeConfigurationSource(file, reloadOnChange: true, DefaultToml));

            return builder;
        }
    }
}
