using System.Text;
using Moose.Core.Terminal.Input;

namespace Moose.Core.Tests.Terminal.Input;

public class KeyEncoderTests
{
    [Theory]
    [InlineData(TerminalKey.Up, KeyModifiers.None, false, "\e[A")]
    [InlineData(TerminalKey.Up, KeyModifiers.None, true, "\eOA")]
    [InlineData(TerminalKey.Left, KeyModifiers.Control, true, "\e[1;5D")]
    [InlineData(TerminalKey.Home, KeyModifiers.Shift, false, "\e[1;2H")]
    [InlineData(TerminalKey.Delete, KeyModifiers.None, false, "\e[3~")]
    [InlineData(TerminalKey.PageUp, KeyModifiers.Alt, false, "\e[5;3~")]
    [InlineData(TerminalKey.F1, KeyModifiers.None, false, "\eOP")]
    [InlineData(TerminalKey.F5, KeyModifiers.Control | KeyModifiers.Shift, false, "\e[15;6~")]
    [InlineData(TerminalKey.Enter, KeyModifiers.None, false, "\r")]
    [InlineData(TerminalKey.Enter, KeyModifiers.Alt, false, "\e\r")]
    [InlineData(TerminalKey.Backspace, KeyModifiers.None, false, "\x7f")]
    [InlineData(TerminalKey.Backspace, KeyModifiers.Control, false, "\b")]
    [InlineData(TerminalKey.Tab, KeyModifiers.Shift, false, "\e[Z")]
    [InlineData(TerminalKey.Escape, KeyModifiers.None, false, "\e")]
    public void Encode_NamedKeys(TerminalKey key, KeyModifiers modifiers, bool applicationCursorKeys, string expected)
    {
        Assert.Equal(expected, KeyEncoder.Encode(key, modifiers, applicationCursorKeys));
    }

    [Theory]
    [InlineData('c', KeyModifiers.Control, "\x03")]
    [InlineData('a', KeyModifiers.Control | KeyModifiers.Shift, "\x01")]
    [InlineData(' ', KeyModifiers.Control, "\0")]
    [InlineData('[', KeyModifiers.Control, "\e")]
    [InlineData('x', KeyModifiers.Alt, "\ex")]
    [InlineData('b', KeyModifiers.Alt | KeyModifiers.Control, "\e\x02")]
    [InlineData('q', KeyModifiers.None, "q")]
    public void EncodeCharacter_AppliesControlAndAlt(char character, KeyModifiers modifiers, string expected)
    {
        Assert.Equal(expected, KeyEncoder.EncodeCharacter(new Rune(character), modifiers));
    }
}
