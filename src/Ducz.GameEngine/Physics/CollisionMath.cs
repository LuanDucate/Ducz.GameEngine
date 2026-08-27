using System.Numerics;

namespace Ducz.Physics;

/// <summary>A collision contact. The normal points in the direction that separates shape A from shape B.</summary>
public struct Contact
{
    public Vector3 Normal;
    public float Depth;
    public Vector3 Point;
}

/// <summary>Shape kinds used by the runtime collision routines.</summary>
internal enum WorldShapeType
{
    Sphere,
    Capsule,
    Box,
    Heightfield,
    Mesh
}

/// <summary>A collision shape resolved into world space.</summary>
internal struct WorldShape
{
    public WorldShapeType Type;
    public Vector3 Center;
    public Vector3 SegA;          // capsule bottom
    public Vector3 SegB;          // capsule top
    public float Radius;
    public Vector3 HalfExtents;
    public Quaternion Orientation;
    public HeightfieldShape? Field;
    public MeshShape? Mesh;
    public Matrix4x4 LocalToWorld;   // mesh: body transform (includes scale)
    public Matrix4x4 WorldToLocal;
    public float MeshScale;          // mesh: uniform scale factor (max axis) for radii
    public Vector3 AabbMin;
    public Vector3 AabbMax;

    public bool AabbOverlaps(in WorldShape other) =>
        AabbMin.X <= other.AabbMax.X && AabbMax.X >= other.AabbMin.X &&
        AabbMin.Y <= other.AabbMax.Y && AabbMax.Y >= other.AabbMin.Y &&
        AabbMin.Z <= other.AabbMax.Z && AabbMax.Z >= other.AabbMin.Z;
}

/// <summary>Low-level collision and raycast routines (all world space).</summary>
internal static class CollisionMath
{
    // ------------------------------------------------------------------
    // Contact generation. Normal separates A from B (push A along +Normal).
    // ------------------------------------------------------------------

    public static bool TryCollide(in WorldShape a, in WorldShape b, out Contact contact)
    {
        contact = default;
        if (!a.AabbOverlaps(b))
            return false;

        // Heightfields and meshes only ever appear as B (they are static).
        if (a.Type is WorldShapeType.Heightfield or WorldShapeType.Mesh)
        {
            if (b.Type is WorldShapeType.Heightfield or WorldShapeType.Mesh)
                return false;
            if (TryCollide(b, a, out var flipped))
            {
                contact = flipped with { Normal = -flipped.Normal };
                return true;
            }
            return false;
        }

        switch (a.Type, b.Type)
        {
            case (WorldShapeType.Sphere, WorldShapeType.Sphere):
                return SphereSphere(a.Center, a.Radius, b.Center, b.Radius, out contact);

            case (WorldShapeType.Sphere, WorldShapeType.Box):
                return SphereBox(a.Center, a.Radius, b, out contact);

            case (WorldShapeType.Sphere, WorldShapeType.Capsule):
            {
                var closest = ClosestPointOnSegment(a.Center, b.SegA, b.SegB);
                return SphereSphere(a.Center, a.Radius, closest, b.Radius, out contact);
            }

            case (WorldShapeType.Sphere, WorldShapeType.Heightfield):
                return SphereHeightfield(a.Center, a.Radius, b.Field!, out contact);

            case (WorldShapeType.Sphere, WorldShapeType.Mesh):
                return SphereMesh(a.Center, a.Radius, b, out contact);

            case (WorldShapeType.Capsule, WorldShapeType.Mesh):
                return CapsuleMesh(a.SegA, a.SegB, a.Radius, b, out contact);

            case (WorldShapeType.Box, WorldShapeType.Mesh):
            {
                // Approximation: the box's inscribed sphere (exact for cubes resting on floors).
                float radius = MathF.Min(a.HalfExtents.X, MathF.Min(a.HalfExtents.Y, a.HalfExtents.Z));
                return SphereMesh(a.Center, radius, b, out contact);
            }

            case (WorldShapeType.Capsule, WorldShapeType.Sphere):
            {
                var closest = ClosestPointOnSegment(b.Center, a.SegA, a.SegB);
                return SphereSphere(closest, a.Radius, b.Center, b.Radius, out contact);
            }

            case (WorldShapeType.Capsule, WorldShapeType.Capsule):
            {
                ClosestPointsSegmentSegment(a.SegA, a.SegB, b.SegA, b.SegB, out var pA, out var pB);
                return SphereSphere(pA, a.Radius, pB, b.Radius, out contact);
            }

            case (WorldShapeType.Capsule, WorldShapeType.Box):
            {
                var point = ClosestPointOnSegmentToBox(a.SegA, a.SegB, b);
                return SphereBox(point, a.Radius, b, out contact);
            }

            case (WorldShapeType.Capsule, WorldShapeType.Heightfield):
            {
                // Use the lower end of the capsule (bottom hemisphere center).
                var bottom = a.SegA.Y <= a.SegB.Y ? a.SegA : a.SegB;
                return SphereHeightfield(bottom, a.Radius, b.Field!, out contact);
            }

            case (WorldShapeType.Box, WorldShapeType.Sphere):
            {
                if (SphereBox(b.Center, b.Radius, a, out var flipped))
                {
                    contact = flipped with { Normal = -flipped.Normal };
                    return true;
                }
                return false;
            }

            case (WorldShapeType.Box, WorldShapeType.Capsule):
            {
                var point = ClosestPointOnSegmentToBox(b.SegA, b.SegB, a);
                if (SphereBox(point, b.Radius, a, out var flipped))
                {
                    contact = flipped with { Normal = -flipped.Normal };
                    return true;
                }
                return false;
            }

            case (WorldShapeType.Box, WorldShapeType.Box):
                return BoxBox(a, b, out contact);

            case (WorldShapeType.Box, WorldShapeType.Heightfield):
                return BoxHeightfield(a, b.Field!, out contact);

            default:
                return false;
        }
    }

