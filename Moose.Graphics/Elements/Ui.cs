using Moose.Graphics.Rendering;

namespace Moose.Graphics.Elements;

public static class Ui
{
    public static Div Div() => new();
    public static Div Row() => new Div().Row();
    public static Div Column() => new Div().Column();
    public static Text Text(string content) => new(content);
    public static Canvas Canvas(Action<Painter> paint) => new(paint);
}
