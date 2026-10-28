using FreeTypeSharp;
using static FreeTypeSharp.FT;

namespace Moose.Graphics.Fonts.FreeType;

internal sealed unsafe class FreeTypeRasterizer : IFontRasterizer, IDisposable
{
    readonly FT_LibraryRec_* library;

    public FreeTypeRasterizer()
    {
        FT_LibraryRec_* handle;
        Check(FT_Init_FreeType(&handle), "FT_Init_FreeType");
        library = handle;
    }

    public IFontFace Load(string path, float size) => new FreeTypeFace(library, path, size);

    public void Dispose() => FT_Done_FreeType(library);

    internal static void Check(FT_Error error, string call)
    {
        if (error != FT_Error.FT_Err_Ok) throw new InvalidOperationException($"{call} failed: {error}");
    }
}
