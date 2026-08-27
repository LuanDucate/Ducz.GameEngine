using System.Numerics;
using Ducz.Physics;
using Ducz.Rendering;
using StbImageSharp;

namespace Ducz;

/// <summary>
/// A heightmap terrain: renders a mesh and registers a physics heightfield so
/// characters and rigid bodies walk on it.
///
/// <code>
/// // Procedural rolling hills
/// var terrain = AddChild(Terrain.FromFunction(
///     (x, z) => MathF.Sin(x * 0.1f) * MathF.Cos(z * 0.1f) * 3f,
///     sizeX: 200, sizeZ: 200, resolution: 128));
///
/// // Or from a grayscale image
/// var terrain2 = AddChild(Terrain.FromHeightmap("heightmap.png", 200, 200, maxHeight: 25));
/// </code>
/// The terrain is centered on its node position (build it at the origin for simplicity).
/// </summary>
public class Terrain : StaticBody3D
{
    private Func<float, float, float> _heightFunction = (_, _) => 0f;

    /// <summary>Total world size along X.</summary>
    public float SizeX { get; private set; }

    /// <summary>Total world size along Z.</summary>
    public float SizeZ { get; private set; }

    /// <summary>The rendered mesh instance (assign <see cref="Rendering.Material"/> via <see cref="Material"/>).</summary>
    public MeshInstance3D MeshInstance { get; }

    /// <summary>Terrain material (defaults to a green-ish lit material with vertex-color slopes).</summary>
    public Material Material
    {
        get => MeshInstance.Material;
        set => MeshInstance.Material = value;
    }

    private Terrain(string? name) : base(name)
    {
        MeshInstance = new MeshInstance3D("TerrainMesh");
        AddChild(MeshInstance);
    }

    /// <summary>Creates a terrain from a height function (world x, z -> height).</summary>
    public static Terrain FromFunction(Func<float, float, float> height, float sizeX, float sizeZ,
        int resolution = 128, string? name = null)
    {
        var terrain = new Terrain(name);
        terrain.Build(height, sizeX, sizeZ, resolution);
        return terrain;
    }

    /// <summary>Creates a completely flat terrain (still useful for the collider + material tiling).</summary>
    public static Terrain Flat(float sizeX, float sizeZ, string? name = null) =>
        FromFunction((_, _) => 0f, sizeX, sizeZ, 2, name);

    /// <summary>
    /// Creates a terrain from a grayscale heightmap image. Black = 0, white = <paramref name="maxHeight"/>.
    /// </summary>
    public static Terrain FromHeightmap(string imagePath, float sizeX, float sizeZ, float maxHeight,
        int resolution = 128, string? name = null)
    {
        var terrain = new Terrain(name);
        terrain.Build(HeightmapSampler(imagePath, sizeX, sizeZ, maxHeight), sizeX, sizeZ, resolution);
        return terrain;
    }

    /// <summary>
    /// Builds a height function from a grayscale image (black = 0, white = <paramref name="maxHeight"/>),
    /// bilinear-filtered, mapped over a centered sizeX x sizeZ area. Shared by the terrain
    /// node and exporters.
    /// </summary>
    public static Func<float, float, float> HeightmapSampler(string imagePath, float sizeX, float sizeZ, float maxHeight)
    {
        var image = ImageResult.FromMemory(Assets.ReadBytes(imagePath), ColorComponents.Grey);

        float Sample(float x, float z)
        {
            // Map world coords (centered) to image pixels.
            float u = Mathf.Clamp01((x + sizeX * 0.5f) / sizeX);
            float v = Mathf.Clamp01((z + sizeZ * 0.5f) / sizeZ);
            float px = u * (image.Width - 1);
            float py = v * (image.Height - 1);

            int x0 = (int)px, y0 = (int)py;
            int x1 = Math.Min(x0 + 1, image.Width - 1);
            int y1 = Math.Min(y0 + 1, image.Height - 1);
            float fx = px - x0, fy = py - y0;

            float h00 = image.Data[y0 * image.Width + x0] / 255f;
            float h10 = image.Data[y0 * image.Width + x1] / 255f;
            float h01 = image.Data[y1 * image.Width + x0] / 255f;
            float h11 = image.Data[y1 * image.Width + x1] / 255f;

            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fy) * maxHeight;
        }

