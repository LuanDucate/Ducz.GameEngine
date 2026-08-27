using System.Numerics;
using Ducz.Rendering;

namespace Ducz.Physics;

/// <summary>
/// A static triangle-mesh collider ("trimesh"): the exact geometry of an imported
/// model or map, so characters walk on its floors and bump into its walls instead
/// of a bounding box. Triangles live in the body's local space and are indexed in a
/// uniform grid for fast queries. Intended for <see cref="StaticBody3D"/> only.
///
/// <code>
/// var instance = Assets.LoadModel("Assets/city.glb").Instantiate();
/// var body = new StaticBody3D(MeshShape.FromNode(instance)!);
/// body.AddChild(instance);
/// </code>
/// </summary>
public sealed class MeshShape : CollisionShape
{
    /// <summary>Local-space vertex positions.</summary>
    public Vector3[] Vertices { get; }

    /// <summary>Triangle indices (3 per triangle) into <see cref="Vertices"/>.</summary>
    public uint[] Indices { get; }

    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public int TriangleCount => Indices.Length / 3;

    // ---- Uniform grid acceleration ----
    private readonly int _nx, _ny, _nz;
    private readonly Vector3 _cellSize;
    private readonly int[][] _cells;          // triangle indices per cell (null-free; empty arrays for empty cells)
    private readonly int[] _triangleStamp;    // last query id that touched each triangle (dedupe)
    private int _queryId;

    public MeshShape(Vector3[] vertices, uint[] indices)
    {
        if (vertices.Length == 0 || indices.Length < 3)
            throw new ArgumentException("MeshShape needs at least one triangle.");
        Vertices = vertices;
        Indices = indices;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        // Pad slightly so flat meshes (a single floor quad) still get a volume.
        BoundsMin = min - new Vector3(0.01f);
        BoundsMax = max + new Vector3(0.01f);

        // Grid resolution: roughly 2 triangles per cell, capped so tiny/huge meshes stay sane.
        int triangles = TriangleCount;
        int perAxis = Math.Clamp((int)MathF.Ceiling(MathF.Cbrt(triangles / 2f)), 1, 48);
        var extent = BoundsMax - BoundsMin;
        float maxExtent = MathF.Max(MathF.Max(extent.X, extent.Y), MathF.Max(extent.Z, 1e-3f));
        _nx = Math.Clamp((int)MathF.Round(perAxis * extent.X / maxExtent), 1, perAxis);
        _ny = Math.Clamp((int)MathF.Round(perAxis * extent.Y / maxExtent), 1, perAxis);
        _nz = Math.Clamp((int)MathF.Round(perAxis * extent.Z / maxExtent), 1, perAxis);
        _cellSize = new Vector3(extent.X / _nx, extent.Y / _ny, extent.Z / _nz);

        var buckets = new List<int>[_nx * _ny * _nz];
        for (int t = 0; t < triangles; t++)
        {
            var (a, b, c) = Triangle(t);
            var tMin = Vector3.Min(a, Vector3.Min(b, c));
            var tMax = Vector3.Max(a, Vector3.Max(b, c));
            CellRange(tMin, tMax, out var lo, out var hi);
            for (int z = lo.Z; z <= hi.Z; z++)
                for (int y = lo.Y; y <= hi.Y; y++)
                    for (int x = lo.X; x <= hi.X; x++)
                    {
                        int index = CellIndex(x, y, z);
                        (buckets[index] ??= new List<int>()).Add(t);
                    }
        }
        _cells = new int[buckets.Length][];
        for (int i = 0; i < buckets.Length; i++)
            _cells[i] = buckets[i]?.ToArray() ?? Array.Empty<int>();
        _triangleStamp = new int[triangles];
    }

    /// <summary>Builds a mesh shape from CPU geometry.</summary>
    public static MeshShape FromMeshData(MeshData data) =>
        new(data.Vertices.Select(v => v.Position).ToArray(), data.Indices);

