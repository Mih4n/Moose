using Microsoft.Extensions.Configuration;

namespace Moose.Core.Settings;

public sealed class ShellOptions
{
    public string Program { get; set; } = "";
    public string[] Args { get; set; } = [];
    [ConfigurationKeyName("working_directory")]
    public string WorkingDirectory { get; set; } = "";
    public Dictionary<string, string> Env { get; set; } = [];
}