    private static bool SphereSphere(Vector3 centerA, float radiusA, Vector3 centerB, float radiusB, out Contact contact)
    {
        contact = default;
        var delta = centerA - centerB;
        float distSq = delta.LengthSquared();
        float radii = radiusA + radiusB;
        if (distSq >= radii * radii)
            return false;

        float dist = MathF.Sqrt(distSq);
        contact.Normal = dist > Mathf.Epsilon ? delta / dist : Vector3.UnitY;
        contact.Depth = radii - dist;
        contact.Point = centerB + contact.Normal * radiusB;
        return true;
    }

    private static bool SphereBox(Vector3 center, float radius, in WorldShape box, out Contact contact)
    {
        contact = default;
        var invRot = Quaternion.Inverse(box.Orientation);
        var local = Vector3.Transform(center - box.Center, invRot);
        var h = box.HalfExtents;

        var clamped = Vector3.Clamp(local, -h, h);
        var delta = local - clamped;
        float distSq = delta.LengthSquared();

        if (distSq > Mathf.Epsilon * Mathf.Epsilon)
        {
            // Sphere center outside the box.
            if (distSq >= radius * radius)
                return false;
            float dist = MathF.Sqrt(distSq);
            var localNormal = delta / dist;
            contact.Normal = Vector3.Transform(localNormal, box.Orientation);
            contact.Depth = radius - dist;
            contact.Point = Vector3.Transform(clamped, box.Orientation) + box.Center;
            return true;
        }

        // Center inside the box: push out along the axis of least penetration.
        float dx = h.X - MathF.Abs(local.X);
        float dy = h.Y - MathF.Abs(local.Y);
        float dz = h.Z - MathF.Abs(local.Z);

        Vector3 localN;
        float depth;
        if (dx <= dy && dx <= dz) { localN = new Vector3(MathF.Sign(local.X), 0, 0); depth = dx; }
        else if (dy <= dz) { localN = new Vector3(0, MathF.Sign(local.Y), 0); depth = dy; }
        else { localN = new Vector3(0, 0, MathF.Sign(local.Z)); depth = dz; }
        if (localN == Vector3.Zero)
            localN = Vector3.UnitY;

        contact.Normal = Vector3.Transform(localN, box.Orientation);
        contact.Depth = depth + radius;
        contact.Point = center;
        return true;
    }

