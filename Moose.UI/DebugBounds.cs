using Moose.Core;
using Moose.Core.Settings;
using Moose.Graphics.Hosting;
using Moose.Graphics.Primitives;

namespace Moose.UI;

sealed class DebugBounds : IFrameEffect
{
    public void Invoke(Frame frame, Action next)
    {
        next();
        if (!Configuration.Bool(Keys.Debug.Bounds)) return;
        var outline = Configuration.Color(Keys.Ui.Colors.DebugBounds);
        var count = frame.Scene.Count;
        for (var i = 0; i < count; i++)
            frame.Scene.Add(frame.Scene[i] with { Color = Color.Transparent, BorderColor = outline, BorderWidth = 1, Uv = default });
    }
}
