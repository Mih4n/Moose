namespace Moose.Core.Pty;

public interface IPseudoTerminal : IDisposable
{
    int ProcessId { get; }

    int Read(Span<byte> buffer);

    void Write(ReadOnlySpan<byte> data);

    void Resize(PtySize size);

    int WaitForExit();

    void Hangup();
}
