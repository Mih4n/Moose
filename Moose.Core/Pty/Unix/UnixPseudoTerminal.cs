using System.Runtime.InteropServices;

namespace Moose.Core.Pty.Unix;

internal sealed class UnixPseudoTerminal(int master, int processId) : IPseudoTerminal
{
    readonly Lock gate = new();
    bool disposed;
    bool reaped;

    public int Read(Span<byte> buffer)
    {
        while (true)
        {
            var count = LibC.read(master, ref MemoryMarshal.GetReference(buffer), buffer.Length);
            if (count >= 0) return (int)count;
            if (Marshal.GetLastPInvokeError() != LibC.Interrupted) return 0;
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        lock (gate)
        {
            while (!disposed && data.Length > 0)
            {
                var written = LibC.write(master, ref MemoryMarshal.GetReference(data), data.Length);
                if (written > 0)
                {
                    data = data[(int)written..];
                    continue;
                }
                var error = Marshal.GetLastPInvokeError();
                if (error != LibC.Interrupted && error != LibC.TryAgain) return;
            }
        }
    }

    public void Resize(PtySize size)
    {
        lock (gate)
        {
            if (disposed) return;
            var winSize = WinSize.From(size);
            LibC.ioctl(master, LibC.SetWindowSize, ref winSize);
        }
    }

    public int WaitForExit()
    {
        int status;
        while (LibC.waitpid(processId, out status, 0) < 0)
            if (Marshal.GetLastPInvokeError() != LibC.Interrupted) return -1;
        lock (gate) reaped = true;
        var signal = status & 0x7f;
        return signal == 0 ? (status >> 8) & 0xff : 128 + signal;
    }

    public void Hangup()
    {
        lock (gate)
            if (!reaped) LibC.kill(processId, LibC.HangupSignal);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            LibC.close(master);
        }
    }
}
