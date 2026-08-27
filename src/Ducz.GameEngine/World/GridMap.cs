using System.Numerics;
using Ducz.Physics;
using Ducz.Rendering;

namespace Ducz;

/// <summary>
/// Builds block-based maps from a small library of reusable pieces - a code-first
/// take on tile/grid mapping. Each cell renders its item's mesh; solid cells also
/// get a box collider.
///
/// <code>
/// var map = AddChild(new GridMap { CellSize = 2f });
/// map.RegisterItem(0, MeshFactory.Cube(2f), stoneMaterial);
/// for (int x = 0; x &lt; 10; x++)
///     map.SetCell(x, 0, 0, 0);       // a wall
/// map.Rebuild();
/// </code>
/// </summary>
public class GridMap : Node3D
{
    private sealed record Item(Mesh Mesh, Material Material, bool Solid);

    private readonly Dictionary<int, Item> _library = new();
    private readonly Dictionary<(int X, int Y, int Z), int> _cells = new();
    private readonly List<Node> _generated = new();
    private bool _dirty;

    /// <summary>Edge length of each cubic cell.</summary>
    public float CellSize { get; set; } = 1f;

    public GridMap(string? name = null) : base(name) { }

    /// <summary>Registers a mesh+material under an item id. Solid items get box colliders.</summary>
    public void RegisterItem(int id, Mesh mesh, Material? material = null, bool solid = true) =>
        _library[id] = new Item(mesh, material ?? new Material(), solid);

    /// <summary>Places an item at a grid cell (overwrites). Call <see cref="Rebuild"/> when done.</summary>
    public void SetCell(int x, int y, int z, int itemId)
    {
        _cells[(x, y, z)] = itemId;
        _dirty = true;
    }

    /// <summary>Removes the item at a cell.</summary>
    public void ClearCell(int x, int y, int z)
    {
        if (_cells.Remove((x, y, z)))
            _dirty = true;
    }

    /// <summary>Returns the item id at a cell, or -1.</summary>
    public int GetCell(int x, int y, int z) => _cells.GetValueOrDefault((x, y, z), -1);

    /// <summary>Fills a rectangular region (inclusive) with an item - handy for floors and walls.</summary>
    public void FillRegion(int x0, int y0, int z0, int x1, int y1, int z1, int itemId)
    {
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
                    SetCell(x, y, z, itemId);
    }

    /// <summary>World-space center of a cell.</summary>
    public Vector3 CellToWorld(int x, int y, int z) =>
        GlobalTransform.TransformPoint(new Vector3((x + 0.5f) * CellSize, (y + 0.5f) * CellSize, (z + 0.5f) * CellSize));

    /// <summary>Converts a world position to cell coordinates.</summary>
    public (int X, int Y, int Z) WorldToCell(Vector3 world)
    {
        Matrix4x4.Invert(GlobalTransform, out var inv);
        var local = inv.TransformPoint(world);
        return ((int)MathF.Floor(local.X / CellSize),
                (int)MathF.Floor(local.Y / CellSize),
                (int)MathF.Floor(local.Z / CellSize));
    }

    protected override void OnUpdate(float dt)
    {
        if (_dirty)
            Rebuild();
    }

