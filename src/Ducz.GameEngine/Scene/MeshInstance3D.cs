using Ducz.Rendering;

namespace Ducz;

/// <summary>One drawable piece of a <see cref="MeshInstance3D"/>: a mesh plus its material.</summary>
public sealed class Surface
{
    public Mesh Mesh { get; set; }
    public Material Material { get; set; }

    public Surface(Mesh mesh, Material? material = null)
    {
        Mesh = mesh;
        Material = material ?? new Material();
    }
}

/// <summary>
/// A node that renders one or more mesh surfaces at its transform.
///
/// <code>
/// var box = AddChild(new MeshInstance3D(MeshFactory.Cube(), Material.FromColor(Color.Red)));
/// box.Position = new Vector3(0, 0.5f, 0);
/// </code>
/// </summary>
public class MeshInstance3D : Node3D
{
    /// <summary>The surfaces (mesh + material pairs) rendered by this node.</summary>
    public List<Surface> Surfaces { get; } = new();

    /// <summary>Convenience accessor for the first surface's mesh.</summary>
    public Mesh? Mesh
    {
        get => Surfaces.Count > 0 ? Surfaces[0].Mesh : null;
        set
        {
            if (value == null)
            {
                Surfaces.Clear();
                return;
            }
            if (Surfaces.Count == 0)
                Surfaces.Add(new Surface(value));
            else
                Surfaces[0].Mesh = value;
        }
    }

    /// <summary>Convenience accessor for the first surface's material.</summary>
    public Material Material
    {
        get
        {
            if (Surfaces.Count == 0)
                throw new InvalidOperationException("MeshInstance3D has no surfaces yet. Assign a Mesh first.");
            return Surfaces[0].Material;
        }
        set
        {
            if (Surfaces.Count == 0)
                throw new InvalidOperationException("MeshInstance3D has no surfaces yet. Assign a Mesh first.");
            Surfaces[0].Material = value;
        }
    }

    /// <summary>
    /// Binds this mesh to a skeleton when it is skinned. Set automatically when
    /// instantiating an animated <see cref="Model"/>.
    /// </summary>
    public SkinBinding? Skin { get; set; }

    /// <summary>Disable to skip frustum culling for this instance (e.g. heavily displaced vertices).</summary>
    public bool FrustumCullingEnabled { get; set; } = true;

    public MeshInstance3D(string? name = null) : base(name) { }

    public MeshInstance3D(Mesh mesh, Material? material = null, string? name = null) : base(name)
    {
        Surfaces.Add(new Surface(mesh, material));
    }

    /// <summary>Adds an extra surface and returns this node (fluent).</summary>
    public MeshInstance3D AddSurface(Mesh mesh, Material? material = null)
    {
        Surfaces.Add(new Surface(mesh, material));
        return this;
    }
}
