using System.Numerics;

namespace Moose.Graphics.Primitives;

public readonly record struct Rect(float X, float Y, float W, float H)
{
    public bool Contains(Vector2 point) => point.X >= X && point.Y >= Y && point.X < X + W && point.Y < Y + H;
}
