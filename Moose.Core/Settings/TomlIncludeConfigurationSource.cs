using Microsoft.Extensions.Configuration;

namespace Moose.Core.Settings;

public sealed class TomlIncludeConfigurationSource(string? path, bool reloadOnChange, string? baseToml = null) : IConfigurationSource
{
    public string? Path => path;
    public bool ReloadOnChange => reloadOnChange;
    public string? BaseToml => baseToml;

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new TomlIncludeConfigurationProvider(this);
}
