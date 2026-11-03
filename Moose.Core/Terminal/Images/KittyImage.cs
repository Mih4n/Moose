namespace Moose.Core.Terminal.Images;

public sealed class ImageException(string code, string message) : Exception(message)
{
    public string Code => code;
}

/// Pixels are never edited in place: every change swaps in a new array, so the array doubles as the GPU cache key.
public sealed class ImageFrame(byte[] pixels, int gap)
{
    public byte[] Pixels { get; set; } = pixels;
    /// Milliseconds until the next frame; negative frames are gapless (skipped while playing).
    public int Gap { get; set; } = gap;
}

public enum AnimationState
{
    Stopped,
    Loading,
    Running,
}

public sealed class KittyImage(uint id, uint number, int width, int height, byte[] pixels, long sequence)
{
    public const int DefaultGap = 40;

    long frameShownAt = -1;
    int loopsPlayed;

    public uint Id => id;
    public uint Number => number;
    public int Width => width;
    public int Height => height;
    /// Newer images win lookups by number and lose eviction ties.
    public long Sequence => sequence;
    public List<ImageFrame> Frames { get; } = [new(pixels, 0)];
    public Dictionary<uint, ImagePlacement> VirtualPlacements { get; } = [];
    public AnimationState State { get; set; }
    /// 0 = untouched, 1 = forever, n = play n - 1 loops.
    public int LoopCount { get; set; }
    public int CurrentFrame { get; private set; }
    public long Bytes => (long)Frames.Count * width * height * 4;

    public void ShowFrame(int index, long now)
    {
        CurrentFrame = Math.Clamp(index, 0, Frames.Count - 1);
        frameShownAt = now;
    }

    public void Play(AnimationState state, long now)
    {
        if (State == AnimationState.Stopped && state != AnimationState.Stopped)
        {
            frameShownAt = now;
            loopsPlayed = 0;
        }
        State = state;
    }

    /// Advances the animation to now; returns the frame to show and when it changes next (ms from now, null when it won't).
    public (ImageFrame Frame, long? NextChange) FrameAt(long now)
    {
        if (State == AnimationState.Stopped || Frames.Count < 2) return (Frames[CurrentFrame], null);
        if (frameShownAt < 0) frameShownAt = now;
        // a tab left in the background shouldn't make us replay every frame it missed
        if (now - frameShownAt > 60_000) frameShownAt = now;
        for (var guard = 0; guard < Frames.Count * 4; guard++)
        {
            var gap = EffectiveGap(Frames[CurrentFrame]);
            if (gap > 0 && now < frameShownAt + gap) return (Frames[CurrentFrame], frameShownAt + gap - now);
            if (!Advance()) return (Frames[CurrentFrame], null);
            frameShownAt += Math.Max(0, gap);
        }
        frameShownAt = now;
        return (Frames[CurrentFrame], null);
    }

    bool Advance()
    {
        var next = CurrentFrame + 1;
        if (next >= Frames.Count)
        {
            if (State == AnimationState.Loading) return false;
            loopsPlayed++;
            if (LoopCount > 1 && loopsPlayed >= LoopCount - 1)
            {
                State = AnimationState.Stopped;
                return false;
            }
            next = 0;
        }
        CurrentFrame = next;
        return true;
    }

    static int EffectiveGap(ImageFrame frame) => frame.Gap switch
    {
        < 0 => 0,
        0 => DefaultGap,
        _ => frame.Gap,
    };

    public void DropAnimationFrames()
    {
        Frames.RemoveRange(1, Frames.Count - 1);
        CurrentFrame = 0;
        State = AnimationState.Stopped;
    }
}
