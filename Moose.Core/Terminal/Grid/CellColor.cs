namespace Moose.Core.Terminal.Grid;

public readonly record struct CellColor(uint Packed)
{
    public static readonly CellColor Default = new(0);

    public ColorKind Kind => (ColorKind)(Packed >> 24);
    public int Index => (int)(Packed & 0xff);
    public uint Rgb => Packed & 0xffffff;

    public static CellColor Indexed(int index) => new(((uint)ColorKind.Indexed << 24) | (uint)(index & 0xff));

    public static CellColor FromRgb(int red, int green, int blue) =>
        new(((uint)ColorKind.Rgb << 24) | ((uint)(red & 0xff) << 16) | ((uint)(green & 0xff) << 8) | (uint)(blue & 0xff));
}
