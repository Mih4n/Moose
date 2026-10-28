using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Configuration;
using Moose.Core.Terminal.Grid;
using Moose.Core.Terminal.Sessions;
using Moose.Graphics.Elements;
using Moose.Graphics.Hosting;
using Moose.UI.Input;
using Moose.UI.Terminal;
using Silk.NET.Input;
using static Moose.Graphics.Elements.Ui;
using GraphicsModifiers = Moose.Graphics.Input.KeyModifiers;
using IInputHandler = Moose.Graphics.Input.IInputHandler;
using MouseAction = Moose.Core.Terminal.Input.MouseAction;
using TerminalKey = Moose.Core.Terminal.Input.TerminalKey;
using TerminalModifiers = Moose.Core.Terminal.Input.KeyModifiers;
using TerminalMouseButton = Moose.Core.Terminal.Input.MouseButton;

namespace Moose.UI;

sealed class RootView : IView, IInputHandler
{
    readonly TerminalSessionFactory sessionFactory;
    readonly AppWindow window;
    readonly IConfiguration configuration;
    readonly ConcurrentQueue<Action> mainThread = new();
    readonly List<TerminalTab> tabs = [];
    readonly TerminalView view;
    UiSettings settings;
    int active;
    bool selecting;
    TerminalMouseButton? reportedButton;
    (int Column, int Row)? lastReportedCell;

    public RootView(TerminalSessionFactory sessionFactory, AppWindow window, IConfiguration configuration)
    {
        this.sessionFactory = sessionFactory;
        this.window = window;
        this.configuration = configuration;
        settings = UiSettings.Load(configuration);
        view = new TerminalView(settings);
        OpenTab();
    }

    TerminalTab? ActiveTab => tabs.Count > 0 ? tabs[Math.Clamp(active, 0, tabs.Count - 1)] : null;

    public Element Render()
    {
        while (mainThread.TryDequeue(out var action)) action();
        if (ActiveTab is not { } activeTab) return Div();

        window.Title = settings.Window.DynamicTitle ? activeTab.Title(dynamic: true) : settings.Window.Title;
        var theme = settings.Theme;
        return Row()
            .Bg(theme.Background)
            .Children(
                Column()
                    .W(220)
                    .Bg(theme.Panel)
                    .Children([.. tabs.Select((tab, index) => TabFor(tab, index, theme)), NewTabButton(theme, OpenTab)]),
                Div()
                    .W(1)
                    .Bg(theme.Border),
                view.Render(activeTab));
    }

    Tab TabFor(TerminalTab tab, int index, UiTheme theme) =>
        new Tab(tab.Title(settings.Window.DynamicTitle), index == active, theme, () => CloseTab(tab))
            .OnClick(() => active = index)
            .OnMiddleClick(() => CloseTab(tab));

    static Element NewTabButton(UiTheme theme, Action open) =>
        Div()
            .H(36)
            .Padding(8)
            .OnClick(open)
            .Hover(style => style.Bg(theme.Hover))
            .Child(Text("+").Color(theme.TextMuted));

    public void KeyDown(Key key, GraphicsModifiers modifiers)
    {
        if (ActiveTab is not { } tab) return;
        var binding = settings.Keybindings.FirstOrDefault(candidate => candidate.Chord == new KeyChord(key, modifiers));
        if (binding != null)
        {
            RunAction(binding.Action, tab);
            return;
        }

        var terminalModifiers = (TerminalModifiers)(int)modifiers;
        if (KeyTranslator.Named(key) is { } named)
        {
            view.RestartBlink();
            tab.Session.SendKey(named, terminalModifiers);
            return;
        }
        if ((modifiers & (GraphicsModifiers.Control | GraphicsModifiers.Alt)) == 0) return;
        if (KeyTranslator.Character(key, modifiers.HasFlag(GraphicsModifiers.Shift)) is { } character)
        {
            view.RestartBlink();
            tab.Session.SendCharacter(character, terminalModifiers);
        }
    }

    public void TextInput(string text)
    {
        if (ActiveTab is not { } tab) return;
        view.RestartBlink();
        tab.Session.SendText(text);
    }

    public void MouseDown(MouseButton button, Vector2 position, GraphicsModifiers modifiers)
    {
        if (ActiveTab is not { } tab || !view.Contains(position)) return;
        var cell = view.CellAt(position);
        if (Reports(tab, modifiers) && Translate(button) is { } reported)
        {
            reportedButton = reported;
            lastReportedCell = cell;
            tab.Session.SendMouse(reported, MouseAction.Press, cell.Column, cell.Row, (TerminalModifiers)(int)modifiers);
            return;
        }
        if (button != MouseButton.Left) return;
        var point = GridPointAt(tab, cell);
        tab.Selection = new Selection(point, point);
        selecting = true;
    }

    public void MouseMove(Vector2 position)
    {
        if (ActiveTab is not { } tab) return;
        var cell = view.CellAt(position);
        if (selecting && tab.Selection is { } selection)
        {
            tab.Selection = selection with { Head = GridPointAt(tab, cell) };
            return;
        }
        if (!view.Contains(position) || cell == lastReportedCell || !Reports(tab, GraphicsModifiers.None)) return;
        lastReportedCell = cell;
        tab.Session.SendMouse(reportedButton ?? TerminalMouseButton.None, MouseAction.Motion, cell.Column, cell.Row, TerminalModifiers.None);
    }

