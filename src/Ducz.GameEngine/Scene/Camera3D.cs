using System.Numerics;

namespace Ducz;

/// <summary>Camera projection type.</summary>
public enum CameraProjection
{
    Perspective,
    Orthographic
}

/// <summary>
/// A 3D camera. Add one to your scene and set <see cref="MakeCurrent"/> (the first
/// camera added becomes current automatically). The camera looks along its -Z axis.
/// </summary>
public class Camera3D : Node3D
{
    internal static Camera3D? CurrentCamera;

    /// <summary>Projection mode.</summary>
    public CameraProjection Projection { get; set; } = CameraProjection.Perspective;

    /// <summary>Vertical field of view in degrees (perspective mode).</summary>
    public float FovDegrees { get; set; } = 60f;

    /// <summary>Half-height of the view volume in world units (orthographic mode).</summary>
    public float OrthographicSize { get; set; } = 5f;

    /// <summary>Near clip plane distance.</summary>
    public float Near { get; set; } = 0.05f;

    /// <summary>Far clip plane distance.</summary>
    public float Far { get; set; } = 500f;

    /// <summary>True when this camera is the one used for rendering.</summary>
    public bool IsCurrent => CurrentCamera == this;

    public Camera3D(string? name = null) : base(name) { }

    /// <summary>Makes this the rendering camera.</summary>
    public void MakeCurrent() => CurrentCamera = this;

    protected override void OnEnterTree()
    {
        base.OnEnterTree();
        CurrentCamera ??= this;
    }

    protected override void OnExitTree()
    {
        if (CurrentCamera == this)
            CurrentCamera = null;
        base.OnExitTree();
    }

    /// <summary>World-to-camera matrix.</summary>
    public Matrix4x4 GetViewMatrix()
    {
        Matrix4x4.Invert(GlobalTransform, out var view);
        return view;
    }

    /// <summary>Camera-to-clip matrix for the given aspect ratio.</summary>
    public Matrix4x4 GetProjectionMatrix(float aspect)
    {
        if (Projection == CameraProjection.Orthographic)
        {
            float height = OrthographicSize * 2f;
            return Mathf.OrthographicGl(height * aspect, height, Near, Far);
        }
        return Mathf.PerspectiveGl(FovDegrees * Mathf.Deg2Rad, aspect, Near, Far);
    }

    /// <summary>Combined view-projection matrix.</summary>
    public Matrix4x4 GetViewProjection(float aspect) => GetViewMatrix() * GetProjectionMatrix(aspect);

    /// <summary>
    /// Converts a screen pixel position into a world-space ray (origin + direction).
    /// Useful for mouse picking.
    /// </summary>
    public (Vector3 Origin, Vector3 Direction) ScreenPointToRay(Vector2 screenPos)
    {
        // A minimised window reports a 0x0 framebuffer; dividing by it would hand out a
        // NaN ray that poisons every raycast downstream. Aim straight ahead instead.
        var fallback = (GlobalPosition, GlobalForward);
        var windowSize = Engine.WindowSize;
        if (windowSize.X < 1f || windowSize.Y < 1f)
            return fallback;

        float aspect = windowSize.X / windowSize.Y;

        var ndc = new Vector2(
            screenPos.X / windowSize.X * 2f - 1f,
            1f - screenPos.Y / windowSize.Y * 2f);

        if (!Matrix4x4.Invert(GetViewProjection(aspect), out var invViewProj))
            return fallback;

        var nearPoint = Vector4.Transform(new Vector4(ndc, -1f, 1f), invViewProj);
        var farPoint = Vector4.Transform(new Vector4(ndc, 1f, 1f), invViewProj);
        if (MathF.Abs(nearPoint.W) < 1e-9f || MathF.Abs(farPoint.W) < 1e-9f)
            return fallback;

        var near3 = new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z) / nearPoint.W;
        var delta = new Vector3(farPoint.X, farPoint.Y, farPoint.Z) / farPoint.W - near3;
        if (!IsFinite(near3) || !IsFinite(delta) || delta.LengthSquared() < 1e-12f)
            return fallback;

        return (near3, Vector3.Normalize(delta));
    }

    private static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// Intersects the ray through a screen pixel with a horizontal plane at
    /// <paramref name="planeY"/>. Returns the world point, or null when the ray is
    /// parallel to the plane. Perfect for placing objects on the ground with the mouse.
    /// </summary>
    public Vector3? ScreenPointToGround(Vector2 screenPos, float planeY = 0f)
    {
        var (origin, direction) = ScreenPointToRay(screenPos);
        if (MathF.Abs(direction.Y) < 1e-6f)
            return null;

        float t = (planeY - origin.Y) / direction.Y;
        if (t < 0f)
            return null;   // plane is behind the camera
        return origin + direction * t;
    }

    /// <summary>Projects a world position to screen pixels. Z is depth (0..1); returns null when behind the camera.</summary>
    public Vector3? WorldToScreenPoint(Vector3 worldPos)
    {
        var windowSize = Engine.WindowSize;
        float aspect = windowSize.Y <= 0 ? 1f : windowSize.X / windowSize.Y;

        var clip = Vector4.Transform(new Vector4(worldPos, 1f), GetViewProjection(aspect));
        if (clip.W <= 0f)
            return null;

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        return new Vector3(
            (ndc.X + 1f) * 0.5f * windowSize.X,
            (1f - ndc.Y) * 0.5f * windowSize.Y,
            ndc.Z * 0.5f + 0.5f);
    }
}
