using System.Runtime.InteropServices;
using System.Text;
using FreeTypeSharp;
using static FreeTypeSharp.FT;

namespace Moose.Graphics.Fonts.FreeType;

internal sealed unsafe class FreeTypeFace : IFontFace
{
    static readonly GlyphBitmap Empty = new(0, 0, 0, 0, 0, []);

    readonly FT_FaceRec_* face;

    public FontMetrics Metrics { get; }

    public FreeTypeFace(FT_LibraryRec_* library, string path, float size)
    {
        var utf8Path = Marshal.StringToCoTaskMemUTF8(path);
        FT_FaceRec_* handle;
        try { FreeTypeRasterizer.Check(FT_New_Face(library, (byte*)utf8Path, 0, &handle), $"FT_New_Face({path})"); }
        finally { Marshal.FreeCoTaskMem(utf8Path); }
        face = handle;

        FreeTypeRasterizer.Check(FT_Set_Char_Size(face, 0, (nint)(size * 64), 72, 72), "FT_Set_Char_Size");
        var scale = (double)face->size->metrics.y_scale;
        Metrics = new FontMetrics(UnitsToPixels(face->ascender, scale), UnitsToPixels(face->height, scale));
    }

    static float UnitsToPixels(int units, double scale) => (float)Math.Ceiling(units * scale / 65536 / 64);

    public GlyphBitmap Rasterize(Rune rune) =>
        FT_Load_Char(face, (nuint)rune.Value, FT_LOAD.FT_LOAD_RENDER) == FT_Error.FT_Err_Ok ? Rendered() : Empty;

    public GlyphBitmap RasterizeGlyph(uint glyph) =>
        FT_Load_Glyph(face, glyph, FT_LOAD.FT_LOAD_RENDER) == FT_Error.FT_Err_Ok ? Rendered() : Empty;

    GlyphBitmap Rendered()
    {
        var slot = face->glyph;
        var bitmap = slot->bitmap;
        var width = (int)bitmap.width;
        var height = (int)bitmap.rows;
        var coverage = new byte[width * height];
        for (var row = 0; row < height; row++)
            new ReadOnlySpan<byte>(bitmap.buffer + row * bitmap.pitch, width).CopyTo(coverage.AsSpan(row * width));
        return new GlyphBitmap(width, height, slot->bitmap_left, slot->bitmap_top, slot->advance.x / 64f, coverage);
    }

    public void Dispose() => FT_Done_Face(face);
}
