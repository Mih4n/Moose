using System.Text;
using Moose.Core.Pty;
using Moose.Core.Terminal.Grid;
using Moose.Core.Terminal.Input;

namespace Moose.Core.Terminal.Sessions;

public sealed class TerminalSession : IDisposable
{
    const int ReadBufferSize = 64 * 1024;

    readonly IPseudoTerminalFactory ptyFactory;
    IPseudoTerminal? pty;
    Thread? reader;

    public TerminalSession(IPseudoTerminalFactory ptyFactory, ShellStartInfo startInfo, int scrollbackLines)
    {
        this.ptyFactory = ptyFactory;
        StartInfo = startInfo;
        Screen = new TerminalScreen(80, 24, scrollbackLines);
        Screen.Reply += Write;
        Screen.TitleChanged += title => TitleChanged?.Invoke(title);
        Screen.WorkingDirectoryChanged += directory => WorkingDirectoryChanged?.Invoke(directory);
        Screen.Bell += () => Bell?.Invoke();
        Screen.ClipboardSet += text => ClipboardSet?.Invoke(text);
    }

    public event Action? Changed;
    public event Action<string>? TitleChanged;
    public event Action<string>? WorkingDirectoryChanged;
    public event Action? Bell;
    public event Action<string>? ClipboardSet;
    public event Action<int>? Exited;

    public ShellStartInfo StartInfo { get; }
    public TerminalScreen Screen { get; }
    public object SyncRoot => Screen;
    public int? ExitCode { get; private set; }
    public bool IsStarted => pty != null;
    public bool IsRunning => pty != null && ExitCode == null;

    /// Where the shell is now: its process's cwd where /proc has it, else the last OSC 7 report, else where it started.
    public string WorkingDirectory
    {
        get
        {
            if (IsRunning && pty is { } terminal && OperatingSystem.IsLinux())
            {
                try
                {
                    if (new DirectoryInfo($"/proc/{terminal.ProcessId}/cwd").LinkTarget is { } target && Directory.Exists(target)) return target;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            lock (SyncRoot) return Screen.WorkingDirectory ?? StartInfo.WorkingDirectory;
        }
    }

    public void Start(int columns, int rows, int pixelWidth = 0, int pixelHeight = 0)
    {
        if (pty != null) throw new InvalidOperationException("Session already started.");
        lock (SyncRoot)
        {
            Screen.Resize(columns, rows);
            Screen.SetPixelSize(pixelWidth, pixelHeight);
        }
        pty = ptyFactory.Start(StartInfo, new PtySize(columns, rows, pixelWidth, pixelHeight));
        reader = new Thread(ReadLoop) { IsBackground = true, Name = $"pty {StartInfo.Program}" };
        reader.Start();
    }

    public void Write(ReadOnlySpan<byte> data) => pty?.Write(data);

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void SendKey(TerminalKey key, KeyModifiers modifiers)
    {
        bool applicationCursorKeys;
        lock (SyncRoot)
        {
            applicationCursorKeys = Screen.ApplicationCursorKeys;
            Screen.ResetViewport();
        }
        Write(KeyEncoder.Encode(key, modifiers, applicationCursorKeys));
    }

    public void SendCharacter(Rune character, KeyModifiers modifiers)
    {
        lock (SyncRoot) Screen.ResetViewport();
        Write(KeyEncoder.EncodeCharacter(character, modifiers));
    }

    public void SendText(string text)
    {
        lock (SyncRoot) Screen.ResetViewport();
        Write(text);
    }

    public void Paste(string text)
    {
        bool bracketed;
        lock (SyncRoot)
        {
            bracketed = Screen.BracketedPaste;
            Screen.ResetViewport();
        }
        var normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
        Write(bracketed ? $"\e[200~{normalized.Replace("\e[201~", "")}\e[201~" : normalized);
    }

    public bool SendMouse(MouseButton button, MouseAction action, int column, int row, KeyModifiers modifiers)
    {
        MouseTracking tracking;
        bool sgr;
        lock (SyncRoot)
        {
            tracking = Screen.MouseTracking;
            sgr = Screen.SgrMouse;
        }
        if (tracking == MouseTracking.None) return false;
        var reported = action != MouseAction.Motion
            || tracking == MouseTracking.AnyEvent
            || (tracking == MouseTracking.ButtonEvent && button != MouseButton.None);
        if (reported && MouseEncoder.Encode(button, action, column, row, modifiers, sgr) is { } sequence) Write(sequence);
        return true;
    }

    public void SetFocus(bool focused)
    {
        bool reporting;
        lock (SyncRoot) reporting = Screen.FocusReporting;
        if (reporting) Write(focused ? "\e[I" : "\e[O");
    }

    /// Pixel sizes are physical px of the whole grid; images are laid out with them, so a font size change counts too.
    public void Resize(int columns, int rows, int pixelWidth = 0, int pixelHeight = 0)
    {
        lock (SyncRoot)
        {
            if (columns == Screen.Columns && rows == Screen.Rows && pixelWidth == Screen.PixelWidth && pixelHeight == Screen.PixelHeight) return;
            Screen.Resize(columns, rows);
            Screen.SetPixelSize(pixelWidth, pixelHeight);
        }
        pty?.Resize(new PtySize(columns, rows, pixelWidth, pixelHeight));
        Changed?.Invoke();
    }

    public void Close() => pty?.Hangup();

    public void Dispose() => Close();

    void ReadLoop()
    {
        var terminal = pty!;
        var buffer = new byte[ReadBufferSize];
        while (true)
        {
            var count = terminal.Read(buffer);
            if (count <= 0) break;
            lock (SyncRoot) Screen.Feed(buffer.AsSpan(0, count));
            Changed?.Invoke();
        }
        var exitCode = terminal.WaitForExit();
        terminal.Dispose();
        ExitCode = exitCode;
        Exited?.Invoke(exitCode);
    }
}
