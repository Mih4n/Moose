namespace Moose.Core.Pty;

public sealed class UnsupportedPseudoTerminalFactory : IPseudoTerminalFactory
{
    public IPseudoTerminal Start(ShellStartInfo startInfo, PtySize size) =>
        throw new PlatformNotSupportedException($"Pseudo terminals are not implemented on {Environment.OSVersion.Platform}; ConPTY support is missing.");
}
