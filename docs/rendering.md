# Rendering

Ducz Engine ships a forward renderer on OpenGL 3.3: Blinn-Phong lighting, one shadow-casting directional light, up to 16 point + 8 spot lights, fog, a procedural sky, transparency and frustum culling. You use it entirely through nodes and materials - no graphics code required (though the raw GL API is reachable via `Engine.Renderer.Device.GL`).

## Meshes

A `Mesh` is GPU geometry. Get one from the primitive factory, from a model file, or build it from raw vertices.

```csharp
MeshFactory.Cube(1f)
MeshFactory.Box(2f, 1f, 3f)
MeshFactory.Sphere(0.5f)
MeshFactory.Plane(10f, 10f)          // XZ, facing up
MeshFactory.Quad(1f, 1f)             // XY, facing +Z (billboards/sprites)
MeshFactory.Cylinder(0.5f, 2f)
MeshFactory.Capsule(0.35f, 1.8f)
MeshFactory.Cone(0.5f, 1f)
MeshFactory.Torus(0.5f, 0.15f)
```

**Building shapes** cover what a box cannot - slopes, roofs, curves and openings. They are CPU
builders (`MeshData`), which is what the map builder, the mesh colliders and the exporter use:

```csharp
MeshFactory.WedgeData(2f, 1f, 4f)                       // ramp: rises toward +Z, centered
MeshFactory.StairsData(2f, 2f, 3f, steps: 8)            // real steps, walkable
MeshFactory.RoofGableData(6f, 2f, 8f, overhang: 0.3f)   // two slopes + gables
MeshFactory.RoofHipData(6f, 2f, 8f, ridgeLength: 3f, overhang: 0.3f)
MeshFactory.RoofShedData(6f, 2f, 8f, thickness: 0.2f)   // single slope
MeshFactory.ArchData(4f, 4f, 0.5f, openingWidth: 2f, openingHeight: 1.5f)
MeshFactory.CurvedWallData(radius: 5f, 3f, 0.3f, arcDegrees: 90f, segments: 16)
MeshFactory.TubeData(0.5f, 3f, 0.15f, segments: 24)     // pipe / column, hollow
MeshFactory.PrismData(1f, 2f, sides: 6)                 // hex/oct column
MeshFactory.PyramidData(2f, 2f, 2f)
MeshFactory.RoundedBoxData(2f, 1f, 2f, bevel: 0.1f)     // chamfered box
MeshFactory.BoxFacesData(2f, 1f, 2f)                    // 6 separate faces (one material each)
MeshFactory.PolygonData(footprint, 7f)                  // any outline extruded (real building shapes)
```

Every shape is built with outward-facing winding, so back-face culling, lighting and glTF export
all agree. `BoxFacesData` returns the faces in `MeshFactory.BoxFace` order
(Front, Back, Right, Left, Top, Bottom) - draw them as separate surfaces to give a box a
different material per face.

Every primitive also exists as a **CPU** builder returning `MeshData` (vertices + indices, no
GPU involved) - `MeshFactory.BoxData(...)`, `SphereData(...)` and so on. `MeshData.ToMesh()`
uploads it; `Transformed(matrix)` and `MeshData.Merge(...)` help when combining geometry, and
the GLB exporter reads these directly. `Box`, `Cube`, `Plane` and `Cylinder` take an optional
`worldUv: true` to UV-map faces in meters (a texture keeps its density on any block size).

Custom geometry:

```csharp
var vertices = new Vertex[]
{
    new(new Vector3(-1, 0, 0), Vector3.UnitY, new Vector2(0, 0)),
    new(new Vector3( 1, 0, 0), Vector3.UnitY, new Vector2(1, 0)),
    new(new Vector3( 0, 0,-1), Vector3.UnitY, new Vector2(0.5f, 1)),
};
var mesh = new Mesh(vertices, new uint[] { 0, 1, 2 });
```

Each `Vertex` has position, normal, UV and an RGBA color (multiplied into the material). `Mesh.RecalculateNormals(vertices, indices)` computes smooth normals for you.

> Meshes need the GPU, so create them in `OnReady` or later - not in field initializers of your Program class.

## Showing meshes: MeshInstance3D

```csharp
var box = AddChild(new MeshInstance3D(MeshFactory.Cube(), Material.FromColor(Color.Orange)));
box.Position = new Vector3(0, 0.5f, 0);
```

A `MeshInstance3D` holds one or more **surfaces** (mesh + material pairs) - models with several materials use multiple surfaces. Meshes and materials are plain objects: share them freely between instances to save memory and draw state changes.

## Materials

```csharp
var material = new Material
{
    Albedo = Color.FromHex("#e74c3c"),   // base color
    AlbedoTexture = Assets.LoadTexture("crate.png"),
    NormalMap = Assets.LoadTexture("crate_normal.png"),  // tangent-space relief
    NormalStrength = 1f,                 // 0 = flat, >1 exaggerated
    RoughnessMap = Assets.LoadTexture("crate_rough.png"),// gray: white matte, black glossy
    SpecularStrength = 0.4f,             // 0 = matte
    Shininess = 32f,                     // highlight tightness
    Emission = Color.Black,              // self-illumination
    Transparent = false,                 // alpha-blended pass
    AlphaCutout = 0f,                    // discard below this alpha (foliage)
    DoubleSided = false,
    CastShadows = true,
    ReceiveShadows = true,
    UvScale = Vector2.One,               // texture tiling
    Unshaded = false                     // ignore all lighting
};
```

