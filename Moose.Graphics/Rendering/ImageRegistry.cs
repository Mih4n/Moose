namespace Moose.Graphics.Rendering;

/// The images painted in the frame being built. Quads have to stay unmanaged for the vertex buffer,
/// so they point at a slot here; the renderer turns each slot into a texture (cached by pixel array).
internal sealed class ImageRegistry
{
    readonly Dictionary<byte[], int> slots = new(ReferenceEqualityComparer.Instance);
    readonly List<ImageSource> sources = [];

    public IReadOnlyList<ImageSource> Sources => sources;

    public int Slot(byte[] rgba, int width, int height)
    {
        if (slots.TryGetValue(rgba, out var slot)) return slot;
        sources.Add(new ImageSource(rgba, width, height));
        return slots[rgba] = sources.Count;
    }

    public void Clear()
    {
        slots.Clear();
        sources.Clear();
    }
}

/// Straight-alpha RGBA8; the array must not change once painted, swap in a new one instead.
internal readonly record struct ImageSource(byte[] Pixels, int Width, int Height);
