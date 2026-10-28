namespace Moose.Core.Pty;

public interface IPseudoTerminalFactory
{
    IPseudoTerminal Start(ShellStartInfo startInfo, PtySize size);
}
