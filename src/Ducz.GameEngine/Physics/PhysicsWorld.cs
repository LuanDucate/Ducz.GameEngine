using System.Numerics;

namespace Ducz.Physics;

/// <summary>Result of a raycast query.</summary>
public struct RaycastHit
{
    /// <summary>The body that was hit.</summary>
    public PhysicsBody3D Body;

    /// <summary>World-space hit point.</summary>
    public Vector3 Point;

    /// <summary>Surface normal at the hit point.</summary>
    public Vector3 Normal;

    /// <summary>Distance from the ray origin.</summary>
    public float Distance;
}

/// <summary>
/// The physics simulation: bodies register automatically when entering the tree.
/// Provides raycasts and overlap queries. Access via <c>Engine.Physics</c>.
/// </summary>
public sealed class PhysicsWorld
{
    /// <summary>Global gravity (default: Earth-like, -Y).</summary>
    public Vector3 Gravity { get; set; } = new(0f, -9.81f, 0f);

    private readonly List<PhysicsBody3D> _allBodies = new();
    private readonly List<RigidBody3D> _rigidBodies = new();
    private readonly List<Area3D> _areas = new();

    /// <summary>Every registered body (statics, rigids, characters and areas).</summary>
    public IReadOnlyList<PhysicsBody3D> Bodies => _allBodies;

    internal void Register(PhysicsBody3D body)
    {
        _allBodies.Add(body);
        if (body is RigidBody3D rigid)
            _rigidBodies.Add(rigid);
        if (body is Area3D area)
            _areas.Add(area);
    }

    internal void Unregister(PhysicsBody3D body)
    {
        _allBodies.Remove(body);
        if (body is RigidBody3D rigid)
            _rigidBodies.Remove(rigid);
        if (body is Area3D area)
            _areas.Remove(area);
    }

    // ------------------------------------------------------------------
    // Simulation step (called by the engine after OnPhysicsUpdate)
    // ------------------------------------------------------------------

    internal void Step(float dt)
    {
        // Integrate rigid bodies.
        foreach (var body in _rigidBodies)
        {
            if (body.Freeze || !body.Active)
                continue;

            body.Velocity += Gravity * body.GravityScale * dt;
            if (body.LinearDamping > 0f)
                body.Velocity /= 1f + body.LinearDamping * dt;
            body.GlobalPosition += body.Velocity * dt;
        }

        // Resolve rigid body contacts.
        foreach (var body in _rigidBodies)
        {
            if (body.Freeze || !body.Active || !body.TryGetWorldShape(out var shape))
                continue;

            foreach (var other in _allBodies)
            {
                if (other == body || other is Area3D || !other.Active)
                    continue;
                if (!LayersInteract(body, other))
                    continue;
                if (!other.TryGetWorldShape(out var otherShape))
                    continue;

                if (other is RigidBody3D otherRigid)
                {
                    // Handle each rigid pair once.
                    if (otherRigid.Id <= body.Id || otherRigid.Freeze)
                        continue;
                    if (!CollisionMath.TryCollide(shape, otherShape, out var pairContact))
                        continue;

                    ResolveRigidPair(body, otherRigid, pairContact);
                    body.RaiseCollision(otherRigid);
                    otherRigid.RaiseCollision(body);
                    body.TryGetWorldShape(out shape);
                    continue;
                }

                if (!CollisionMath.TryCollide(shape, otherShape, out var contact))
                    continue;

                // Static or character: push the rigid body out fully.
                body.GlobalPosition += contact.Normal * contact.Depth;

                float into = Vector3.Dot(body.Velocity, contact.Normal);
                if (into < 0f)
                {
                    var normalVelocity = contact.Normal * into;
                    var tangentVelocity = body.Velocity - normalVelocity;
                    body.Velocity = tangentVelocity * MathF.Max(0f, 1f - body.Friction * 10f * dt)
                                    - normalVelocity * body.Restitution;
                }

                body.RaiseCollision(other);
                body.TryGetWorldShape(out shape);
            }
        }

        // Area overlap events.
        var overlapping = new HashSet<PhysicsBody3D>();
        foreach (var area in _areas)
        {
            overlapping.Clear();
            if (area.Active && area.TryGetWorldShape(out var areaShape))
            {
                foreach (var other in _allBodies)
                {
                    if (other == area || !other.Active)
                        continue;
                    if ((area.CollisionMask & other.CollisionLayer) == 0)
                        continue;
                    if (!other.TryGetWorldShape(out var otherShape))
                        continue;
                    if (CollisionMath.TryCollide(areaShape, otherShape, out _))
                        overlapping.Add(other);
                }
            }
            area.UpdateOverlaps(overlapping);
        }
    }

