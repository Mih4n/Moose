namespace Moose.Core.Terminal.Images;

/// One kitty graphics protocol escape: `ESC _ G key=value,key=value;base64 payload ESC \`.
public sealed class GraphicsCommand
{
    readonly long[] values = new long[128];
    readonly bool[] present = new bool[128];

    GraphicsCommand(byte[] payload) => Payload = payload;

    /// Still base64 encoded: chunked transfers decode it once the last chunk arrives.
    public byte[] Payload { get; }

    public char Action => Char('a', 't');
    public char Medium => Char('t', 'd');
    public int Format => Int('f', 32);
    public bool More => Int('m') == 1;
    public int Quiet => Int('q');
    public uint ImageId => UInt('i');
    public uint ImageNumber => UInt('I');
    public uint PlacementId => UInt('p');

    public bool Has(char key) => present[key];

    public int Int(char key, int fallback = 0) => present[key] ? (int)Math.Clamp(values[key], int.MinValue, int.MaxValue) : fallback;

    public uint UInt(char key) => present[key] ? (uint)Math.Clamp(values[key], 0, uint.MaxValue) : 0;

    public char Char(char key, char fallback) => present[key] ? (char)values[key] : fallback;

    /// Only m and q may follow the first chunk of a chunked transfer.
    public bool IsContinuation
    {
        get
        {
            for (var key = 0; key < present.Length; key++)
                if (present[key] && key is not ('m' or 'q')) return false;
            return true;
        }
    }

    public static GraphicsCommand? Parse(ReadOnlySpan<byte> apc)
    {
        if (apc.IsEmpty || apc[0] != 'G') return null;
        apc = apc[1..];
        var separator = apc.IndexOf((byte)';');
        var control = separator < 0 ? apc : apc[..separator];
        var command = new GraphicsCommand(separator < 0 ? [] : apc[(separator + 1)..].ToArray());

        while (!control.IsEmpty)
        {
            var comma = control.IndexOf((byte)',');
            var pair = comma < 0 ? control : control[..comma];
            control = comma < 0 ? [] : control[(comma + 1)..];
            if (pair.Length < 3 || pair[1] != '=' || pair[0] >= 128) continue;
            var key = pair[0];
            var value = pair[2..];
            if (value.Length == 1 && (char)value[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            {
                command.Set(key, value[0]);
                continue;
            }
            if (ParseNumber(value) is { } number) command.Set(key, number);
        }
        return command;
    }

    void Set(byte key, long value)
    {
        values[key] = value;
        present[key] = true;
    }

    static long? ParseNumber(ReadOnlySpan<byte> text)
    {
        var negative = !text.IsEmpty && text[0] == '-';
        if (negative) text = text[1..];
        if (text.IsEmpty || text.Length > 11) return null;
        long value = 0;
        foreach (var digit in text)
        {
            if (digit is < (byte)'0' or > (byte)'9') return null;
            value = value * 10 + digit - '0';
        }
        return negative ? -value : value;
    }
}
