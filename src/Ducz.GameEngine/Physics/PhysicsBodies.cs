using System.Numerics;

namespace Ducz.Physics;

/// <summary>
/// Base class for every node that participates in physics. Attach a
/// <see cref="CollisionShape"/> and place the node; the shape is centered
/// on the node's global transform.
/// </summary>
public abstract class PhysicsBody3D : Node3D
{
    /// <summary>The collider shape. Required for the body to collide.</summary>
    public CollisionShape? Shape { get; set; }

    /// <summary>Bitmask of layers this body occupies (default: layer 1).</summary>
    public uint CollisionLayer { get; set; } = 1;

    /// <summary>Bitmask of layers this body collides with / detects (default: layer 1).</summary>
    public uint CollisionMask { get; set; } = 1;

    protected PhysicsBody3D(string? name = null) : base(name) { }

    protected override void OnEnterTree()
    {
        base.OnEnterTree();
        Engine.Physics.Register(this);
    }

    protected override void OnExitTree()
    {
        Engine.Physics.Unregister(this);
        base.OnExitTree();
    }

    /// <summary>Resolves the shape into world space. Returns false when no shape is set.</summary>
    internal bool TryGetWorldShape(out WorldShape shape)
    {
        shape = default;
        if (Shape == null)
            return false;

        var transform = GlobalTransform;
        var position = transform.Translation;
        var rotation = GlobalRotation;
        var scale = Scale; // note: nested scaling is intentionally ignored for stability

        switch (Shape)
        {
            case SphereShape sphere:
            {
                float radius = sphere.Radius * MathF.Max(MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y)), MathF.Abs(scale.Z));
                shape = new WorldShape
                {
                    Type = WorldShapeType.Sphere,
                    Center = position,
                    Radius = radius,
                    AabbMin = position - new Vector3(radius),
                    AabbMax = position + new Vector3(radius)
                };
                return true;
            }
            case CapsuleShape capsule:
            {
                float radius = capsule.Radius * MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Z));
                float half = MathF.Max(0f, capsule.Height * MathF.Abs(scale.Y) * 0.5f - radius);
                var up = Vector3.Transform(Vector3.UnitY, rotation);
                var segA = position - up * half; // bottom
                var segB = position + up * half; // top
                shape = new WorldShape
                {
                    Type = WorldShapeType.Capsule,
                    Center = position,
                    SegA = segA,
                    SegB = segB,
                    Radius = radius,
                    AabbMin = Vector3.Min(segA, segB) - new Vector3(radius),
                    AabbMax = Vector3.Max(segA, segB) + new Vector3(radius)
                };
                return true;
            }
            case BoxShape box:
            {
                var half = box.HalfExtents * Mathf.Abs(scale);
                var rot = Matrix4x4.CreateFromQuaternion(rotation);
                // World AABB of an oriented box: sum of |axis| * extent.
                var ext = new Vector3(
                    MathF.Abs(rot.M11) * half.X + MathF.Abs(rot.M21) * half.Y + MathF.Abs(rot.M31) * half.Z,
                    MathF.Abs(rot.M12) * half.X + MathF.Abs(rot.M22) * half.Y + MathF.Abs(rot.M32) * half.Z,
                    MathF.Abs(rot.M13) * half.X + MathF.Abs(rot.M23) * half.Y + MathF.Abs(rot.M33) * half.Z);
                shape = new WorldShape
                {
                    Type = WorldShapeType.Box,
                    Center = position,
                    HalfExtents = half,
                    Orientation = rotation,
                    AabbMin = position - ext,
                    AabbMax = position + ext
                };
                return true;
            }
            case MeshShape mesh:
            {
                // The full global matrix (including parent scale) maps mesh-local geometry to the world.
                var localToWorld = transform;
                if (!Matrix4x4.Invert(localToWorld, out var worldToLocal))
                    return false;
                float uniformScale = MathF.Max(
                    new Vector3(localToWorld.M11, localToWorld.M12, localToWorld.M13).Length(),
                    MathF.Max(new Vector3(localToWorld.M21, localToWorld.M22, localToWorld.M23).Length(),
                              new Vector3(localToWorld.M31, localToWorld.M32, localToWorld.M33).Length()));

                // World AABB from the 8 transformed corners of the local bounds.
                var aabbMin = new Vector3(float.MaxValue);
                var aabbMax = new Vector3(float.MinValue);
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? mesh.BoundsMin.X : mesh.BoundsMax.X,
                        (i & 2) == 0 ? mesh.BoundsMin.Y : mesh.BoundsMax.Y,
                        (i & 4) == 0 ? mesh.BoundsMin.Z : mesh.BoundsMax.Z);
                    var world = Vector3.Transform(corner, localToWorld);
                    aabbMin = Vector3.Min(aabbMin, world);
                    aabbMax = Vector3.Max(aabbMax, world);
                }

                shape = new WorldShape
                {
                    Type = WorldShapeType.Mesh,
                    Mesh = mesh,
                    LocalToWorld = localToWorld,
                    WorldToLocal = worldToLocal,
                    MeshScale = uniformScale,
                    Center = position,
                    AabbMin = aabbMin,
                    AabbMax = aabbMax
                };
                return true;
            }
            case HeightfieldShape field:
            {
                shape = new WorldShape
                {
                    Type = WorldShapeType.Heightfield,
                    Field = field,
                    Center = position,
                    AabbMin = new Vector3(field.BoundsX.X, field.MinHeight, field.BoundsZ.X),
                    AabbMax = new Vector3(field.BoundsX.Y, field.MaxHeight, field.BoundsZ.Y)
                };
                return true;
            }
        }
        return false;
    }
}

