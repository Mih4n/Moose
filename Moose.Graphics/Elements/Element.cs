using System.Numerics;
using Moose.Graphics.Fonts;
using Moose.Graphics.Primitives;
using Moose.Graphics.Rendering;
using Silk.NET.Input;

namespace Moose.Graphics.Elements;

public abstract class Element
{
    internal readonly List<Element> children = [];
    internal bool row;
    internal float padding;
    internal float gap;
    internal float grow = 1;
    internal float radius;
    internal float borderWidth;
    internal float? width;
    internal float? height;
    internal Color bg = Color.Transparent;
    internal Color borderColor = Color.Transparent;
    internal Action? onClick;
    internal Action? onMiddleClick;
    internal Action? hover;

    public Rect Bounds { get; private set; }

    internal virtual void Measure(TextSystem textSystem)
    {
        foreach (var child in children) child.Measure(textSystem);
    }

    internal void Layout(Rect rect)
    {
        Bounds = rect;
        var x = rect.X + padding;
        var y = rect.Y + padding;
        var width = rect.W - 2 * padding;
        var height = rect.H - 2 * padding;
        var free = (row ? width : height) - gap * Math.Max(0, children.Count - 1);
        var grows = 0f;
        foreach (var child in children)
            if ((row ? child.width : child.height) is { } fixedSize) free -= fixedSize; else grows += child.grow;

        var position = row ? x : y;
        foreach (var child in children)
        {
            var main = (row ? child.width : child.height) ?? (grows > 0 ? Math.Max(0, free) * child.grow / grows : 0);
            var cross = (row ? child.height : child.width) ?? (row ? height : width);
            child.Layout(row ? new Rect(position, y, main, cross) : new Rect(x, position, cross, main));
            position += main + gap;
        }
    }

    internal virtual void Paint(List<Quad> scene, Vector2 mouse)
    {
        if (hover != null && Bounds.Contains(mouse)) hover();
        if (bg.A > 0 || borderWidth > 0) scene.Add(new Quad(Bounds, bg, borderColor, radius, borderWidth));
        foreach (var child in children) child.Paint(scene, mouse);
    }

    internal Action? ClickHandler(MouseButton button) => button == MouseButton.Middle ? onMiddleClick : onClick;

    internal Element? HitClick(Vector2 point, MouseButton button = MouseButton.Left)
    {
        if (!Bounds.Contains(point)) return null;
        for (var i = children.Count - 1; i >= 0; i--)
            if (children[i].HitClick(point, button) is { } hit) return hit;
        return ClickHandler(button) != null ? this : null;
    }
}

public abstract class Element<TSelf> : Element where TSelf : Element<TSelf>
{
    TSelf Self => (TSelf)this;

    public TSelf Row() { row = true; return Self; }
    public TSelf Column() { row = false; return Self; }
    public TSelf Bg(Color color) { bg = color; return Self; }
    public TSelf Rounded(float radius) { this.radius = radius; return Self; }
    public TSelf Border(float width, Color color) { borderWidth = width; borderColor = color; return Self; }
    public TSelf Padding(float padding) { this.padding = padding; return Self; }
    public TSelf Gap(float gap) { this.gap = gap; return Self; }
    public TSelf W(float width) { this.width = width; return Self; }
    public TSelf H(float height) { this.height = height; return Self; }
    public TSelf Size(float width, float height) { this.width = width; this.height = height; return Self; }
    public TSelf Flex(float grow = 1) { this.grow = grow; return Self; }
    public TSelf Child(Element child) { children.Add(child); return Self; }
    public TSelf Children(params IEnumerable<Element> items) { children.AddRange(items); return Self; }
    public TSelf OnClick(Action handler) { onClick = handler; return Self; }
    public TSelf OnMiddleClick(Action handler) { onMiddleClick = handler; return Self; }
    public TSelf Hover(Action<TSelf> style) { hover = () => style(Self); return Self; }
}
