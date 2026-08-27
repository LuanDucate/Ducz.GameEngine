using System.Numerics;

namespace Ducz.Rendering;

/// <summary>
/// Procedural primitive meshes. Every primitive comes in two flavors: <c>Box(...)</c>
/// returns a GPU <see cref="Mesh"/>, <c>BoxData(...)</c> returns the CPU <see cref="MeshData"/>
/// (used by exporters and tools). All primitives are centered at the origin
/// and sized in meters (engine units).
/// </summary>
public static partial class MeshFactory
{
    /// <summary>
    /// A box with the given full size. With <paramref name="worldUv"/> each face is UV-mapped
    /// in meters (a 4 m wide face spans U 0..4) so a texture keeps the same density on every
    /// block regardless of its size - the material's <c>UvScale</c> then means "tiles per meter".
    /// </summary>
    public static Mesh Box(float sizeX = 1f, float sizeY = 1f, float sizeZ = 1f, bool worldUv = false) =>
        BoxData(sizeX, sizeY, sizeZ, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Box"/>.</summary>
    public static MeshData BoxData(float sizeX = 1f, float sizeY = 1f, float sizeZ = 1f, bool worldUv = false)
    {
        float x = sizeX * 0.5f, y = sizeY * 0.5f, z = sizeZ * 0.5f;

        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        void Face(Vector3 normal, Vector3 up, Vector3 right, Vector3 center)
        {
            var uvScale = worldUv ? new Vector2(right.Length() * 2f, up.Length() * 2f) : Vector2.One;
            uint baseIndex = (uint)vertices.Count;
            vertices.Add(new Vertex(center - right - up, normal, new Vector2(0, 1) * uvScale));
            vertices.Add(new Vertex(center + right - up, normal, new Vector2(1, 1) * uvScale));
            vertices.Add(new Vertex(center + right + up, normal, new Vector2(1, 0) * uvScale));
            vertices.Add(new Vertex(center - right + up, normal, new Vector2(0, 0) * uvScale));
            indices.AddRange(new[] { baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3 });
        }

        Face(Vector3.UnitZ, Vector3.UnitY * y, Vector3.UnitX * x, Vector3.UnitZ * z);            // front (+Z)
        Face(-Vector3.UnitZ, Vector3.UnitY * y, -Vector3.UnitX * x, -Vector3.UnitZ * z);         // back (-Z)
        Face(Vector3.UnitX, Vector3.UnitY * y, -Vector3.UnitZ * z, Vector3.UnitX * x);           // right (+X)
        Face(-Vector3.UnitX, Vector3.UnitY * y, Vector3.UnitZ * z, -Vector3.UnitX * x);          // left (-X)
        Face(Vector3.UnitY, -Vector3.UnitZ * z, Vector3.UnitX * x, Vector3.UnitY * y);           // top (+Y)
        Face(-Vector3.UnitY, Vector3.UnitZ * z, Vector3.UnitX * x, -Vector3.UnitY * y);          // bottom (-Y)

        return new MeshData(vertices, indices);
    }

    /// <summary>A cube with equal sides.</summary>
    public static Mesh Cube(float size = 1f, bool worldUv = false) => Box(size, size, size, worldUv);

    /// <summary>CPU geometry of <see cref="Cube"/>.</summary>
    public static MeshData CubeData(float size = 1f, bool worldUv = false) => BoxData(size, size, size, worldUv);

    /// <summary>A UV sphere.</summary>
    public static Mesh Sphere(float radius = 0.5f, int rings = 24, int segments = 32) => SphereData(radius, rings, segments).ToMesh();

    /// <summary>CPU geometry of <see cref="Sphere"/>.</summary>
    public static MeshData SphereData(float radius = 0.5f, int rings = 24, int segments = 32)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        for (int ring = 0; ring <= rings; ring++)
        {
            float v = ring / (float)rings;
            float phi = v * MathF.PI;
            float y = MathF.Cos(phi);
            float r = MathF.Sin(phi);

            for (int seg = 0; seg <= segments; seg++)
            {
                float u = seg / (float)segments;
                float theta = u * Mathf.Tau;
                var normal = new Vector3(r * MathF.Cos(theta), y, r * MathF.Sin(theta));
                vertices.Add(new Vertex(normal * radius, normal, new Vector2(u, v)));
            }
        }

        int stride = segments + 1;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                uint a = (uint)(ring * stride + seg);
                uint b = (uint)((ring + 1) * stride + seg);
                indices.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        }