/// <summary>An immovable collider: floors, walls, terrain, props.</summary>
public class StaticBody3D : PhysicsBody3D
{
    public StaticBody3D(string? name = null) : base(name) { }

    public StaticBody3D(CollisionShape shape, string? name = null) : base(name)
    {
        Shape = shape;
    }
}

/// <summary>
/// A body moved by the physics simulation: gravity, impulses and collisions.
/// The simulation is simplified (no rotation dynamics) - great for crates,
/// projectiles and pickups.
/// </summary>
public class RigidBody3D : PhysicsBody3D
{
    /// <summary>Mass in kilograms (affects collision response between rigid bodies).</summary>
    public float Mass { get; set; } = 1f;

    /// <summary>Current linear velocity (world units/second).</summary>
    public Vector3 Velocity { get; set; }

    /// <summary>Multiplier over the world gravity (0 = floats).</summary>
    public float GravityScale { get; set; } = 1f;

    /// <summary>Bounciness on impact (0 = none, 1 = full bounce).</summary>
    public float Restitution { get; set; } = 0.1f;

    /// <summary>How quickly sliding motion is damped on contact (0..1 typical).</summary>
    public float Friction { get; set; } = 0.8f;

    /// <summary>Velocity damping per second (drag).</summary>
    public float LinearDamping { get; set; } = 0.05f;

    /// <summary>When true the body ignores simulation (position controlled by code).</summary>
    public bool Freeze { get; set; }

    /// <summary>Raised when the body touches another physics body this step.</summary>
    public event Action<PhysicsBody3D>? BodyCollided;

    public RigidBody3D(string? name = null) : base(name) { }

    public RigidBody3D(CollisionShape shape, string? name = null) : base(name)
    {
        Shape = shape;
    }

    /// <summary>Adds an instantaneous velocity change (mass-independent).</summary>
    public void ApplyImpulse(Vector3 impulse) => Velocity += impulse / MathF.Max(0.001f, Mass);

    internal void RaiseCollision(PhysicsBody3D other) => BodyCollided?.Invoke(other);
}

/// <summary>
/// A player/NPC controller body. Set <see cref="Velocity"/> and call
/// <see cref="MoveAndSlide"/> from <c>OnPhysicsUpdate</c>; the body collides
/// with the world, slides along walls and reports floor contact.
///
/// <code>
/// protected override void OnPhysicsUpdate(float dt)
/// {
///     var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
///     Velocity = new Vector3(input.X * 5, Velocity.Y - 20 * dt, input.Y * 5);
///     if (IsOnFloor &amp;&amp; Input.IsActionPressed("jump"))
///         Velocity = Velocity with { Y = 8 };
///     MoveAndSlide();
/// }
/// </code>
/// </summary>
public class CharacterBody3D : PhysicsBody3D
{
    /// <summary>Velocity applied by <see cref="MoveAndSlide"/> (world units/second).</summary>
    public Vector3 Velocity { get; set; }

