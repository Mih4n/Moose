using System.ComponentModel;
using System.Diagnostics;

namespace Moose.Graphics.Fonts;

internal sealed class FontconfigLocator : IFontLocator
{
    public string? Find(string family)
    {
        var start = new ProcessStartInfo("fc-match") { RedirectStandardOutput = true, ArgumentList = { "-f", "%{file}", family } };
        try
        {
            using var process = Process.Start(start)!;
            var path = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && File.Exists(path) ? path : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
