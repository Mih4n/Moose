using System.Buffers.Text;
using System.IO.Compression;
using System.Text;

namespace Moose.Core.Terminal.Images;

/// Turns a transmit command into RGBA: base64 chunks, the medium (direct, file, temp file, shared memory), zlib, the pixel format.
public sealed class ImageTransmission(GraphicsCommand command, long maxBytes)
{
    byte[] data = new byte[Math.Min(4096, maxBytes)];
    int length;
    readonly List<byte> carry = [];

    public GraphicsCommand Command => command;

    /// Decodes a chunk; base64 split at any point, or padded chunk by chunk, is accepted.
    public void Append(ReadOnlySpan<byte> base64, bool final)
    {
        if (carry.Count > 0)
        {
            base64 = (byte[])[.. carry, .. base64];
            carry.Clear();
        }
        while (!base64.IsEmpty)
        {
            // decode up to the end of the first padded block, or every whole block when there's no padding
            var padding = base64.IndexOf((byte)'=');
            var end = padding < 0 ? base64.Length / 4 * 4 : Math.Min(base64.Length, padding / 4 * 4 + 4);
            if (end == 0) break;
            Decode(base64[..end], padding >= 0);
            base64 = base64[end..];
            while (!base64.IsEmpty && base64[0] == '=') base64 = base64[1..];
        }
        if (base64.IsEmpty) return;
        if (!final)
        {
            carry.AddRange(base64);
            return;
        }
        Span<byte> padded = [(byte)'=', (byte)'=', (byte)'=', (byte)'='];
        base64.CopyTo(padded);
        Decode(padded, final: true);
    }

    void Decode(ReadOnlySpan<byte> base64, bool final)
    {
        Reserve(Base64.GetMaxDecodedFromUtf8Length(base64.Length));
        var status = Base64.DecodeFromUtf8(base64, data.AsSpan(length), out var consumed, out var written, final);
        length += written;
        if (status != System.Buffers.OperationStatus.Done || consumed != base64.Length) throw new ImageException("EINVAL", "bad base64 data");
    }

    void Reserve(int extra)
    {
        if ((long)length + extra > maxBytes + 3) throw new ImageException("EFBIG", "image data too large");
        if (length + extra <= data.Length) return;
        Array.Resize(ref data, (int)Math.Min(maxBytes + 3, Math.Max((long)data.Length * 2, length + extra)));
    }

    /// Frames default to the image's size when s and v are left out.
    public (int Width, int Height, byte[] Rgba) Decode(long maxPixels, int defaultWidth = 0, int defaultHeight = 0)
    {
        var bytes = Read();
        if (command.Char('o', '\0') == 'z') bytes = Inflate(bytes, maxPixels);
        var format = command.Format;
        if (format == 100) return PngDecoder.Decode(bytes, maxPixels);
        if (format is not (24 or 32)) throw new ImageException("EINVAL", $"unknown format {format}");
        var width = command.Int('s', defaultWidth);
        var height = command.Int('v', defaultHeight);
        if (width <= 0 || height <= 0) throw new ImageException("EINVAL", "missing image size");
        if ((long)width * height > maxPixels) throw new ImageException("EFBIG", "image too large");
        var channels = format / 8;
        var expected = (long)width * height * channels;
        if (bytes.Length < expected) throw new ImageException("ENODATA", $"insufficient image data: {bytes.Length} < {expected}");
        if (channels == 4) return (width, height, bytes.Length == expected ? bytes : bytes[..(int)expected]);
        var rgba = new byte[(long)width * height * 4];
        for (int source = 0, target = 0; target < rgba.Length; source += 3, target += 4)
        {
            rgba[target] = bytes[source];
            rgba[target + 1] = bytes[source + 1];
            rgba[target + 2] = bytes[source + 2];
            rgba[target + 3] = 255;
        }
        return (width, height, rgba);
    }

    byte[] Read()
    {
        var payload = data.AsSpan(0, length);
        var medium = command.Medium;
        if (medium == 'd')
        {
            if (payload.IsEmpty) throw new ImageException("ENODATA", "no image data");
            return payload.ToArray();
        }
        if (medium is not ('f' or 't' or 's')) throw new ImageException("EINVAL", $"unknown transmission medium {medium}");

        var name = Encoding.UTF8.GetString(payload);
        var path = medium == 's' ? "/dev/shm/" + name.TrimStart('/') : name;
        try
        {
            if (medium == 't' && !IsTemporary(path)) throw new ImageException("EINVAL", "not a temporary file");
            if (!IsReadable(path, medium == 's')) throw new ImageException("EINVAL", "file not allowed");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var offset = Math.Max(0, command.Int('O'));
            var size = command.Int('S');
            var available = Math.Max(0, stream.Length - offset);
            var count = size > 0 ? Math.Min(size, available) : available;
            if (count > maxBytes) throw new ImageException("EFBIG", "image file too large");
            stream.Position = offset;
            var bytes = new byte[count];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ImageException("EBADF", exception.Message);
        }
        finally
        {
            if (medium is 't' or 's') TryDelete(path);
        }
    }

    static bool IsTemporary(string path)
    {
        if (!path.Contains("tty-graphics-protocol")) return false;
        var full = Path.GetFullPath(path);
        string[] roots = [Path.GetTempPath(), "/tmp/", "/dev/shm/", Environment.GetEnvironmentVariable("TMPDIR") ?? "/tmp/"];
        return roots.Any(root => full.StartsWith(Path.TrimEndingDirectorySeparator(root) + "/", StringComparison.Ordinal));
    }

    // only regular files, and nothing from the kernel's pseudo filesystems: reading /dev/zero or /proc/kmsg would hang or leak
    static bool IsReadable(string path, bool sharedMemory)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return false;
        var target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
        if (target.StartsWith("/proc/") || target.StartsWith("/sys/")) return false;
        if (target.StartsWith("/dev/") && !(sharedMemory || target.StartsWith("/dev/shm/"))) return false;
        return (File.GetAttributes(target) & (FileAttributes.Directory | FileAttributes.Device)) == 0;
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    static byte[] Inflate(byte[] compressed, long maxPixels)
    {
        try
        {
            using var input = new MemoryStream(compressed);
            using var inflater = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = inflater.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > maxPixels * 4) throw new ImageException("EFBIG", "decompressed data too large");
            }
            return output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new ImageException("EINVAL", "bad zlib data: " + exception.Message);
        }
    }
}
