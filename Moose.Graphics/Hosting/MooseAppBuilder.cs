using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moose.Graphics.Fonts;
using Moose.Graphics.Fonts.FreeType;

namespace Moose.Graphics.Hosting;

public sealed class MooseAppBuilder(string[] args) : IHostApplicationBuilder
{
    readonly HostApplicationBuilder host = Host.CreateApplicationBuilder(args);
    readonly WindowBuilder window = new();

    public IServiceCollection Services => host.Services;
    public IConfigurationManager Configuration => host.Configuration;
    public IHostEnvironment Environment => host.Environment;
    public ILoggingBuilder Logging => host.Logging;
    public IMetricsBuilder Metrics => host.Metrics;
    public IDictionary<object, object> Properties => ((IHostApplicationBuilder)host).Properties;

    public void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory, Action<TContainerBuilder>? configure = null)
        where TContainerBuilder : notnull => host.ConfigureContainer(factory, configure);

    public MooseAppBuilder Window(Action<WindowBuilder> configure) { configure(window); return this; }

    public MooseApp Build()
    {
        Services.TryAddSingleton<IFontLocator, FontconfigLocator>();
        Services.TryAddSingleton<IFontRasterizer, FreeTypeRasterizer>();
        Services.TryAddSingleton<TextSystem>();
        Services.TryAddSingleton<AppWindow>();
        return new(host.Build(), window.options);
    }
}
