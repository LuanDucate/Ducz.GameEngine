using System.Numerics;

namespace Ducz;

/// <summary>Spatial helpers for node hierarchies.</summary>
public static class NodeBoundsExtensions
{
    /// <summary>
    /// Computes the axis-aligned bounding box of every mesh under this node,
    /// in the node's local space. Returns null when there are no meshes.
    /// Useful for auto-generating colliders and framing cameras around models.
    /// </summary>
    public static (Vector3 Min, Vector3 Max)? ComputeVisualBounds(this Node3D root)
    {
        Matrix4x4.Invert(root.GlobalTransform, out var inverseRoot);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        var corners = new Vector3[8];

        void Include(MeshInstance3D instance)
        {
            var relative = instance.GlobalTransform * inverseRoot;
            foreach (var surface in instance.Surfaces)
            {
                var b0 = surface.Mesh.BoundsMin;
                var b1 = surface.Mesh.BoundsMax;
                corners[0] = new Vector3(b0.X, b0.Y, b0.Z);
                corners[1] = new Vector3(b1.X, b0.Y, b0.Z);
                corners[2] = new Vector3(b0.X, b1.Y, b0.Z);
                corners[3] = new Vector3(b1.X, b1.Y, b0.Z);
                corners[4] = new Vector3(b0.X, b0.Y, b1.Z);
                corners[5] = new Vector3(b1.X, b0.Y, b1.Z);
                corners[6] = new Vector3(b0.X, b1.Y, b1.Z);
                corners[7] = new Vector3(b1.X, b1.Y, b1.Z);
                foreach (var corner in corners)
                {
                    var p = Vector3.Transform(corner, relative);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    any = true;
                }
            }
        }

        if (root is MeshInstance3D selfInstance)
            Include(selfInstance);
        foreach (var descendant in root.Descendants())
            if (descendant is MeshInstance3D instance)
                Include(instance);

        return any ? (min, max) : null;
    }
}
