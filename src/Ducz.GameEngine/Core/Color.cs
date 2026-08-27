using System.Numerics;

namespace Ducz;

/// <summary>
/// RGBA color with float components in the 0..1 range.
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    public readonly float R;
    public readonly float G;
    public readonly float B;
    public readonly float A;

    public Color(float r, float g, float b, float a = 1f)
    {
        R = r; G = g; B = b; A = a;
    }

    /// <summary>Creates a color from 0..255 byte components.</summary>
    public static Color FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>Creates a color from a hex string like "#RRGGBB" or "#RRGGBBAA".</summary>
    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[..2], 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        byte a = hex.Length >= 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
        return FromBytes(r, g, b, a);
    }

    /// <summary>Creates a color from hue (0..1), saturation (0..1) and value (0..1).</summary>
    public static Color FromHsv(float h, float s, float v, float a = 1f)
    {
        h = (h % 1f + 1f) % 1f;
        float c = v * s;
        float x = c * (1f - MathF.Abs(h * 6f % 2f - 1f));
        float m = v - c;
        (float r, float g, float b) = (h * 6f) switch
        {
            < 1f => (c, x, 0f),
            < 2f => (x, c, 0f),
            < 3f => (0f, c, x),
            < 4f => (0f, x, c),
            < 5f => (x, 0f, c),
            _ => (c, 0f, x)
        };
        return new Color(r + m, g + m, b + m, a);
    }

    public Color WithAlpha(float alpha) => new(R, G, B, alpha);

    public static Color Lerp(Color a, Color b, float t) => new(
        Mathf.Lerp(a.R, b.R, t),
        Mathf.Lerp(a.G, b.G, t),
        Mathf.Lerp(a.B, b.B, t),
        Mathf.Lerp(a.A, b.A, t));

    public Color Darkened(float amount) => new(R * (1 - amount), G * (1 - amount), B * (1 - amount), A);
    public Color Lightened(float amount) => Lerp(this, White.WithAlpha(A), amount);

    public Vector3 ToVector3() => new(R, G, B);
    public Vector4 ToVector4() => new(R, G, B, A);

    public static Color operator *(Color c, float f) => new(c.R * f, c.G * f, c.B * f, c.A);
    public static Color operator *(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is Color c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public static bool operator ==(Color a, Color b) => a.Equals(b);
    public static bool operator !=(Color a, Color b) => !a.Equals(b);
    public override string ToString() => $"Color({R:0.###}, {G:0.###}, {B:0.###}, {A:0.###})";

    // Common colors
    public static readonly Color White = new(1, 1, 1);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Transparent = new(0, 0, 0, 0);
    public static readonly Color Red = new(1, 0, 0);
    public static readonly Color Green = new(0, 1, 0);
    public static readonly Color Blue = new(0, 0, 1);
    public static readonly Color Yellow = new(1, 1, 0);
    public static readonly Color Cyan = new(0, 1, 1);
    public static readonly Color Magenta = new(1, 0, 1);
    public static readonly Color Orange = new(1, 0.55f, 0);
    public static readonly Color Purple = new(0.55f, 0, 0.85f);
    public static readonly Color Gray = new(0.5f, 0.5f, 0.5f);
    public static readonly Color DarkGray = new(0.25f, 0.25f, 0.25f);
    public static readonly Color LightGray = new(0.75f, 0.75f, 0.75f);
    public static readonly Color CornflowerBlue = FromBytes(100, 149, 237);
    public static readonly Color SkyBlue = FromBytes(135, 206, 235);
}