    /// <summary>Steepest slope that still counts as floor, in degrees.</summary>
    public float FloorMaxAngleDegrees { get; set; } = 45f;

    /// <summary>Downward snap distance that keeps the body glued to slopes/stairs.</summary>
    public float FloorSnapLength { get; set; } = 0.2f;

    /// <summary>True when the last <see cref="MoveAndSlide"/> ended standing on a floor.</summary>
    public bool IsOnFloor { get; private set; }

    /// <summary>True when the last move touched a wall.</summary>
    public bool IsOnWall { get; private set; }

    /// <summary>Normal of the floor from the last move.</summary>
    public Vector3 FloorNormal { get; private set; } = Vector3.UnitY;

    public CharacterBody3D(string? name = null) : base(name)
    {
        Shape = new CapsuleShape();
    }

    /// <summary>
    /// Moves the body by Velocity * fixed delta, resolving collisions and sliding.
    /// Call once per <c>OnPhysicsUpdate</c>.
    /// </summary>
    public void MoveAndSlide()
    {
        bool wasOnFloor = IsOnFloor;
        IsOnFloor = false;
        IsOnWall = false;
        FloorNormal = Vector3.UnitY;

        float dt = Time.FixedDeltaTime;
        float floorCos = MathF.Cos(FloorMaxAngleDegrees * Mathf.Deg2Rad);

        GlobalPosition += Velocity * dt;

        // Depenetration + slide iterations.
        for (int iteration = 0; iteration < 4; iteration++)
        {
            if (!Engine.Physics.TryGetDeepestContact(this, out var contact, out _))
                break;

            GlobalPosition += contact.Normal * contact.Depth;

            if (contact.Normal.Y >= floorCos)
            {
                IsOnFloor = true;
                FloorNormal = contact.Normal;
            }
            else if (contact.Normal.Y > -0.5f)
            {
                IsOnWall = true;
            }

            float into = Vector3.Dot(Velocity, contact.Normal);
            if (into < 0f)
                Velocity -= contact.Normal * into;
        }

        // Floor snapping keeps the body attached when walking down slopes.
        if (!IsOnFloor && wasOnFloor && Velocity.Y <= 0f && FloorSnapLength > 0f)
        {
            var before = GlobalPosition;
            GlobalPosition = before - new Vector3(0f, FloorSnapLength, 0f);
            if (Engine.Physics.TryGetDeepestContact(this, out var contact, out _) && contact.Normal.Y >= floorCos)
            {
                GlobalPosition += contact.Normal * contact.Depth;
                IsOnFloor = true;
                FloorNormal = contact.Normal;
                float into = Vector3.Dot(Velocity, contact.Normal);
                if (into < 0f)
                    Velocity -= contact.Normal * into;
            }
            else
            {
                GlobalPosition = before;
            }
        }
    }
}

/// <summary>
/// A trigger volume: detects bodies and other areas entering/leaving without
/// blocking them. Perfect for pickups, damage zones and level transitions.
/// </summary>
public class Area3D : PhysicsBody3D
{
    private readonly HashSet<PhysicsBody3D> _overlapping = new();

    /// <summary>Raised when a body starts overlapping this area.</summary>
    public event Action<PhysicsBody3D>? BodyEntered;

    /// <summary>Raised when a body stops overlapping this area.</summary>
    public event Action<PhysicsBody3D>? BodyExited;

    /// <summary>Bodies currently inside the area.</summary>
    public IReadOnlyCollection<PhysicsBody3D> OverlappingBodies => _overlapping;

    public Area3D(string? name = null) : base(name) { }

    public Area3D(CollisionShape shape, string? name = null) : base(name)
    {
        Shape = shape;
    }

    internal void UpdateOverlaps(HashSet<PhysicsBody3D> current)
    {
        // Exited
        _overlapping.RemoveWhere(body =>
        {
            if (!current.Contains(body))
            {
                BodyExited?.Invoke(body);
                return true;
            }
            return false;
        });

        // Entered
        foreach (var body in current)
        {
            if (_overlapping.Add(body))
                BodyEntered?.Invoke(body);
        }
    }
}
