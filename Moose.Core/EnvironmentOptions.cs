using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Moose.Core;

public record EnvironmentOptions(
    string? Home,
    string? Shell,
    string? HomeConfig
);

public static class EnvironmentOptionsExtender
{
    extension(HostApplicationBuilder builder)
    {
        public HostApplicationBuilder AddEnvironmentOptions()
        {
            var services = builder.Services;
            var configuration = builder.Configuration;
            var mapping = new Dictionary<string, string?>()
            {
                ["Environment:Home"] = Environment.GetEnvironmentVariable("HOME"),
                ["Environment:Shell"] = Environment.GetEnvironmentVariable("SHELL"),
                ["Environment:HomeConfig"] = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            };

            configuration.AddInMemoryCollection(mapping);
            services.Configure<EnvironmentOptions>(configuration.GetSection("Environment"));
            services.AddSingleton<EnvironmentOptions>(sp => sp.GetRequiredService<IOptions<EnvironmentOptions>>().Value);

            return builder;
        }
    }
}
