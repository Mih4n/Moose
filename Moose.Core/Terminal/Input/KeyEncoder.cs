using System.Text;

namespace Moose.Core.Terminal.Input;

public static class KeyEncoder
{
    public static string Encode(TerminalKey key, KeyModifiers modifiers, bool applicationCursorKeys)
    {
        var modifierCode = ModifierCode(modifiers);
        var alt = modifiers.HasFlag(KeyModifiers.Alt) ? "\e" : "";
        return key switch
        {
            TerminalKey.Enter => alt + "\r",
            TerminalKey.Tab => modifiers.HasFlag(KeyModifiers.Shift) ? "\e[Z" : alt + "\t",
            TerminalKey.Backspace => alt + (modifiers.HasFlag(KeyModifiers.Control) ? "\b" : "\x7f"),
            TerminalKey.Escape => alt + "\e",
            TerminalKey.Up => Cursor('A', modifierCode, applicationCursorKeys),
            TerminalKey.Down => Cursor('B', modifierCode, applicationCursorKeys),
            TerminalKey.Right => Cursor('C', modifierCode, applicationCursorKeys),
            TerminalKey.Left => Cursor('D', modifierCode, applicationCursorKeys),
            TerminalKey.Home => Cursor('H', modifierCode, applicationCursorKeys),
            TerminalKey.End => Cursor('F', modifierCode, applicationCursorKeys),
            TerminalKey.Insert => Tilde(2, modifierCode),
            TerminalKey.Delete => Tilde(3, modifierCode),
            TerminalKey.PageUp => Tilde(5, modifierCode),
            TerminalKey.PageDown => Tilde(6, modifierCode),
            TerminalKey.F1 => Function('P', modifierCode),
            TerminalKey.F2 => Function('Q', modifierCode),
            TerminalKey.F3 => Function('R', modifierCode),
            TerminalKey.F4 => Function('S', modifierCode),
            TerminalKey.F5 => Tilde(15, modifierCode),
            TerminalKey.F6 => Tilde(17, modifierCode),
            TerminalKey.F7 => Tilde(18, modifierCode),
            TerminalKey.F8 => Tilde(19, modifierCode),
            TerminalKey.F9 => Tilde(20, modifierCode),
            TerminalKey.F10 => Tilde(21, modifierCode),
            TerminalKey.F11 => Tilde(23, modifierCode),
            TerminalKey.F12 => Tilde(24, modifierCode),
            _ => "",
        };
    }

    public static string EncodeCharacter(Rune character, KeyModifiers modifiers)
    {
        var text = character.ToString();
        if (modifiers.HasFlag(KeyModifiers.Control) && ControlCode(character.Value) is { } control) text = control.ToString();
        return modifiers.HasFlag(KeyModifiers.Alt) ? "\e" + text : text;
    }

    static char? ControlCode(int codepoint) => codepoint switch
    {
        >= 'a' and <= 'z' => (char)(codepoint - 'a' + 1),
        >= 'A' and <= 'Z' => (char)(codepoint - 'A' + 1),
        '@' or ' ' or '2' => '\0',
        '[' or '3' => '\e',
        '\\' or '4' => (char)0x1c,
        ']' or '5' => (char)0x1d,
        '^' or '6' => (char)0x1e,
        '_' or '-' or '/' or '7' => (char)0x1f,
        '?' or '8' => (char)0x7f,
        _ => null,
    };

    static int ModifierCode(KeyModifiers modifiers) => 1 + (int)(modifiers & (KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control));

    static string Cursor(char final, int modifierCode, bool applicationCursorKeys)
    {
        if (modifierCode > 1) return $"\e[1;{modifierCode}{final}";
        return applicationCursorKeys ? $"\eO{final}" : $"\e[{final}";
    }

    static string Tilde(int number, int modifierCode) => modifierCode > 1 ? $"\e[{number};{modifierCode}~" : $"\e[{number}~";

    static string Function(char final, int modifierCode) => modifierCode > 1 ? $"\e[1;{modifierCode}{final}" : $"\eO{final}";
}
