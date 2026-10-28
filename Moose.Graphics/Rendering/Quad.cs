using Moose.Graphics.Primitives;

namespace Moose.Graphics.Rendering;

public record struct Quad(Rect Rect, Color Color, Color BorderColor, float Radius, float BorderWidth, Rect Uv = default);
