namespace Moose.Graphics.Fonts.HarfBuzz;

internal readonly record struct ShapedGlyph(uint Glyph, int Cluster, float Advance, float OffsetX, float OffsetY);
