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
}
