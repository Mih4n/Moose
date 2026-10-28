using System.Collections;
using Moose.Core.Pty;
using Moose.Core.Settings;

namespace Moose.Core.Terminal.Sessions;

public sealed class TerminalSessionFactory(IPseudoTerminalFactory ptyFactory, ShellOptions shell, ScrollbackOptions scrollback, EnvironmentOptions environment)
{
    const string FallbackShell = "/bin/sh";

    public TerminalSession Create() => new(ptyFactory, ResolveStartInfo(), scrollback.Lines);

    public ShellStartInfo ResolveStartInfo()
    {
        var program = FirstNonEmpty(shell.Program, environment.Shell) ?? FallbackShell;
        var home = FirstNonEmpty(environment.Home) ?? "/";
        var workingDirectory = FirstNonEmpty(shell.WorkingDirectory) is { } configured && Directory.Exists(configured) ? configured : home;

        var variables = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) variables[(string)entry.Key] = (string?)entry.Value ?? "";
        foreach (var variable in shell.Env) variables[variable.Key] = variable.Value;

        return new ShellStartInfo(program, shell.Args, workingDirectory, variables);
    }

    static string? FirstNonEmpty(params string?[] candidates) => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
}
