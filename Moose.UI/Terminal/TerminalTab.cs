using System.ComponentModel;
using Moose.Core.Terminal.Grid;
using Moose.Core.Terminal.Sessions;

namespace Moose.UI.Terminal;

sealed class TerminalTab(TerminalSession session) : IDisposable
{
    bool startFailed;

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

    public void Dispose() => session.Dispose();
}
