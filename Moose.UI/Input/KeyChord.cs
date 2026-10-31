using Moose.Graphics.Input;
using Silk.NET.Input;

namespace Moose.UI.Input;

readonly record struct KeyChord(Key Key, KeyModifiers Modifiers)
{
    public static KeyChord? Parse(string text)
    {
        var modifiers = KeyModifiers.None;
        Key? key = null;
        foreach (var part in text.ToLowerInvariant().Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part)
            {
                case "ctrl" or "control": modifiers |= KeyModifiers.Control; break;
                case "shift": modifiers |= KeyModifiers.Shift; break;
                case "alt": modifiers |= KeyModifiers.Alt; break;
                case "super" or "cmd": modifiers |= KeyModifiers.Super; break;
                case [>= '0' and <= '9']: key = Key.Number0 + (part[0] - '0'); break;
                default:
                    if (!Enum.TryParse<Key>(part.Replace("_", ""), ignoreCase: true, out var parsed)) return null;
                    key = parsed;
                    break;
            }
        }
        return key is { } chordKey ? new KeyChord(chordKey, modifiers) : null;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Super)) parts.Add("Super");
        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    static string KeyName(Key key) => key switch
    {
        >= Key.Number0 and <= Key.Number9 => ((char)('0' + (key - Key.Number0))).ToString(),
        Key.Equal => "=",
        Key.Minus => "-",
        Key.Comma => ",",
        Key.Period => ".",
        Key.Slash => "/",
        Key.Semicolon => ";",
        Key.LeftBracket => "[",
        Key.RightBracket => "]",
        _ => key.ToString(),
    };
}
