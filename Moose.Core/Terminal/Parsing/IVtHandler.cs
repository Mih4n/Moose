using System.Text;

namespace Moose.Core.Terminal.Parsing;

public interface IVtHandler
{
    void Print(Rune rune);

    void Execute(byte control);

    void EscapeDispatch(ReadOnlySpan<byte> intermediates, byte final);

    void CsiDispatch(CsiParameters parameters, byte privateMarker, ReadOnlySpan<byte> intermediates, byte final);

    void OscDispatch(ReadOnlySpan<byte> payload);
}
