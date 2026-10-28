using System.Text;
using Moose.Core.Terminal.Input;
using Silk.NET.Input;

namespace Moose.UI.Input;

static class KeyTranslator
{
    public static TerminalKey? Named(Key key) => key switch
    {
        Key.Enter or Key.KeypadEnter => TerminalKey.Enter,
        Key.Tab => TerminalKey.Tab,
        Key.Backspace => TerminalKey.Backspace,
        Key.Escape => TerminalKey.Escape,
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Left => TerminalKey.Left,
        Key.Right => TerminalKey.Right,
        Key.Home => TerminalKey.Home,
        Key.End => TerminalKey.End,
        Key.Insert => TerminalKey.Insert,
        Key.Delete => TerminalKey.Delete,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        >= Key.F1 and <= Key.F12 => TerminalKey.F1 + (key - Key.F1),
        _ => null,
    };

    public static Rune? Character(Key key, bool shift) => key switch
    {
        >= Key.A and <= Key.Z => new Rune((shift ? 'A' : 'a') + (key - Key.A)),
        >= Key.Number0 and <= Key.Number9 => new Rune('0' + (key - Key.Number0)),
        Key.Space => new Rune(' '),
        Key.LeftBracket => new Rune('['),
        Key.RightBracket => new Rune(']'),
        Key.BackSlash => new Rune('\\'),
        Key.Slash => new Rune('/'),
        Key.Minus => new Rune('-'),
        Key.Equal => new Rune('='),
        Key.Semicolon => new Rune(';'),
        Key.Apostrophe => new Rune('\''),
        Key.Comma => new Rune(','),
        Key.Period => new Rune('.'),
        Key.GraveAccent => new Rune('`'),
        _ => null,
    };
}
