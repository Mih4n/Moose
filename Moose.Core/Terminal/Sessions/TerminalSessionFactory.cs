using System.Collections;
using Moose.Core.Pty;
using Moose.Core.Settings;

namespace Moose.Core.Terminal.Sessions;

public sealed class TerminalSessionFactory(IPseudoTerminalFactory ptyFactory, ShellOptions shell, ScrollbackOptions scrollback, EnvironmentOptions environment)
{
    const string FallbackShell = "/bin/sh";

    public TerminalSession Create(TabLayout? tab = null) => new(ptyFactory, ResolveStartInfo(tab), scrollback.Lines);

    /// The [shell] settings, with whatever the layout's tab sets taking precedence.
    public ShellStartInfo ResolveStartInfo(TabLayout? tab = null)
    {
        var program = FirstNonEmpty(tab?.Program, shell.Program, environment.Shell) ?? FallbackShell;
        IReadOnlyList<string> arguments = tab?.Args ?? (FirstNonEmpty(tab?.Program) != null ? [] : shell.Args);
        var home = FirstNonEmpty(environment.Home) ?? "/";
        var workingDirectory = new[] { tab?.WorkingDirectory, shell.WorkingDirectory }.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) ?? home;

        var variables = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) variables[(string)entry.Key] = (string?)entry.Value ?? "";
        foreach (var variable in shell.Env) variables[variable.Key] = variable.Value;

        return new ShellStartInfo(program, arguments, workingDirectory, variables);
    }

    static string? FirstNonEmpty(params string?[] candidates) => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
}
