namespace Moose.Core.Settings;

public sealed class AnsiColorOptions
{
    public string Black { get; set; } = "";
    public string Red { get; set; } = "";
    public string Green { get; set; } = "";
    public string Yellow { get; set; } = "";
    public string Blue { get; set; } = "";
    public string Magenta { get; set; } = "";
    public string Cyan { get; set; } = "";
    public string White { get; set; } = "";

    public string[] InOrder() => [Black, Red, Green, Yellow, Blue, Magenta, Cyan, White];
}