    private static bool SphereHeightfield(Vector3 center, float radius, HeightfieldShape field, out Contact contact)
    {
        contact = default;
        if (!field.ContainsXz(center.X, center.Z))
            return false;

        float ground = field.GetHeight(center.X, center.Z);
        float bottom = center.Y - radius;
        if (bottom >= ground)
            return false;

        contact.Normal = field.GetNormal(center.X, center.Z);
        contact.Depth = ground - bottom;
        contact.Point = new Vector3(center.X, ground, center.Z);
        return true;
    }

    private static bool BoxHeightfield(in WorldShape box, HeightfieldShape field, out Contact contact)
    {
        contact = default;

        // Sample the box's bottom corners and center against the field.
        Span<Vector3> localPoints = stackalloc Vector3[5]
        {
            new(0, -box.HalfExtents.Y, 0),
            new(-box.HalfExtents.X, -box.HalfExtents.Y, -box.HalfExtents.Z),
            new(box.HalfExtents.X, -box.HalfExtents.Y, -box.HalfExtents.Z),
            new(-box.HalfExtents.X, -box.HalfExtents.Y, box.HalfExtents.Z),
            new(box.HalfExtents.X, -box.HalfExtents.Y, box.HalfExtents.Z)
        };

        float maxDepth = 0f;
        Vector3 bestPoint = default;
        foreach (var lp in localPoints)
        {
            var wp = Vector3.Transform(lp, box.Orientation) + box.Center;
            if (!field.ContainsXz(wp.X, wp.Z))
                continue;
            float ground = field.GetHeight(wp.X, wp.Z);
            float depth = ground - wp.Y;
            if (depth > maxDepth)
            {
                maxDepth = depth;
                bestPoint = wp;
            }
        }

        if (maxDepth <= 0f)
            return false;

        contact.Normal = field.GetNormal(bestPoint.X, bestPoint.Z);
        contact.Depth = maxDepth;
        contact.Point = bestPoint;
        return true;
    }

    private static bool BoxBox(in WorldShape a, in WorldShape b, out Contact contact)
    {
        contact = default;

        var rotA = Matrix4x4.CreateFromQuaternion(a.Orientation);
        var rotB = Matrix4x4.CreateFromQuaternion(b.Orientation);

        Span<Vector3> axesA = stackalloc Vector3[3]
        {
            new(rotA.M11, rotA.M12, rotA.M13),
            new(rotA.M21, rotA.M22, rotA.M23),
            new(rotA.M31, rotA.M32, rotA.M33)
        };
        Span<Vector3> axesB = stackalloc Vector3[3]
        {
            new(rotB.M11, rotB.M12, rotB.M13),
            new(rotB.M21, rotB.M22, rotB.M23),
            new(rotB.M31, rotB.M32, rotB.M33)
        };

        // 15 separating axis candidates: 3 + 3 face normals, 9 edge cross products.
        Span<Vector3> candidates = stackalloc Vector3[15];
        int count = 0;
        for (int i = 0; i < 3; i++)
            candidates[count++] = axesA[i];
        for (int i = 0; i < 3; i++)
            candidates[count++] = axesB[i];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                candidates[count++] = Vector3.Cross(axesA[i], axesB[j]);

        var delta = a.Center - b.Center;
        float minOverlap = float.MaxValue;
        Vector3 bestAxis = Vector3.UnitY;

        for (int i = 0; i < count; i++)
        {
            var axis = candidates[i];
            float lengthSq = axis.LengthSquared();
            if (lengthSq < 1e-6f)
                continue; // degenerate cross product

            axis /= MathF.Sqrt(lengthSq);

            float projA = ProjectBox(axis, axesA, a.HalfExtents);
            float projB = ProjectBox(axis, axesB, b.HalfExtents);
            float distance = Vector3.Dot(delta, axis);
            float overlap = projA + projB - MathF.Abs(distance);
            if (overlap <= 0f)
                return false; // separating axis found

            if (overlap < minOverlap)
            {
                minOverlap = overlap;
                bestAxis = distance < 0f ? -axis : axis;
            }
        }

        contact.Normal = bestAxis;
        contact.Depth = minOverlap;
        contact.Point = (a.Center + b.Center) * 0.5f;
        return true;
    }

