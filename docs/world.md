# World Building

Tools for building levels in code: terrain, prefab helpers, grid maps, camera rigs and saving.

## Terrain

A `Terrain` node renders a heightmap mesh **and** registers a physics heightfield - characters walk on it, rigid bodies rest on it, raycasts hit it. Place it at the world origin.

### From a function (procedural)

```csharp
var terrain = AddChild(Terrain.FromFunction(
    (x, z) => MathF.Sin(x * 0.08f) * MathF.Cos(z * 0.06f) * 3f,   // height at world x,z
    sizeX: 200f, sizeZ: 200f, resolution: 128));
```

### From a grayscale image

```csharp
var terrain = AddChild(Terrain.FromHeightmap("Assets/heightmap.png",
    sizeX: 200f, sizeZ: 200f, maxHeight: 25f, resolution: 200));
```

Black pixels = height 0, white = `maxHeight`, bilinear-filtered between pixels.

### Using it

```csharp
float y = terrain.GetHeight(x, z);           // place objects on the ground
Vector3 n = terrain.GetNormal(x, z);         // align to slopes
terrain.Material = myGrassMaterial;          // default has slope/height vertex colors
```

`Terrain.Flat(w, d)` gives you an infinite-feeling ground plane with collision in one line.

## Prefabs - one-line level pieces

Each helper returns a body with mesh and collider already wired:

```csharp
AddChild(Prefabs.Floor(40f, 40f, checkerMat));            // top surface at Y=0

var box = AddChild(Prefabs.Box(new Vector3(2, 1, 2), mat));
box.Position = new Vector3(4, 0.5f, 0);

var wall = AddChild(Prefabs.Wall(length: 10f, height: 4f, mat));
wall.Position = new Vector3(0, 2f, -10);                   // centered: lift by height/2
wall.RotationDegrees = new Vector3(0, 90, 0);

var ramp = AddChild(Prefabs.Ramp(width: 3f, height: 2f, length: 6f, mat));

var crate = AddChild(Prefabs.Crate(1f, crateMat));         // RigidBody3D
crate.Position = new Vector3(0, 5, 0);

var gem = AddChild(Prefabs.Pickup(MeshFactory.Torus(), gemMat));   // spinning Area3D
gem.BodyEntered += body => { if (body is Player) Collect(gem); };
```

## GridMap - block-based maps

Register meshes once, stamp them into a 3D grid - a code-first tile map:

```csharp
var map = AddChild(new GridMap { CellSize = 2f });
map.RegisterItem(0, MeshFactory.Cube(2f), stoneMat);           // solid (box collider)
map.RegisterItem(1, MeshFactory.Cube(2f), lavaMat, solid: false);

map.FillRegion(0, -1, 0, 15, -1, 15, itemId: 0);   // floor
for (int x = 0; x < 16; x++)
    map.SetCell(x, 0, 0, 0);                        // north wall
map.Rebuild();                                      // (also runs automatically next frame)

map.GetCell(3, 0, 3);        // -1 = empty
map.CellToWorld(3, 0, 3);    // center of a cell
map.WorldToCell(playerPos);
```

## Camera rigs

### FlyCamera - instant scene inspection

```csharp
AddChild(new FlyCamera { MoveSpeed = 10f });
// WASD + E/Q up/down, hold right mouse to look, Shift = fast
```

### ThirdPersonCamera - follow camera

```csharp
var cam = AddChild(new ThirdPersonCamera
{
    Target = player,
    Distance = 6f,
    TargetHeight = 1.4f,
    CollisionEnabled = true      // pulls in when walls block the view
});
Input.SetMouseMode(MouseMode.Captured);
```

For camera-relative movement, use its planar directions:

```csharp
var move = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
var dir = cam.PlanarForward * -move.Y + cam.PlanarRight * move.X;
```

### TopDownCamera - RTS / strategy camera

A top-down angled camera for city builders and strategy games. It looks down at a
focus point on the ground; you pan the focus, zoom and rotate.

```csharp
var cam = AddChild(new TopDownCamera
{
    FocusPoint = Vector3.Zero,
    Distance = 24f,          // wheel zooms between MinDistance..MaxDistance
    PitchDegrees = 55f,      // 90 = straight down
    PanLimit = 40f           // clamp the focus to a box (0 = unbounded)
});
cam.MakeCurrent();
```

Controls (all toggleable): **WASD / arrows** and **screen-edge** pan, **middle-drag**
pan, **mouse wheel** zoom, **Q/E** rotate. Pan speed scales with zoom so it feels
consistent at any distance.

Pick the tile under the cursor with the ground-intersection helper (see below):

```csharp
var point = cam.ScreenPointToGround(Input.MousePosition);   // world point on y = 0
if (point is { } p)
{
    var cell = ((int)MathF.Round(p.X / TileSize), (int)MathF.Round(p.Z / TileSize));
    // ...place a building on that grid cell
}
```

`ScreenPointToGround(screenPos, planeY)` is available on **any** `Camera3D` - it
intersects the mouse ray with a horizontal plane, the standard way to place objects
on the ground with the mouse - the map builder's placement is built on the same idea.

## Saving and loading

```csharp
class SaveData
{
    public int Level = 1;
    public int Coins;
    public List<string> UnlockedItems = new();
}

// Save
SaveSystem.Save("slot1", new SaveData { Level = 3, Coins = 120 });

// Load (null when missing/corrupt)
var data = SaveSystem.Load<SaveData>("slot1") ?? new SaveData();

SaveSystem.Exists("slot1");
SaveSystem.Delete("slot1");
SaveSystem.SaveDirectory = "custom/path";   // default: %AppData%/DuczEngine/Saves
```

Data is plain JSON - human-readable, versionable, debuggable.

## Putting a level together

A typical scene `OnReady` reads like a recipe:

```csharp
protected override void OnReady()
{
    // 1. Atmosphere
    var env = Engine.Renderer.Environment;
    env.FogEnabled = true; env.FogStart = 40; env.FogEnd = 150;
    AddChild(new DirectionalLight3D().WithDirection(-45, 30));

    // 2. Ground & architecture
    AddChild(Terrain.FromFunction((x, z) => 0f, 100, 100));
    AddChild(Prefabs.Wall(20, 4, stoneMat)).Position = new Vector3(0, 2, -20);

    // 3. Actors
    var player = AddChild(new Player());
    AddChild(new ThirdPersonCamera { Target = player });
    for (int i = 0; i < 5; i++)
        AddChild(new Enemy()).Position = SpawnPoint(i);

    // 4. UI
    AddChild(new Hud());
}
```
