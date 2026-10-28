using Microsoft.Extensions.Configuration;

namespace Moose.Core.Settings;

public sealed class MouseOptions
{
    [ConfigurationKeyName("copy_on_select")]
    public bool CopyOnSelect { get; set; }
}
