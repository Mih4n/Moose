using System.Numerics;
using Moose.Graphics.Elements;
using Moose.Graphics.Primitives;
using Silk.NET.Input;

namespace Moose.Graphics.Tests.Elements;

public class ElementTests
{
    readonly Div fixedChild = new Div().W(100);
    readonly Div clickableChild = new Div().OnClick(() => { });
    readonly Div middleClickableChild = new Div()
        .W(10)
        .OnMiddleClick(() => { });
    readonly Div doubleGrowChild = new Div()
        .Flex(2)
        .H(40);
    readonly Div root;

    public ElementTests()
    {
        root = new Div()
            .Row()
            .Padding(10)
            .Gap(10)
            .Child(fixedChild)
            .Child(clickableChild)
            .Child(middleClickableChild)
            .Child(doubleGrowChild);
        root.Layout(new Rect(0, 0, 450, 100));
    }

    [Fact]
    public void Layout_SplitsFreeSpaceByGrow()
    {
        Assert.Equal(new Rect(10, 10, 100, 80), fixedChild.Bounds);
        Assert.Equal(120, clickableChild.Bounds.X, 3);
        Assert.Equal(290 / 3f, clickableChild.Bounds.W, 3);
        Assert.Equal(580 / 3f, doubleGrowChild.Bounds.W, 3);
        Assert.Equal(40, doubleGrowChild.Bounds.H);
    }

    [Fact]
    public void HitClick_ReturnsClickableElementUnderPoint()
    {
        Assert.Same(clickableChild, root.HitClick(new(150, 50)));
        Assert.Null(root.HitClick(new(50, 50)));
        Assert.Null(root.HitClick(new(5, 5)));
    }

    [Fact]
    public void HitClick_MatchesHandlerForButton()
    {
        var middle = new Vector2(middleClickableChild.Bounds.X + 5, 50);

        Assert.Same(middleClickableChild, root.HitClick(middle, MouseButton.Middle));
        Assert.Null(root.HitClick(middle, MouseButton.Left));
        Assert.Null(root.HitClick(new(150, 50), MouseButton.Middle));
    }
}
