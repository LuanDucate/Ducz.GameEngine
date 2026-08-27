using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Ducz.Rendering;

/// <summary>A single mesh vertex: position, normal, texture coordinates and color.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public Vector4 Color;

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Color = Vector4.One;
    }

    public Vertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4 color)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Color = color;
    }
}

/// <summary>Optional per-vertex skinning data (4 bone influences).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VertexSkin
{
    /// <summary>Bone indices (stored as floats for the GPU).</summary>
    public Vector4 Joints;
    /// <summary>Bone weights, normalized to sum 1.</summary>
    public Vector4 Weights;
}

/// <summary>
/// GPU triangle mesh (vertex + index buffers). Create procedurally, via
/// <see cref="MeshFactory"/> primitives, or by loading models with <see cref="Assets.Model"/>.
/// </summary>
public sealed class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly uint _skinVbo;

    /// <summary>Number of indices.</summary>
    public int IndexCount { get; }

    /// <summary>True when the mesh carries skinning data (bones).</summary>
    public bool IsSkinned { get; }

    /// <summary>Axis-aligned bounding box minimum (local space).</summary>
    public Vector3 BoundsMin { get; }

    /// <summary>Axis-aligned bounding box maximum (local space).</summary>
    public Vector3 BoundsMax { get; }

    /// <summary>Local-space bounding sphere radius (around the bounds center), used for culling.</summary>
    public float BoundsRadius { get; }

    /// <summary>Center of the bounding box (local space).</summary>
    public Vector3 BoundsCenter => (BoundsMin + BoundsMax) * 0.5f;

    /// <summary>CPU copy of positions, kept for physics/collision generation. May be empty if not requested.</summary>
    public Vector3[] CpuPositions { get; }

    /// <summary>CPU copy of the triangle indices (kept together with <see cref="CpuPositions"/>). May be empty if not requested.</summary>
    public uint[] CpuIndices { get; }

    public unsafe Mesh(Vertex[] vertices, uint[] indices, VertexSkin[]? skin = null, bool keepCpuPositions = false)
    {
        if (vertices.Length == 0 || indices.Length == 0)
            throw new ArgumentException("Mesh needs at least one vertex and one index.");
        if (skin != null && skin.Length != vertices.Length)
            throw new ArgumentException("Skin data must match the vertex count.");

        _gl = Engine.Renderer.Device.GL;
        IndexCount = indices.Length;
        IsSkinned = skin != null;

        // Bounds
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }
        BoundsMin = min;
        BoundsMax = max;
        BoundsRadius = (max - BoundsCenter).Length();

        CpuPositions = keepCpuPositions
            ? vertices.Select(v => v.Position).ToArray()
            : Array.Empty<Vector3>();
        CpuIndices = keepCpuPositions ? (uint[])indices.Clone() : Array.Empty<uint>();

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        // Vertex buffer
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (Vertex* ptr = vertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(Vertex)), ptr, BufferUsageARB.StaticDraw);
        }

        uint stride = (uint)sizeof(Vertex);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)12);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)24);
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (void*)32);

        // Optional skin buffer
        if (skin != null)
        {
            _skinVbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _skinVbo);
            fixed (VertexSkin* ptr = skin)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(skin.Length * sizeof(VertexSkin)), ptr, BufferUsageARB.StaticDraw);
            }

            uint skinStride = (uint)sizeof(VertexSkin);
            _gl.EnableVertexAttribArray(4);
            _gl.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, skinStride, (void*)0);
            _gl.EnableVertexAttribArray(5);
            _gl.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, skinStride, (void*)16);
        }

        // Index buffer
        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* ptr = indices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        }

        _gl.BindVertexArray(0);
    }

    /// <summary>Issues the draw call. The correct shader must already be bound.</summary>
    public unsafe void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)IndexCount, DrawElementsType.UnsignedInt, (void*)0);
        _gl.BindVertexArray(0);

        Engine.Renderer.Device.DrawCalls++;
        Engine.Renderer.Device.Triangles += IndexCount / 3;
    }

    /// <summary>Recomputes flat-ish smooth normals from triangle geometry (in place on a vertex array).</summary>
    public static void RecalculateNormals(Vertex[] vertices, uint[] indices)
    {
        for (int i = 0; i < vertices.Length; i++)
            vertices[i].Normal = Vector3.Zero;

        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = (int)indices[i], b = (int)indices[i + 1], c = (int)indices[i + 2];
            var normal = Vector3.Cross(
                vertices[b].Position - vertices[a].Position,
                vertices[c].Position - vertices[a].Position);
            vertices[a].Normal += normal;
            vertices[b].Normal += normal;
            vertices[c].Normal += normal;
        }

        for (int i = 0; i < vertices.Length; i++)
            vertices[i].Normal = Mathf.NormalizeSafe(vertices[i].Normal);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        if (IsSkinned)
            _gl.DeleteBuffer(_skinVbo);
    }
}
