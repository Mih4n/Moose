namespace Moose.Core.Terminal.Input;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
    Super = 8,
}
