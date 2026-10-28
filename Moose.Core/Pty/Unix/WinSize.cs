using System.Runtime.InteropServices;

namespace Moose.Core.Pty.Unix;

[StructLayout(LayoutKind.Sequential)]
internal struct WinSize
{
    public ushort Rows;
    public ushort Columns;
    public ushort PixelWidth;
    public ushort PixelHeight;

    public static WinSize From(PtySize size) => new()
    {
        Rows = (ushort)Math.Clamp(size.Rows, 1, ushort.MaxValue),
        Columns = (ushort)Math.Clamp(size.Columns, 1, ushort.MaxValue),
        PixelWidth = (ushort)Math.Clamp(size.PixelWidth, 0, ushort.MaxValue),
        PixelHeight = (ushort)Math.Clamp(size.PixelHeight, 0, ushort.MaxValue),
    };
}
