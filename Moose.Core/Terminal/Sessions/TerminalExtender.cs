using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moose.Core.Pty;
using Moose.Core.Pty.Unix;
using Moose.Core.Settings;

namespace Moose.Core.Terminal.Sessions;

public static class TerminalExtender
{
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddTerminal()
        {
            builder.AddOptions<ShellOptions>("shell");
            builder.AddOptions<ScrollbackOptions>("scrollback");
            builder.Services.AddSingleton<IPseudoTerminalFactory>(_ => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? new UnixPseudoTerminalFactory()
                : new UnsupportedPseudoTerminalFactory());
            builder.Services.AddSingleton<TerminalSessionFactory>();
            return builder;
        }
    }
}
