using System.Numerics;
using Moose.Graphics.Fonts;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;

namespace Moose.Graphics.Elements;

public sealed class Text(string content) : Element<Text>
{
    Color color = new(1, 1, 1);
    float? fontSize;
    bool truncate;
    TextSystem? textSystem;

    public Text Color(Color color) { this.color = color; return this; }
    public Text FontSize(float size) { fontSize = size; return this; }
    public Text Truncate() { truncate = true; return this; }

    internal override void Measure(TextSystem textSystem)
    {
        this.textSystem = textSystem;
        var size = textSystem.Measure(content, fontSize ?? textSystem.DefaultSize);
        if (!truncate) width ??= size.X + 2 * padding;
        height ??= size.Y + 2 * padding;
        base.Measure(textSystem);
    }

    internal override void Paint(List<Quad> scene, Vector2 mouse)
    {
        base.Paint(scene, mouse);
        if (textSystem == null) return;
        var size = fontSize ?? textSystem.DefaultSize;
        var text = truncate ? textSystem.Truncate(content, size, Bounds.W - 2 * padding) : content;
        textSystem.Paint(scene, text, size, new Vector2(Bounds.X + padding, Bounds.Y + padding), color);
    }
}
