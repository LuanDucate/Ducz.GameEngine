using System.Numerics;
using Ducz.Rendering;
using StbTrueTypeSharp;

namespace Ducz.UI;

/// <summary>
/// A bitmap font baked from a TTF file at a fixed pixel size.
/// Covers ASCII and Latin-1 accented characters.
/// </summary>
public sealed class Font
{
    private struct Glyph
    {
        public Vector2 UvMin;
        public Vector2 UvMax;
        public Vector2 Offset;   // from pen position (baseline) to quad top-left
        public Vector2 Size;     // quad size in pixels
        public float Advance;
    }

    private const int AtlasSize = 1024;
    private const int RangeAStart = 32, RangeACount = 95;    // ASCII
    private const int RangeBStart = 160, RangeBCount = 96;   // Latin-1 supplement

    private readonly Glyph[] _glyphs = new Glyph[RangeACount + RangeBCount];

    /// <summary>The GPU texture atlas holding the glyphs.</summary>
    public Texture2D Atlas { get; }

    /// <summary>Pixel size the font was baked at.</summary>
    public int Size { get; }

    /// <summary>Distance from the top of a line to the baseline.</summary>
    public float Ascent { get; }

    /// <summary>Vertical distance between two lines of text.</summary>
    public float LineHeight { get; }

    private unsafe Font(byte[] ttfData, int size)
    {
        Size = size;

        var pixels = new byte[AtlasSize * AtlasSize];
        var packedA = new StbTrueType.stbtt_packedchar[RangeACount];
        var packedB = new StbTrueType.stbtt_packedchar[RangeBCount];

        fixed (byte* ttfPtr = ttfData)
        fixed (byte* pixelPtr = pixels)
        fixed (StbTrueType.stbtt_packedchar* packedAPtr = packedA)
        fixed (StbTrueType.stbtt_packedchar* packedBPtr = packedB)
        {
            var context = new StbTrueType.stbtt_pack_context();
            StbTrueType.stbtt_PackBegin(context, pixelPtr, AtlasSize, AtlasSize, 0, 1, null);
            StbTrueType.stbtt_PackSetOversampling(context, 2, 2);
            StbTrueType.stbtt_PackFontRange(context, ttfPtr, 0, size, RangeAStart, RangeACount, packedAPtr);
            StbTrueType.stbtt_PackFontRange(context, ttfPtr, 0, size, RangeBStart, RangeBCount, packedBPtr);
            StbTrueType.stbtt_PackEnd(context);

            // Vertical metrics
            var info = new StbTrueType.stbtt_fontinfo();
            StbTrueType.stbtt_InitFont(info, ttfPtr, 0);
            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, size);
            Ascent = ascent * scale;
            LineHeight = (ascent - descent + lineGap) * scale;
        }

        void Store(StbTrueType.stbtt_packedchar[] packed, int offset)
        {
            // The atlas is packed with 2x oversampling, so rects are twice the
            // on-screen glyph size (xoff/yoff/xadvance are already in screen units).
            const float oversample = 2f;
            for (int i = 0; i < packed.Length; i++)
            {
                var p = packed[i];
                _glyphs[offset + i] = new Glyph
                {
                    UvMin = new Vector2(p.x0 / (float)AtlasSize, p.y0 / (float)AtlasSize),
                    UvMax = new Vector2(p.x1 / (float)AtlasSize, p.y1 / (float)AtlasSize),
                    Offset = new Vector2(p.xoff, p.yoff),
                    Size = new Vector2((p.x1 - p.x0) / oversample, (p.y1 - p.y0) / oversample),
                    Advance = p.xadvance
                };
            }
        }
        Store(packedA, 0);
        Store(packedB, RangeACount);

        // Expand single-channel coverage into white RGBA.
        var rgba = new byte[AtlasSize * AtlasSize * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            rgba[i * 4] = 255;
            rgba[i * 4 + 1] = 255;
            rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = pixels[i];
        }
        Atlas = Texture2D.FromPixels(AtlasSize, AtlasSize, rgba, TextureFilter.Linear, repeat: false, generateMipmaps: false);
    }

    /// <summary>Bakes a font from a .ttf file at the given pixel size.</summary>
    public static Font FromFile(string path, int size = 24) => new(File.ReadAllBytes(path), size);

    /// <summary>Bakes a font from TTF bytes.</summary>
    public static Font FromBytes(byte[] ttfData, int size = 24) => new(ttfData, size);

    private int GlyphIndex(char c)
    {
        if (c >= RangeAStart && c < RangeAStart + RangeACount)
            return c - RangeAStart;
        if (c >= RangeBStart && c < RangeBStart + RangeBCount)
            return RangeACount + (c - RangeBStart);
        return '?' - RangeAStart;
    }

    /// <summary>Measures the pixel size of a text block (handles \n).</summary>
    public Vector2 MeasureText(string text, float scale = 1f)
    {
        float maxWidth = 0f, width = 0f;
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                maxWidth = MathF.Max(maxWidth, width);
                width = 0f;
                lines++;
                continue;
            }
            width += _glyphs[GlyphIndex(c)].Advance * scale;
        }
        maxWidth = MathF.Max(maxWidth, width);
        return new Vector2(maxWidth, lines * LineHeight * scale);
    }

    /// <summary>Draws text with its top-left corner at <paramref name="position"/>. Returns the drawn width.</summary>
    internal float Draw(SpriteBatch batch, string text, Vector2 position, Color color, float scale = 1f)
    {
        float x = position.X;
        float y = position.Y + Ascent * scale;
        float maxWidth = 0f;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                maxWidth = MathF.Max(maxWidth, x - position.X);
                x = position.X;
                y += LineHeight * scale;
                continue;
            }

            var glyph = _glyphs[GlyphIndex(c)];
            if (glyph.Size.X > 0f && glyph.Size.Y > 0f)
            {
                var quadPos = new Vector2(x + glyph.Offset.X * scale, y + glyph.Offset.Y * scale);
                batch.DrawTexture(Atlas, quadPos, glyph.Size * scale, glyph.UvMin, glyph.UvMax, color);
            }
            x += glyph.Advance * scale;
        }
        return MathF.Max(maxWidth, x - position.X);
    }
}
