using System.Text;

namespace Moose.Core.Terminal.Parsing;

public sealed class VtParser(IVtHandler handler)
{
    const int MaxIntermediates = 2;
    const int MaxOscLength = 1 << 20;
    const byte Escape = 0x1b;
    const byte Bell = 0x07;

    readonly CsiParameters parameters = new();
    readonly byte[] intermediates = new byte[MaxIntermediates];
    byte[] osc = new byte[256];
    int oscLength;
    int intermediateCount;
    byte privateMarker;
    int utf8Remaining;
    int utf8Codepoint;
    int utf8Minimum;

    public ParserState State { get; private set; }

    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (var value in data) Advance(value);
    }

    void Advance(byte value)
    {
        if (utf8Remaining > 0)
        {
            if ((value & 0xc0) == 0x80)
            {
                utf8Codepoint = (utf8Codepoint << 6) | (value & 0x3f);
                if (--utf8Remaining == 0) PrintDecoded();
                return;
            }
            utf8Remaining = 0;
            handler.Print(Rune.ReplacementChar);
        }

        if (value is 0x18 or 0x1a)
        {
            handler.Execute(value);
            State = ParserState.Ground;
            return;
        }
        if (value == Escape)
        {
            if (State == ParserState.OscString) DispatchOsc();
            Clear();
            State = ParserState.Escape;
            return;
        }

        switch (State)
        {
            case ParserState.Ground: Ground(value); break;
            case ParserState.Escape: EscapeEntry(value); break;
            case ParserState.EscapeIntermediate: EscapeIntermediate(value); break;
            case ParserState.CsiEntry: CsiEntry(value); break;
            case ParserState.CsiParam: CsiParam(value); break;
            case ParserState.CsiIntermediate: CsiIntermediate(value); break;
            case ParserState.CsiIgnore: CsiIgnore(value); break;
            case ParserState.OscString: OscString(value); break;
            case ParserState.DcsString:
            case ParserState.IgnoredString:
                break;
        }
    }

    void Ground(byte value)
    {
        if (value < 0x20) handler.Execute(value);
        else if (value < 0x7f) handler.Print(new Rune(value));
        else if (value == 0x7f) return;
        else if (value is >= 0xc2 and <= 0xdf) BeginUtf8(value & 0x1f, 1, 0x80);
        else if (value is >= 0xe0 and <= 0xef) BeginUtf8(value & 0x0f, 2, 0x800);
        else if (value is >= 0xf0 and <= 0xf4) BeginUtf8(value & 0x07, 3, 0x10000);
        else handler.Print(Rune.ReplacementChar);
    }

    void BeginUtf8(int bits, int remaining, int minimum)
    {
        utf8Codepoint = bits;
        utf8Remaining = remaining;
        utf8Minimum = minimum;
    }

    void PrintDecoded()
    {
        var valid = utf8Codepoint >= utf8Minimum && Rune.IsValid(utf8Codepoint);
        handler.Print(valid ? new Rune(utf8Codepoint) : Rune.ReplacementChar);
    }

    void EscapeEntry(byte value)
    {
        switch (value)
        {
            case < 0x20: handler.Execute(value); break;
            case < 0x30: Collect(value); State = ParserState.EscapeIntermediate; break;
            case (byte)'[': State = ParserState.CsiEntry; break;
            case (byte)']': oscLength = 0; State = ParserState.OscString; break;
            case (byte)'P': State = ParserState.DcsString; break;
            case (byte)'X' or (byte)'^' or (byte)'_': State = ParserState.IgnoredString; break;
            case < 0x7f: DispatchEscape(value); break;
        }
    }

    void EscapeIntermediate(byte value)
    {
        switch (value)
        {
            case < 0x20: handler.Execute(value); break;
            case < 0x30: Collect(value); break;
            case < 0x7f: DispatchEscape(value); break;
        }
    }

    void CsiEntry(byte value)
    {
        switch (value)
        {
            case < 0x20: handler.Execute(value); break;
            case < 0x30: Collect(value); State = ParserState.CsiIntermediate; break;
            case < 0x3c: Parameter(value); State = ParserState.CsiParam; break;
            case < 0x40: privateMarker = value; State = ParserState.CsiParam; break;
            case < 0x7f: DispatchCsi(value); break;
        }
    }

    void CsiParam(byte value)
    {
        switch (value)
        {
            case < 0x20: handler.Execute(value); break;
            case < 0x30: Collect(value); State = ParserState.CsiIntermediate; break;
            case < 0x3c: Parameter(value); break;
            case < 0x40: State = ParserState.CsiIgnore; break;
            case < 0x7f: DispatchCsi(value); break;
        }
    }

    void CsiIntermediate(byte value)
    {
        switch (value)
        {
            case < 0x20: handler.Execute(value); break;
            case < 0x30: Collect(value); break;
            case < 0x40: State = ParserState.CsiIgnore; break;
            case < 0x7f: DispatchCsi(value); break;
        }
    }

    void CsiIgnore(byte value)
    {
        if (value < 0x20) handler.Execute(value);
        else if (value is >= 0x40 and < 0x7f) State = ParserState.Ground;
    }

    void OscString(byte value)
    {
        if (value == Bell)
        {
            DispatchOsc();
            State = ParserState.Ground;
            return;
        }
        if (value < 0x20 || oscLength >= MaxOscLength) return;
        if (oscLength == osc.Length) Array.Resize(ref osc, osc.Length * 2);
        osc[oscLength++] = value;
    }

    void Parameter(byte value)
    {
        if (value == ';') parameters.Separate(colon: false);
        else if (value == ':') parameters.Separate(colon: true);
        else parameters.AddDigit(value - '0');
    }

    void Collect(byte value)
    {
        if (intermediateCount < MaxIntermediates) intermediates[intermediateCount++] = value;
    }

    void Clear()
    {
        parameters.Clear();
        intermediateCount = 0;
        privateMarker = 0;
    }

    void DispatchEscape(byte final)
    {
        handler.EscapeDispatch(intermediates.AsSpan(0, intermediateCount), final);
        State = ParserState.Ground;
    }

    void DispatchCsi(byte final)
    {
        handler.CsiDispatch(parameters, privateMarker, intermediates.AsSpan(0, intermediateCount), final);
        State = ParserState.Ground;
    }

    void DispatchOsc()
    {
        handler.OscDispatch(osc.AsSpan(0, oscLength));
        oscLength = 0;
    }
}
