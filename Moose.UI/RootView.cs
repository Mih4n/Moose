using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Moose.Core;
using Moose.Core.Terminal.Grid;
using Moose.Core.Terminal.Sessions;
using Moose.Graphics.Elements;
using Moose.Graphics.Hosting;
using Moose.UI.Input;
using Moose.UI.Palette;
using Moose.UI.Terminal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
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
    readonly SessionStore sessions;
    readonly ILogger<RootView> logger;
    readonly AppWindow window;
    readonly ConcurrentQueue<Action> mainThread = new();
    readonly List<TerminalTab> tabs = [];
    readonly TerminalView view;
    readonly CommandPalette palette = new();
    UiSettings settings;
    int active;
    bool selecting;
    TerminalMouseButton? reportedButton;
    (int Column, int Row)? lastReportedCell;
    float wheelRemainder;

    public RootView(TerminalSessionFactory sessionFactory, SessionStore sessions, StartupSession startup, AppWindow window, ILogger<RootView> logger)
    {
        this.sessionFactory = sessionFactory;
        this.sessions = sessions;
        this.logger = logger;
        this.window = window;
        settings = UiSettings.Load();
        view = new TerminalView(settings, window);
        // the config file reloads itself when it changes; pick that up on the main thread
        ChangeToken.OnChange(Configuration.Current.GetReloadToken, () => Post(ApplySettings));
        if (startup.Layout is { Tabs.Count: > 0 } layout) OpenLayout(layout);
        else OpenTab();
    }

    TerminalTab? ActiveTab => tabs.Count > 0 ? tabs[Math.Clamp(active, 0, tabs.Count - 1)] : null;

    public Element Render()
    {
        while (mainThread.TryDequeue(out var action)) action();
        if (ActiveTab is not { } activeTab) return Div();

        window.Title = settings.Window.DynamicTitle ? activeTab.Title(dynamic: true) : settings.Window.Title;
        var theme = settings.Theme;
        var content = Row()
            .Bg(theme.Background)
            .Children(
                Column()
                    .W(220)
                    .Bg(theme.Panel)
                    .Children([.. tabs.Select((tab, index) => TabFor(tab, index, theme)), NewTabButton(theme, () => OpenTab())]),
                Div()
                    .W(theme.BorderWidth)
                    .Bg(theme.Border),
                view.Render(activeTab));
        return palette.IsOpen ? Stack().Children(content, palette.Render(theme, settings.Keybindings, action => RunPaletteAction(action, activeTab))) : content;
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
        var chord = new KeyChord(key, modifiers);
        var binding = settings.Keybindings.LastOrDefault(candidate => candidate.Chords.Contains(chord));

        if (palette.IsOpen)
        {
            if (binding?.Action == "command_palette") palette.Close();
            else if (palette.KeyDown(key, modifiers) is { } picked) RunPaletteAction(picked, tab);
            return;
        }
        // "none" unbinds a default chord so it reaches the shell again, and copy_or_interrupt only keeps it while there's a selection
        if (binding is { Action: "copy_or_interrupt" } && tab.Selection is { IsEmpty: false })
        {
            Copy(tab);
            tab.Selection = null;
            return;
        }
        if (binding is { Action: not ("none" or "copy_or_interrupt") })
        {
            RunAction(binding.Action, tab);
            return;
        }

        var terminalModifiers = (TerminalModifiers)(int)modifiers;
        if (KeyTranslator.Named(key) is { } named)
        {
            Typed(tab);
            tab.Session.SendKey(named, terminalModifiers);
            return;
        }
        if ((modifiers & (GraphicsModifiers.Control | GraphicsModifiers.Alt)) == 0) return;
        if (KeyTranslator.Character(key, modifiers.HasFlag(GraphicsModifiers.Shift)) is { } character)
        {
            Typed(tab);
            tab.Session.SendCharacter(character, terminalModifiers);
        }
    }

    public void TextInput(string text)
    {
        if (ActiveTab is not { } tab) return;
        if (palette.IsOpen)
        {
            palette.TextInput(text);
            return;
        }
        Typed(tab);
        tab.Session.SendText(text);
    }

    public void MouseDown(MouseButton button, Vector2 position, GraphicsModifiers modifiers)
    {
        if (palette.IsOpen || ActiveTab is not { } tab || !view.Contains(position)) return;
        if (button == MouseButton.Left && view.ScrollbarContains(position))
        {
            view.BeginScrollbarDrag(tab, position.Y);
            return;
        }
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
        if (palette.IsOpen || ActiveTab is not { } tab) return;
        view.ScrollbarHovered = view.ScrollbarContains(position);
        if (view.ScrollbarDragging)
        {
            view.DragScrollbar(tab, position.Y);
            return;
        }
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
        if (view.ScrollbarDragging)
        {
            view.EndScrollbarDrag();
            return;
        }
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
        if (palette.IsOpen)
        {
            palette.Scroll(delta);
            return;
        }
        if (ActiveTab is not { } tab || !view.Contains(position) || delta == 0) return;
        var multiplier = Math.Max(1, settings.Scrollback.Multiplier);
        bool alternateScreen;
        lock (tab.Session.SyncRoot) alternateScreen = tab.Session.Screen.IsAlternateScreen;
        if (!Reports(tab, modifiers) && !alternateScreen)
        {
            // one wheel notch = multiplier lines; touchpads send fractions of a notch, which scroll by pixels
            tab.ScrollPixels(delta * multiplier * view.LineHeight, view.LineHeight);
            return;
        }
        // the application only understands whole steps: collect fractional deltas until they add up to one
        wheelRemainder += delta;
        var notches = (int)wheelRemainder;
        wheelRemainder -= notches;
        if (notches == 0) return;
        if (Reports(tab, modifiers))
        {
            var cell = view.CellAt(position);
            var wheel = notches > 0 ? TerminalMouseButton.WheelUp : TerminalMouseButton.WheelDown;
            for (var notch = 0; notch < Math.Abs(notches); notch++) tab.Session.SendMouse(wheel, MouseAction.Press, cell.Column, cell.Row, (TerminalModifiers)(int)modifiers);
            return;
        }
        for (var line = 0; line < Math.Abs(notches) * multiplier; line++) tab.Session.SendKey(notches > 0 ? TerminalKey.Up : TerminalKey.Down, TerminalModifiers.None);
    }

    void Typed(TerminalTab tab)
    {
        tab.Selection = null;
        view.RestartBlink();
        tab.ResetScrollFraction();
    }

    public void FocusChanged(bool focused) => ActiveTab?.Session.SetFocus(focused);

    public void FileDrop(string[] paths)
    {
        if (palette.IsOpen || ActiveTab is not { } tab || paths.Length == 0) return;
        Typed(tab);
        tab.Session.Paste(string.Join(' ', paths) + " ");
    }

    void RunAction(string action, TerminalTab tab)
    {
        switch (action)
        {
            case "copy": Copy(tab); break;
            case "paste":
                Typed(tab);
                tab.Session.Paste(window.Clipboard);
                break;
            case "font_size_increase": view.ChangeFontSize(1); break;
            case "font_size_decrease": view.ChangeFontSize(-1); break;
            case "font_size_reset": view.ResetFontSize(); break;
            case "scroll_page_up": ScrollPage(tab, 1); break;
            case "scroll_page_down": ScrollPage(tab, -1); break;
            case "scroll_line_up": ScrollLines(tab, 1); break;
            case "scroll_line_down": ScrollLines(tab, -1); break;
            case "scroll_to_top": ScrollToTop(tab); break;
            case "scroll_to_bottom": ScrollToBottom(tab); break;
            case "new_tab": OpenTab(); break;
            case "close_tab": CloseTab(tab); break;
            case "rename_tab": palette.Prompt("Tab name… (empty resets it)", Command.RenameTab); break;
            case "next_tab": SwitchTab(1); break;
            case "previous_tab": SwitchTab(-1); break;
            case "command_palette":
                palette.Open([
                    .. tabs.Select((candidate, index) => Command.SwitchTab(index, candidate.Title(settings.Window.DynamicTitle))),
                    .. sessions.Names().Select(Command.OpenSession),
                ]);
                break;
            case "new_window": StartNewWindow(); break;
            case "reload_config": ReloadConfiguration(); break;
            case "save_session": SaveLastSession(); break;
            case "save_session_as": palette.Prompt("Session name…", Command.SaveSessionAs); break;
            case "restore_session": OpenSession(sessions.LoadLast, "the previous session"); break;
            default:
                if (Command.TabIndex(action) is { } index && index < tabs.Count) active = index;
                else if (Command.TabName(action) is { } name) tab.Name = name.Length > 0 ? name : null;
                else if (Command.SessionToOpen(action) is { } session) OpenSession(() => sessions.Load(session), $"session '{session}'");
                else if (Command.SessionToSave(action) is { } saveAs) SaveSession(saveAs);
                break;
        }
    }

    void RunPaletteAction(string action, TerminalTab tab)
    {
        palette.Close();
        RunAction(action, tab);
    }

    void Post(Action action)
    {
        mainThread.Enqueue(action);
        window.Invalidate();
    }

    void OpenTab(TabLayout? layout = null)
    {
        var tab = new TerminalTab(sessionFactory.Create(layout), layout);
        tab.Session.Changed += window.Invalidate;
        tab.Session.Exited += _ => Post(() => CloseTab(tab));
        tab.Session.ClipboardSet += text => Post(() => window.Clipboard = text);
        tabs.Add(tab);
        active = tabs.Count - 1;
    }

    /// Adds the layout's tabs after the open ones and activates the one it marks active.
    void OpenLayout(SessionLayout layout)
    {
        var first = tabs.Count;
        foreach (var tab in layout.Tabs) OpenTab(tab);
        active = first + Math.Clamp(layout.Active, 0, Math.Max(0, layout.Tabs.Count - 1));
    }

    void OpenSession(Func<SessionLayout?> load, string description)
    {
        try
        {
            if (load() is { Tabs.Count: > 0 } layout) OpenLayout(layout);
            else logger.LogWarning("There's no {Session} to open.", description);
        }
        catch (Exception exception) when (exception is IOException or FormatException or UnauthorizedAccessException)
        {
            logger.LogWarning("Couldn't open {Session}: {Message}", description, exception.Message);
        }
    }

    /// The open tabs with the directories their shells are in now, or null when there are none.
    public SessionLayout? Snapshot() => tabs.Count > 0 ? new SessionLayout([.. tabs.Select(tab => tab.Snapshot())], active) : null;

    public void SaveLastSession()
    {
        if (Snapshot() is not { } layout) return;
        try { sessions.SaveLast(layout); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Couldn't save the session: {Message}", exception.Message);
        }
    }

    void SaveSession(string name)
    {
        if (Snapshot() is not { } layout) return;
        try { logger.LogInformation("Saved the session to {Path}", sessions.Save(name, layout)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Couldn't save session '{Name}': {Message}", name, exception.Message);
        }
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

    void SwitchTab(int direction)
    {
        if (tabs.Count > 0) active = ((active + direction) % tabs.Count + tabs.Count) % tabs.Count;
    }

    void ScrollPage(TerminalTab tab, int direction) => ScrollLines(tab, direction * Math.Max(1, view.Rows - 1));

    static void ScrollLines(TerminalTab tab, int lines)
    {
        tab.ResetScrollFraction();
        lock (tab.Session.SyncRoot) tab.Session.Screen.ScrollViewport(lines);
    }

    static void ScrollToTop(TerminalTab tab)
    {
        tab.ResetScrollFraction();
        lock (tab.Session.SyncRoot) tab.Session.Screen.ScrollViewport(tab.Session.Screen.Scrollback.Count);
    }

    static void ScrollToBottom(TerminalTab tab)
    {
        tab.ResetScrollFraction();
        lock (tab.Session.SyncRoot) tab.Session.Screen.ResetViewport();
    }

    void ReloadConfiguration()
    {
        Configuration.Reload();
        ApplySettings();
    }

    void ApplySettings()
    {
        settings = UiSettings.Load();
        view.Configure(settings);
    }

    static void StartNewWindow()
    {
        if (Environment.ProcessPath is { } path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, ArgumentList = { StartupSession.NoRestore } })?.Dispose();
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
