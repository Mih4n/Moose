using System.Text;
using Moose.Core.Terminal.Grid;
using Moose.Core.Terminal.Images;

namespace Moose.Core.Tests.Terminal.Images;

public class KittyGraphicsTests
{
    readonly List<string> replies = [];
    readonly TerminalScreen screen;

    public KittyGraphicsTests()
    {
        // 10 x 20 px cells
        screen = new TerminalScreen(10, 4, scrollbackLines: 100);
        screen.SetPixelSize(100, 80);
        screen.Reply += replies.Add;
    }

    static string Rgba(int width, int height, byte value = 200) => Convert.ToBase64String(Enumerable.Repeat(value, width * height * 4).ToArray());

    void Graphics(string control, string payload = "") => screen.Feed($"\e_G{control};{payload}\e\\");

    List<(ImagePlacement Placement, int Row, int Column)> Placements() => [.. screen.PlacementsFrom(screen.Scrollback.TotalPushed, screen.Rows)];

    [Fact]
    public void Transmit_StoresImageAndReplies()
    {
        Graphics("a=t,f=32,s=2,v=3,i=7", Rgba(2, 3));

        Assert.Equal(["\e_Gi=7;OK\e\\"], replies);
        var image = screen.ImageById(7)!;
        Assert.Equal((2, 3), (image.Width, image.Height));
        Assert.Empty(Placements());
    }

    [Fact]
    public void Transmit_ConvertsRgbAndHonorsQuiet()
    {
        Graphics("a=t,f=24,s=1,v=1,i=1,q=1", Convert.ToBase64String([1, 2, 3]));
        Graphics("a=t,f=24,s=9,v=9,i=2,q=2", Convert.ToBase64String([1, 2, 3]));

        Assert.Empty(replies);
        Assert.Equal<byte>([1, 2, 3, 255], screen.ImageById(1)!.Frames[0].Pixels);
        Assert.Null(screen.ImageById(2));
    }

    [Fact]
    public void Transmit_ReportsErrors()
    {
        Graphics("a=t,f=32,s=4,v=4,i=3", Rgba(1, 1));
        Graphics("a=p,i=99");

        Assert.StartsWith("\e_Gi=3;ENODATA:", replies[0]);
        Assert.StartsWith("\e_Gi=99;ENOENT:", replies[1]);
    }

    [Fact]
    public void Transmit_JoinsChunksSplitAnywhere()
    {
        var data = Rgba(3, 3, 9);
        Graphics("a=t,f=32,s=3,v=3,i=4,m=1", data[..5]);
        Graphics("m=1", data[5..17]);
        Graphics("m=0", data[17..]);

        Assert.Equal(["\e_Gi=4;OK\e\\"], replies);
        Assert.All(screen.ImageById(4)!.Frames[0].Pixels, value => Assert.Equal(9, value));
    }

    [Fact]
    public void Transmit_AcceptsChunksPaddedOneByOne()
    {
        var pixels = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        Graphics("a=t,f=32,s=2,v=2,i=5,m=1", Convert.ToBase64String(pixels[..7]));
        Graphics("m=0", Convert.ToBase64String(pixels[7..]));

        Assert.Equal(pixels, screen.ImageById(5)!.Frames[0].Pixels);
    }

