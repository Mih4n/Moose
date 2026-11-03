using System.Text;
using Moose.Core.Terminal.Parsing;

namespace Moose.Core.Tests.Terminal.Parsing;

sealed class RecordingHandler : IVtHandler
{
    public List<string> Events { get; } = [];

    public void Print(Rune rune) => Events.Add($"print {rune}");

    public void Execute(byte control) => Events.Add($"execute {control:x2}");

    public void EscapeDispatch(ReadOnlySpan<byte> intermediates, byte final) =>
        Events.Add($"esc {Encoding.ASCII.GetString(intermediates)}{(char)final}");

    public void CsiDispatch(CsiParameters parameters, byte privateMarker, ReadOnlySpan<byte> intermediates, byte final)
    {
        var values = Enumerable.Range(0, parameters.Count).Select(index => (parameters.IsSubparameter(index) ? ":" : "") + parameters[index]);
        var marker = privateMarker == 0 ? "" : ((char)privateMarker).ToString();
        Events.Add($"csi {marker}[{string.Join(",", values)}]{Encoding.ASCII.GetString(intermediates)}{(char)final}");
    }

    public void OscDispatch(ReadOnlySpan<byte> payload) => Events.Add($"osc {Encoding.UTF8.GetString(payload)}");

    public void ApcDispatch(ReadOnlySpan<byte> payload) => Events.Add($"apc {Encoding.UTF8.GetString(payload)}");
}
