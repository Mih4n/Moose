using Moose.Core.Pty;
using Moose.Core.Pty.Unix;
using Moose.Core.Terminal.Sessions;

namespace Moose.Core.Tests.Terminal.Sessions;

public class TerminalSessionTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    static TerminalSession Shell(string script, Dictionary<string, string>? environment = null) => new(
        new UnixPseudoTerminalFactory(),
        new ShellStartInfo("/bin/sh", ["-c", script], "/", environment ?? new Dictionary<string, string> { ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "" }),
        scrollbackLines: 100);

    static async Task<int> RunToExit(TerminalSession session, int columns = 40, int rows = 10)
    {
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += exited.SetResult;
        session.Start(columns, rows);
        return await exited.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    static string Row(TerminalSession session, int row)
    {
        lock (session.SyncRoot) return session.Screen[row].Text(0, session.Screen.Columns).TrimEnd();
    }

    [Fact]
    public async Task Start_RunsShellOnControllingTerminalAndRendersOutput()
    {
        using var session = Shell("printf '\\033[31mred\\033[0m\\n'; test -t 0 && echo tty; stty size; echo \"$TERM\"; exit 3", new() { ["TERM"] = "xterm-256color", ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "" });

        var exitCode = await RunToExit(session);

        Assert.Equal(3, exitCode);
        Assert.Equal("red", Row(session, 0));
        Assert.Equal("tty", Row(session, 1));
        Assert.Equal("10 40", Row(session, 2));
        Assert.Equal("xterm-256color", Row(session, 3));
    }

    [Fact]
    public async Task Write_ReachesShellStandardInput()
    {
        using var session = Shell("read line; echo \"got:$line\"");
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += exited.SetResult;
        session.Start(40, 10);

        session.SendText("hello\r");
        await exited.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal("hello", Row(session, 0));
        Assert.Equal("got:hello", Row(session, 1));
    }

    [Fact]
    public async Task Resize_IsVisibleToChild()
    {
        using var session = Shell("read line; stty size");
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += exited.SetResult;
        session.Start(40, 10);

        session.Resize(50, 12);
        session.SendText("\r");
        await exited.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.Equal("12 50", Row(session, 1));
    }

    [Fact]
    public async Task DeviceAttributesQuery_IsAnsweredThroughThePty()
    {
        using var session = Shell("stty -echo raw; printf '\\033[c'; dd bs=1 count=9 2>/dev/null | od -An -c | tr -d ' \\n'; echo");

        await RunToExit(session);

        Assert.Equal("033[?62;22c", Row(session, 0));
    }

    [Fact]
    public async Task Close_HangsUpTheShell()
    {
        using var session = Shell("sleep 30");
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += exited.SetResult;
        session.Start(40, 10);

        session.Close();

        Assert.Equal(129, await exited.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Close_AfterExitDoesNotSignalAnything()
    {
        using var session = Shell("exit 0");

        Assert.Equal(0, await RunToExit(session));
        session.Close();

        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Start_UnknownProgramThrows()
    {
        using var session = new TerminalSession(new UnixPseudoTerminalFactory(), new ShellStartInfo("/nonexistent/shell", [], "/", new Dictionary<string, string>()), 10);

        Assert.ThrowsAny<Exception>(() => session.Start(10, 10));
    }
}