    /// <summary>
    /// Gathers every <see cref="MeshInstance3D"/> under <paramref name="root"/> (including
    /// itself) whose meshes kept CPU geometry, transformed into the root's local space.
    /// Returns null when there is no usable geometry.
    /// </summary>
    public static MeshShape? FromNode(Node3D root)
    {
        var vertices = new List<Vector3>();
        var indices = new List<uint>();

        Matrix4x4.Invert(root.GlobalTransform, out var rootInverse);
        var instances = root.Descendants().OfType<MeshInstance3D>().ToList();
        if (root is MeshInstance3D self)
            instances.Insert(0, self);

        foreach (var instance in instances)
        {
            var toRoot = instance.GlobalTransform * rootInverse;
            foreach (var surface in instance.Surfaces)
            {
                var mesh = surface.Mesh;
                if (mesh.CpuPositions.Length == 0 || mesh.CpuIndices.Length == 0)
                    continue;
                uint baseIndex = (uint)vertices.Count;
                foreach (var p in mesh.CpuPositions)
                    vertices.Add(Vector3.Transform(p, toRoot));
                foreach (var i in mesh.CpuIndices)
                    indices.Add(baseIndex + i);
            }
        }

        return vertices.Count > 0 && indices.Count >= 3
            ? new MeshShape(vertices.ToArray(), indices.ToArray())
            : null;
    }

    /// <summary>Corner positions of triangle <paramref name="index"/>.</summary>
    public (Vector3 A, Vector3 B, Vector3 C) Triangle(int index)
    {
        int i = index * 3;
        return (Vertices[Indices[i]], Vertices[Indices[i + 1]], Vertices[Indices[i + 2]]);
    }

    /// <summary>Geometric (winding) normal of a triangle, unit length.</summary>
    public Vector3 TriangleNormal(int index)
    {
        var (a, b, c) = Triangle(index);
        return Mathf.NormalizeSafe(Vector3.Cross(b - a, c - a));
    }

    // ------------------------------------------------------------------
    // Queries (local space)
    // ------------------------------------------------------------------

