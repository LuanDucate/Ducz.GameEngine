# Core Concepts

Everything in a Ducz Engine game revolves around a handful of ideas: the **game host**, the **scene tree of nodes**, the **lifecycle callbacks**, **input actions** and **time**.

## The Game host

```csharp
var game = new Game(new GameSettings
{
    Title = "My Game",
    Width = 1600,
    Height = 900,
    VSync = true,
    Msaa = 4,                    // anti-aliasing samples
    Fullscreen = false,
    PhysicsTicksPerSecond = 60,
    QuitOnEscape = false
});

game.Run(() => new MainScene()); // blocks until quit
```

`Game` owns the window, the render loop, physics stepping and every subsystem. From anywhere in your code you can reach those subsystems through the static `Engine` facade:

| Accessor | What it is |
| --- | --- |
| `Engine.Game` | The running `Game` (e.g. `Engine.Game.ChangeScene(...)`) |
| `Engine.Tree` | The `SceneTree` |
| `Engine.Renderer` | Renderer: environment, stats, sprite batch |
| `Engine.Physics` | Physics world: raycasts, overlap queries, gravity |
| `Engine.Audio` | Audio engine |
| `Engine.WindowSize` | Current framebuffer size in pixels |
| `Engine.Quit()` | Close the game |

## Nodes and the scene tree

A game is a tree of `Node` objects. Nodes with a 3D transform derive from `Node3D`. The engine ships many ready-made nodes (`Camera3D`, `MeshInstance3D`, `DirectionalLight3D`, `CharacterBody3D`, `Canvas`, ...) and you create your own by subclassing.

```csharp
class Player : CharacterBody3D
{
    protected override void OnReady()        { /* build children, load stuff */ }
    protected override void OnUpdate(float dt)        { /* every frame */ }
    protected override void OnPhysicsUpdate(float dt) { /* fixed 60 Hz */ }
    protected override void OnExitTree()     { /* cleanup */ }
}
```

### Lifecycle

| Callback | When |
| --- | --- |
| `OnEnterTree` | Node was just attached to the running tree |
| `OnReady` | Once, right after entering the tree the first time. Build your children here. |
| `OnUpdate(dt)` | Every rendered frame (`dt` = `Time.DeltaTime`) |
| `OnPhysicsUpdate(dt)` | Fixed rate (default 60 Hz), before the physics step |
| `OnExitTree` | Node removed from the tree (including `QueueFree`) |

### Building hierarchies

`AddChild` returns the child, so scenes read top-to-bottom:

```csharp
var body = AddChild(new Node3D("Body"));
var gun  = body.AddChild(new MeshInstance3D(MeshFactory.Box(0.1f, 0.1f, 0.5f)));
gun.Position = new Vector3(0.3f, 0f, -0.4f);   // relative to Body
```

Useful hierarchy tools:

```csharp
FindNode<AnimationPlayer>()          // first descendant of a type (optionally by name)
FindNode("Sword")                    // by name
FindAncestor<GameScene>()            // walk up
Descendants()                        // enumerate everything below
node.QueueFree()                     // safe delayed destruction (end of frame)
node.RemoveFromParent()              // detach without destroying
```

### Groups

Tag nodes into named groups and query them globally - great for "all enemies", "all pickups":

```csharp
AddToGroup("enemies");
foreach (var node in Tree!.GetNodesInGroup("enemies")) { ... }
```

### Transforms (Node3D)

```csharp
node.Position          // local position (relative to parent)
node.Rotation          // local rotation (Quaternion)
node.RotationDegrees   // convenience Euler angles (pitch, yaw, roll)
node.Scale
node.GlobalPosition    // world space, get/set
node.GlobalRotation
node.GlobalTransform   // full world matrix (Matrix4x4)
node.GlobalForward     // -Z in world space (also GlobalRight / GlobalUp)
node.LookAt(target)    // aim -Z at a world point
node.RotateY(radians)  // spin around Y
node.Visible           // hides this node and its children from rendering
```

The engine uses `System.Numerics` types everywhere (`Vector3`, `Quaternion`, `Matrix4x4`). Forward is **-Z**, up is **+Y**, units are meters.

## Changing scenes

```csharp
Engine.Game.ChangeScene(new Level2());
```

The previous scene is removed from the tree; the new node becomes `Tree.CurrentScene`.

## Input

Poll keyboard/mouse state from anywhere:

```csharp
Input.IsKeyDown(Key.W)            // held
Input.IsKeyPressed(Key.Space)     // this frame only
Input.IsKeyReleased(Key.Space)
Input.IsMouseButtonPressed(MouseButton.Left)
Input.MousePosition               // pixels, top-left origin
Input.MouseDelta                  // movement this frame
Input.ScrollDelta
Input.SetMouseMode(MouseMode.Captured)   // FPS-style mouse lock
```

### Actions (recommended)

Bind names to keys once, query by name everywhere - remappable for free:

```csharp
InputMap.AddAction("jump", Key.Space);
InputMap.AddAction("shoot", MouseButton.Left);
InputMap.AddDefaultMovementActions();   // move_left/right/forward/back, jump, sprint

if (Input.IsActionPressed("jump")) { ... }
var move = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
```

## Time

```csharp
Time.DeltaTime        // seconds since last frame (scaled)
Time.FixedDeltaTime   // physics step (1/60 by default)
Time.TotalTime        // seconds since start
Time.Scale            // slow motion! 0.5 = half speed, 0 = pause gameplay
Time.Fps              // smoothed frames per second
```

## Tweens

Animate any value over time without writing update code:

```csharp
Tree!.CreateTween()
    .To(v => door.Position = door.Position with { Y = v }, 0f, 3f, 0.8f, Ease.OutCubic)
    .Wait(0.5f)
    .Call(() => Log.Info("door open"))
    .SetLooping(false);
```

Steps run in sequence. Available eases: `Linear`, `In/Out/InOutQuad`, `Cubic`, `Sine`, `OutBack`, `OutBounce`, `OutElastic`.

## Logging & saves

```csharp
Log.Info("spawned boss");
Log.Warning("...");            // colored console output; hook Log.OnMessage to redirect

SaveSystem.Save("slot1", myData);          // JSON, under %AppData%/DuczEngine
var data = SaveSystem.Load<MyData>("slot1");
```

## Randomness

```csharp
Rng.Range(1, 7)              // int, max exclusive
Rng.Range(0.5f, 2f)          // float
Rng.Chance(0.25f)            // true 25% of the time
Rng.InsideUnitSphere()
Rng.Pick(lootTable)
Rng.Seed = 12345;            // reproducible runs
```
