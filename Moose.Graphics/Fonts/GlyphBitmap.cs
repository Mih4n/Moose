namespace Moose.Graphics.Fonts;

public sealed record GlyphBitmap(int Width, int Height, int Left, int Top, float Advance, byte[] Coverage);
