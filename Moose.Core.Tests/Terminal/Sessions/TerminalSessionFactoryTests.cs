using Moose.Core.Pty;
using Moose.Core.Settings;
using Moose.Core.Terminal.Sessions;

namespace Moose.Core.Tests.Terminal.Sessions;

public class TerminalSessionFactoryTests
{
    static ShellStartInfo Resolve(ShellOptions shell, EnvironmentOptions environment) =>
        new TerminalSessionFactory(new UnsupportedPseudoTerminalFactory(), shell, new ScrollbackOptions(), environment).ResolveStartInfo();

    [Fact]
    public void ResolveStartInfo_FallsBackToEnvironmentShellAndHome()
    {
        var startInfo = Resolve(new ShellOptions(), new EnvironmentOptions("/tmp", "/usr/bin/fish", null));

        Assert.Equal("/usr/bin/fish", startInfo.Program);
        Assert.Equal("/tmp", startInfo.WorkingDirectory);
    }

    [Fact]
    public void ResolveStartInfo_UsesConfiguredValuesAndMergesEnvironment()
    {
        var shell = new ShellOptions { Program = "/bin/sh", Args = ["-l"], WorkingDirectory = "/", Env = new Dictionary<string, string> { ["TERM"] = "xterm-256color" } };

        var startInfo = Resolve(shell, new EnvironmentOptions("/tmp", "/usr/bin/fish", null));

        Assert.Equal("/bin/sh", startInfo.Program);
        Assert.Equal(["-l"], startInfo.Arguments);
        Assert.Equal("/", startInfo.WorkingDirectory);
        Assert.Equal("xterm-256color", startInfo.Environment["TERM"]);
        Assert.True(startInfo.Environment.ContainsKey("PATH"));
    }

    [Fact]
    public void ResolveStartInfo_WithoutAnyShellUsesBinSh()
    {
        Assert.Equal("/bin/sh", Resolve(new ShellOptions(), new EnvironmentOptions(null, null, null)).Program);
    }
}
