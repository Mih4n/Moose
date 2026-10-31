using Moose.Graphics.Rendering;

namespace Moose.Graphics.Hosting;

public sealed class AppWindow
{
    internal VulkanRenderer? Renderer { get; set; }

    public string Clipboard
    {
        get => Renderer?.Keyboard.ClipboardText ?? "";
        set
        {
            if (Renderer != null) Renderer.Keyboard.ClipboardText = value;
        }
    }

    public string Title
    {
        get => Renderer?.Window.Title ?? "";
        set
        {
            if (Renderer != null && Renderer.Window.Title != value) Renderer.Window.Title = value;
        }
    }

    public void Close() => Renderer?.Close();

    /// Asks for a new frame. Frames are only drawn on demand, so state changed outside input handling (pty output, timers) must call this.
    public void Invalidate() => Renderer?.Invalidate();

    public void Invalidate(TimeSpan delay) => Renderer?.Invalidate(delay);
}
