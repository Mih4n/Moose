namespace Moose.Core.Pty;

public readonly record struct PtySize(int Columns, int Rows, int PixelWidth = 0, int PixelHeight = 0);