    private static void ResolveRigidPair(RigidBody3D a, RigidBody3D b, in Contact contact)
    {
        float invMassA = 1f / MathF.Max(0.001f, a.Mass);
        float invMassB = 1f / MathF.Max(0.001f, b.Mass);
        float totalInvMass = invMassA + invMassB;

        // Positional correction split by mass.
        a.GlobalPosition += contact.Normal * (contact.Depth * invMassA / totalInvMass);
        b.GlobalPosition -= contact.Normal * (contact.Depth * invMassB / totalInvMass);

        // Impulse along the normal.
        var relative = a.Velocity - b.Velocity;
        float velAlongNormal = Vector3.Dot(relative, contact.Normal);
        if (velAlongNormal < 0f)
        {
            float restitution = MathF.Min(a.Restitution, b.Restitution);
            float impulse = -(1f + restitution) * velAlongNormal / totalInvMass;
            a.Velocity += contact.Normal * (impulse * invMassA);
            b.Velocity -= contact.Normal * (impulse * invMassB);
        }
    }

    private static bool LayersInteract(PhysicsBody3D a, PhysicsBody3D b) =>
        (a.CollisionMask & b.CollisionLayer) != 0 || (b.CollisionMask & a.CollisionLayer) != 0;

    // ------------------------------------------------------------------
    // Queries
    // ------------------------------------------------------------------

    /// <summary>
    /// Casts a ray and returns the closest hit.
    /// <paramref name="mask"/> filters by collision layer; <paramref name="ignore"/> skips one body (e.g. yourself).
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit,
        uint mask = uint.MaxValue, PhysicsBody3D? ignore = null, bool includeAreas = false)
    {
        hit = default;
        direction = Mathf.NormalizeSafe(direction);
        if (direction == Vector3.Zero)
            return false;
        // A degenerate camera/window can hand us a NaN ray; the shape maths would trip over it.
        if (!Mathf.IsFinite(origin) || !Mathf.IsFinite(direction) || !float.IsFinite(maxDistance))
            return false;

        float closest = float.MaxValue;
        foreach (var body in _allBodies)
        {
            if (body == ignore || !body.Active)
                continue;
            if (!includeAreas && body is Area3D)
                continue;
            if ((mask & body.CollisionLayer) == 0)
                continue;
            if (!body.TryGetWorldShape(out var shape))
                continue;

            if (CollisionMath.Raycast(shape, origin, direction, maxDistance, out float distance, out var normal)
                && distance < closest)
            {
                closest = distance;
                hit = new RaycastHit
                {
                    Body = body,
                    Point = origin + direction * distance,
                    Normal = normal,
                    Distance = distance
                };
            }
        }
        return closest < float.MaxValue;
    }

    /// <summary>Returns all bodies whose shapes overlap a sphere.</summary>
    public List<PhysicsBody3D> OverlapSphere(Vector3 center, float radius, uint mask = uint.MaxValue,
        PhysicsBody3D? ignore = null, bool includeAreas = false)
    {
        var results = new List<PhysicsBody3D>();
        var probe = new WorldShape
        {
            Type = WorldShapeType.Sphere,
            Center = center,
            Radius = radius,
            AabbMin = center - new Vector3(radius),
            AabbMax = center + new Vector3(radius)
        };

        foreach (var body in _allBodies)
        {
            if (body == ignore || !body.Active)
                continue;
            if (!includeAreas && body is Area3D)
                continue;
            if ((mask & body.CollisionLayer) == 0)
                continue;
            if (!body.TryGetWorldShape(out var shape))
                continue;
            if (CollisionMath.TryCollide(probe, shape, out _))
                results.Add(body);
        }
        return results;
    }

    /// <summary>
    /// Finds the deepest contact for a body against everything it collides with.
    /// Used by <see cref="CharacterBody3D.MoveAndSlide"/>.
    /// </summary>
    internal bool TryGetDeepestContact(PhysicsBody3D body, out Contact contact, out PhysicsBody3D? other)
    {
        contact = default;
        other = null;
        if (!body.TryGetWorldShape(out var shape))
            return false;

        float deepest = 0f;
        foreach (var candidate in _allBodies)
        {
            if (candidate == body || candidate is Area3D || !candidate.Active)
                continue;
            if ((body.CollisionMask & candidate.CollisionLayer) == 0)
                continue;
            if (!candidate.TryGetWorldShape(out var otherShape))
                continue;

            if (CollisionMath.TryCollide(shape, otherShape, out var c) && c.Depth > deepest)
            {
                deepest = c.Depth;
                contact = c;
                other = candidate;
            }
        }
        return deepest > 0f;
    }
}
