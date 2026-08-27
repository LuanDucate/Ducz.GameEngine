# AI

The `Ducz.AI` namespace provides the three building blocks most game AI needs: **state machines** (decisions), **navigation** (where can I walk) and **steering** (how do I move).

## State machines

```csharp
using Ducz.AI;

class Guard : CharacterBody3D
{
    private readonly StateMachine _fsm = new();

    protected override void OnReady()
    {
        _fsm.AddState("patrol",
            onEnter: () => _anim.Play("Walk"),
            onUpdate: dt =>
            {
                FollowPatrolRoute(dt);
                if (CanSeePlayer())
                    _fsm.ChangeState("chase");
            });

        _fsm.AddState("chase",
            onEnter: () => _anim.Play("Run"),
            onUpdate: dt =>
            {
                ChasePlayer(dt);
                if (_fsm.TimeInState > 10f && !CanSeePlayer())
                    _fsm.ChangeState("patrol");
            },
            onExit: () => Log.Debug("lost the player"));

        _fsm.Start("patrol");
    }

    protected override void OnPhysicsUpdate(float dt) => _fsm.Update(dt);
}
```

Handy members: `CurrentState`, `TimeInState`, `IsIn("chase")`, `StateChanged` event. States are just callbacks - no classes to declare.

## Navigation: NavGrid + A*

A `NavGrid` covers a rectangular area of the XZ plane with walkable/blocked cells and answers path queries with A* (8-directional, no corner cutting).

```csharp
// 50x50 meters, 1m cells, starting at (-25, 0, -25)
var nav = new NavGrid(new Vector3(-25, 0, -25), width: 50, depth: 50, cellSize: 1f);

// Block cells that overlap static/rigid colliders (call after building the level):
nav.BakeFromPhysics(agentRadius: 0.4f);

// Or author by hand:
nav.SetWalkable(10, 12, false);

List<Vector3> path = nav.FindPath(enemy.GlobalPosition, player.GlobalPosition);
// empty list = unreachable
```

Debug it visually with `nav.DebugDrawGrid()` (green = walkable, red = blocked).

### Following a path

`PathFollower` turns waypoint lists into movement velocities:

```csharp
private readonly PathFollower _follower = new();
private float _repathTimer;

protected override void OnPhysicsUpdate(float dt)
{
    // Re-plan occasionally, not every frame.
    _repathTimer -= dt;
    if (_repathTimer <= 0f)
    {
        _repathTimer = 0.5f;
        _follower.SetPath(nav.FindPath(GlobalPosition, player.GlobalPosition));
    }

    var desired = _follower.GetVelocity(GlobalPosition, speed: 4f);
    Velocity = new Vector3(desired.X, Velocity.Y - 20f * dt, desired.Z);
    MoveAndSlide();
}
```

## Steering behaviors

Stateless helpers that return desired planar velocities - combine by adding:

```csharp
using Ducz.AI;

var v = Steering.Seek(pos, target, speed);                 // straight at it
var v = Steering.Arrive(pos, target, speed, slowRadius: 3f); // decelerate near goal
var v = Steering.Flee(pos, threat, speed);
var v = Steering.Wander(ref _wanderAngle, speed);          // smooth roaming
var v = Steering.Separation(pos, neighborPositions, radius: 2.5f, strength: 4f);

// Classic combo: chase the player but don't clump with other enemies
var desired = Steering.Seek(GlobalPosition, player.GlobalPosition, 4.5f)
            + Steering.Separation(GlobalPosition, otherEnemyPositions, 2.5f, 4.5f);
```

All steering outputs are flat (Y = 0) - keep your own gravity on the Y axis.

## Perception helpers

Line-of-sight is a raycast:

```csharp
bool CanSeePlayer()
{
    var toPlayer = player.GlobalPosition - EyePosition;
    if (toPlayer.Length() > SightRange) return false;
    // Anything solid between us?
    return !Engine.Physics.Raycast(EyePosition, Vector3.Normalize(toPlayer),
        toPlayer.Length(), out var hit, mask: 1 /* world only */);
}
```

Proximity checks are overlap queries: `Engine.Physics.OverlapSphere(pos, hearingRadius, mask: 2)`.

## Putting it together

A typical enemy combines everything on this page: a 3-state FSM (wander/chase/attack), wander + seek + separation steering, `CharacterBody3D` movement, tween-based attack animation and particle death effects - about 180 lines of C#.
