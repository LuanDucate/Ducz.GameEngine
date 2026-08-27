using System.Numerics;

namespace Ducz.AI;

/// <summary>
/// Steering helpers that produce desired velocities for agents.
/// Combine them, then assign to a <see cref="Physics.CharacterBody3D"/> velocity (keep your own Y/gravity).
/// </summary>
public static class Steering
{
    /// <summary>Full-speed velocity towards a target.</summary>
    public static Vector3 Seek(Vector3 position, Vector3 target, float speed)
    {
        var dir = Mathf.NormalizeSafe((target - position).Flat());
        return dir * speed;
    }

    /// <summary>Like Seek but slows down inside <paramref name="slowRadius"/> and stops at <paramref name="stopRadius"/>.</summary>
    public static Vector3 Arrive(Vector3 position, Vector3 target, float speed,
        float slowRadius = 3f, float stopRadius = 0.3f)
    {
        var offset = (target - position).Flat();
        float distance = offset.Length();
        if (distance <= stopRadius)
            return Vector3.Zero;

        float targetSpeed = distance < slowRadius ? speed * (distance / slowRadius) : speed;
        return offset / distance * targetSpeed;
    }

    /// <summary>Full-speed velocity away from a threat.</summary>
    public static Vector3 Flee(Vector3 position, Vector3 threat, float speed)
    {
        var dir = Mathf.NormalizeSafe((position - threat).Flat());
        return dir * speed;
    }

    /// <summary>
    /// Smooth random wandering. Keep a persistent <paramref name="wanderAngle"/> per agent
    /// and pass it by ref.
    /// </summary>
    public static Vector3 Wander(ref float wanderAngle, float speed, float jitter = 2f)
    {
        wanderAngle += Rng.Range(-jitter, jitter) * Time.DeltaTime * 10f;
        return new Vector3(MathF.Cos(wanderAngle), 0f, MathF.Sin(wanderAngle)) * speed;
    }

    /// <summary>Pushes an agent away from nearby neighbors (flocking/de-clumping).</summary>
    public static Vector3 Separation(Vector3 position, IEnumerable<Vector3> neighborPositions,
        float radius, float strength)
    {
        var force = Vector3.Zero;
        foreach (var neighbor in neighborPositions)
        {
            var away = (position - neighbor).Flat();
            float distance = away.Length();
            if (distance < Mathf.Epsilon || distance >= radius)
                continue;
            force += away / distance * (1f - distance / radius);
        }
        return force * strength;
    }
}

/// <summary>
/// Follows a list of waypoints (e.g. from <see cref="NavGrid.FindPath"/>).
/// Ask it every tick for the direction to move in.
/// </summary>
public sealed class PathFollower
{
    private List<Vector3> _path = new();
    private int _index;

    /// <summary>Distance at which a waypoint counts as reached.</summary>
    public float WaypointTolerance { get; set; } = 0.5f;

    /// <summary>True when every waypoint has been reached (or no path is set).</summary>
    public bool Finished => _index >= _path.Count;

    /// <summary>The waypoint currently being approached.</summary>
    public Vector3? CurrentWaypoint => Finished ? null : _path[_index];

    /// <summary>Replaces the current path.</summary>
    public void SetPath(List<Vector3> path)
    {
        _path = path;
        _index = 0;
    }

    public void Clear() => SetPath(new List<Vector3>());

    /// <summary>
    /// Returns the desired planar velocity to follow the path, advancing
    /// waypoints as they are reached. Zero when finished.
    /// </summary>
    public Vector3 GetVelocity(Vector3 position, float speed)
    {
        while (!Finished && Vector3.Distance(position.Flat(), _path[_index].Flat()) <= WaypointTolerance)
            _index++;

        if (Finished)
            return Vector3.Zero;

        return Steering.Seek(position, _path[_index], speed);
    }

    /// <summary>Draws the remaining path with <see cref="Rendering.DebugDraw"/>.</summary>
    public void DebugDrawPath(Vector3 from)
    {
        var previous = from;
        for (int i = _index; i < _path.Count; i++)
        {
            Rendering.DebugDraw.Line(previous + Vector3.UnitY * 0.1f, _path[i] + Vector3.UnitY * 0.1f, Color.Cyan);
            previous = _path[i];
        }
    }
}
