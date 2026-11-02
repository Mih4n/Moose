using Silk.NET.Windowing;

namespace Moose.Graphics.Hosting;

public sealed class WindowBuilder
{
    internal WindowOptions options = WindowOptions.DefaultVulkan;
    internal bool libdecor = true;

    public WindowBuilder Title(string title) { options.Title = title; return this; }
    public WindowBuilder Size(int width, int height) { options.Size = new(width, height); return this; }
    public WindowBuilder Decorations(bool enabled) { options.WindowBorder = enabled ? WindowBorder.Resizable : WindowBorder.Hidden; return this; }
    public WindowBuilder Libdecor(bool enabled) { libdecor = enabled; return this; }
}
