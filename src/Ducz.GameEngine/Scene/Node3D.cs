using System.Numerics;

namespace Ducz;

/// <summary>
/// A node with a 3D transform (position, rotation, scale) relative to its parent.
/// Global (world-space) values are computed lazily through the parent chain.
/// </summary>
public class Node3D : Node
{
    private Vector3 _position = Vector3.Zero;
    private Quaternion _rotation = Quaternion.Identity;
    private Vector3 _scale = Vector3.One;

    private Matrix4x4 _localTransform = Matrix4x4.Identity;
    private Matrix4x4 _globalTransform = Matrix4x4.Identity;
    private bool _localDirty = true;
    private bool _globalDirty = true;

    private bool _visible = true;

    public Node3D(string? name = null) : base(name) { }

    // ---- Local transform ----

    /// <summary>Position relative to the parent.</summary>
    public Vector3 Position
    {
        get => _position;
        set { _position = value; MarkDirty(); }
    }

    /// <summary>Rotation relative to the parent.</summary>
    public Quaternion Rotation
    {
        get => _rotation;
        set { _rotation = value; MarkDirty(); }
    }

    /// <summary>Rotation as Euler angles in degrees (X = pitch, Y = yaw, Z = roll). Convenience over <see cref="Rotation"/>.</summary>
    public Vector3 RotationDegrees
    {
        get
        {
            var e = ToEuler(_rotation);
            return e * Mathf.Rad2Deg;
        }
        set
        {
            var r = value * Mathf.Deg2Rad;
            _rotation = Quaternion.CreateFromYawPitchRoll(r.Y, r.X, r.Z);
            MarkDirty();
        }
    }

    /// <summary>Scale relative to the parent.</summary>
    public Vector3 Scale
    {
        get => _scale;
        set { _scale = value; MarkDirty(); }
    }

    /// <summary>Local transform matrix (scale * rotation * translation).</summary>
    public Matrix4x4 LocalTransform
    {
        get
        {
            if (_localDirty)
            {
                _localTransform = Matrix4x4.CreateScale(_scale)
                                * Matrix4x4.CreateFromQuaternion(_rotation)
                                * Matrix4x4.CreateTranslation(_position);
                _localDirty = false;
            }
            return _localTransform;
        }
    }

    // ---- Global transform ----

    /// <summary>World-space transform matrix.</summary>
    public Matrix4x4 GlobalTransform
    {
        get
        {
            if (_globalDirty)
            {
                var parent3D = FindParent3D();
                _globalTransform = parent3D == null
                    ? LocalTransform
                    : LocalTransform * parent3D.GlobalTransform;
                _globalDirty = false;
            }
            return _globalTransform;
        }
    }

    /// <summary>World-space position. Setting it converts back into local space.</summary>
    public Vector3 GlobalPosition
    {
        get => GlobalTransform.Translation;
        set
        {
            var parent3D = FindParent3D();
            if (parent3D == null)
            {
                Position = value;
            }
            else
            {
                Matrix4x4.Invert(parent3D.GlobalTransform, out var inv);
                Position = Vector3.Transform(value, inv);
            }
        }
    }

    /// <summary>World-space rotation. Setting it converts back into local space.</summary>
    public Quaternion GlobalRotation
    {
        get
        {
            var parent3D = FindParent3D();
            return parent3D == null ? _rotation : parent3D.GlobalRotation * _rotation;
        }
        set
        {
            var parent3D = FindParent3D();
            Rotation = parent3D == null
                ? value
                : Quaternion.Inverse(parent3D.GlobalRotation) * value;
        }
    }

    /// <summary>World-space forward direction (-Z).</summary>
    public Vector3 GlobalForward => Vector3.Normalize(GlobalTransform.TransformDirection(-Vector3.UnitZ));

    /// <summary>World-space right direction (+X).</summary>
    public Vector3 GlobalRight => Vector3.Normalize(GlobalTransform.TransformDirection(Vector3.UnitX));

    /// <summary>World-space up direction (+Y).</summary>
    public Vector3 GlobalUp => Vector3.Normalize(GlobalTransform.TransformDirection(Vector3.UnitY));

    // ---- Visibility ----

    /// <summary>Local visibility flag. A node renders only if all ancestors are visible too.</summary>
    public bool Visible
    {
        get => _visible;
        set => _visible = value;
    }

    /// <summary>True when this node and every 3D ancestor are visible.</summary>
    public bool IsVisibleInTree
    {
        get
        {
            if (!_visible)
                return false;
            var parent = FindParent3D();
            return parent == null || parent.IsVisibleInTree;
        }
    }

    // ---- Helpers ----

    /// <summary>Rotates the node so -Z points at <paramref name="target"/> (world space).</summary>
    public void LookAt(Vector3 target, Vector3? up = null)
    {
        var dir = target - GlobalPosition;
        if (dir.LengthSquared() < Mathf.Epsilon * Mathf.Epsilon)
            return;
        GlobalRotation = Mathf.LookRotation(dir, up);
    }

    /// <summary>Moves the node in local space (respects current rotation).</summary>
    public void TranslateLocal(Vector3 offset)
    {
        Position += Vector3.Transform(offset, _rotation);
    }

    /// <summary>Rotates around a world-space axis by an angle in radians.</summary>
    public void RotateAxis(Vector3 axis, float radians)
    {
        Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians) * Rotation);
    }

    /// <summary>Rotates around the local Y axis (yaw) by an angle in radians.</summary>
    public void RotateY(float radians)
    {
        Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians) * Rotation);
    }

    private Node3D? FindParent3D()
    {
        var current = Parent;
        while (current != null)
        {
            if (current is Node3D node3D)
                return node3D;
            current = current.Parent;
        }
        return null;
    }

    private void MarkDirty()
    {
        _localDirty = true;
        InvalidateGlobal();
    }

    internal void InvalidateGlobal()
    {
        if (_globalDirty)
            return;
        _globalDirty = true;
        InvalidateChildren(this);
    }

    private static void InvalidateChildren(Node node)
    {
        foreach (var child in node.Children)
        {
            if (child is Node3D node3D)
                node3D.InvalidateGlobal();
            else
                InvalidateChildren(child);
        }
    }

    protected override void OnParentChanged()
    {
        InvalidateGlobal();
        // A newly attached subtree must recompute globals.
        foreach (var descendant in Descendants())
            if (descendant is Node3D n3d)
                n3d.InvalidateGlobal();
    }

    private static Vector3 ToEuler(Quaternion q)
    {
        // Yaw (Y), Pitch (X), Roll (Z) extraction matching CreateFromYawPitchRoll.
        float sinPitch = -2f * (q.Y * q.Z - q.W * q.X);
        float pitch = MathF.Abs(sinPitch) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinPitch) : MathF.Asin(sinPitch);
        float yaw = MathF.Atan2(2f * (q.X * q.Z + q.W * q.Y), 1f - 2f * (q.X * q.X + q.Y * q.Y));
        float roll = MathF.Atan2(2f * (q.X * q.Y + q.W * q.Z), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch, yaw, roll);
    }
}
