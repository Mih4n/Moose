using System.Numerics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Moose.Graphics.Elements;
using Moose.Graphics.Fonts;
using Moose.Graphics.Input;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.Graphics.Hosting;

public sealed class MooseApp(IHost host, WindowOptions window)
{
    readonly List<Action<Frame, Action>> middlewares = [];

    public static MooseAppBuilder CreateBuilder(string[] args) => new(args);

    public IServiceProvider Services => host.Services;
    public IConfiguration Configuration => Services.GetRequiredService<IConfiguration>();

    public MooseApp Use(Action<Frame, Action> middleware) { middlewares.Add(middleware); return this; }

    public MooseApp UseEffect<T>() where T : IFrameEffect => Use(ActivatorUtilities.GetServiceOrCreateInstance<T>(Services).Invoke);

    public void Run<TView>() where TView : IView
    {
        var view = ActivatorUtilities.GetServiceOrCreateInstance<TView>(Services);
        Run(view.Render, view as IInputHandler);
    }

    public void Run(Func<Element> render, IInputHandler? input = null)
    {
        var textSystem = Services.GetRequiredService<TextSystem>();
        var appWindow = Services.GetRequiredService<AppWindow>();
        host.Start();
        using var renderer = new VulkanRenderer(window, textSystem.Atlas, Services.GetRequiredService<ILogger<VulkanRenderer>>());
        using var stopping = Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(renderer.Close);
        appWindow.Renderer = renderer;

        (Vector2 Position, MouseButton Button)? click = null;
        renderer.Mouse.MouseDown += (mouse, button) =>
        {
            if (button is MouseButton.Left or MouseButton.Middle) click = (mouse.Position, button);
        };
        var repeat = input != null ? Route(renderer, input) : null;

        Action<Frame> pipeline = frame =>
        {
            if (repeat?.Due() is { } repeatedKey) input!.KeyDown(repeatedKey, Modifiers(renderer.Keyboard));
            var root = render();
            textSystem.Scale = renderer.Scale;
            root.Measure(textSystem);
            root.Layout(new Rect(0, 0, frame.Size.X, frame.Size.Y));
            if (click is { } pressed)
            {
                root.HitClick(pressed.Position, pressed.Button)?.ClickHandler(pressed.Button)!();
                click = null;
            }
            root.Paint(frame.Scene, renderer.Mouse.Position);
            if (repeat?.Remaining is { } untilRepeat) renderer.Invalidate(untilRepeat);
        };
        for (var i = middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = middlewares[i];
            var next = pipeline;
            pipeline = frame => middleware(frame, () => next(frame));
        }

        renderer.Run(size =>
        {
            var frame = new Frame(Services, size);
            pipeline(frame);
            return frame;
        });
        appWindow.Renderer = null;
        host.StopAsync().GetAwaiter().GetResult();
    }

    static KeyRepeat Route(VulkanRenderer renderer, IInputHandler input)
    {
        var keyboard = renderer.Keyboard;
        var repeat = new KeyRepeat(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(33));
        keyboard.KeyDown += (_, key, _) =>
        {
            repeat.Press(key);
            input.KeyDown(key, Modifiers(keyboard));
        };
        keyboard.KeyUp += (_, key, _) => repeat.Release(key);
        keyboard.KeyChar += (_, character) => input.TextInput(character.ToString());
        renderer.Mouse.MouseDown += (mouse, button) => input.MouseDown(button, mouse.Position, Modifiers(keyboard));
        renderer.Mouse.MouseUp += (mouse, button) => input.MouseUp(button, mouse.Position, Modifiers(keyboard));
        renderer.Mouse.MouseMove += (_, position) => input.MouseMove(position);
        renderer.Mouse.Scroll += (mouse, wheel) => input.Scroll(mouse.Position, wheel.Y, Modifiers(keyboard));
        renderer.Window.FocusChanged += input.FocusChanged;
        return repeat;
    }

    static KeyModifiers Modifiers(IKeyboard keyboard)
    {
        var modifiers = KeyModifiers.None;
        if (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight)) modifiers |= KeyModifiers.Shift;
        if (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight)) modifiers |= KeyModifiers.Alt;
        if (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)) modifiers |= KeyModifiers.Control;
        if (keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight)) modifiers |= KeyModifiers.Super;
        return modifiers;
    }
}
