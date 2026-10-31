using System.Numerics;
using System.Text;
using Moose.Graphics.Fonts;
using Moose.Graphics.Primitives;

namespace Moose.Graphics.Rendering;

public sealed class Painter
{
    readonly List<Quad> scene;
    readonly TextSystem textSystem;

    internal Painter(List<Quad> scene, TextSystem textSystem, Rect bounds)
    {
        this.scene = scene;
        this.textSystem = textSystem;
        Bounds = bounds;
    }

    public Rect Bounds { get; }
    public float DefaultFontSize => textSystem.DefaultSize;

    public Vector2 CellSize(float fontSize) => textSystem.CellSize(fontSize);

    public Vector2 Snap(Vector2 point) => new(MathF.Round(point.X * textSystem.Scale.X) / textSystem.Scale.X, MathF.Round(point.Y * textSystem.Scale.Y) / textSystem.Scale.Y);

    public void Fill(Rect rect, Color color, float radius = 0) => scene.Add(new Quad(rect, color, Color.Transparent, radius, 0));

    public void Glyph(Rune rune, Vector2 origin, float fontSize, Color color) => textSystem.PaintRune(scene, rune, fontSize, origin, color);

    /// One cell per codepoint from origin, shaped together so ligatures form.
    public void Glyphs(ReadOnlySpan<int> codepoints, Vector2 origin, float fontSize, Color color) => textSystem.PaintCells(scene, codepoints, fontSize, origin, color);
}
