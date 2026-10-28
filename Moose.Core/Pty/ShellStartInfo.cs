namespace Moose.Core.Pty;

public sealed record ShellStartInfo(
    string Program,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment
);
