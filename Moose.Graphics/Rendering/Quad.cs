using Moose.Graphics.Primitives;

namespace Moose.Graphics.Rendering;

/// Texture is 0 for shapes and glyphs, otherwise the 1-based ImageRegistry slot whose pixels Uv samples.
public record struct Quad(Rect Rect, Color Color, Color BorderColor, float Radius, float BorderWidth, Rect Uv = default, int Texture = 0);
