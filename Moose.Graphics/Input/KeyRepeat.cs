using System.Diagnostics;
using Silk.NET.Input;

namespace Moose.Graphics.Input;

internal sealed class KeyRepeat(TimeSpan delay, TimeSpan interval)
{
    Key? held;
    long nextRepeat;

    public void Press(Key key)
    {
        held = key;
        nextRepeat = Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency);
    }

    public void Release(Key key)
    {
        if (held == key) held = null;
    }

    public Key? Due()
    {
        var now = Stopwatch.GetTimestamp();
        if (held == null || now < nextRepeat) return null;
        nextRepeat = now + (long)(interval.TotalSeconds * Stopwatch.Frequency);
        return held;
    }
}
