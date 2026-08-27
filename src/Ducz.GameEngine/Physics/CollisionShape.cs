using System.Numerics;

namespace Ducz.Physics;

/// <summary>Base class for collision shapes attached to physics bodies.</summary>
public abstract class CollisionShape
{
}

/// <summary>An oriented box collider.</summary>
public sealed class BoxShape : CollisionShape
{
    /// <summary>Half the size along each axis (a 1x1x1 cube has half extents of 0.5).</summary>
    public Vector3 HalfExtents { get; set; } = new(0.5f);

    public BoxShape() { }
    public BoxShape(Vector3 halfExtents) => HalfExtents = halfExtents;
    public BoxShape(float sizeX, float sizeY, float sizeZ) => HalfExtents = new Vector3(sizeX, sizeY, sizeZ) * 0.5f;

    /// <summary>Creates a box shape from a full size.</summary>
    public static BoxShape FromSize(Vector3 size) => new(size * 0.5f);
}

/// <summary>A sphere collider.</summary>
public sealed class SphereShape : CollisionShape
{
    public float Radius { get; set; } = 0.5f;

    public SphereShape() { }
    public SphereShape(float radius) => Radius = radius;
}

/// <summary>A capsule collider aligned with the body's local Y axis.</summary>
public sealed class CapsuleShape : CollisionShape
{
    public float Radius { get; set; } = 0.35f;

    /// <summary>Total height, caps included.</summary>
    public float Height { get; set; } = 1.8f;

    public CapsuleShape() { }
    public CapsuleShape(float radius, float height)
    {
        Radius = radius;
        Height = height;
    }
}

/// <summary>
/// A heightfield collider defined by a height function over the XZ plane
/// (used by <see cref="Terrain"/>; can also be created manually).
/// </summary>
public sealed class HeightfieldShape : CollisionShape
{
    /// <summary>Returns terrain height (world Y) for world X/Z coordinates.</summary>
    public required Func<float, float, float> GetHeight { get; init; }

    /// <summary>World-space bounds on X.</summary>
    public required Vector2 BoundsX { get; init; }

    /// <summary>World-space bounds on Z.</summary>
    public required Vector2 BoundsZ { get; init; }

    /// <summary>Maximum height used for the broadphase box.</summary>
    public float MaxHeight { get; init; } = 1000f;

    /// <summary>Minimum height used for the broadphase box.</summary>
    public float MinHeight { get; init; } = -1000f;

    public bool ContainsXz(float x, float z) =>
        x >= BoundsX.X && x <= BoundsX.Y && z >= BoundsZ.X && z <= BoundsZ.Y;

    /// <summary>Surface normal from central differences.</summary>
    public Vector3 GetNormal(float x, float z, float epsilon = 0.25f)
    {
        float hL = GetHeight(x - epsilon, z);
        float hR = GetHeight(x + epsilon, z);
        float hD = GetHeight(x, z - epsilon);
        float hU = GetHeight(x, z + epsilon);
        return Vector3.Normalize(new Vector3(hL - hR, 2f * epsilon, hD - hU));
    }
}
