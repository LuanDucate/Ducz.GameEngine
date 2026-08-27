using System.Numerics;
using Ducz.Physics;
using Ducz.Rendering;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// The reachability overlay (key <b>F</b>). Flood-fills the walkable surfaces of the map from
/// the spawn using the real colliders and the player's real limits (climbs ~0.4 m, survives a
/// ~4 m drop), then paints reachable floor GREEN and floored-but-isolated spots RED. It is the
/// answer to "do my ramps, stairs and upper floors actually connect?" - the single check that
/// turns a flat plate into a map with real paths and elevation, without leaving the editor.
/// </summary>
partial class EditorScene
{
    private const float ReachCell = 1.6f;      // sample spacing
    private const float ReachStepUp = 0.42f;   // the controller climbs ~0.35 m
    private const float ReachDrop = 4.0f;      // a drop bigger than this is a fall, not a path
    private const float ReachHeadroom = 1.7f;
    private const float ReachTop = 80f;        // cast rays down from here

    private bool _reachOn;
    private readonly List<Vector3> _reachGreen = new();
    private readonly List<Vector3> _reachRed = new();

    private void ToggleReachability()
    {
        if (_reachOn)
        {
            _reachOn = false;
            _reachGreen.Clear();
            _reachRed.Clear();
            SetStatus("Reachability overlay off");
            return;
        }
        ComputeReachability();
        _reachOn = true;
    }

    /// <summary>Draws the cached overlay each frame while it is on.</summary>
    private void DrawReachability()
    {
        if (!_reachOn)
            return;
        var green = Color.FromHex("#4dff7a");
        var red = Color.FromHex("#ff3838");
        // A short upright post at each reachable cell reads clearly from any angle; the flat
        // cross alone washed out on bright floors.
        foreach (var p in _reachGreen)
        {
            DebugDraw.Line(p, p + new Vector3(0, 0.7f, 0), green);
            DebugDraw.Line(p - new Vector3(0.3f, 0, 0), p + new Vector3(0.3f, 0, 0), green);
            DebugDraw.Line(p - new Vector3(0, 0, 0.3f), p + new Vector3(0, 0, 0.3f), green);
        }
        foreach (var p in _reachRed)
        {
            DebugDraw.Aabb(p - new Vector3(0.75f, 0f, 0.75f), p + new Vector3(0.75f, 0.6f, 0.75f), red);
            DebugDraw.Line(p, p + new Vector3(0, 4f, 0), red);   // a tall red beacon over isolated spots
        }
    }

    private void ComputeReachability()
    {
        _reachGreen.Clear();
        _reachRed.Clear();

        var spawn = _doc.Nodes.FirstOrDefault(n => n.Type.Equals("spawn", StringComparison.OrdinalIgnoreCase));
        if (spawn?.Position is not { Length: >= 3 } sp)
        {
            SetStatus("Add a spawn point first, then press F to check reachability.");
            return;
        }

        // Map bounds from every placed object.
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var node in _defByNode.Keys)
            if (node.IsInsideTree && node.ComputeVisualBounds() is { } b)
            {
                var (bmin, bmax) = WorldBounds(node, b);
                min = Vector3.Min(min, bmin);
                max = Vector3.Max(max, bmax);
            }
        if (min.X > max.X)
        {
            SetStatus("Nothing to check.");
            return;
        }
        min -= new Vector3(2f, 0f, 2f);
        max += new Vector3(2f, 0f, 2f);

        int nx = (int)MathF.Ceiling((max.X - min.X) / ReachCell);
        int nz = (int)MathF.Ceiling((max.Z - min.Z) / ReachCell);
        if ((long)nx * nz > 40000)   // keep it snappy on huge maps
        {
            SetStatus($"Map too large for the overlay ({nx}x{nz} cells).");
            return;
        }

        // Surfaces per cell: the walkable tops in that column (a corridor floor and the roof
        // above it are two separate nodes). Stored as index -> list of heights.
        var surfaces = new List<float>[nx, nz];
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                surfaces[ix, iz] = ColumnSurfaces(min.X + (ix + 0.5f) * ReachCell,
                                                  min.Z + (iz + 0.5f) * ReachCell);

