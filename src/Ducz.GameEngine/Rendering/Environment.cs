namespace Ducz.Rendering;

/// <summary>How the background is drawn.</summary>
public enum BackgroundMode
{
    /// <summary>Flat clear color.</summary>
    SolidColor,
    /// <summary>Procedural gradient sky (top / horizon / ground) with an optional sun disk.</summary>
    ProceduralSky
}

/// <summary>
/// World environment settings: background, ambient light and fog.
/// Access via <c>Engine.Renderer.Environment</c>.
/// </summary>
public sealed class Environment
{
    /// <summary>Background rendering mode.</summary>
    public BackgroundMode Background { get; set; } = BackgroundMode.ProceduralSky;

    /// <summary>Clear color used in <see cref="BackgroundMode.SolidColor"/> mode.</summary>
    public Color ClearColor { get; set; } = Color.CornflowerBlue;

    /// <summary>Sky color straight up.</summary>
    public Color SkyTopColor { get; set; } = Color.FromHex("#3d63a8");

    /// <summary>Sky color at the horizon.</summary>
    public Color SkyHorizonColor { get; set; } = Color.FromHex("#b8cfe8");

    /// <summary>Color below the horizon.</summary>
    public Color SkyGroundColor { get; set; } = Color.FromHex("#4a4238");

    /// <summary>Draw a sun disk where the first directional light points from.</summary>
    public bool SkySunEnabled { get; set; } = true;

    /// <summary>Ambient light color (applied to every lit surface).</summary>
    public Color AmbientColor { get; set; } = Color.White;

    /// <summary>Ambient light intensity (0..1 typical).</summary>
    public float AmbientIntensity { get; set; } = 0.25f;

    /// <summary>Enables linear distance fog.</summary>
    public bool FogEnabled { get; set; }

    /// <summary>Fog color (usually close to the horizon color).</summary>
    public Color FogColor { get; set; } = Color.FromHex("#b8cfe8");

    /// <summary>Distance where fog starts.</summary>
    public float FogStart { get; set; } = 30f;

    /// <summary>Distance where fog is fully opaque.</summary>
    public float FogEnd { get; set; } = 150f;
}
