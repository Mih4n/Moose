using System.Buffers.Binary;
using System.IO.Compression;

namespace Moose.Core.Terminal.Images;

/// Decodes every PNG the spec allows (all color types and bit depths, tRNS, Adam7) to straight-alpha RGBA8.
public static class PngDecoder
{
    static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    static readonly (int X, int Y, int Dx, int Dy)[] Adam7 =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    public static (int Width, int Height, byte[] Rgba) Decode(ReadOnlySpan<byte> data, long maxPixels)
    {
        if (!data.StartsWith(Signature)) throw new ImageException("EBADPNG", "not a PNG file");
        int width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        var position = Signature.Length;
        while (position + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data[position..]);
            if (length < 0 || position + 12L + length > data.Length) throw new ImageException("EBADPNG", "truncated chunk");
            var type = data.Slice(position + 4, 4);
            var body = data.Slice(position + 8, length);
            position += 12 + length;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (length < 13) throw new ImageException("EBADPNG", "short IHDR");
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                depth = body[8];
                colorType = body[9];
                interlace = body[12];
            }
            else if (type.SequenceEqual("PLTE"u8)) palette = body.ToArray();
            else if (type.SequenceEqual("tRNS"u8)) transparency = body.ToArray();
            else if (type.SequenceEqual("IDAT"u8)) compressed.Write(body);
            else if (type.SequenceEqual("IEND"u8)) break;
        }

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        var depthValid = colorType switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => depth is 8 or 16,
        };
        if (width <= 0 || height <= 0 || channels == 0 || !depthValid || interlace > 1) throw new ImageException("EBADPNG", "unsupported header");
        if ((long)width * height > maxPixels) throw new ImageException("EFBIG", "image too large");
        if (colorType == 3 && palette == null) throw new ImageException("EBADPNG", "missing palette");

        var bitsPerPixel = channels * depth;
        var passes = interlace == 1 ? Adam7 : [(0, 0, 1, 1)];
        long rawSize = 0;
        foreach (var (x, y, dx, dy) in passes)
        {
            var (passWidth, passHeight) = PassSize(width, height, x, y, dx, dy);
            if (passWidth > 0 && passHeight > 0) rawSize += passHeight * (1 + ((long)passWidth * bitsPerPixel + 7) / 8);
        }

        var raw = new byte[rawSize];
        compressed.Position = 0;
        try
        {
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            inflater.ReadAtLeast(raw, raw.Length, throwOnEndOfStream: false); // a short stream leaves the rest black, like browsers
        }
        catch (InvalidDataException exception)
        {
            throw new ImageException("EBADPNG", exception.Message);
        }

        var format = new PixelFormat(colorType, depth, channels, palette, transparency);
        var rgba = new byte[(long)width * height * 4];
        var offset = 0;
        foreach (var (x, y, dx, dy) in passes)
        {
            var (passWidth, passHeight) = PassSize(width, height, x, y, dx, dy);
            if (passWidth == 0 || passHeight == 0) continue;
            offset = DecodePass(raw, offset, format, rgba, width, passWidth, passHeight, x, y, dx, dy);
        }
        return (width, height, rgba);
    }

    static (int Width, int Height) PassSize(int width, int height, int x, int y, int dx, int dy) =>
        (Math.Max(0, (width - x + dx - 1) / dx), Math.Max(0, (height - y + dy - 1) / dy));

    static int DecodePass(byte[] raw, int offset, PixelFormat format, byte[] rgba, int imageWidth, int width, int height, int x0, int y0, int dx, int dy)
    {
        var bitsPerPixel = format.Channels * format.Depth;
        var stride = (width * bitsPerPixel + 7) / 8;
        var unit = Math.Max(1, bitsPerPixel / 8);
        var previous = new byte[stride];
        var current = new byte[stride];
        for (var row = 0; row < height; row++)
        {
            var filter = raw[offset];
            raw.AsSpan(offset + 1, stride).CopyTo(current);
            offset += stride + 1;
            Unfilter(filter, current, previous, unit);
            var target = ((y0 + row * dy) * imageWidth + x0) * 4;
            for (var column = 0; column < width; column++, target += dx * 4) format.Write(current, column, rgba.AsSpan(target, 4));
            (previous, current) = (current, previous);
        }
        return offset;
    }

    static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> above, int unit)
    {
        switch (filter)
        {
            case 1:
                for (var i = unit; i < row.Length; i++) row[i] += row[i - unit];
                break;
            case 2:
                for (var i = 0; i < row.Length; i++) row[i] += above[i];
                break;
            case 3:
                for (var i = 0; i < row.Length; i++) row[i] += (byte)(((i >= unit ? row[i - unit] : 0) + above[i]) / 2);
                break;
            case 4:
                for (var i = 0; i < row.Length; i++)
                    row[i] += Paeth(i >= unit ? row[i - unit] : (byte)0, above[i], i >= unit ? above[i - unit] : (byte)0);
                break;
        }
    }

    static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }

    sealed class PixelFormat(int colorType, int depth, int channels, byte[]? palette, byte[]? transparency)
    {
        public int Depth => depth;
        public int Channels => channels;

        public void Write(ReadOnlySpan<byte> row, int column, Span<byte> pixel)
        {
            var first = column * channels;
            switch (colorType)
            {
                case 0:
                    var gray = Sample(row, first);
                    pixel[0] = pixel[1] = pixel[2] = To8(gray);
                    pixel[3] = transparency is { Length: >= 2 } && gray == BinaryPrimitives.ReadUInt16BigEndian(transparency) ? (byte)0 : (byte)255;
                    break;
                case 2:
                    int red = Sample(row, first), green = Sample(row, first + 1), blue = Sample(row, first + 2);
                    (pixel[0], pixel[1], pixel[2]) = (To8(red), To8(green), To8(blue));
                    var keyed = transparency is { Length: >= 6 }
                        && red == BinaryPrimitives.ReadUInt16BigEndian(transparency)
                        && green == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2))
                        && blue == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4));
                    pixel[3] = keyed ? (byte)0 : (byte)255;
                    break;
                case 3:
                    var index = Sample(row, first);
                    if (index * 3 + 2 < palette!.Length) (pixel[0], pixel[1], pixel[2]) = (palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2]);
                    pixel[3] = transparency != null && index < transparency.Length ? transparency[index] : (byte)255;
                    break;
                case 4:
                    pixel[0] = pixel[1] = pixel[2] = To8(Sample(row, first));
                    pixel[3] = To8(Sample(row, first + 1));
                    break;
                case 6:
                    for (var channel = 0; channel < 4; channel++) pixel[channel] = To8(Sample(row, first + channel));
                    break;
            }
        }

        int Sample(ReadOnlySpan<byte> row, int index) => depth switch
        {
            8 => row[index],
            16 => (row[index * 2] << 8) | row[index * 2 + 1],
            _ => (row[index * depth / 8] >> (8 - depth - index * depth % 8)) & ((1 << depth) - 1),
        };

        byte To8(int sample) => depth switch
        {
            8 => (byte)sample,
            16 => (byte)(sample >> 8),
            _ => (byte)(sample * 255 / ((1 << depth) - 1)),
        };
    }
}
