using Silk.NET.Windowing;

namespace Moose.Graphics.Hosting;

public sealed class WindowBuilder
{
    internal WindowOptions options = WindowOptions.DefaultVulkan;

    public WindowBuilder Title(string title) { options.Title = title; return this; }
    public WindowBuilder Size(int width, int height) { options.Size = new(width, height); return this; }
}
