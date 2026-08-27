namespace Ducz;

/// <summary>Base class for all light nodes.</summary>
public abstract class Light3D : Node3D
{
    /// <summary>Light color.</summary>
    public Color Color { get; set; } = Color.White;

    /// <summary>Brightness multiplier.</summary>
    public float Energy { get; set; } = 1f;

    protected Light3D(string? name = null) : base(name) { }
}

/// <summary>
/// A sun-like light: parallel rays along the node's -Z (forward) direction.
/// Use <see cref="Node3D.LookAt"/> or <see cref="Node3D.RotationDegrees"/> to aim it.
/// Only the first directional light with <see cref="ShadowsEnabled"/> casts shadows.
/// </summary>
public class DirectionalLight3D : Light3D
{
    /// <summary>Enable shadow mapping for this light.</summary>
    public bool ShadowsEnabled { get; set; } = true;

    /// <summary>Half-size of the area around the camera covered by the shadow map (world units).</summary>
    public float ShadowOrthoSize { get; set; } = 30f;

    /// <summary>Distance behind and in front of the camera included in the shadow volume.</summary>
    public float ShadowDepthRange { get; set; } = 100f;

    public DirectionalLight3D(string? name = null) : base(name) { }

    /// <summary>Convenience: aims the light using pitch and yaw in degrees (e.g. -45, 30).</summary>
    public DirectionalLight3D WithDirection(float pitchDegrees, float yawDegrees)
    {
        RotationDegrees = new System.Numerics.Vector3(pitchDegrees, yawDegrees, 0);
        return this;
    }
}

/// <summary>An omnidirectional light with limited range (lamp, torch, explosion flash).</summary>
public class PointLight3D : Light3D
{
    /// <summary>Maximum reach of the light in world units.</summary>
    public float Range { get; set; } = 10f;

    public PointLight3D(string? name = null) : base(name) { }
}

/// <summary>A cone-shaped light along the node's -Z direction (flashlight, headlight).</summary>
public class SpotLight3D : Light3D
{
    /// <summary>Maximum reach of the light in world units.</summary>
    public float Range { get; set; } = 15f;

    /// <summary>Full cone angle in degrees.</summary>
    public float AngleDegrees { get; set; } = 45f;

    /// <summary>Softness of the cone edge (0 = hard, 0.5 = very soft).</summary>
    public float Softness { get; set; } = 0.1f;

    public SpotLight3D(string? name = null) : base(name) { }
}
