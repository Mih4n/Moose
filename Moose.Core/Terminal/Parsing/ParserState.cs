namespace Moose.Core.Terminal.Parsing;

public enum ParserState
{
    Ground,
    Escape,
    EscapeIntermediate,
    CsiEntry,
    CsiParam,
    CsiIntermediate,
    CsiIgnore,
    DcsString,
    OscString,
    IgnoredString,
}
