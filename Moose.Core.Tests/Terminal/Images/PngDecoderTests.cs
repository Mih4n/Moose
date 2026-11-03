using System.Buffers.Binary;
using System.IO.Compression;
using Moose.Core.Terminal.Images;

namespace Moose.Core.Tests.Terminal.Images;

public class PngDecoderTests
{
    /// A PNG with the given raw (already filtered) scanlines; the decoder doesn't check CRCs, so they're left zero.
    internal static byte[] Png(int width, int height, int depth, int colorType, byte[] scanlines, byte[]? palette = null, byte[]? transparency = null, bool interlaced = false)
    {
        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        (header[8], header[9], header[12]) = ((byte)depth, (byte)colorType, (byte)(interlaced ? 1 : 0));
        Chunk(stream, "IHDR", header);
        if (palette != null) Chunk(stream, "PLTE", palette);
        if (transparency != null) Chunk(stream, "tRNS", transparency);
        using var compressed = new MemoryStream();
        using (var deflater = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) deflater.Write(scanlines);
        var data = compressed.ToArray();
        // split IDAT to check the chunks are joined
        Chunk(stream, "IDAT", data[..(data.Length / 2)]);
        Chunk(stream, "IDAT", data[(data.Length / 2)..]);
        Chunk(stream, "IEND", []);
        return stream.ToArray();
    }

    static void Chunk(Stream stream, string type, byte[] body)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        stream.Write(length);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        stream.Write(body);
        stream.Write(new byte[4]);
    }

    [Fact]
    public void Decode_ReadsRgbaWithFilters()
    {
        // row 0 unfiltered, row 1 "up" filtered: adds row 0 to each byte
        byte[] scanlines = [0, 10, 20, 30, 255, 1, 2, 3, 4, 2, 1, 1, 1, 0, 0, 0, 0, 0];
        var (width, height, rgba) = PngDecoder.Decode(Png(2, 2, 8, 6, scanlines), 1 << 20);

        Assert.Equal((2, 2), (width, height));
        Assert.Equal<byte>([10, 20, 30, 255, 1, 2, 3, 4, 11, 21, 31, 255, 1, 2, 3, 4], rgba);
    }

    [Fact]
    public void Decode_ExpandsPaletteWithTransparency()
    {
        // 2 bits per index: indices 0,1,2,1
        byte[] scanlines = [0, 0b00_01_10_01];
        var (_, _, rgba) = PngDecoder.Decode(Png(4, 1, 2, 3, scanlines, palette: [255, 0, 0, 0, 255, 0, 0, 0, 255], transparency: [128]), 1 << 20);

        Assert.Equal<byte>([255, 0, 0, 128, 0, 255, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255], rgba);
    }

    [Fact]
    public void Decode_ScalesLowBitGrayscale()
    {
        byte[] scanlines = [0, 0b1010_0000];
        var (_, _, rgba) = PngDecoder.Decode(Png(3, 1, 1, 0, scanlines), 1 << 20);

        Assert.Equal<byte>([255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255], rgba);
    }

    [Fact]
    public void Decode_ReadsSixteenBitRgbWithColorKey()
    {
        byte[] scanlines = [0, 0x12, 0x34, 0x56, 0x78, 0x9a, 0xbc, 0, 1, 0, 2, 0, 3];
        var (_, _, rgba) = PngDecoder.Decode(Png(2, 1, 16, 2, scanlines, transparency: [0, 1, 0, 2, 0, 3]), 1 << 20);

        Assert.Equal<byte>([0x12, 0x56, 0x9a, 255, 0, 0, 0, 0], rgba);
    }

    [Fact]
    public void Decode_DeinterlacesAdam7()
    {
        // 2x2 gray: pass 1 has (0,0), pass 6 has (1,0), pass 7 has row 1
        byte[] scanlines = [0, 1, 0, 2, 0, 3, 4];
        var (_, _, rgba) = PngDecoder.Decode(Png(2, 2, 8, 0, scanlines, interlaced: true), 1 << 20);

        Assert.Equal<byte>([1, 2, 3, 4], [rgba[0], rgba[4], rgba[8], rgba[12]]);
    }

    [Fact]
    public void Decode_RejectsGarbageAndHugeImages()
    {
        Assert.Equal("EBADPNG", Assert.Throws<ImageException>(() => PngDecoder.Decode("not a png"u8, 1 << 20)).Code);
        Assert.Equal("EFBIG", Assert.Throws<ImageException>(() => PngDecoder.Decode(Png(4096, 4096, 8, 6, []), 1 << 20)).Code);
    }
}
