# Physics

The engine has a deliberately simple, game-oriented 3D physics system: shape colliders, static/rigid/character bodies, trigger areas, raycasts, overlap queries and heightfield terrain. Bodies register themselves automatically when entering the scene tree.

## Shapes

Attach one `CollisionShape` to each body. The shape is centered on the node's transform (rotation is respected; boxes are oriented).

```csharp
new SphereShape(radius: 0.5f)
new CapsuleShape(radius: 0.35f, height: 1.8f)   // total height, Y-aligned
new BoxShape(halfExtents)                        // or BoxShape.FromSize(fullSize)
new HeightfieldShape { ... }                     // terrain; usually made by Terrain for you
```

## Body types

### StaticBody3D - the world

Immovable geometry: floors, walls, props.

```csharp
var wall = AddChild(new StaticBody3D(BoxShape.FromSize(new Vector3(10, 4, 0.5f))));
wall.Position = new Vector3(0, 2, -8);
```

`Prefabs.Floor/Wall/Box/Ramp` build the mesh + collider pair in one call (see [World Building](world.md)).

### RigidBody3D - simulated props

Gravity, bouncing, sliding, stacking. Simplified simulation: **linear motion only** (crates slide and bounce, they don't tumble).

```csharp
var crate = AddChild(new RigidBody3D(BoxShape.FromSize(Vector3.One))
{
    Mass = 2f,
    Restitution = 0.2f,     // bounciness
    Friction = 0.8f,
    GravityScale = 1f
});
crate.Position = new Vector3(0, 5, 0);

crate.ApplyImpulse(new Vector3(0, 6, -3));   // launch it
crate.BodyCollided += other => { ... };
crate.Freeze = true;                          // take over control by code
```

### CharacterBody3D - players and NPCs

The workhorse for anything walking. You own the velocity; `MoveAndSlide()` handles collision, sliding along walls, slopes and floor snapping.

```csharp
class Player : CharacterBody3D
{
    public Player()
    {
        Shape = new CapsuleShape(0.4f, 1.8f);   // default shape is already a capsule
    }

    protected override void OnPhysicsUpdate(float dt)
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var velocity = Velocity;

        velocity.X = input.X * 6f;
        velocity.Z = input.Y * 6f;
        velocity.Y -= 20f * dt;                       // gravity

        if (IsOnFloor && Input.IsActionPressed("jump"))
            velocity.Y = 8f;

        Velocity = velocity;
        MoveAndSlide();
    }
}
```

Properties: `IsOnFloor`, `IsOnWall`, `FloorNormal`, `FloorMaxAngleDegrees` (default 45°), `FloorSnapLength` (keeps you glued walking downhill).

> Call `MoveAndSlide()` from `OnPhysicsUpdate`, not `OnUpdate` - physics runs on a fixed 60 Hz step.

### Area3D - triggers

Detects overlaps without blocking: pickups, damage zones, level exits.

```csharp
var coin = AddChild(new Area3D(new SphereShape(0.6f)));
coin.BodyEntered += body =>
{
    if (body is Player)
    {
        Score++;
        coin.QueueFree();
    }
};
coin.BodyExited += body => { ... };
coin.OverlappingBodies                     // current set
```

## Layers and masks

Every body has a `CollisionLayer` (what I am) and a `CollisionMask` (what I interact with), both 32-bit masks. Defaults put everything on layer 1.

```csharp
// Layer plan:  1 = world, 2 = player, 4 = enemies, 8 = player bullets
player.CollisionLayer = 2;
player.CollisionMask  = 1 | 4;      // collide with world and enemies

enemy.CollisionLayer = 4;
enemy.CollisionMask  = 1 | 2;

// Raycast that only hits enemies:
Engine.Physics.Raycast(origin, dir, 100f, out var hit, mask: 4);
```

## Queries

### Raycast

```csharp
if (Engine.Physics.Raycast(origin, direction, maxDistance: 100f, out RaycastHit hit,
        mask: uint.MaxValue, ignore: this))
{
    hit.Body       // what was hit
    hit.Point      // world position
    hit.Normal     // surface normal
    hit.Distance
}
```

Mouse picking = `camera.ScreenPointToRay(Input.MousePosition)` + raycast.

### Overlap

```csharp
// Explosion damage
foreach (var body in Engine.Physics.OverlapSphere(center, radius: 4f))
    if (body is Enemy enemy)
        enemy.TakeDamage(50);
```

## Gravity & tuning

```csharp
Engine.Physics.Gravity = new Vector3(0, -9.81f, 0);   // world gravity for rigid bodies
Time.FixedDeltaTime                                    // via GameSettings.PhysicsTicksPerSecond
```

Character bodies apply their own gravity in your movement code (that keeps jump feel fully under your control).

## Terrain collision

`Terrain` registers a `HeightfieldShape` automatically - characters walk on hills, rigid bodies rest on slopes, raycasts hit the ground. See [World Building](world.md).

## Design notes & limits

- Broadphase is brute force with AABB rejection - comfortably handles hundreds of bodies; profile before shipping thousands.
- Rigid bodies do not rotate from collisions (no angular dynamics); characters never push rigid bodies implicitly (apply an impulse yourself if you want push behavior).
- Capsule-vs-box uses an iterative closest-point approximation; extremely thin boxes may feel slightly soft at edges.
- All physics state lives on nodes - there is no separate "physics scene" to keep in sync.
