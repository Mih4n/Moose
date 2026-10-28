using System.Numerics;
using Moose.Graphics.Fonts;
using Moose.Graphics.Rendering;

namespace Moose.Graphics.Elements;

public sealed class Canvas(Action<Painter> paint) : Element<Canvas>
{
    TextSystem? textSystem;

    internal override void Measure(TextSystem textSystem)
    {
        this.textSystem = textSystem;
        base.Measure(textSystem);
    }

    internal override void Paint(List<Quad> scene, Vector2 mouse)
    {
        base.Paint(scene, mouse);
        if (textSystem != null) paint(new Painter(scene, textSystem, Bounds));
    }
}
