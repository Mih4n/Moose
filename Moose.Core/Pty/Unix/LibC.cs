using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Moose.Core.Pty.Unix;

internal static class LibC
{
    const string Library = "libc";

    public const int ReadWrite = 2;
    public const int Interrupted = 4;
    public const int TryAgain = 11;
    public const int HangupSignal = 1;
    public const int SpawnSetSignalDefault = 0x04;
    public const int SpawnSetSignalMask = 0x08;

    public static int NoControllingTerminal => OperatingSystem.IsMacOS() ? 0x20000 : 0x100;
    public static int CloseOnExec => OperatingSystem.IsMacOS() ? 0 : 0x80000;
    public static int SpawnSetSessionId => OperatingSystem.IsMacOS() ? 0x400 : 0x80;
    public static int SpawnCloseOnExecDefault => OperatingSystem.IsMacOS() ? 0x4000 : 0;
    public static nuint SetWindowSize => OperatingSystem.IsMacOS() ? 0x80087467 : 0x5414;

    [DllImport(Library, SetLastError = true)]
    public static extern int posix_openpt(int flags);

    [DllImport(Library, SetLastError = true)]
    public static extern int grantpt(int fileDescriptor);

    [DllImport(Library, SetLastError = true)]
    public static extern int unlockpt(int fileDescriptor);

    [DllImport(Library)]
    public static extern int ptsname_r(int fileDescriptor, byte[] buffer, nint length);

    [DllImport(Library)]
    public static extern int posix_spawn_file_actions_init(nint actions);

    [DllImport(Library)]
    public static extern int posix_spawn_file_actions_destroy(nint actions);

    [DllImport(Library)]
    public static extern int posix_spawn_file_actions_addopen(nint actions, int fileDescriptor, string path, int flags, int mode);

    [DllImport(Library)]
    public static extern int posix_spawn_file_actions_adddup2(nint actions, int fileDescriptor, int newFileDescriptor);

    [DllImport(Library)]
    public static extern int posix_spawn_file_actions_addchdir_np(nint actions, string path);

    [DllImport(Library)]
    public static extern int posix_spawnattr_init(nint attributes);

    [DllImport(Library)]
    public static extern int posix_spawnattr_destroy(nint attributes);

    [DllImport(Library)]
    public static extern int posix_spawnattr_setflags(nint attributes, short flags);

    [DllImport(Library)]
    public static extern int posix_spawnattr_setsigmask(nint attributes, nint signals);

    [DllImport(Library)]
    public static extern int posix_spawnattr_setsigdefault(nint attributes, nint signals);

    [DllImport(Library)]
    public static extern int sigemptyset(nint signals);

    [DllImport(Library)]
    public static extern int sigfillset(nint signals);

    [DllImport(Library)]
    public static extern int posix_spawnp(out int processId, string file, nint actions, nint attributes, nint[] arguments, nint[] environment);

    [DllImport(Library, SetLastError = true)]
    public static extern nint read(int fileDescriptor, ref byte buffer, nint count);

    [DllImport(Library, SetLastError = true)]
    public static extern nint write(int fileDescriptor, ref byte buffer, nint count);

    [DllImport(Library, SetLastError = true)]
    public static extern int ioctl(int fileDescriptor, nuint request, ref WinSize size);

    [DllImport(Library, SetLastError = true)]
    public static extern int waitpid(int processId, out int status, int options);

    [DllImport(Library, SetLastError = true)]
    public static extern int kill(int processId, int signal);

    [DllImport(Library, SetLastError = true)]
    public static extern int close(int fileDescriptor);

    public static void Check(int result, string call)
    {
        if (result < 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"{call} failed");
    }

    public static void CheckError(int error, string call)
    {
        if (error != 0) throw new Win32Exception(error, $"{call} failed");
    }
}
