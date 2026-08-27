using System.Numerics;

namespace Ducz;

/// <summary>
/// Math helpers used across the engine. The engine uses <see cref="System.Numerics"/>
/// types (Vector2, Vector3, Quaternion, Matrix4x4) everywhere.
/// </summary>
public static class Mathf
{
    public const float Pi = MathF.PI;
    public const float Tau = MathF.PI * 2f;
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;
    public const float Epsilon = 1e-6f;

    public static float Clamp(float value, float min, float max) => MathF.Max(min, MathF.Min(max, value));
    public static float Clamp01(float value) => Clamp(value, 0f, 1f);
    public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float InverseLerp(float a, float b, float value) =>
        MathF.Abs(b - a) < Epsilon ? 0f : Clamp01((value - a) / (b - a));

    /// <summary>Framerate-independent exponential smoothing. Use in place of Lerp inside Update.</summary>
    public static float Damp(float current, float target, float smoothing, float dt) =>
        Lerp(current, target, 1f - MathF.Exp(-smoothing * dt));

    public static Vector3 Damp(Vector3 current, Vector3 target, float smoothing, float dt) =>
        Vector3.Lerp(current, target, 1f - MathF.Exp(-smoothing * dt));

    /// <summary>Moves <paramref name="current"/> towards <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
    public static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
            return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
    {
        var delta = target - current;
        float dist = delta.Length();
        if (dist <= maxDelta || dist < Epsilon)
            return target;
        return current + delta / dist * maxDelta;
    }

    /// <summary>Wraps an angle in radians to the (-PI, PI] range.</summary>
    public static float WrapAngle(float radians)
    {
        radians %= Tau;
        if (radians > Pi) radians -= Tau;
        if (radians <= -Pi) radians += Tau;
        return radians;
    }

    /// <summary>Interpolates between two angles (radians) along the shortest arc.</summary>
    public static float LerpAngle(float a, float b, float t) => a + WrapAngle(b - a) * t;

    /// <summary>Returns a quaternion that looks along <paramref name="forward"/> (must be non-zero).</summary>
    public static Quaternion LookRotation(Vector3 forward, Vector3? up = null)
    {
        var fwd = Vector3.Normalize(forward);
        var upDir = up ?? Vector3.UnitY;
        if (MathF.Abs(Vector3.Dot(fwd, upDir)) > 0.999f)
            upDir = MathF.Abs(fwd.Y) > 0.999f ? Vector3.UnitZ : Vector3.UnitY;

        var right = Vector3.Normalize(Vector3.Cross(upDir, fwd));
        var realUp = Vector3.Cross(fwd, right);

        // Column-major basis: right, up, forward. Engine convention: -Z is forward,
        // matching typical OpenGL cameras, so we build the matrix with -forward.
        var m = new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            realUp.X, realUp.Y, realUp.Z, 0,
            -fwd.X, -fwd.Y, -fwd.Z, 0,
            0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(m);
    }

    /// <summary>Component-wise absolute value.</summary>
    public static Vector3 Abs(Vector3 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));

    /// <summary>Safe normalize: returns Vector3.Zero when the vector is (nearly) zero.</summary>
    public static Vector3 NormalizeSafe(Vector3 v)
    {
        float len = v.Length();
        return len < Epsilon ? Vector3.Zero : v / len;
    }

    /// <summary>True when every component is a real number (no NaN, no infinity).</summary>
    public static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Projects a vector onto a plane defined by its normal.</summary>
    public static Vector3 ProjectOnPlane(Vector3 v, Vector3 planeNormal)
    {
        var n = Vector3.Normalize(planeNormal);
        return v - n * Vector3.Dot(v, n);
    }

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Right-handed perspective projection with OpenGL clip space (Z in -1..1).
    /// Preferred over Matrix4x4.CreatePerspectiveFieldOfView (which targets 0..1 depth).
    /// </summary>
    public static Matrix4x4 PerspectiveGl(float fovRadians, float aspect, float near, float far)
    {
        float f = 1f / MathF.Tan(fovRadians * 0.5f);
        var m = new Matrix4x4
        {
            M11 = f / aspect,
            M22 = f,
            M33 = (far + near) / (near - far),
            M34 = -1f,
            M43 = 2f * far * near / (near - far)
        };
        return m;
    }

    /// <summary>Right-handed orthographic projection with OpenGL clip space (Z in -1..1).</summary>
    public static Matrix4x4 OrthographicGl(float width, float height, float near, float far)
    {
        var m = new Matrix4x4
        {
            M11 = 2f / width,
            M22 = 2f / height,
            M33 = -2f / (far - near),
            M43 = -(far + near) / (far - near),
            M44 = 1f
        };
        return m;
    }
}

/// <summary>Extension helpers for <see cref="System.Numerics"/> types.</summary>
public static class VectorExtensions
{
    /// <summary>The vector with Y zeroed (useful for planar movement).</summary>
    public static Vector3 Flat(this Vector3 v) => new(v.X, 0f, v.Z);

    public static Vector2 Xz(this Vector3 v) => new(v.X, v.Z);
    public static Vector3 WithY(this Vector3 v, float y) => new(v.X, y, v.Z);

    /// <summary>Transforms a direction (w = 0) by a matrix.</summary>
    public static Vector3 TransformDirection(this Matrix4x4 m, Vector3 dir) =>
        Vector3.TransformNormal(dir, m);

    /// <summary>Transforms a point (w = 1) by a matrix.</summary>
    public static Vector3 TransformPoint(this Matrix4x4 m, Vector3 point) =>
        Vector3.Transform(point, m);

    /// <summary>Forward direction (-Z) of a rotation.</summary>
    public static Vector3 Forward(this Quaternion q) => Vector3.Transform(-Vector3.UnitZ, q);

    /// <summary>Right direction (+X) of a rotation.</summary>
    public static Vector3 Right(this Quaternion q) => Vector3.Transform(Vector3.UnitX, q);

    /// <summary>Up direction (+Y) of a rotation.</summary>
    public static Vector3 Up(this Quaternion q) => Vector3.Transform(Vector3.UnitY, q);
}
