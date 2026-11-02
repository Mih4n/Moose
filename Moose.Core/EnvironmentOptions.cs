using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moose.Core.Settings;

namespace Moose.Core;

public record EnvironmentOptions(
    string? Home,
    string? Shell,
    string? HomeConfig,
    string? HomeState = null
)
{
    public EnvironmentOptions() : this(null, null, null) { }

    public static EnvironmentOptions Load() => new(Value(Keys.Environment.Home), Value(Keys.Environment.Shell), Value(Keys.Environment.HomeConfig), Value(Keys.Environment.HomeState));

    static string? Value(string key) => Configuration.Raw(key) is { Length: > 0 } value ? value : null;
}

public static class EnvironmentOptionsExtender
{
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddEnvironmentOptions()
        {
            var mapping = new Dictionary<string, string?>()
            {
                [Keys.Environment.Home] = Environment.GetEnvironmentVariable("HOME"),
                [Keys.Environment.Shell] = Environment.GetEnvironmentVariable("SHELL"),
                [Keys.Environment.HomeConfig] = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
                [Keys.Environment.HomeState] = Environment.GetEnvironmentVariable("XDG_STATE_HOME")
            };

            builder.Configuration.AddInMemoryCollection(mapping);
            Configuration.Initialize(builder.Configuration);
            builder.Services.AddSingleton(_ => EnvironmentOptions.Load());

            return builder;
        }
    }
}
