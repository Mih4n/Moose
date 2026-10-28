using System.Text;
using Moose.Core.Terminal.Input;

namespace Moose.Core.Tests.Terminal.Input;

public class MouseEncoderTests
{
    [Fact]
    public void Sgr_EncodesPressReleaseMotionAndWheel()
    {
        Assert.Equal("\e[<0;3;2M", Encode(MouseButton.Left, MouseAction.Press, sgr: true));
        Assert.Equal("\e[<0;3;2m", Encode(MouseButton.Left, MouseAction.Release, sgr: true));
        Assert.Equal("\e[<34;3;2M", Encode(MouseButton.Right, MouseAction.Motion, sgr: true));
        Assert.Equal("\e[<64;3;2M", Encode(MouseButton.WheelUp, MouseAction.Press, sgr: true));
        Assert.Equal("\e[<20;3;2M", Ascii(MouseEncoder.Encode(MouseButton.Left, MouseAction.Press, 2, 1, KeyModifiers.Shift | KeyModifiers.Control, sgr: true)));
    }

    [Fact]
    public void X10_EncodesOffsetBytesAndGenericRelease()
    {
        Assert.Equal([0x1b, (byte)'[', (byte)'M', 32, 35, 34], MouseEncoder.Encode(MouseButton.Left, MouseAction.Press, 2, 1, KeyModifiers.None, sgr: false));
        Assert.Equal([0x1b, (byte)'[', (byte)'M', 35, 35, 34], MouseEncoder.Encode(MouseButton.Left, MouseAction.Release, 2, 1, KeyModifiers.None, sgr: false));
        Assert.Null(MouseEncoder.Encode(MouseButton.Left, MouseAction.Press, 300, 1, KeyModifiers.None, sgr: false));
    }

    static string Encode(MouseButton button, MouseAction action, bool sgr) => Ascii(MouseEncoder.Encode(button, action, 2, 1, KeyModifiers.None, sgr));

    static string Ascii(byte[]? bytes) => Encoding.ASCII.GetString(bytes!);
}