    private static float ProjectBox(Vector3 axis, ReadOnlySpan<Vector3> axes, Vector3 halfExtents) =>
        MathF.Abs(Vector3.Dot(axis, axes[0])) * halfExtents.X +
        MathF.Abs(Vector3.Dot(axis, axes[1])) * halfExtents.Y +
        MathF.Abs(Vector3.Dot(axis, axes[2])) * halfExtents.Z;

    // ------------------------------------------------------------------
    // Closest-point helpers
    // ------------------------------------------------------------------

    public static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float lengthSq = ab.LengthSquared();
        if (lengthSq < Mathf.Epsilon)
            return a;
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
        return a + ab * t;
    }

    /// <summary>Closest points between two segments (Ericson, Real-Time Collision Detection).</summary>
    public static void ClosestPointsSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2,
        out Vector3 c1, out Vector3 c2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        float a = d1.LengthSquared();
        float e = d2.LengthSquared();
        float f = Vector3.Dot(d2, r);

        float s, t;
        if (a <= Mathf.Epsilon && e <= Mathf.Epsilon)
        {
            c1 = p1; c2 = p2;
            return;
        }
        if (a <= Mathf.Epsilon)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= Mathf.Epsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom > Mathf.Epsilon ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }

    /// <summary>Approximate closest point on a segment to an oriented box (iterative refinement).</summary>
    public static Vector3 ClosestPointOnSegmentToBox(Vector3 segA, Vector3 segB, in WorldShape box)
    {
        var point = ClosestPointOnSegment(box.Center, segA, segB);
        for (int i = 0; i < 3; i++)
        {
            var onBox = ClosestPointOnBox(point, box);
            var refined = ClosestPointOnSegment(onBox, segA, segB);
            if (Vector3.DistanceSquared(refined, point) < 1e-8f)
                return refined;
            point = refined;
        }
        return point;
    }

    public static Vector3 ClosestPointOnBox(Vector3 point, in WorldShape box)
    {
        var invRot = Quaternion.Inverse(box.Orientation);
        var local = Vector3.Transform(point - box.Center, invRot);
        var clamped = Vector3.Clamp(local, -box.HalfExtents, box.HalfExtents);
        return Vector3.Transform(clamped, box.Orientation) + box.Center;
    }

    // ------------------------------------------------------------------
    // Raycasts. Direction must be normalized.
    // ------------------------------------------------------------------

    public static bool Raycast(in WorldShape shape, Vector3 origin, Vector3 direction, float maxDistance,
        out float distance, out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.UnitY;
        // A NaN or infinite ray (degenerate camera, minimised window) has no meaningful answer:
        // report a miss rather than letting the comparisons below produce nonsense.
        if (!Mathf.IsFinite(origin) || !Mathf.IsFinite(direction) || !float.IsFinite(maxDistance))
            return false;

        switch (shape.Type)
        {
            case WorldShapeType.Sphere:
                return RaySphere(origin, direction, maxDistance, shape.Center, shape.Radius, out distance, out normal);
            case WorldShapeType.Box:
                return RayBox(origin, direction, maxDistance, shape, out distance, out normal);
            case WorldShapeType.Capsule:
                return RayCapsule(origin, direction, maxDistance, shape, out distance, out normal);
            case WorldShapeType.Heightfield:
                return RayHeightfield(origin, direction, maxDistance, shape.Field!, out distance, out normal);
            case WorldShapeType.Mesh:
                return RayMesh(origin, direction, maxDistance, shape, out distance, out normal);
            default:
                distance = 0f;
                normal = Vector3.UnitY;
                return false;
        }
    }

    private static bool RaySphere(Vector3 origin, Vector3 dir, float maxDist, Vector3 center, float radius,
        out float distance, out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.UnitY;

        var m = origin - center;
        float b = Vector3.Dot(m, dir);
        float c = m.LengthSquared() - radius * radius;
        if (c > 0f && b > 0f)
            return false;

        float discriminant = b * b - c;
        if (discriminant < 0f)
            return false;

        float t = -b - MathF.Sqrt(discriminant);
        if (t < 0f) t = 0f;
        if (t > maxDist)
            return false;

        distance = t;
        normal = Mathf.NormalizeSafe(origin + dir * t - center);
        if (normal == Vector3.Zero)
            normal = -dir;
        return true;
    }

    private static bool RayBox(Vector3 origin, Vector3 dir, float maxDist, in WorldShape box,
        out float distance, out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.UnitY;

        var invRot = Quaternion.Inverse(box.Orientation);
        var localOrigin = Vector3.Transform(origin - box.Center, invRot);
        var localDir = Vector3.Transform(dir, invRot);
        var h = box.HalfExtents;

        float tMin = 0f;
        float tMax = maxDist;
        var normalAxis = 0;
        var normalSign = 1f;

        for (int i = 0; i < 3; i++)
        {
            float o = i == 0 ? localOrigin.X : i == 1 ? localOrigin.Y : localOrigin.Z;
            float d = i == 0 ? localDir.X : i == 1 ? localDir.Y : localDir.Z;
            float extent = i == 0 ? h.X : i == 1 ? h.Y : h.Z;

            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < -extent || o > extent)
                    return false;
                continue;
            }

            float inv = 1f / d;
            float t1 = (-extent - o) * inv;
            float t2 = (extent - o) * inv;
            float sign = d < 0f ? 1f : -1f;   // not MathF.Sign: that throws on NaN
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }
            if (t1 > tMin)
            {
                tMin = t1;
                normalAxis = i;
                normalSign = sign;
            }
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax)
                return false;
        }

        distance = tMin;
        var localNormal = normalAxis switch
        {
            0 => new Vector3(normalSign, 0, 0),
            1 => new Vector3(0, normalSign, 0),
            _ => new Vector3(0, 0, normalSign)
        };
        normal = Vector3.Transform(localNormal, box.Orientation);
        return true;
    }

    private static bool RayCapsule(Vector3 origin, Vector3 dir, float maxDist, in WorldShape capsule,
        out float distance, out Vector3 normal)
    {
        distance = float.MaxValue;
        normal = Vector3.UnitY;
        bool hit = false;

        // Caps
        if (RaySphere(origin, dir, maxDist, capsule.SegA, capsule.Radius, out float t, out var n) && t < distance)
        {
            distance = t; normal = n; hit = true;
        }
        if (RaySphere(origin, dir, maxDist, capsule.SegB, capsule.Radius, out t, out n) && t < distance)
        {
            distance = t; normal = n; hit = true;
        }

        // Cylinder body
        var axis = capsule.SegB - capsule.SegA;
        float axisLen = axis.Length();
        if (axisLen > Mathf.Epsilon)
        {
            var axisDir = axis / axisLen;
            var ao = origin - capsule.SegA;

            var d = dir - axisDir * Vector3.Dot(dir, axisDir);
            var o = ao - axisDir * Vector3.Dot(ao, axisDir);

            float a = d.LengthSquared();
            if (a > 1e-8f)
            {
                float b = 2f * Vector3.Dot(o, d);
                float c = o.LengthSquared() - capsule.Radius * capsule.Radius;
                float disc = b * b - 4f * a * c;
                if (disc >= 0f)
                {
                    float tCyl = (-b - MathF.Sqrt(disc)) / (2f * a);
                    if (tCyl >= 0f && tCyl <= maxDist)
                    {
                        var point = origin + dir * tCyl;
                        float proj = Vector3.Dot(point - capsule.SegA, axisDir);
                        if (proj >= 0f && proj <= axisLen && tCyl < distance)
                        {
                            distance = tCyl;
                            var onAxis = capsule.SegA + axisDir * proj;
                            normal = Mathf.NormalizeSafe(point - onAxis);
                            hit = true;
                        }
                    }
                }
            }
        }

        return hit;
    }

    private static bool RayHeightfield(Vector3 origin, Vector3 dir, float maxDist, HeightfieldShape field,
        out float distance, out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.UnitY;

        const float step = 0.5f;
        float traveled = 0f;
        var prev = origin;
        bool prevAbove = !field.ContainsXz(origin.X, origin.Z) || origin.Y > field.GetHeight(origin.X, origin.Z);

        while (traveled <= maxDist)
        {
            traveled = MathF.Min(traveled + step, maxDist + step);
            var point = origin + dir * MathF.Min(traveled, maxDist);
            bool inside = field.ContainsXz(point.X, point.Z);
            bool above = !inside || point.Y > field.GetHeight(point.X, point.Z);

            if (prevAbove && !above)
            {
                // Bisect between prev and point.
                var lo = prev;
                var hi = point;
                for (int i = 0; i < 12; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    bool midAbove = !field.ContainsXz(mid.X, mid.Z) || mid.Y > field.GetHeight(mid.X, mid.Z);
                    if (midAbove) lo = mid;
                    else hi = mid;
                }
                var hitPoint = (lo + hi) * 0.5f;
                distance = Vector3.Distance(origin, hitPoint);
                if (distance > maxDist)
                    return false;
                normal = field.GetNormal(hitPoint.X, hitPoint.Z);
                return true;
            }

            prev = point;
            prevAbove = above;
            if (traveled >= maxDist)
                break;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Triangle meshes (queries run in the mesh's local space)
    // ------------------------------------------------------------------

    [ThreadStatic] private static List<int>? _meshQueryBuffer;

    private static bool SphereMesh(Vector3 center, float radius, in WorldShape meshShape, out Contact contact)
    {
        contact = default;
        var mesh = meshShape.Mesh!;
        float scale = MathF.Max(meshShape.MeshScale, 1e-5f);
        var localCenter = Vector3.Transform(center, meshShape.WorldToLocal);
        float localRadius = radius / scale;

        var candidates = _meshQueryBuffer ??= new List<int>();
        mesh.QueryTriangles(localCenter - new Vector3(localRadius), localCenter + new Vector3(localRadius), candidates);
        if (candidates.Count == 0)
            return false;

        float bestDepth = 0f;
        Vector3 bestNormal = Vector3.UnitY, bestPoint = default;
        foreach (int tri in candidates)
        {
            var (a, b, c) = mesh.Triangle(tri);
            var closest = MeshShape.ClosestPointOnTriangle(localCenter, a, b, c);
            var delta = localCenter - closest;
            float distSq = delta.LengthSquared();
            if (distSq >= localRadius * localRadius)
                continue;

            float dist = MathF.Sqrt(distSq);
            var faceNormal = Mathf.NormalizeSafe(Vector3.Cross(b - a, c - a));
            // Very close to (or on) the surface: use the face normal so we push out, not through.
            var normal = dist > 1e-4f ? delta / dist : faceNormal;
            if (Vector3.Dot(normal, faceNormal) < 0f && dist < localRadius * 0.5f)
                normal = faceNormal;   // center slipped slightly behind a wall: still push to the front

            float depth = localRadius - dist;
            if (depth > bestDepth)
            {
                bestDepth = depth;
                bestNormal = normal;
                bestPoint = closest;
            }
        }

        if (bestDepth <= 0f)
            return false;

        contact.Normal = Mathf.NormalizeSafe(Vector3.TransformNormal(bestNormal, meshShape.LocalToWorld));
        contact.Depth = bestDepth * scale;
        contact.Point = Vector3.Transform(bestPoint, meshShape.LocalToWorld);
        return true;
    }

    private static bool CapsuleMesh(Vector3 segA, Vector3 segB, float radius, in WorldShape meshShape, out Contact contact)
    {
        contact = default;
        var mesh = meshShape.Mesh!;
        float scale = MathF.Max(meshShape.MeshScale, 1e-5f);
        var p = Vector3.Transform(segA, meshShape.WorldToLocal);
        var q = Vector3.Transform(segB, meshShape.WorldToLocal);
        float localRadius = radius / scale;

        var candidates = _meshQueryBuffer ??= new List<int>();
        mesh.QueryTriangles(Vector3.Min(p, q) - new Vector3(localRadius), Vector3.Max(p, q) + new Vector3(localRadius), candidates);
        if (candidates.Count == 0)
            return false;

        float bestDepth = 0f;
        Vector3 bestNormal = Vector3.UnitY, bestPoint = default;
        foreach (int tri in candidates)
        {
            var (a, b, c) = mesh.Triangle(tri);
            ClosestPointsSegmentTriangle(p, q, a, b, c, out var onSegment, out var onTriangle);
            var delta = onSegment - onTriangle;
            float distSq = delta.LengthSquared();
            if (distSq >= localRadius * localRadius)
                continue;

            float dist = MathF.Sqrt(distSq);
            var faceNormal = Mathf.NormalizeSafe(Vector3.Cross(b - a, c - a));
            var normal = dist > 1e-4f ? delta / dist : faceNormal;
            if (Vector3.Dot(normal, faceNormal) < 0f && dist < localRadius * 0.5f)
                normal = faceNormal;

            float depth = localRadius - dist;
            if (depth > bestDepth)
            {
                bestDepth = depth;
                bestNormal = normal;
                bestPoint = onTriangle;
            }
        }

        if (bestDepth <= 0f)
            return false;

        contact.Normal = Mathf.NormalizeSafe(Vector3.TransformNormal(bestNormal, meshShape.LocalToWorld));
        contact.Depth = bestDepth * scale;
        contact.Point = Vector3.Transform(bestPoint, meshShape.LocalToWorld);
        return true;
    }

    /// <summary>Closest points between a segment and a triangle.</summary>
    private static void ClosestPointsSegmentTriangle(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c,
        out Vector3 onSegment, out Vector3 onTriangle)
    {
        // 1) Segment crossing the triangle's interior: distance zero.
        var dir = q - p;
        if (MeshShape.RayTriangle(p, dir, a, b, c, out float t) && t >= 0f && t <= 1f)
        {
            onSegment = onTriangle = p + dir * t;
            return;
        }

        // 2) Otherwise the minimum is at an endpoint or between the segment and an edge.
        Span<Vector3> segPoints = stackalloc Vector3[5];
        Span<Vector3> triPoints = stackalloc Vector3[5];
        segPoints[0] = p; triPoints[0] = MeshShape.ClosestPointOnTriangle(p, a, b, c);
        segPoints[1] = q; triPoints[1] = MeshShape.ClosestPointOnTriangle(q, a, b, c);
        ClosestPointsSegmentSegment(p, q, a, b, out segPoints[2], out triPoints[2]);
        ClosestPointsSegmentSegment(p, q, b, c, out segPoints[3], out triPoints[3]);
        ClosestPointsSegmentSegment(p, q, c, a, out segPoints[4], out triPoints[4]);

        float best = float.MaxValue;
        onSegment = p;
        onTriangle = a;
        for (int i = 0; i < 5; i++)
        {
            float d = Vector3.DistanceSquared(segPoints[i], triPoints[i]);
            if (d < best)
            {
                best = d;
                onSegment = segPoints[i];
                onTriangle = triPoints[i];
            }
        }
    }

    private static bool RayMesh(Vector3 origin, Vector3 dir, float maxDist, in WorldShape meshShape,
        out float distance, out Vector3 normal)
    {
        var localOrigin = Vector3.Transform(origin, meshShape.WorldToLocal);
        var localDir = Vector3.TransformNormal(dir, meshShape.WorldToLocal);   // not normalized: t stays in world units
        if (meshShape.Mesh!.Raycast(localOrigin, localDir, maxDist, out float t, out var localNormal))
        {
            distance = t;
            normal = Mathf.NormalizeSafe(Vector3.TransformNormal(localNormal, meshShape.LocalToWorld));
            return true;
        }
        distance = 0f;
        normal = Vector3.UnitY;
        return false;
    }
}
