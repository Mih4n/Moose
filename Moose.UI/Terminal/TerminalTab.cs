using System.ComponentModel;
using Moose.Core.Terminal.Grid;
using Moose.Core.Terminal.Sessions;

namespace Moose.UI.Terminal;

sealed class TerminalTab(TerminalSession session) : IDisposable
{
    bool startFailed;
    // sub-line scroll: how many logical px of the line above the viewport are scrolled into view;
    // only valid while the viewport still starts at the line it was measured against
    float scrollFraction;
    long fractionAnchor = -1;

    public TerminalSession Session => session;
    public Selection? Selection { get; set; }

    public string Title(bool dynamic)
    {
        string title;
        lock (session.SyncRoot) title = session.Screen.Title;
        return dynamic && !string.IsNullOrWhiteSpace(title) ? title : Path.GetFileName(session.StartInfo.Program);
    }

    public void EnsureStarted(int columns, int rows)
    {
        if (session.IsStarted || startFailed) return;
        try
        {
            session.Start(columns, rows);
        }
        catch (Exception exception) when (exception is Win32Exception or PlatformNotSupportedException)
        {
            startFailed = true;
            lock (session.SyncRoot) session.Screen.Feed($"Failed to start '{session.StartInfo.Program}': {exception.Message}\r\n");
        }
    }

    /// Viewport position in logical px from the bottom, with SyncRoot held.
    public float ScrollPosition(TerminalScreen screen, float lineHeight) => screen.ViewportOffset * lineHeight + ScrollFraction(screen, lineHeight);

    /// Sub-line part of the position, with SyncRoot held.
    public float ScrollFraction(TerminalScreen screen, float lineHeight) =>
        !screen.IsAlternateScreen && screen.FirstViewportLine == fractionAnchor ? Math.Min(scrollFraction, lineHeight) : 0;

    /// Scrolls by pixels (positive = back into history), keeping the sub-line remainder so touchpads scroll smoothly.
    public void ScrollPixels(float pixels, float lineHeight)
    {
        if (lineHeight <= 0) return;
        lock (session.SyncRoot)
        {
            var screen = session.Screen;
            if (screen.IsAlternateScreen) return;
            var position = Math.Clamp(ScrollPosition(screen, lineHeight) + pixels, 0, screen.Scrollback.Count * lineHeight);
            var lines = Math.Min(screen.Scrollback.Count, (int)(position / lineHeight));
            screen.ScrollViewport(lines - screen.ViewportOffset);
            scrollFraction = Math.Max(0, position - lines * lineHeight);
            fractionAnchor = screen.FirstViewportLine;
        }
    }

    public void ResetScrollFraction() => scrollFraction = 0;

    public void Dispose() => session.Dispose();
}
