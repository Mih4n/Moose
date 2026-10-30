using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Tomlyn.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moose.Core.Settings;

namespace Moose.Core;

public static class OptionsExtender
{
    private static string ResolveConfigurationFile(EnvironmentOptions options)
    {
        var home = options.HomeConfig ?? options.Home;
        var configDir = Path.Combine(home!, options.HomeConfig == null ? ".config" : "", "moose");

        var directory = Directory.CreateDirectory(configDir);
        var file = CreateDefaultConfig(directory.FullName);

        return file;
    }

    public static IConfiguration LoadDefaults()
    {
        using var stream = OpenDefaultConfig();
        return new ConfigurationBuilder()
            .AddTomlStream(stream)
            .Build();
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

        public IHostApplicationBuilder AddTomlConfiguration()
        {
            using var serviceProvider = builder.Services.BuildServiceProvider();
            var env = serviceProvider.GetRequiredService<EnvironmentOptions>();

            var file = ResolveConfigurationFile(env);
            builder.Configuration.Add(new TomlIncludeConfigurationSource(file, reloadOnChange: true));

            return builder;
        }

        public IHostApplicationBuilder AddOptions<T>(string section) where T : class
        {
            var services = builder.Services;
            var configuration = builder.Configuration;

            services.Configure<T>(configuration.GetSection(section));
            services.AddSingleton<T>(sp => sp.GetRequiredService<IOptions<T>>().Value);

            return builder;
        }
    }
}
