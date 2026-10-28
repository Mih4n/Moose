namespace Moose.Graphics.Hosting;

public interface IFrameEffect
{
    void Invoke(Frame frame, Action next);
}
