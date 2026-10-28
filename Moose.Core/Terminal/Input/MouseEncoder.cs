using System.Text;

namespace Moose.Core.Terminal.Input;

public static class MouseEncoder
{
    const int X10Offset = 32;
    const int MotionFlag = 32;

    public static byte[]? Encode(MouseButton button, MouseAction action, int column, int row, KeyModifiers modifiers, bool sgr)
    {
        var code = (int)button;
        if (action == MouseAction.Motion) code += MotionFlag;
        if (modifiers.HasFlag(KeyModifiers.Shift)) code += 4;
        if (modifiers.HasFlag(KeyModifiers.Alt)) code += 8;
        if (modifiers.HasFlag(KeyModifiers.Control)) code += 16;

        if (sgr) return Encoding.ASCII.GetBytes($"\e[<{code};{column + 1};{row + 1}{(action == MouseAction.Release ? 'm' : 'M')}");

        if (action == MouseAction.Release) code = (code & ~3) | 3;
        var encodedColumn = column + 1 + X10Offset;
        var encodedRow = row + 1 + X10Offset;
        if (encodedColumn > 255 || encodedRow > 255) return null;
        return [0x1b, (byte)'[', (byte)'M', (byte)(code + X10Offset), (byte)encodedColumn, (byte)encodedRow];
    }
}
