using System.Text;
using Moose.Core.Terminal.Parsing;

namespace Moose.Core.Tests.Terminal.Parsing;

public class VtParserTests
{
    readonly RecordingHandler handler = new();
    readonly VtParser parser;

    public VtParserTests()
    {
        parser = new VtParser(handler);
    }

    void Feed(string text) => parser.Feed(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Ground_PrintsTextAndExecutesControls()
    {
        Feed("a\r\n");

        Assert.Equal(["print a", "execute 0d", "execute 0a"], handler.Events);
    }

    [Fact]
    public void Ground_DecodesUtf8IncludingSplitSequences()
    {
        var bytes = Encoding.UTF8.GetBytes("é中🦆");
        parser.Feed(bytes.AsSpan(0, 1));
        parser.Feed(bytes.AsSpan(1));

        Assert.Equal(["print é", "print 中", "print 🦆"], handler.Events);
    }

    [Fact]
    public void Ground_ReplacesInvalidUtf8()
    {
        parser.Feed([0xc3, (byte)'a', 0xff, 0xc0, 0x80]);

        Assert.Equal(["print �", "print a", "print �", "print �", "print �"], handler.Events);
    }

    [Fact]
    public void Csi_CollectsParametersMarkersAndIntermediates()
    {
        Feed("\e[1;22H\e[?1049h\e[2 q\e[m\e[;5H");

        Assert.Equal(["csi [1,22]H", "csi ?[1049]h", "csi [2] q", "csi []m", "csi [0,5]H"], handler.Events);
    }

    [Fact]
    public void Csi_KeepsColonSubparameters()
    {
        Feed("\e[38:2::10:20:30m");

        Assert.Equal(["csi [38,:2,:0,:10,:20,:30]m"], handler.Events);
    }

    [Fact]
    public void Csi_ExecutesEmbeddedControls()
    {
        Feed("\e[1\n;2H");

        Assert.Equal(["execute 0a", "csi [1,2]H"], handler.Events);
    }

    [Fact]
    public void Csi_MalformedSequenceIsIgnored()
    {
        Feed("\e[1?2hX");

        Assert.Equal(["print X"], handler.Events);
    }

    [Fact]
    public void Escape_DispatchesWithIntermediates()
    {
        Feed("\e7\e(0\ec");

        Assert.Equal(["esc 7", "esc (0", "esc c"], handler.Events);
    }

    [Fact]
    public void Osc_TerminatesOnBellAndStringTerminator()
    {
        Feed("\e]0;héllo\a\e]2;world\e\\x");

        Assert.Equal(["osc 0;héllo", "osc 2;world", "esc \\", "print x"], handler.Events);
    }

    [Fact]
    public void Apc_IsDispatchedOnStringTerminator()
    {
        Feed("\e_Ga=T,f=100;QUJD\e\\x\e^privacy\e\\");

        Assert.Equal(["apc Ga=T,f=100;QUJD", "esc \\", "print x", "esc \\"], handler.Events);
    }

    [Fact]
    public void Dcs_IsSwallowedUntilStringTerminator()
    {
        Feed("\eP1$qm\e\\a");

        Assert.Equal(["esc \\", "print a"], handler.Events);
    }

    [Fact]
    public void Cancel_AbortsSequence()
    {
        Feed("\e[12\x18" + "b");

        Assert.Equal(["execute 18", "print b"], handler.Events);
    }
}
