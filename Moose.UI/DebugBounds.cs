using Microsoft.Extensions.Configuration;
using Moose.Graphics.Hosting;
using Moose.Graphics.Primitives;

namespace Moose.UI;

sealed class DebugBounds(IConfiguration config) : IFrameEffect
{
    public void Invoke(Frame frame, Action next)
    {
        next();
        if (config["debug:bounds"] != "true") return;
        var outline = UiSettings.Load(config).Theme.DebugBounds;
        var count = frame.Scene.Count;
        for (var i = 0; i < count; i++)
            frame.Scene.Add(frame.Scene[i] with { Color = Color.Transparent, BorderColor = outline, BorderWidth = 1, Uv = default });
    }
}
