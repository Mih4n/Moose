using Moose.Core.Settings;
using Moose.Core.Terminal.Sessions;

namespace Moose.UI;

/// The layout the first window opens with, picked from the command line and the [session] settings.
sealed record StartupSession(SessionLayout? Layout)
{
    public const string Restore = "--restore";
    public const string NoRestore = "--no-restore";
    public const string Session = "--session";

    public const string Usage = """
        Usage: moose [options]

          --session <name|file>  open the tabs of a layout file, or of a session saved under that name
          --restore              reopen the tabs that were open when Moose last closed
          --no-restore           start with one tab even if [session] restore_on_start is set
          -h, --help             show this help

        A layout is a TOML file:

          active = 0             # index of the tab to focus

          [[tabs]]
          title = "server"       # optional; replaces the title the shell sets
          working_directory = "~/project"
          program = "fish"       # optional; [shell] program by default
          args = ["-c", "dotnet watch"]
        """;

    public sealed record Arguments(string? Session, bool Restore, bool NoRestore, bool Help, string[] Rest);

    /// Pulls Moose's own options out of args; everything else is passed on to the host.
    public static Arguments Parse(string[] args)
    {
        string? session = null;
        bool restore = false, noRestore = false, help = false;
        var rest = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case Restore: restore = true; break;
                case NoRestore: noRestore = true; break;
                case "-h" or "--help": help = true; break;
                case Session when index + 1 < args.Length: session = args[++index]; break;
                case var arg when arg.StartsWith(Session + "=", StringComparison.Ordinal): session = arg[(Session.Length + 1)..]; break;
                default: rest.Add(args[index]); break;
            }
        }
        return new Arguments(session, restore, noRestore, help, [.. rest]);
    }

    /// Throws FileNotFoundException or FormatException when a layout named on the command line can't be read;
    /// the saved session is only best effort, since there's none before the first exit.
    public static StartupSession Resolve(Arguments arguments, SessionStore store, SessionOptions options)
    {
        if (arguments.Session is { } session) return new(store.Load(session));
        if (arguments.NoRestore || !(arguments.Restore || options.RestoreOnStart)) return new((SessionLayout?)null);
        try { return new(store.LoadLast()); }
        catch (Exception exception) when (exception is IOException or FormatException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"moose: couldn't restore the previous session: {exception.Message}");
            return new((SessionLayout?)null);
        }
    }
}