Shortcuts: `Material.FromColor(c)`, `Material.FromTexture(t)`, `Material.Emissive(c, energy)`, `material.Clone()`.

`NormalMap` needs no vertex tangents - the shader derives the tangent frame from screen-space
derivatives, so it works on every mesh, including imported ones. In JSON scenes the maps are
`normalMap` / `normalStrength` / `roughnessMap`, and a material that only sets `texture` picks up
a sibling `*_normal` / `*_roughness` file automatically (`"autoMaps": false` disables that).

## Textures

```csharp
Texture2D.FromFile("albedo.png")                    // PNG, JPG, BMP, TGA, GIF
Texture2D.FromColor(Color.Red)                      // 1x1
Texture2D.CreateCheckerboard(256, 8)                // procedural prototyping floor
Texture2D.FromPixels(w, h, rgbaBytes)               // raw RGBA8
```

Use `TextureFilter.Nearest` for pixel-art. Prefer `Assets.LoadTexture(path)` - it caches by path.

## Lights

```csharp
// The sun: direction matters, position does not. Casts shadows by default.
AddChild(new DirectionalLight3D { Color = Color.White, Energy = 1.2f }
    .WithDirection(pitchDegrees: -45, yawDegrees: 30));

// Local lights
var lamp = AddChild(new PointLight3D { Color = Color.Orange, Energy = 2f, Range = 8f });
lamp.Position = new Vector3(0, 3, 0);

var flashlight = AddChild(new SpotLight3D { Range = 20f, AngleDegrees = 35f, Softness = 0.15f });
flashlight.LookAt(target);
```

Shadow tuning on the directional light: `ShadowsEnabled`, `ShadowOrthoSize` (area covered around the camera), `ShadowDepthRange`.

## Environment: sky, ambient, fog

```csharp
var env = Engine.Renderer.Environment;

env.Background = BackgroundMode.ProceduralSky;   // or SolidColor + env.ClearColor
env.SkyTopColor = Color.FromHex("#2a4a8f");
env.SkyHorizonColor = Color.FromHex("#b8cfe8");
env.SkyGroundColor = Color.FromHex("#4a4238");
env.SkySunEnabled = true;                        // draws a sun disk for the directional light

env.AmbientColor = Color.White;
env.AmbientIntensity = 0.25f;

env.FogEnabled = true;
env.FogColor = env.SkyHorizonColor;
env.FogStart = 30f;
env.FogEnd = 150f;
```

## Cameras

```csharp
var camera = AddChild(new Camera3D
{
    FovDegrees = 70f,
    Near = 0.05f,
    Far = 500f
});
camera.MakeCurrent();                 // needed only if you have several cameras
```

- `Projection = CameraProjection.Orthographic` + `OrthographicSize` for iso/2.5D looks.
- **Mouse picking:** `var (origin, dir) = camera.ScreenPointToRay(Input.MousePosition);` then `Engine.Physics.Raycast(...)`.
- **World → screen:** `camera.WorldToScreenPoint(worldPos)` (returns null behind the camera) - perfect for floating health bars.

Ready-made rigs (see [World Building](world.md)): `FlyCamera` (free flight debug camera) and `ThirdPersonCamera` (orbit + follow + wall avoidance).

## Particles

`ParticleSystem3D` is a CPU-simulated billboard system:

```csharp
var fire = AddChild(new ParticleSystem3D
{
    Amount = 80,                       // max alive
    Lifetime = 1.2f,
    Shape = EmissionShape.Sphere,      // Point, Sphere, Box
    ShapeRadius = 0.2f,
    Direction = Vector3.UnitY,
    SpreadDegrees = 20f,
    Speed = 2f,
    Gravity = new Vector3(0, 1f, 0),   // fire rises
    StartSize = 0.35f, EndSize = 0.05f,
    StartColor = new Color(1f, 0.6f, 0.1f),
    EndColor = new Color(1f, 0.1f, 0f, 0f),
    Additive = true                    // glowing blend
});
```

One-shot bursts (explosions):

```csharp
var burst = Tree!.Root.AddChild(new ParticleSystem3D { OneShot = true, Amount = 60, ... });
burst.GlobalPosition = hitPoint;
burst.EmitBurst(60);
```

## Debug drawing

Immediate-mode lines from anywhere, cleared each frame:

```csharp
DebugDraw.Line(a, b, Color.Red);
DebugDraw.Ray(origin, dir * 10, Color.Yellow, duration: 2f);   // stays 2 seconds
DebugDraw.Sphere(center, 1f, Color.Cyan);
DebugDraw.Aabb(min, max, Color.Green);
DebugDraw.Axes(node.GlobalTransform);
DebugDraw.Grid(20, 1f);
```

## Performance notes

- `Engine.Renderer.Device.DrawCalls` / `.Triangles` - per-frame stats for your HUD.
- Frustum culling is automatic per surface (bounding sphere). Disable per instance with `FrustumCullingEnabled = false` if you displace vertices in a custom way.
- Share meshes/materials between instances; every unique material = uniform changes, every unique mesh = a VAO bind.
- Transparent surfaces are depth-sorted per frame; keep their count reasonable.

## Custom shaders (advanced)

The built-in shader covers most needs, but `Shader.FromSource(device, vertexGlsl, fragmentGlsl)` compiles your own GLSL 330 programs, and `Engine.Renderer.Device.GL` exposes the full Silk.NET OpenGL API for fully custom passes. See `src/Ducz.GameEngine/Rendering/BuiltinShaders.cs` for reference shaders and the vertex attribute layout (0=position, 1=normal, 2=uv, 3=color, 4=joints, 5=weights).
