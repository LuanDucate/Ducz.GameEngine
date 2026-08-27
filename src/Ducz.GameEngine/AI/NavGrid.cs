using System.Numerics;

namespace Ducz.AI;

/// <summary>
/// A 2D navigation grid over the XZ plane with A* pathfinding.
/// Mark cells blocked manually or bake from physics colliders.
///
/// <code>
/// var nav = new NavGrid(new Vector3(-25, 0, -25), 50, 50, cellSize: 1f);
/// nav.BakeFromPhysics(agentRadius: 0.4f);
/// var path = nav.FindPath(enemy.GlobalPosition, player.GlobalPosition);
/// </code>
/// </summary>
public sealed class NavGrid
{
    private readonly bool[,] _walkable;

    /// <summary>World position of the grid's (0,0) cell corner.</summary>
    public Vector3 Origin { get; }

    /// <summary>Number of cells along X.</summary>
    public int Width { get; }

    /// <summary>Number of cells along Z.</summary>
    public int Depth { get; }

    /// <summary>Size of each square cell in world units.</summary>
    public float CellSize { get; }

    public NavGrid(Vector3 origin, int width, int depth, float cellSize = 1f)
    {
        Origin = origin;
        Width = width;
        Depth = depth;
        CellSize = cellSize;
        _walkable = new bool[width, depth];
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                _walkable[x, z] = true;
    }

    /// <summary>Marks a cell walkable or blocked.</summary>
    public void SetWalkable(int x, int z, bool walkable)
    {
        if (InBounds(x, z))
            _walkable[x, z] = walkable;
    }

    public bool IsWalkable(int x, int z) => InBounds(x, z) && _walkable[x, z];

    /// <summary>True when the world position falls on a walkable cell.</summary>
    public bool IsWalkable(Vector3 worldPosition)
    {
        var (x, z) = WorldToCell(worldPosition);
        return IsWalkable(x, z);
    }

    private bool InBounds(int x, int z) => x >= 0 && x < Width && z >= 0 && z < Depth;

    /// <summary>Converts a world position to cell coordinates.</summary>
    public (int X, int Z) WorldToCell(Vector3 world) => (
        (int)MathF.Floor((world.X - Origin.X) / CellSize),
        (int)MathF.Floor((world.Z - Origin.Z) / CellSize));

    /// <summary>Center of a cell in world space (Y = origin Y).</summary>
    public Vector3 CellToWorld(int x, int z) => new(
        Origin.X + (x + 0.5f) * CellSize,
        Origin.Y,
        Origin.Z + (z + 0.5f) * CellSize);

    /// <summary>
    /// Blocks every cell that overlaps a physics collider (except heightfields).
    /// Call after building the level. <paramref name="agentRadius"/> inflates obstacles.
    /// </summary>
    public void BakeFromPhysics(float agentRadius = 0.4f, uint mask = uint.MaxValue, float probeHeight = 1f)
    {
        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Depth; z++)
            {
                var center = CellToWorld(x, z) + new Vector3(0f, probeHeight * 0.5f, 0f);
                var hits = Engine.Physics.OverlapSphere(center, CellSize * 0.5f + agentRadius, mask);
                bool blocked = hits.Any(body =>
                    body.Shape is not Physics.HeightfieldShape &&
                    body is Physics.StaticBody3D or Physics.RigidBody3D);
                _walkable[x, z] = !blocked;
            }
        }
    }

    /// <summary>
    /// A* pathfinding between two world positions. Returns waypoints (cell centers,
    /// start cell excluded) or an empty list when no path exists.
    /// </summary>
    public List<Vector3> FindPath(Vector3 fromWorld, Vector3 toWorld)
    {
        var start = WorldToCell(fromWorld);
        var goal = WorldToCell(toWorld);
        var result = new List<Vector3>();

        if (!InBounds(start.X, start.Z) || !InBounds(goal.X, goal.Z) || !_walkable[goal.X, goal.Z])
            return result;

        var open = new PriorityQueue<(int X, int Z), float>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var costSoFar = new Dictionary<(int, int), float> { [(start.X, start.Z)] = 0f };

        open.Enqueue(start, 0f);

        Span<(int dx, int dz, float cost)> neighbors = stackalloc (int, int, float)[]
        {
            (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
            (1, 1, 1.414f), (1, -1, 1.414f), (-1, 1, 1.414f), (-1, -1, 1.414f)
        };

        bool found = false;
        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current == goal)
            {
                found = true;
                break;
            }

            foreach (var (dx, dz, cost) in neighbors)
            {
                int nx = current.X + dx, nz = current.Z + dz;
                if (!IsWalkable(nx, nz))
                    continue;
                // Prevent cutting corners diagonally through blocked cells.
                if (dx != 0 && dz != 0 && (!IsWalkable(current.X + dx, current.Z) || !IsWalkable(current.X, current.Z + dz)))
                    continue;

                float newCost = costSoFar[(current.X, current.Z)] + cost;
                if (!costSoFar.TryGetValue((nx, nz), out float existing) || newCost < existing)
                {
                    costSoFar[(nx, nz)] = newCost;
                    float heuristic = MathF.Sqrt((goal.X - nx) * (goal.X - nx) + (goal.Z - nz) * (goal.Z - nz));
                    open.Enqueue((nx, nz), newCost + heuristic);
                    cameFrom[(nx, nz)] = (current.X, current.Z);
                }
            }
        }

        if (!found)
            return result;

        // Reconstruct
        var cell = (goal.X, goal.Z);
        while (cell != (start.X, start.Z))
        {
            result.Add(CellToWorld(cell.Item1, cell.Item2));
            cell = cameFrom[cell];
        }
        result.Reverse();
        return result;
    }

    /// <summary>Draws walkable/blocked cells with <see cref="Rendering.DebugDraw"/> (debugging aid).</summary>
    public void DebugDrawGrid()
    {
        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Depth; z++)
            {
                var center = CellToWorld(x, z);
                var color = _walkable[x, z] ? Color.Green.WithAlpha(0.25f) : Color.Red.WithAlpha(0.6f);
                float h = CellSize * 0.45f;
                Rendering.DebugDraw.Line(center + new Vector3(-h, 0.02f, -h), center + new Vector3(h, 0.02f, h), color);
                Rendering.DebugDraw.Line(center + new Vector3(-h, 0.02f, h), center + new Vector3(h, 0.02f, -h), color);
            }
        }
    }
}
