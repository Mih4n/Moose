namespace Moose.Core.Pty;

public interface IPseudoTerminal : IDisposable
{
    int Read(Span<byte> buffer);

    void Write(ReadOnlySpan<byte> data);

    void Resize(PtySize size);

    int WaitForExit();

    void Hangup();
}