        return Sample;
    }

    /// <summary>The procedural "rolling hills" height function used by JSON terrains (mode "hills").</summary>
    public static Func<float, float, float> HillsFunction(float amplitude, float frequency) =>
        (x, z) => MathF.Sin(x * frequency) * MathF.Cos(z * frequency * 0.85f) * amplitude
                  + MathF.Sin(x * frequency * 0.31f + 1.7f) * amplitude * 0.5f;

    /// <summary>Terrain height at world coordinates (useful for placing objects).</summary>
    public float GetHeight(float worldX, float worldZ) => _heightFunction(worldX, worldZ);

    /// <summary>Surface normal at world coordinates.</summary>
    public Vector3 GetNormal(float worldX, float worldZ) =>
        ((HeightfieldShape)Shape!).GetNormal(worldX, worldZ);

    private void Build(Func<float, float, float> height, float sizeX, float sizeZ, int resolution)
    {
        _heightFunction = height;
        SizeX = sizeX;
        SizeZ = sizeZ;

        var data = BuildMeshData(height, sizeX, sizeZ, resolution, out float minHeight, out float maxHeight);

        MeshInstance.Surfaces.Clear();
        MeshInstance.Surfaces.Add(new Surface(data.ToMesh(), new Material
        {
            Albedo = Color.White,
            SpecularStrength = 0.05f,
            Shininess = 8f
        }));

        Shape = new HeightfieldShape
        {
            GetHeight = (x, z) => _heightFunction(x, z),
            BoundsX = new Vector2(-sizeX * 0.5f, sizeX * 0.5f),
            BoundsZ = new Vector2(-sizeZ * 0.5f, sizeZ * 0.5f),
            MinHeight = minHeight - 1f,
            MaxHeight = maxHeight + 1f
        };
    }

    /// <summary>
    /// Generates the terrain geometry on the CPU (positions, smooth normals, tiled UVs and
    /// slope/height vertex colors). Used by the node itself and by exporters.
    /// </summary>
    public static MeshData BuildMeshData(Func<float, float, float> height, float sizeX, float sizeZ, int resolution) =>
        BuildMeshData(height, sizeX, sizeZ, resolution, out _, out _);

    private static MeshData BuildMeshData(Func<float, float, float> height, float sizeX, float sizeZ, int resolution,
        out float minHeight, out float maxHeight)
    {
        int quads = Math.Max(1, resolution);
        var vertices = new Vertex[(quads + 1) * (quads + 1)];
        minHeight = float.MaxValue;
        maxHeight = float.MinValue;

        for (int zi = 0; zi <= quads; zi++)
        {
            for (int xi = 0; xi <= quads; xi++)
            {
                float u = xi / (float)quads;
                float v = zi / (float)quads;
                float x = (u - 0.5f) * sizeX;
                float z = (v - 0.5f) * sizeZ;
                float y = height(x, z);
                minHeight = MathF.Min(minHeight, y);
                maxHeight = MathF.Max(maxHeight, y);

                vertices[zi * (quads + 1) + xi] = new Vertex(
                    new Vector3(x, y, z),
                    Vector3.UnitY,
                    new Vector2(u, v) * (sizeX / 8f)); // tile every 8 units
            }
        }

        var indices = new uint[quads * quads * 6];
        int index = 0;
        int stride = quads + 1;
        for (int zi = 0; zi < quads; zi++)
        {
            for (int xi = 0; xi < quads; xi++)
            {
                uint a = (uint)(zi * stride + xi);
                uint b = (uint)((zi + 1) * stride + xi);
                indices[index++] = a;
                indices[index++] = b;
                indices[index++] = a + 1;
                indices[index++] = a + 1;
                indices[index++] = b;
                indices[index++] = b + 1;
            }
        }

        Mesh.RecalculateNormals(vertices, indices);

        // Vertex color by slope + height for a pleasant default look.
        for (int i = 0; i < vertices.Length; i++)
        {
            float slope = 1f - vertices[i].Normal.Y;
            float heightT = maxHeight > minHeight
                ? (vertices[i].Position.Y - minHeight) / (maxHeight - minHeight)
                : 0f;
            var grass = new Vector4(0.45f, 0.65f, 0.3f, 1f);
            var rock = new Vector4(0.5f, 0.45f, 0.42f, 1f);
            var snow = new Vector4(0.95f, 0.95f, 0.97f, 1f);
            var color = Vector4.Lerp(grass, rock, Mathf.Clamp01(slope * 3f));
            color = Vector4.Lerp(color, snow, Mathf.SmoothStep(0.75f, 0.95f, heightT));
            vertices[i].Color = color;
        }

        return new MeshData(vertices, indices);
    }
}
