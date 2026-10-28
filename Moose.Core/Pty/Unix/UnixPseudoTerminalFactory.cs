using System.Runtime.InteropServices;
using System.Text;

namespace Moose.Core.Pty.Unix;

public sealed class UnixPseudoTerminalFactory : IPseudoTerminalFactory
{
    const int NativeBufferSize = 1024;

    public IPseudoTerminal Start(ShellStartInfo startInfo, PtySize size)
    {
        var master = LibC.posix_openpt(LibC.ReadWrite | LibC.NoControllingTerminal | LibC.CloseOnExec);
        LibC.Check(master, "posix_openpt");
        try
        {
            LibC.Check(LibC.grantpt(master), "grantpt");
            LibC.Check(LibC.unlockpt(master), "unlockpt");
            var winSize = WinSize.From(size);
            LibC.Check(LibC.ioctl(master, LibC.SetWindowSize, ref winSize), "ioctl(TIOCSWINSZ)");
            var processId = Spawn(startInfo, SlavePath(master));
            return new UnixPseudoTerminal(master, processId);
        }
        catch
        {
            LibC.close(master);
            throw;
        }
    }

    static string SlavePath(int master)
    {
        var buffer = new byte[256];
        LibC.CheckError(LibC.ptsname_r(master, buffer, buffer.Length), "ptsname_r");
        return Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0));
    }

    static int Spawn(ShellStartInfo startInfo, string slavePath)
    {
        var actions = Marshal.AllocHGlobal(NativeBufferSize);
        var attributes = Marshal.AllocHGlobal(NativeBufferSize);
        var signals = Marshal.AllocHGlobal(NativeBufferSize);
        var arguments = ToNativeStrings([startInfo.Program, .. startInfo.Arguments]);
        var environment = ToNativeStrings(startInfo.Environment.Select(pair => $"{pair.Key}={pair.Value}"));
        try
        {
            LibC.CheckError(LibC.posix_spawn_file_actions_init(actions), "posix_spawn_file_actions_init");
            LibC.CheckError(LibC.posix_spawnattr_init(attributes), "posix_spawnattr_init");
            LibC.CheckError(LibC.posix_spawn_file_actions_addopen(actions, 0, slavePath, LibC.ReadWrite, 0), "posix_spawn_file_actions_addopen");
            LibC.CheckError(LibC.posix_spawn_file_actions_adddup2(actions, 0, 1), "posix_spawn_file_actions_adddup2");
            LibC.CheckError(LibC.posix_spawn_file_actions_adddup2(actions, 0, 2), "posix_spawn_file_actions_adddup2");
            LibC.CheckError(LibC.posix_spawn_file_actions_addchdir_np(actions, startInfo.WorkingDirectory), "posix_spawn_file_actions_addchdir_np");

            LibC.sigemptyset(signals);
            LibC.CheckError(LibC.posix_spawnattr_setsigmask(attributes, signals), "posix_spawnattr_setsigmask");
            LibC.sigfillset(signals);
            LibC.CheckError(LibC.posix_spawnattr_setsigdefault(attributes, signals), "posix_spawnattr_setsigdefault");
            var flags = (short)(LibC.SpawnSetSessionId | LibC.SpawnSetSignalMask | LibC.SpawnSetSignalDefault | LibC.SpawnCloseOnExecDefault);
            LibC.CheckError(LibC.posix_spawnattr_setflags(attributes, flags), "posix_spawnattr_setflags");

            LibC.CheckError(LibC.posix_spawnp(out var processId, startInfo.Program, actions, attributes, arguments, environment), $"posix_spawnp({startInfo.Program})");
            return processId;
        }
        finally
        {
            LibC.posix_spawn_file_actions_destroy(actions);
            LibC.posix_spawnattr_destroy(attributes);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(signals);
            FreeNativeStrings(arguments);
            FreeNativeStrings(environment);
        }
    }

    static nint[] ToNativeStrings(IEnumerable<string> values) => [.. values.Select(Marshal.StringToCoTaskMemUTF8), 0];

    static void FreeNativeStrings(nint[] pointers)
    {
        foreach (var pointer in pointers) Marshal.FreeCoTaskMem(pointer);
    }
}