    [Fact]
    public void Transmit_DecompressesZlibAndPng()
    {
        var png = PngDecoderTests.Png(1, 1, 8, 2, [0, 5, 6, 7]);
        Graphics("a=t,f=100,i=6", Convert.ToBase64String(png));
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest)) zlib.Write(new byte[] { 1, 2, 3, 4 });
        Graphics("a=t,f=32,s=1,v=1,o=z,i=8", Convert.ToBase64String(compressed.ToArray()));

        Assert.Equal<byte>([5, 6, 7, 255], screen.ImageById(6)!.Frames[0].Pixels);
        Assert.Equal<byte>([1, 2, 3, 4], screen.ImageById(8)!.Frames[0].Pixels);
    }

    [Fact]
    public void Transmit_ReadsFilesAndDeletesTemporaryOnes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tty-graphics-protocol-{Guid.NewGuid()}");
        File.WriteAllBytes(path, [0, 0, 1, 2, 3, 4]);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(path));

        Graphics("a=t,t=f,f=32,s=1,v=1,O=2,S=4,i=9", encoded);
        Assert.True(File.Exists(path));
        Graphics("a=t,t=t,f=32,s=1,v=1,O=2,i=10", encoded);

        Assert.Equal<byte>([1, 2, 3, 4], screen.ImageById(9)!.Frames[0].Pixels);
        Assert.NotNull(screen.ImageById(10));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Transmit_RefusesDeviceFiles()
    {
        Graphics("a=t,t=f,f=32,s=1,v=1,i=11", Convert.ToBase64String("/dev/zero"u8));

        Assert.StartsWith("\e_Gi=11;EINVAL:", replies[0]);
    }

    [Fact]
    public void Query_RepliesWithoutStoring()
    {
        Graphics("a=q,f=32,s=1,v=1,i=31", Rgba(1, 1));

        Assert.Equal(["\e_Gi=31;OK\e\\"], replies);
        Assert.False(screen.HasImages);
    }

    [Fact]
    public void ImageNumber_GetsAnIdAssigned()
    {
        Graphics("a=t,f=32,s=1,v=1,I=13", Rgba(1, 1));

        Assert.Matches(@"^\e_Gi=\d+,I=13;OK\e\\$", replies[0]);
        Graphics("a=p,I=13,p=2");
        Assert.Single(Placements());
    }

    [Fact]
    public void TransmitAndPut_PlacesAtCursorAndMovesPastIt()
    {
        screen.Feed("ab");
        Graphics("a=T,f=32,s=25,v=30,i=1", Rgba(25, 30));

        var (placement, row, column) = Assert.Single(Placements());
        Assert.Equal((0, 2), (row, column));
        Assert.Equal((3, 2), placement.CellSpan(screen.CellPixelWidth, screen.CellPixelHeight));
        Assert.Equal((1, 5), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void Put_WithoutCursorMovementAndExplicitSize()
    {
        Graphics("a=t,f=32,s=1,v=1,i=1", Rgba(1, 1));
        Graphics("a=p,i=1,c=4,r=3,C=1");

        var (placement, _, _) = Assert.Single(Placements());
        Assert.Equal((4f, 3f), placement.Size(10, 20));
        Assert.Equal((0, 0), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void Put_KeepsAspectWhenOneSideIsGiven()
    {
        Graphics("a=t,f=32,s=20,v=40,i=1", Rgba(20, 40));
        Graphics("a=p,i=1,c=4,C=1");

        Assert.Equal((4f, 4f), Assert.Single(Placements()).Placement.Size(10, 20));
    }

    [Fact]
    public void Put_ReplacesPlacementWithSameId()
    {
        Graphics("a=t,f=32,s=1,v=1,i=1", Rgba(1, 1));
        Graphics("a=p,i=1,p=5,C=1");
        screen.Feed("\e[3;4H");
        Graphics("a=p,i=1,p=5,C=1");

        var (_, row, column) = Assert.Single(Placements());
        Assert.Equal((2, 3), (row, column));
    }

    [Fact]
    public void Placement_ScrollsWithItsLine()
    {
        Graphics("a=T,f=32,s=1,v=1,i=1,C=1", Rgba(1, 1));
        screen.Feed("\e[4;1H\n\n");

        Assert.Equal(-2, Assert.Single(Placements()).Row);
        var history = screen.PlacementsFrom(screen.Scrollback.TotalPushed - 2, 1).Single();
        Assert.Equal(0, history.Row);
    }

    [Fact]
    public void RelativePlacement_FollowsParent()
    {
        Graphics("a=t,f=32,s=1,v=1,i=1", Rgba(1, 1));
        Graphics("a=t,f=32,s=1,v=1,i=2", Rgba(1, 1));
        screen.Feed("\e[2;3H");
        Graphics("a=p,i=1,p=1,C=1");
        Graphics("a=p,i=2,p=1,P=1,Q=1,H=2,V=-1");

        var child = Placements().Single(entry => entry.Placement.Image.Id == 2);
        Assert.Equal((0, 4), (child.Row, child.Column));
        Assert.StartsWith("\e_Gi=2,p=1;OK", replies[^1]);

        Graphics("a=p,i=2,p=2,P=1,Q=9");
        Assert.StartsWith("\e_Gi=2,p=2;ENOPARENT", replies[^1]);
    }

    [Fact]
    public void Delete_ByIdKeepsDataUnlessUppercase()
    {
        Graphics("a=T,f=32,s=1,v=1,i=1,C=1", Rgba(1, 1));
        Graphics("a=d,d=i,i=1");
        Assert.Empty(Placements());
        Assert.NotNull(screen.ImageById(1));

        Graphics("a=p,i=1,C=1");
        Graphics("a=d,d=I,i=1");
        Assert.Null(screen.ImageById(1));
    }

    [Fact]
    public void Delete_ByPositionAndZ()
    {
        Graphics("a=t,f=32,s=1,v=1,i=1", Rgba(1, 1));
        Graphics("a=p,i=1,p=1,c=2,r=2,C=1");
        screen.Feed("\e[3;5H");
        Graphics("a=p,i=1,p=2,z=-5,C=1");

        Graphics("a=d,d=p,x=2,y=2");
        Assert.Equal(2u, Assert.Single(Placements()).Placement.Id);
        Graphics("a=d,d=z,z=-5");
        Assert.Empty(Placements());
    }

    [Fact]
    public void ClearScreen_DropsPlacementsOnScreen()
    {
        Graphics("a=T,f=32,s=1,v=1,i=1", Rgba(1, 1));
        screen.Feed("\e[2J");

        Assert.Empty(Placements());
        Assert.NotNull(screen.ImageById(1));
    }

    [Fact]
    public void Reset_ForgetsImages()
    {
        Graphics("a=T,f=32,s=1,v=1,i=1", Rgba(1, 1));
        screen.Feed("\ec");

        Assert.False(screen.HasImages);
        Assert.Empty(Placements());
    }

    [Fact]
    public void Placeholders_ResolveRowsColumnsAndInferredCells()
    {
        Graphics("a=t,f=32,s=1,v=1,i=42", Rgba(1, 1));
        Graphics("a=p,U=1,i=42,c=3,r=2");
        var placeholder = char.ConvertFromUtf32(ImagePlaceholder.Codepoint);
        string Mark(int value) => char.ConvertFromUtf32(ImagePlaceholder.DiacriticFor(value));
        // first cell spells out row 1 column 0; the next two infer column 1 and 2
        screen.Feed($"\e[38;5;42m{placeholder}{Mark(1)}{Mark(0)}{placeholder}{placeholder}");

        var cells = new List<PlaceholderCell>();
        screen.ResolvePlaceholders(screen[0], cells);
        Assert.Equal([(0, 1, 0), (1, 1, 1), (2, 1, 2)], cells.Select(cell => (cell.Index, cell.Row, cell.Column)));
        Assert.All(cells, cell => Assert.Equal(42u, cell.Placement.Image.Id));
    }

    [Fact]
    public void Placeholders_UseHighByteDiacritic()
    {
        Graphics("a=t,f=32,s=1,v=1,i=" + ((3u << 24) | 0x010203), Rgba(1, 1));
        Graphics("a=p,U=1,i=" + ((3u << 24) | 0x010203));
        var placeholder = char.ConvertFromUtf32(ImagePlaceholder.Codepoint);
        string Mark(int value) => char.ConvertFromUtf32(ImagePlaceholder.DiacriticFor(value));
        screen.Feed($"\e[38;2;1;2;3m{placeholder}{Mark(0)}{Mark(0)}{Mark(3)}");

        var cells = new List<PlaceholderCell>();
        screen.ResolvePlaceholders(screen[0], cells);
        Assert.Single(cells);
    }

    [Fact]
    public void Animation_AddsFramesAndPlays()
    {
        Graphics("a=t,f=32,s=1,v=1,i=1", Rgba(1, 1, 10));
        Graphics("a=f,f=32,i=1,z=100", Rgba(1, 1, 20));
        Graphics("a=f,f=32,i=1,c=1,X=1,z=50", Convert.ToBase64String([1, 2, 3, 128]));
        Graphics("a=a,i=1,r=1,z=30");
        Graphics("a=a,i=1,s=3,v=1");

        var image = screen.ImageById(1)!;
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal<byte>([1, 2, 3, 128], image.Frames[2].Pixels);
        Assert.Equal(3, replies.Count); // a=t and both a=f; a=a stays silent

        var start = Environment.TickCount64;
        var (frame, next) = image.FrameAt(start + 10);
        Assert.Same(image.Frames[0], frame);
        Assert.InRange(next!.Value, 1, 30);
        Assert.Same(image.Frames[1], image.FrameAt(start + 40).Frame);
        Assert.Same(image.Frames[2], image.FrameAt(start + 140).Frame);
        Assert.Same(image.Frames[0], image.FrameAt(start + 185).Frame);
    }

    [Fact]
    public void Animation_ComposesAndBlends()
    {
        Graphics("a=t,f=32,s=2,v=1,i=1", Convert.ToBase64String([0, 0, 0, 255, 0, 0, 0, 255]));
        Graphics("a=f,f=32,i=1,s=1,v=1,x=1", Convert.ToBase64String([255, 255, 255, 128]));
        Graphics("a=c,i=1,c=2,r=1,X=1,w=1,h=1");

        Assert.Equal<byte>([0, 0, 0, 0], screen.ImageById(1)!.Frames[1].Pixels[..4]);
        Assert.Equal<byte>([128, 128, 128, 255], screen.ImageById(1)!.Frames[0].Pixels[..4]);
    }

    [Fact]
    public void WindowReports_UsePixelSize()
    {
        screen.Feed("\e[14t\e[16t\e[18t");

        Assert.Equal(["\e[4;80;100t", "\e[6;20;10t", "\e[8;4;10t"], replies);
    }
}