    public void MouseUp(MouseButton button, Vector2 position, GraphicsModifiers modifiers)
    {
        if (ActiveTab is not { } tab) return;
        if (reportedButton is { } reported)
        {
            var cell = view.CellAt(position);
            reportedButton = null;
            tab.Session.SendMouse(reported, MouseAction.Release, cell.Column, cell.Row, (TerminalModifiers)(int)modifiers);
            return;
        }
        if (!selecting) return;
        selecting = false;
        if (tab.Selection is { IsEmpty: true }) tab.Selection = null;
        else if (settings.Mouse.CopyOnSelect) Copy(tab);
    }

    public void Scroll(Vector2 position, float delta, GraphicsModifiers modifiers)
    {
        if (ActiveTab is not { } tab || !view.Contains(position) || delta == 0) return;
        var notches = Math.Max(1, (int)MathF.Round(MathF.Abs(delta)));
        if (Reports(tab, modifiers))
        {
            var cell = view.CellAt(position);
            var wheel = delta > 0 ? TerminalMouseButton.WheelUp : TerminalMouseButton.WheelDown;
            for (var notch = 0; notch < notches; notch++) tab.Session.SendMouse(wheel, MouseAction.Press, cell.Column, cell.Row, (TerminalModifiers)(int)modifiers);
            return;
        }
        var lines = notches * Math.Max(1, settings.Scrollback.Multiplier);
        bool alternateScreen;
        lock (tab.Session.SyncRoot) alternateScreen = tab.Session.Screen.IsAlternateScreen;
        if (alternateScreen)
        {
            for (var line = 0; line < lines; line++) tab.Session.SendKey(delta > 0 ? TerminalKey.Up : TerminalKey.Down, TerminalModifiers.None);
            return;
        }
        lock (tab.Session.SyncRoot) tab.Session.Screen.ScrollViewport(delta > 0 ? lines : -lines);
    }

    public void FocusChanged(bool focused) => ActiveTab?.Session.SetFocus(focused);

    void RunAction(string action, TerminalTab tab)
    {
        switch (action)
        {
            case "copy": Copy(tab); break;
            case "paste": tab.Session.Paste(window.Clipboard); break;
            case "font_size_increase": view.ChangeFontSize(1); break;
            case "font_size_decrease": view.ChangeFontSize(-1); break;
            case "font_size_reset": view.ResetFontSize(); break;
            case "scroll_page_up": ScrollPage(tab, 1); break;
            case "scroll_page_down": ScrollPage(tab, -1); break;
            case "new_tab": OpenTab(); break;
            case "new_window": StartNewWindow(); break;
            case "reload_config": ReloadConfiguration(); break;
        }
    }

    void OpenTab()
    {
        var tab = new TerminalTab(sessionFactory.Create());
        tab.Session.Exited += _ => mainThread.Enqueue(() => CloseTab(tab));
        tab.Session.ClipboardSet += text => mainThread.Enqueue(() => window.Clipboard = text);
        tabs.Add(tab);
        active = tabs.Count - 1;
    }

    void CloseTab(TerminalTab tab)
    {
        var index = tabs.IndexOf(tab);
        if (index < 0) return;
        tabs.RemoveAt(index);
        tab.Dispose();
        if (active > index || active >= tabs.Count) active = Math.Max(0, active - 1);
        if (tabs.Count == 0) window.Close();
    }

    void Copy(TerminalTab tab)
    {
        if (tab.Selection is not { IsEmpty: false } selection) return;
        string text;
        lock (tab.Session.SyncRoot) text = tab.Session.Screen.GetText(selection);
        if (text.Length > 0) window.Clipboard = text;
    }

    void ScrollPage(TerminalTab tab, int direction)
    {
        lock (tab.Session.SyncRoot) tab.Session.Screen.ScrollViewport(direction * Math.Max(1, view.Rows - 1));
    }

    void ReloadConfiguration()
    {
        if (configuration is IConfigurationRoot root) root.Reload();
        settings = UiSettings.Load(configuration);
        view.Configure(settings);
    }

    static void StartNewWindow()
    {
        if (Environment.ProcessPath is { } path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = false })?.Dispose();
    }

    GridPoint GridPointAt(TerminalTab tab, (int Column, int Row) cell)
    {
        lock (tab.Session.SyncRoot) return new GridPoint(tab.Session.Screen.FirstViewportLine + cell.Row, cell.Column);
    }

    static bool Reports(TerminalTab tab, GraphicsModifiers modifiers)
    {
        if (modifiers.HasFlag(GraphicsModifiers.Shift)) return false;
        lock (tab.Session.SyncRoot) return tab.Session.Screen.MouseTracking != MouseTracking.None;
    }

    static TerminalMouseButton? Translate(MouseButton button) => button switch
    {
        MouseButton.Left => TerminalMouseButton.Left,
        MouseButton.Middle => TerminalMouseButton.Middle,
        MouseButton.Right => TerminalMouseButton.Right,
        _ => null,
    };
}
