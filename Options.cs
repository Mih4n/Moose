using Microsoft.Extensions.Hosting;
using Tomlyn.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Moose;

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

    private static string CreateDefaultConfig(string directory)
    {
        var file = Path.Combine(directory, "config.toml");
        if (File.Exists(file)) return file;

        var resource = "Moose.config.toml";
        var assembly = typeof(MooseAssamblyPointer).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource);

        if (stream is null) throw new Exception("No config resource found!");
        using var fileStream = new FileStream(file, FileMode.Create, FileAccess.Write);

        stream.CopyTo(fileStream);

        return file;
    }

    extension(HostApplicationBuilder builder)
    {

        public HostApplicationBuilder AddTomlConfiguration()
        {
            using var serviceProvider = builder.Services.BuildServiceProvider();
            var env = serviceProvider.GetRequiredService<EnvironmentOptions>();

            var file = ResolveConfigurationFile(env);
            builder.Configuration.AddTomlFile(file, optional: false, reloadOnChange: true);

            return builder;
        }

        public HostApplicationBuilder AddOptions<T>(string section) where T : class
        {
            var services = builder.Services;
            var configuration = builder.Configuration;

            services.Configure<T>(configuration.GetSection(section));
            services.AddSingleton<T>(sp => sp.GetRequiredService<IOptions<T>>().Value);

            return builder;
        }
    }
}
