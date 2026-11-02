using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Moose.Graphics.Fonts;

internal sealed class FontconfigLocator : IFontLocator
{
    // fontconfig ranks fonts covering the charset first; past the first few the list is every installed font
    const int FallbackCandidates = 8;

    public string? Find(string family) => Match(family).FirstOrDefault() is { } path && File.Exists(path) ? path : null;

    public IEnumerable<string> FindFallbacks(string family, Rune rune) =>
        Match($"{family}:charset={rune.Value.ToString("x", CultureInfo.InvariantCulture)}", sorted: true)
            .Where(File.Exists)
            .Distinct()
            .Take(FallbackCandidates);

    static IReadOnlyList<string> Match(string pattern, bool sorted = false)
    {
        var start = new ProcessStartInfo("fc-match") { RedirectStandardOutput = true, ArgumentList = { "-f", "%{file}\\n", pattern } };
        if (sorted) start.ArgumentList.Insert(0, "-s");
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        }
        catch (Win32Exception)
        {
            return [];
        }
    }
}