    /// <summary>Collects the indices of triangles whose cells overlap a local-space AABB.</summary>
    internal void QueryTriangles(Vector3 aabbMin, Vector3 aabbMax, List<int> results)
    {
        results.Clear();
        if (aabbMax.X < BoundsMin.X || aabbMin.X > BoundsMax.X ||
            aabbMax.Y < BoundsMin.Y || aabbMin.Y > BoundsMax.Y ||
            aabbMax.Z < BoundsMin.Z || aabbMin.Z > BoundsMax.Z)
            return;

        int stamp = ++_queryId;
        CellRange(aabbMin, aabbMax, out var lo, out var hi);
        for (int z = lo.Z; z <= hi.Z; z++)
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int x = lo.X; x <= hi.X; x++)
                    foreach (int t in _cells[CellIndex(x, y, z)])
                    {
                        if (_triangleStamp[t] == stamp)
                            continue;
                        _triangleStamp[t] = stamp;
                        results.Add(t);
                    }
    }

    /// <summary>
    /// Local-space ray query (direction need not be normalized: <paramref name="t"/> is in
    /// units of the direction). Walks the grid cells along the ray and returns the nearest hit.
    /// </summary>
    internal bool Raycast(Vector3 origin, Vector3 direction, float maxT, out float t, out Vector3 normal)
    {
        t = float.MaxValue;
        normal = Vector3.UnitY;

        // Clip the ray to the mesh bounds (slab test).
        float tEnter = 0f, tExit = maxT;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = Component(origin, axis), d = Component(direction, axis);
            float lo = Component(BoundsMin, axis), hi = Component(BoundsMax, axis);
            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < lo || o > hi)
                    return false;
                continue;
            }
            float t0 = (lo - o) / d, t1 = (hi - o) / d;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tEnter = MathF.Max(tEnter, t0);
            tExit = MathF.Min(tExit, t1);
            if (tEnter > tExit)
                return false;
        }

        // 3D DDA through the cells.
        var start = origin + direction * tEnter;
        int cx = Math.Clamp((int)((start.X - BoundsMin.X) / _cellSize.X), 0, _nx - 1);
        int cy = Math.Clamp((int)((start.Y - BoundsMin.Y) / _cellSize.Y), 0, _ny - 1);
        int cz = Math.Clamp((int)((start.Z - BoundsMin.Z) / _cellSize.Z), 0, _nz - 1);

        int stepX = direction.X > 0 ? 1 : direction.X < 0 ? -1 : 0;
        int stepY = direction.Y > 0 ? 1 : direction.Y < 0 ? -1 : 0;
        int stepZ = direction.Z > 0 ? 1 : direction.Z < 0 ? -1 : 0;

        float NextBoundary(int cell, int step, float cellSize, float boundsMin) =>
            boundsMin + (cell + (step > 0 ? 1 : 0)) * cellSize;

        float tMaxX = stepX != 0 ? (NextBoundary(cx, stepX, _cellSize.X, BoundsMin.X) - origin.X) / direction.X : float.MaxValue;
        float tMaxY = stepY != 0 ? (NextBoundary(cy, stepY, _cellSize.Y, BoundsMin.Y) - origin.Y) / direction.Y : float.MaxValue;
        float tMaxZ = stepZ != 0 ? (NextBoundary(cz, stepZ, _cellSize.Z, BoundsMin.Z) - origin.Z) / direction.Z : float.MaxValue;
        float tDeltaX = stepX != 0 ? MathF.Abs(_cellSize.X / direction.X) : float.MaxValue;
        float tDeltaY = stepY != 0 ? MathF.Abs(_cellSize.Y / direction.Y) : float.MaxValue;
        float tDeltaZ = stepZ != 0 ? MathF.Abs(_cellSize.Z / direction.Z) : float.MaxValue;

        int stamp = ++_queryId;
        int guard = (_nx + _ny + _nz) * 3 + 8;
        while (guard-- > 0)
        {
            foreach (int tri in _cells[CellIndex(cx, cy, cz)])
            {
                if (_triangleStamp[tri] == stamp)
                    continue;
                _triangleStamp[tri] = stamp;
                var (a, b, c) = Triangle(tri);
                if (RayTriangle(origin, direction, a, b, c, out float hitT) && hitT >= 0f && hitT <= maxT && hitT < t)
                {
                    t = hitT;
                    var n = Vector3.Cross(b - a, c - a);
                    normal = Mathf.NormalizeSafe(Vector3.Dot(n, direction) > 0f ? -n : n);
                }
            }

            // The nearest hit so far is inside the cells already visited: done.
            float cellExit = MathF.Min(tMaxX, MathF.Min(tMaxY, tMaxZ));
            if (t <= cellExit || cellExit > tExit)
                break;

            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                cx += stepX; tMaxX += tDeltaX;
                if (cx < 0 || cx >= _nx) break;
            }
            else if (tMaxY < tMaxZ)
            {
                cy += stepY; tMaxY += tDeltaY;
                if (cy < 0 || cy >= _ny) break;
            }
            else
            {
                cz += stepZ; tMaxZ += tDeltaZ;
                if (cz < 0 || cz >= _nz) break;
            }
        }
        return t < float.MaxValue;
    }

    /// <summary>Möller–Trumbore ray/triangle intersection (both faces).</summary>
    public static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(direction, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f)
            return false;
        float invDet = 1f / det;
        var s = origin - a;
        float u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f)
            return false;
        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(direction, q) * invDet;
        if (v < 0f || u + v > 1f)
            return false;
        t = Vector3.Dot(e2, q) * invDet;
        return true;
    }

    /// <summary>Closest point on a triangle to a point (Ericson, Real-Time Collision Detection).</summary>
    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    // ------------------------------------------------------------------
    // Grid helpers
    // ------------------------------------------------------------------

    private void CellRange(Vector3 min, Vector3 max, out (int X, int Y, int Z) lo, out (int X, int Y, int Z) hi)
    {
        lo = (Math.Clamp((int)((min.X - BoundsMin.X) / _cellSize.X), 0, _nx - 1),
              Math.Clamp((int)((min.Y - BoundsMin.Y) / _cellSize.Y), 0, _ny - 1),
              Math.Clamp((int)((min.Z - BoundsMin.Z) / _cellSize.Z), 0, _nz - 1));
        hi = (Math.Clamp((int)((max.X - BoundsMin.X) / _cellSize.X), 0, _nx - 1),
              Math.Clamp((int)((max.Y - BoundsMin.Y) / _cellSize.Y), 0, _ny - 1),
              Math.Clamp((int)((max.Z - BoundsMin.Z) / _cellSize.Z), 0, _nz - 1));
    }

    private int CellIndex(int x, int y, int z) => (z * _ny + y) * _nx + x;

    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