        return new MeshData(vertices, indices);
    }

    /// <summary>A flat plane on the XZ axis facing up, with optional subdivisions.</summary>
    public static Mesh Plane(float sizeX = 1f, float sizeZ = 1f, int subdivisions = 1, float uvTiling = 1f, bool worldUv = false) =>
        PlaneData(sizeX, sizeZ, subdivisions, uvTiling, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Plane"/>. With <paramref name="worldUv"/> UVs are in meters.</summary>
    public static MeshData PlaneData(float sizeX = 1f, float sizeZ = 1f, int subdivisions = 1, float uvTiling = 1f, bool worldUv = false)
    {
        var uvScale = worldUv ? new Vector2(sizeX, sizeZ) : new Vector2(uvTiling);
        int quads = Math.Max(1, subdivisions);
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        for (int zi = 0; zi <= quads; zi++)
        {
            for (int xi = 0; xi <= quads; xi++)
            {
                float u = xi / (float)quads;
                float v = zi / (float)quads;
                var pos = new Vector3((u - 0.5f) * sizeX, 0f, (v - 0.5f) * sizeZ);
                vertices.Add(new Vertex(pos, Vector3.UnitY, new Vector2(u, v) * uvScale));
            }
        }

        int stride = quads + 1;
        for (int zi = 0; zi < quads; zi++)
        {
            for (int xi = 0; xi < quads; xi++)
            {
                uint a = (uint)(zi * stride + xi);
                uint b = (uint)((zi + 1) * stride + xi);
                indices.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        }

        return new MeshData(vertices, indices);
    }

    /// <summary>A vertical quad facing +Z. Useful for billboards and sprites in 3D.</summary>
    public static Mesh Quad(float width = 1f, float height = 1f) => QuadData(width, height).ToMesh();

    /// <summary>CPU geometry of <see cref="Quad"/>.</summary>
    public static MeshData QuadData(float width = 1f, float height = 1f)
    {
        float x = width * 0.5f, y = height * 0.5f;
        var vertices = new[]
        {
            new Vertex(new Vector3(-x, -y, 0), Vector3.UnitZ, new Vector2(0, 1)),
            new Vertex(new Vector3(x, -y, 0), Vector3.UnitZ, new Vector2(1, 1)),
            new Vertex(new Vector3(x, y, 0), Vector3.UnitZ, new Vector2(1, 0)),
            new Vertex(new Vector3(-x, y, 0), Vector3.UnitZ, new Vector2(0, 0))
        };
        var indices = new uint[] { 0, 1, 2, 0, 2, 3 };
        return new MeshData(vertices, indices);
    }

    /// <summary>A cylinder aligned with the Y axis.</summary>
    public static Mesh Cylinder(float radius = 0.5f, float height = 1f, int segments = 32, bool worldUv = false) =>
        CylinderData(radius, height, segments, worldUv).ToMesh();

    /// <summary>CPU geometry of <see cref="Cylinder"/>. With <paramref name="worldUv"/> the side is UV-mapped in meters.</summary>
    public static MeshData CylinderData(float radius = 0.5f, float height = 1f, int segments = 32, bool worldUv = false)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        float half = height * 0.5f;
        var sideUv = worldUv ? new Vector2(Mathf.Tau * radius, height) : Vector2.One;

        // Side
        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float theta = u * Mathf.Tau;
            var normal = new Vector3(MathF.Cos(theta), 0, MathF.Sin(theta));
            vertices.Add(new Vertex(normal * radius - Vector3.UnitY * half, normal, new Vector2(u, 1) * sideUv));
            vertices.Add(new Vertex(normal * radius + Vector3.UnitY * half, normal, new Vector2(u, 0) * sideUv));
        }
        for (int i = 0; i < segments; i++)
        {
            uint a = (uint)(i * 2);
            indices.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
        }

        // Caps
        float capUv = worldUv ? radius * 2f : 1f;
        AddDisk(vertices, indices, radius, half, Vector3.UnitY, segments, capUv);
        AddDisk(vertices, indices, radius, -half, -Vector3.UnitY, segments, capUv);

        return new MeshData(vertices, indices);
    }

    private static void AddDisk(List<Vertex> vertices, List<uint> indices, float radius, float y, Vector3 normal, int segments, float uvScale = 1f)
    {
        uint center = (uint)vertices.Count;
        vertices.Add(new Vertex(new Vector3(0, y, 0), normal, new Vector2(0.5f, 0.5f) * uvScale));
        for (int i = 0; i <= segments; i++)
        {
            float theta = i / (float)segments * Mathf.Tau;
            var dir = new Vector3(MathF.Cos(theta), 0, MathF.Sin(theta));
            vertices.Add(new Vertex(dir * radius + Vector3.UnitY * y, normal,
                new Vector2(0.5f + dir.X * 0.5f, 0.5f + dir.Z * 0.5f) * uvScale));
        }
        for (int i = 0; i < segments; i++)
        {
            uint a = center + 1 + (uint)i;
            if (normal.Y > 0)
                indices.AddRange(new[] { center, a + 1, a });
            else
                indices.AddRange(new[] { center, a, a + 1 });
        }
    }

    /// <summary>A capsule aligned with the Y axis. Total height includes both hemisphere caps.</summary>
    public static Mesh Capsule(float radius = 0.35f, float height = 1.8f, int rings = 12, int segments = 24) => CapsuleData(radius, height, rings, segments).ToMesh();

    /// <summary>CPU geometry of <see cref="Capsule"/>.</summary>
    public static MeshData CapsuleData(float radius = 0.35f, float height = 1.8f, int rings = 12, int segments = 24)
    {
        float cylinderHalf = MathF.Max(0f, height * 0.5f - radius);
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        // Build like a sphere but offset the two halves apart.
        int totalRings = rings * 2 + 1;
        for (int ring = 0; ring <= totalRings; ring++)
        {
            float v = ring / (float)totalRings;
            float phi = v * MathF.PI;
            float y = MathF.Cos(phi);
            float r = MathF.Sin(phi);
            float yOffset = ring <= totalRings / 2 ? cylinderHalf : -cylinderHalf;

            for (int seg = 0; seg <= segments; seg++)
            {
                float u = seg / (float)segments;
                float theta = u * Mathf.Tau;
                var normal = new Vector3(r * MathF.Cos(theta), y, r * MathF.Sin(theta));
                var pos = normal * radius + Vector3.UnitY * yOffset;
                vertices.Add(new Vertex(pos, normal, new Vector2(u, v)));
            }
        }

        int stride = segments + 1;
        for (int ring = 0; ring < totalRings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                uint a = (uint)(ring * stride + seg);
                uint b = (uint)((ring + 1) * stride + seg);
                indices.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        }

        return new MeshData(vertices, indices);
    }

    /// <summary>A cone pointing up (+Y), base on the bottom.</summary>
    public static Mesh Cone(float radius = 0.5f, float height = 1f, int segments = 32) => ConeData(radius, height, segments).ToMesh();

    /// <summary>CPU geometry of <see cref="Cone"/>.</summary>
    public static MeshData ConeData(float radius = 0.5f, float height = 1f, int segments = 32)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        float half = height * 0.5f;
        float slopeY = radius / MathF.Sqrt(radius * radius + height * height);
        float slopeR = height / MathF.Sqrt(radius * radius + height * height);

        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float theta = u * Mathf.Tau;
            var dir = new Vector3(MathF.Cos(theta), 0, MathF.Sin(theta));
            var normal = Vector3.Normalize(dir * slopeR + Vector3.UnitY * slopeY);
            vertices.Add(new Vertex(dir * radius - Vector3.UnitY * half, normal, new Vector2(u, 1)));
            vertices.Add(new Vertex(Vector3.UnitY * half, normal, new Vector2(u, 0)));
        }
        for (int i = 0; i < segments; i++)
        {
            uint a = (uint)(i * 2);
            indices.AddRange(new[] { a, a + 1, a + 2 });
        }

        AddDisk(vertices, indices, radius, -half, -Vector3.UnitY, segments);
        return new MeshData(vertices, indices);
    }

    /// <summary>A torus (donut) in the XZ plane.</summary>
    public static Mesh Torus(float radius = 0.5f, float thickness = 0.15f, int segments = 32, int sides = 16) => TorusData(radius, thickness, segments, sides).ToMesh();

    /// <summary>CPU geometry of <see cref="Torus"/>.</summary>
    public static MeshData TorusData(float radius = 0.5f, float thickness = 0.15f, int segments = 32, int sides = 16)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float theta = u * Mathf.Tau;
            var center = new Vector3(MathF.Cos(theta), 0, MathF.Sin(theta)) * radius;
            var ringDir = Vector3.Normalize(center);

            for (int j = 0; j <= sides; j++)
            {
                float v = j / (float)sides;
                float phi = v * Mathf.Tau;
                var normal = ringDir * MathF.Cos(phi) + Vector3.UnitY * MathF.Sin(phi);
                vertices.Add(new Vertex(center + normal * thickness, normal, new Vector2(u * 4f, v)));
            }
        }

        int stride = sides + 1;
        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                uint a = (uint)(i * stride + j);
                uint b = (uint)((i + 1) * stride + j);
                indices.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        }

        return new MeshData(vertices, indices);
    }
}
