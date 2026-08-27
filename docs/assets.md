# Assets & Models

## The asset cache

`Assets` loads and caches by path - request the same file twice, get the same object:

```csharp
var texture = Assets.LoadTexture("Assets/Textures/crate.png");
var model   = Assets.LoadModel("Assets/Models/hero.glb");
var sound   = Assets.LoadAudio("Assets/Sfx/explosion.wav");
var font    = Assets.LoadFont("Assets/Fonts/PixelFont.ttf", 24);
```

Relative paths resolve against `Assets.BasePath` (the executable folder by default). Ship files next to your binary by adding this to the `.csproj`:

```xml
<ItemGroup>
  <None Include="Assets/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## Importing 3D models

Two import pipelines share the same `Assets.LoadModel` call, picked by file extension:

| Formats | Pipeline | Supports |
| --- | --- | --- |
| `.glb`, `.gltf` | SharpGLTF | Meshes, materials, textures, skeletons, skinning, animations (incl. cubic keys) |
| `.fbx`, `.obj`, `.dae`, `.stl`, `.3ds`, `.ply` | Assimp | Node hierarchy, meshes, materials (diffuse color/texture, opacity, emissive), embedded FBX textures, **skeletons, skinning and animations** |

Both pipelines produce the same `Model`, so props and characters work from either:

```csharp
var house = AddChild(Assets.LoadModel("Assets/Models/house.fbx").Instantiate());
house.Position = new Vector3(10, 0, -5);

// Auto-fit a collider around any prop:
var bounds = house.ComputeVisualBounds();
```

OBJ `.mtl` files and FBX embedded textures are handled automatically; external texture paths are also searched by filename (any image extension) next to the model file.

### Separate animation files (Unreal-style workflow)

Many packs ship one skeleton mesh plus one file per animation. Load extra clips onto any model that shares the skeleton (tracks target bones by name):

```csharp
var hero = AddChild(Assets.LoadModel("SKM_Manny_UE5.FBX").Instantiate());
var animator = hero.FindNode<AnimationPlayer>() ?? hero.AddChild(new AnimationPlayer());

animator.AddClip(Model.LoadAnimationClips("Walk/Walk_F.FBX", renameTo: "walk")[0]);
animator.AddClip(Model.LoadAnimationClips("Run/Run_F.FBX", renameTo: "run")[0]);
animator.Play("walk");
```

For the playable character, the JSON `player.visual` block does all of this (including automatic idle/walk/run switching) - see [JSON Scenes](json-scenes.md#animated-character-models-skeleton--animation-files).

> Unreal-style FBX quirks: files are in **centimeters** (scale 0.01) and **Z-up** (rotate X by -90°). Both are one-liners in the JSON visual block or on the instance.

### glTF / GLB details

glTF is the engine's *full* model format - the open standard that Blender, Maya, Mixamo and every asset store export. Prefer `.glb` (single binary file, textures embedded).

```csharp
var model = Assets.LoadModel("Assets/Models/robot.glb");

// Every call to Instantiate creates an independent copy for the scene:
var robot = AddChild(model.Instantiate());
robot.Position = new Vector3(0, 0, -5);
robot.Scale = new Vector3(1.5f);
```

What gets imported:

| glTF feature | Result |
| --- | --- |
| Meshes & primitives | `MeshInstance3D` with one surface per primitive |
| Base color factor & texture | `Material.Albedo` / `Material.AlbedoTexture` |
| Vertex colors | Multiplied into the material |
| Emissive factor | `Material.Emission` |
| Alpha mode BLEND / MASK | `Transparent` / `AlphaCutout` |
| Double-sided flag | `Material.DoubleSided` |
| Node hierarchy | `Node3D` tree (names preserved, made unique) |
| Skins (bones + weights) | `Skeleton3D` + GPU skinning |
| Animations (T/R/S; linear, step, cubic) | `AnimationClip`s on an `AnimationPlayer` |

### Playing imported animations

Models with animations get an `AnimationPlayer` automatically:

```csharp
var hero = AddChild(Assets.LoadModel("hero.glb").Instantiate());
var anim = hero.FindNode<AnimationPlayer>()!;

Log.Info(string.Join(", ", anim.ClipNames));   // see what's inside
anim.Play("Idle");
// later...
anim.Play("Run", fadeSeconds: 0.25f);          // smooth cross-fade
```

`Play` is safe to call every frame with the same clip - it only switches when the name changes. See [Animation](animation.md) for the full API.

### Skinned vs rigid models

- **Models with skins** (characters): the whole node hierarchy becomes a `Skeleton3D`; skinned meshes deform on the GPU. Meshes attached to joints (a sword in a hand) follow their bone automatically through `BoneAttachment3D`.
- **Models without skins** (props, buildings): a plain `Node3D` hierarchy. Animations (moving platforms, spinning fans) drive the nodes directly.

### Attaching things to bones

```csharp
var skeleton = hero.FindNode<Skeleton3D>()!;
var hand = skeleton.AddChild(new BoneAttachment3D("mixamorig:RightHand"));
hand.AddChild(swordModel.Instantiate());
```

### Sharing and instancing

`Model` keeps GPU meshes, materials and clips **shared** across instances - instantiating 100 robots uploads the geometry once. Note that materials are shared too: changing `instance.Material.Albedo` tints every instance. Use `material.Clone()` when you need a per-instance variation:

```csharp
var mesh = robot.FindNode<MeshInstance3D>()!;
mesh.Material = mesh.Material.Clone();
mesh.Material.Albedo = Color.Red;
```

### Where to get models

- [Mixamo](https://www.mixamo.com) - free rigged + animated characters (export FBX for Unity → convert, or use Blender to re-export as glTF)
- [Kenney](https://kenney.nl/assets) - free low-poly packs with glTF included
- [Sketchfab](https://sketchfab.com) - huge library, most downloads offer glTF
- Blender: `File > Export > glTF 2.0`, keep **+Y up** (default)

## Textures

See [Rendering - Textures](rendering.md#textures). `Assets.LoadTexture` accepts PNG, JPG, BMP, TGA and GIF. For pixel-art use `Assets.LoadTexture(path, TextureFilter.Nearest)`.

## Audio files

`Assets.LoadAudio` reads **WAV (PCM 8/16-bit, mono/stereo)**. Convert anything else with e.g. Audacity or ffmpeg:

```bash
ffmpeg -i music.mp3 -acodec pcm_s16le -ar 44100 music.wav
```

Or skip files entirely while prototyping - see [Audio - procedural clips](audio.md).

## Fonts

`Assets.LoadFont(path, size)` bakes a TTF at a pixel size for use with UI labels (via `UITheme.FontPath`) or direct `SpriteBatch.DrawText`. Without a custom font the UI auto-detects a system font (Segoe UI, Arial, DejaVu...).
