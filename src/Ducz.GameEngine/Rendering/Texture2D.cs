using Silk.NET.OpenGL;
using StbImageSharp;

namespace Ducz.Rendering;

/// <summary>Texture filtering modes.</summary>
public enum TextureFilter
{
    /// <summary>Smooth (bilinear/trilinear). Best for most 3D textures.</summary>
    Linear,
    /// <summary>Crisp pixels. Best for pixel art.</summary>
    Nearest
}

/// <summary>
/// A 2D GPU texture. Create from an image file, raw pixels or procedurally.
/// Textures must be created after the engine window is open (e.g. inside OnReady).
/// </summary>
public sealed class Texture2D : IDisposable
{
    private readonly GL _gl;

    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    private unsafe Texture2D(GraphicsDevice device, int width, int height, byte[] rgbaPixels,
        TextureFilter filter, bool repeat, bool generateMipmaps)
    {
        _gl = device.GL;
        Width = width;
        Height = height;

        Handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, Handle);

        fixed (byte* ptr = rgbaPixels)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }

        int wrap = repeat ? (int)GLEnum.Repeat : (int)GLEnum.ClampToEdge;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);

        if (generateMipmaps)
        {
            _gl.GenerateMipmap(TextureTarget.Texture2D);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                filter == TextureFilter.Linear ? (int)GLEnum.LinearMipmapLinear : (int)GLEnum.NearestMipmapNearest);
        }
        else
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                filter == TextureFilter.Linear ? (int)GLEnum.Linear : (int)GLEnum.Nearest);
        }

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
            filter == TextureFilter.Linear ? (int)GLEnum.Linear : (int)GLEnum.Nearest);

        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    // ---- Factory methods ----

    /// <summary>Loads a texture from an image file (PNG, JPG, BMP, TGA, GIF).</summary>
    public static Texture2D FromFile(string path, TextureFilter filter = TextureFilter.Linear,
        bool repeat = true, bool generateMipmaps = true)
    {
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return FromPixels(Device(), image.Width, image.Height, image.Data, filter, repeat, generateMipmaps);
    }

    /// <summary>Creates a texture from an in-memory encoded image (PNG/JPG bytes).</summary>
    public static Texture2D FromEncodedBytes(byte[] encodedImage, TextureFilter filter = TextureFilter.Linear,
        bool repeat = true, bool generateMipmaps = true)
    {
        var image = ImageResult.FromMemory(encodedImage, ColorComponents.RedGreenBlueAlpha);
        return FromPixels(Device(), image.Width, image.Height, image.Data, filter, repeat, generateMipmaps);
    }

    /// <summary>Creates a texture from raw RGBA8 pixels (4 bytes per pixel, row-major, top-left first).</summary>
    public static Texture2D FromPixels(int width, int height, byte[] rgbaPixels,
        TextureFilter filter = TextureFilter.Linear, bool repeat = true, bool generateMipmaps = true) =>
        FromPixels(Device(), width, height, rgbaPixels, filter, repeat, generateMipmaps);

    internal static Texture2D FromPixels(GraphicsDevice device, int width, int height, byte[] rgbaPixels,
        TextureFilter filter = TextureFilter.Linear, bool repeat = true, bool generateMipmaps = true) =>
        new(device, width, height, rgbaPixels, filter, repeat, generateMipmaps);

    /// <summary>Creates a 1x1 texture of a solid color.</summary>
    public static Texture2D FromColor(Color color)
    {
        var px = new byte[]
        {
            (byte)(Mathf.Clamp01(color.R) * 255),
            (byte)(Mathf.Clamp01(color.G) * 255),
            (byte)(Mathf.Clamp01(color.B) * 255),
            (byte)(Mathf.Clamp01(color.A) * 255)
        };
        return FromPixels(Device(), 1, 1, px, TextureFilter.Nearest, true, false);
    }

    /// <summary>Creates a procedural checkerboard - great for prototyping floors.</summary>
    public static Texture2D CreateCheckerboard(int size = 256, int cells = 8, Color? colorA = null, Color? colorB = null) =>
        FromPixels(Device(), size, size, CheckerboardPixels(size, cells, colorA, colorB));

    /// <summary>Raw RGBA8 pixels of a checkerboard (what <see cref="CreateCheckerboard"/> uploads). Handy for exporters.</summary>
    public static byte[] CheckerboardPixels(int size = 256, int cells = 8, Color? colorA = null, Color? colorB = null)
    {
        var a = colorA ?? Color.White;
        var b = colorB ?? Color.LightGray;
        var pixels = new byte[size * size * 4];
        int cellSize = Math.Max(1, size / cells);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool even = (x / cellSize + y / cellSize) % 2 == 0;
                var c = even ? a : b;
                int i = (y * size + x) * 4;
                pixels[i] = (byte)(c.R * 255);
                pixels[i + 1] = (byte)(c.G * 255);
                pixels[i + 2] = (byte)(c.B * 255);
                pixels[i + 3] = (byte)(c.A * 255);
            }
        }
        return pixels;
    }

    private static GraphicsDevice Device() => Engine.Renderer.Device;

    /// <summary>Binds this texture to a texture unit (0-based).</summary>
    public void Bind(int unit = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}
