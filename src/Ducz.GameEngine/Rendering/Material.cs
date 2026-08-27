using System.Numerics;

namespace Ducz.Rendering;

/// <summary>
/// Surface appearance: colors, texture and lighting response (Blinn-Phong).
/// Materials can be shared between many mesh instances.
/// </summary>
public sealed class Material
{
    /// <summary>Base color, multiplied with <see cref="AlbedoTexture"/> and vertex colors.</summary>
    public Color Albedo { get; set; } = Color.White;

    /// <summary>Optional albedo texture (defaults to white).</summary>
    public Texture2D? AlbedoTexture { get; set; }

    /// <summary>
    /// Optional tangent-space normal map: adds surface detail (bricks, panels, grooves) without
    /// extra geometry. Works with any texture pack that ships a "_normal"/"_nrm" image.
    /// </summary>
    public Texture2D? NormalMap { get; set; }

    /// <summary>Strength of <see cref="NormalMap"/> (0 = flat, 1 = as authored, >1 exaggerated).</summary>
    public float NormalStrength { get; set; } = 1f;

    /// <summary>
    /// Optional grayscale roughness map (white = matte, black = glossy). It modulates
    /// <see cref="SpecularStrength"/> and <see cref="Shininess"/> per pixel.
    /// </summary>
    public Texture2D? RoughnessMap { get; set; }

    /// <summary>Strength of specular highlights (0 = matte, 1 = shiny plastic).</summary>
    public float SpecularStrength { get; set; } = 0.4f;

    /// <summary>Specular sharpness (Blinn-Phong exponent). Higher = tighter highlight.</summary>
    public float Shininess { get; set; } = 32f;

    /// <summary>Self-illumination color (not affected by lights).</summary>
    public Color Emission { get; set; } = Color.Black;

    /// <summary>Multiplier for <see cref="Emission"/>.</summary>
    public float EmissionEnergy { get; set; } = 1f;

    /// <summary>When true the material ignores all lighting and shadows (flat color).</summary>
    public bool Unshaded { get; set; }

    /// <summary>Render in the transparent pass with alpha blending.</summary>
    public bool Transparent { get; set; }

    /// <summary>Discards pixels whose alpha is below this value (0 disables). Useful for foliage.</summary>
    public float AlphaCutout { get; set; }

    /// <summary>Draw both faces of every triangle.</summary>
    public bool DoubleSided { get; set; }

    /// <summary>Whether this surface is drawn into the shadow map.</summary>
    public bool CastShadows { get; set; } = true;

    /// <summary>Whether this surface samples the shadow map.</summary>
    public bool ReceiveShadows { get; set; } = true;

    /// <summary>Tiling factor for texture coordinates.</summary>
    public Vector2 UvScale { get; set; } = Vector2.One;

    /// <summary>Offset for texture coordinates.</summary>
    public Vector2 UvOffset { get; set; } = Vector2.Zero;

    public Material() { }

    public Material(Color albedo) => Albedo = albedo;

    public Material(Texture2D texture) => AlbedoTexture = texture;

    /// <summary>Shallow copy - useful to derive variations of a shared material.</summary>
    public Material Clone() => (Material)MemberwiseClone();

    // ---- Convenience factories ----

    public static Material FromColor(Color color) => new(color);

    public static Material FromTexture(Texture2D texture) => new(texture);

    public static Material Emissive(Color color, float energy = 1f) => new()
    {
        Albedo = Color.Black,
        Emission = color,
        EmissionEnergy = energy,
        Unshaded = true
    };
}
