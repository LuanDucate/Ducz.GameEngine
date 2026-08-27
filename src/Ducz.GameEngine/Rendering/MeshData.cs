using System.Numerics;

namespace Ducz.Rendering;

/// <summary>
/// CPU-side triangle geometry: vertices + indices. <see cref="MeshFactory"/> builds
/// these, <see cref="Mesh"/> uploads them to the GPU, and exporters (GLB) read them
/// back without ever touching OpenGL.
/// </summary>
public sealed class MeshData
{
    public Vertex[] Vertices { get; }
    public uint[] Indices { get; }

    public int TriangleCount => Indices.Length / 3;

    public MeshData(Vertex[] vertices, uint[] indices)
    {
        Vertices = vertices;
        Indices = indices;
    }

    public MeshData(List<Vertex> vertices, List<uint> indices)
        : this(vertices.ToArray(), indices.ToArray()) { }

    /// <summary>Uploads the geometry to the GPU. Must be called after the engine window is open.</summary>
    public Mesh ToMesh(bool keepCpuPositions = false) => new(Vertices, Indices, null, keepCpuPositions);

    /// <summary>Axis-aligned bounds of the vertex positions.</summary>
    public (Vector3 Min, Vector3 Max) ComputeBounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in Vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }
        return (min, max);
    }

    /// <summary>Returns a copy with positions and normals transformed by a matrix.</summary>
    public MeshData Transformed(Matrix4x4 matrix)
    {
        var vertices = new Vertex[Vertices.Length];
        Matrix4x4.Invert(matrix, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = Vertices[i];
            v.Position = Vector3.Transform(v.Position, matrix);
            v.Normal = Mathf.NormalizeSafe(Vector3.TransformNormal(v.Normal, normalMatrix));
            vertices[i] = v;
        }
        var indices = (uint[])Indices.Clone();
        // A mirroring transform flips winding; keep faces front-facing.
        if (matrix.GetDeterminant() < 0f)
            for (int i = 0; i < indices.Length; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        return new MeshData(vertices, indices);
    }

    /// <summary>Concatenates several geometries into one (indices are re-based).</summary>
    public static MeshData Merge(IEnumerable<MeshData> parts)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        foreach (var part in parts)
        {
            uint baseIndex = (uint)vertices.Count;
            vertices.AddRange(part.Vertices);
            foreach (var index in part.Indices)
                indices.Add(baseIndex + index);
        }
        return new MeshData(vertices, indices);
    }
}
