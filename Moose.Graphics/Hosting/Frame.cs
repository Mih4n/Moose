using System.Numerics;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.Graphics.Hosting;

public sealed class Frame(IServiceProvider services, Vector2 size)
{
    public IServiceProvider Services => services;
    public Vector2 Size => size;
    public Color ClearColor { get; set; } = Color.Transparent;
    public List<Quad> Scene { get; } = [];
}