        // BFS from the spawn's cell/surface.
        int sx = Math.Clamp((int)((sp[0] - min.X) / ReachCell), 0, nx - 1);
        int sz = Math.Clamp((int)((sp[2] - min.Z) / ReachCell), 0, nz - 1);
        float startH = NearestSurface(surfaces[sx, sz], sp[1]);
        if (float.IsNaN(startH))
        {
            SetStatus("The spawn is not standing on anything - move it onto the floor.");
            return;
        }

        var seen = new HashSet<(int, int, int)>();      // (ix, iz, surfaceIndexRounded)
        var queue = new Queue<(int ix, int iz, float h)>();
        void Visit(int ix, int iz, float h)
        {
            var key = (ix, iz, (int)MathF.Round(h * 4f));
            if (seen.Add(key))
                queue.Enqueue((ix, iz, h));
        }
        Visit(sx, sz, startH);

        (int, int)[] dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        var reachedCells = new HashSet<(int, int)>();
        while (queue.Count > 0)
        {
            var (ix, iz, h) = queue.Dequeue();
            reachedCells.Add((ix, iz));
            _reachGreen.Add(new Vector3(min.X + (ix + 0.5f) * ReachCell, h + 0.05f,
                                        min.Z + (iz + 0.5f) * ReachCell));
            foreach (var (dx, dz) in dirs)
            {
                int jx = ix + dx, jz = iz + dz;
                if (jx < 0 || jx >= nx || jz < 0 || jz >= nz)
                    continue;
                foreach (float there in surfaces[jx, jz])
                    if (there - h <= ReachStepUp && h - there <= ReachDrop)
                        Visit(jx, jz, there);
            }
        }

        // Any cell that has floor but none of its surfaces were reached is isolated.
        int isolated = 0;
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                if (surfaces[ix, iz].Count == 0 || reachedCells.Contains((ix, iz)))
                    continue;
                isolated++;
                float top = surfaces[ix, iz].Max();
                _reachRed.Add(new Vector3(min.X + (ix + 0.5f) * ReachCell, top + 0.05f,
                                          min.Z + (iz + 0.5f) * ReachCell));
            }

        SetStatus(isolated == 0
            ? $"Reachability: everything connects ({reachedCells.Count} walkable cells). Press F to hide."
            : $"Reachability: {isolated} ISOLATED spots (red) - a ramp or stair is missing. Press F to hide.");
    }

    /// <summary>Walkable surface heights in a column, casting down and continuing below each hit.</summary>
    private List<float> ColumnSurfaces(float x, float z)
    {
        var result = new List<float>();
        float y = ReachTop;
        for (int guard = 0; guard < 12 && y > -10f; guard++)
        {
            var origin = new Vector3(x, y, z);
            if (!Engine.Physics.Raycast(origin, -Vector3.UnitY, y + 20f, out var hit))
                break;
            float top = hit.Point.Y;
            // Only near-flat surfaces are floor (walls give steep normals).
            if (hit.Normal.Y > 0.6f && top < 60f)
            {
                bool blocked = false;   // headroom: is there another solid just above?
                foreach (float other in result)
                    if (other > top + 0.05f && other < top + ReachHeadroom)
                        blocked = true;
                if (!blocked)
                    result.Add(top);
            }
            y = top - 0.2f;   // continue below this hit to find lower surfaces
        }
        return result;
    }

    private static float NearestSurface(List<float> surfaces, float y)
    {
        if (surfaces.Count == 0)
            return float.NaN;
        float best = surfaces[0];
        foreach (float s in surfaces)
            if (MathF.Abs(s - y) < MathF.Abs(best - y))
                best = s;
        return best;
    }

    private static (Vector3 Min, Vector3 Max) WorldBounds(Node3D node, (Vector3 Min, Vector3 Max) local)
    {
        var t = node.GlobalTransform;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? local.Min.X : local.Max.X,
                (i & 2) == 0 ? local.Min.Y : local.Max.Y,
                (i & 4) == 0 ? local.Min.Z : local.Max.Z);
            var w = t.TransformPoint(corner);
            min = Vector3.Min(min, w);
            max = Vector3.Max(max, w);
        }
        return (min, max);
    }
}