    /// <summary>Regenerates merged meshes and colliders from the current cells.</summary>
    public void Rebuild()
    {
        _dirty = false;

        foreach (var node in _generated)
            node.RemoveFromParent();
        _generated.Clear();

        // Group by item id.
        foreach (var group in _cells.GroupBy(c => c.Value))
        {
            if (!_library.TryGetValue(group.Key, out var item))
            {
                Log.Warning($"GridMap: item id {group.Key} not registered.");
                continue;
            }

            // One instance per cell (meshes and materials are shared, so this stays cheap).
            foreach (var (cell, _) in group)
            {
                var instance = new MeshInstance3D(item.Mesh, item.Material)
                {
                    Position = new Vector3((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize, (cell.Z + 0.5f) * CellSize)
                };
                _generated.Add(AddChild(instance));
            }

            // Colliders.
            if (item.Solid)
            {
                foreach (var (cell, _) in group)
                {
                    var body = new StaticBody3D(new BoxShape(new Vector3(CellSize * 0.5f)), $"Cell_{cell.X}_{cell.Y}_{cell.Z}")
                    {
                        Position = new Vector3((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize, (cell.Z + 0.5f) * CellSize)
                    };
                    _generated.Add(AddChild(body));
                }
            }
        }
    }
}

/// <summary>
/// One-line builders for the most common level pieces: floors, walls, boxes and ramps.
/// Every helper returns a <see cref="StaticBody3D"/> with the mesh already attached -
/// just AddChild and position it.
///
/// <code>
/// AddChild(Prefabs.Floor(40, 40, checkerMaterial));
/// AddChild(Prefabs.Box(new Vector3(2, 1, 2), crateMaterial)).Position = new Vector3(4, 0.5f, 0);
/// </code>
/// </summary>
public static class Prefabs
{
    /// <summary>A flat floor centered at the origin (top surface at Y = 0).</summary>
    public static StaticBody3D Floor(float sizeX, float sizeZ, Material? material = null, float thickness = 0.5f, bool worldUv = false)
    {
        var body = new StaticBody3D(new BoxShape(new Vector3(sizeX * 0.5f, thickness * 0.5f, sizeZ * 0.5f)), "Floor")
        {
            Position = new Vector3(0f, -thickness * 0.5f, 0f)
        };
        body.AddChild(new MeshInstance3D(MeshFactory.Box(sizeX, thickness, sizeZ, worldUv), material));
        return body;
    }

    /// <summary>A solid box (mesh + collider), centered on the node.</summary>
    public static StaticBody3D Box(Vector3 size, Material? material = null, bool worldUv = false)
    {
        var body = new StaticBody3D(BoxShape.FromSize(size), "Box");
        body.AddChild(new MeshInstance3D(MeshFactory.Box(size.X, size.Y, size.Z, worldUv), material));
        return body;
    }

    /// <summary>
    /// A wall segment: length along X, thickness along Z, centered on the node
    /// (position it at half its height, e.g. <c>wall.Position = new Vector3(0, 2, -10)</c>
    /// for a 4-unit-tall wall standing on the ground). Rotate the node to orient it.
    /// </summary>
    public static StaticBody3D Wall(float length, float height, Material? material = null, float thickness = 0.3f, bool worldUv = false)
    {
        var body = new StaticBody3D(new BoxShape(new Vector3(length * 0.5f, height * 0.5f, thickness * 0.5f)), "Wall");
        body.AddChild(new MeshInstance3D(MeshFactory.Box(length, height, thickness, worldUv), material));
        return body;
    }

    /// <summary>A walkable ramp centered on the node: low end at -length/2 (Z), high end at +length/2 - it rises toward +Z. Rotate the node to aim it.</summary>
    public static StaticBody3D Ramp(float width, float height, float length, Material? material = null, bool worldUv = false)
    {
        float angle = MathF.Atan2(height, length);
        float slopeLength = MathF.Sqrt(height * height + length * length);
        const float thickness = 0.3f;

        var body = new StaticBody3D(new BoxShape(new Vector3(width * 0.5f, thickness * 0.5f, slopeLength * 0.5f)), "Ramp")
        {
            Position = new Vector3(0f, height * 0.5f, 0f),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -angle)
        };
        body.AddChild(new MeshInstance3D(MeshFactory.Box(width, thickness, slopeLength, worldUv), material));
        return body;
    }

    /// <summary>A dynamic crate (RigidBody3D with box mesh + collider).</summary>
    public static RigidBody3D Crate(float size = 1f, Material? material = null, float mass = 2f, bool worldUv = false)
    {
        var body = new RigidBody3D(new BoxShape(new Vector3(size * 0.5f)), "Crate") { Mass = mass };
        body.AddChild(new MeshInstance3D(MeshFactory.Cube(size, worldUv), material));
        return body;
    }

    /// <summary>A collectible trigger: a spinning mesh inside an <see cref="Area3D"/>.</summary>
    public static Area3D Pickup(Mesh mesh, Material? material = null, float triggerRadius = 0.8f)
    {
        var area = new Area3D(new SphereShape(triggerRadius), "Pickup");
        var visual = area.AddChild(new MeshInstance3D(mesh, material, "PickupVisual"));
        area.AddChild(new Spinner(visual));
        return area;
    }

    private sealed class Spinner : Node
    {
        private readonly Node3D _target;
        public Spinner(Node3D target) => _target = target;

        protected override void OnUpdate(float dt) => _target.RotateY(2.5f * dt);
    }
}
