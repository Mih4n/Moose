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
            builder.Services.AddSingleton(_ => ShellOptions.Load());
            builder.Services.AddSingleton(_ => ScrollbackOptions.Load());
            builder.Services.AddSingleton<IPseudoTerminalFactory>(_ => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? new UnixPseudoTerminalFactory()
                : new UnsupportedPseudoTerminalFactory());
            builder.Services.AddSingleton<TerminalSessionFactory>();
            return builder;
        }
    }
}
