# Tutorial: Importing Models & Animations

This tutorial covers the full glTF workflow: getting a model, loading it, playing its animations, swapping materials and attaching objects to bones - all in code.

## 1. Get a model

Any `.glb` or `.gltf` file works. Good free sources:

- **[Kenney](https://kenney.nl/assets)** - prop/character packs, glTF included, CC0
- **[Mixamo](https://www.mixamo.com)** - auto-rigged characters with hundreds of animations (download FBX, re-export from Blender as glTF)
- **[glTF sample assets](https://github.com/KhronosGroup/glTF-Sample-Assets)** - test models incl. the animated `Fox.glb` and `CesiumMan.glb`

**Blender export**: `File → Export → glTF 2.0 (.glb)`. Defaults are correct (glTF is +Y-up, meters - exactly the engine's convention). Check *"Animation"* in the export panel if the file should include actions.

## 2. Project setup

Put models in an `Assets/Models` folder and make the build copy them:

```xml
<!-- .csproj -->
<ItemGroup>
  <None Include="Assets/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## 3. Load and instantiate

```csharp
class ModelViewer : Node3D
{
    protected override void OnReady()
    {
        AddChild(new DirectionalLight3D().WithDirection(-45, 30));
        AddChild(new FlyCamera()).Position = new Vector3(0, 1.5f, 4);
        AddChild(Prefabs.Floor(20, 20, Material.FromTexture(Texture2D.CreateCheckerboard())));

        var model = Assets.LoadModel("Assets/Models/Fox.glb");
        Log.Info($"Animations: {string.Join(", ", model.AnimationNames)}");

        var fox = AddChild(model.Instantiate());
        fox.Scale = new Vector3(0.03f);        // the sample Fox is in centimeters
    }
}
```

`Assets.LoadModel` parses and uploads once; every `Instantiate()` is a cheap copy sharing GPU meshes, materials and clips.

## 4. Play animations

```csharp
var anim = fox.FindNode<AnimationPlayer>()!;
anim.Play("Survey");                   // idle-ish clip in the Fox sample

// Later, based on gameplay:
anim.Play("Walk", fadeSeconds: 0.3f);  // smooth cross-fade
anim.Play("Run",  fadeSeconds: 0.3f);
```

A typical movement-driven pattern (call every frame; `Play` ignores repeat calls):

```csharp
float speed = Velocity.Flat().Length();
anim.Play(speed < 0.1f ? "Idle" : speed < 4f ? "Walk" : "Run", 0.25f);
```

One-shot actions:

```csharp
anim.GetClip("Attack")!.Loop = false;
anim.Play("Attack", 0.1f);
anim.AnimationFinished += _ => anim.Play("Idle", 0.2f);
```

## 5. Inspect what you imported

```csharp
foreach (var node in fox.Descendants())
    Log.Info($"{node.GetType().Name}  \"{node.Name}\"");
```

Typical structure for an animated character:

```
AnimationPlayer  "AnimationPlayer"
Skeleton3D       "Skeleton"
MeshInstance3D   "fox_mesh"        (Skin bound to the skeleton)
```

Props (no skins) are plain `Node3D`/`MeshInstance3D` trees mirroring the file's hierarchy, names preserved.

## 6. Change materials

```csharp
var meshInstance = fox.FindNode<MeshInstance3D>()!;

// Shared across all instances of this model:
meshInstance.Material.SpecularStrength = 0.1f;

// Per-instance variation - clone first:
meshInstance.Material = meshInstance.Material.Clone();
meshInstance.Material.Albedo = Color.Red;

// Multi-material models: one Surface per glTF primitive
foreach (var surface in meshInstance.Surfaces)
    surface.Material.ReceiveShadows = false;
```

## 7. Attach objects to bones

```csharp
var skeleton = hero.FindNode<Skeleton3D>()!;

// Find the bone name first (varies per rig):
foreach (var bone in skeleton.Bones)
    Log.Info(bone.Name);            // e.g. "mixamorig:RightHand"

var grip = skeleton.AddChild(new BoneAttachment3D("mixamorig:RightHand"));
var sword = grip.AddChild(swordModel.Instantiate());
sword.Position = new Vector3(0, 0.05f, 0);      // fine-tune the grip offset
sword.RotationDegrees = new Vector3(0, 0, 90);
```

## 8. Give the model physics

Imported models are visuals only. Wrap them in a body:

```csharp
class Hero : CharacterBody3D
{
    protected override void OnReady()
    {
        Shape = new CapsuleShape(0.4f, 1.8f);
        var visual = AddChild(Assets.LoadModel("Assets/Models/hero.glb").Instantiate());
        visual.Position = new Vector3(0, -0.9f, 0);   // feet at capsule bottom
    }
}
```

For static scenery, pair the model with box colliders that approximate it:

```csharp
var house = AddChild(Assets.LoadModel("house.glb").Instantiate());
var collider = AddChild(new StaticBody3D(BoxShape.FromSize(new Vector3(6, 5, 8))));
collider.Position = house.Position + new Vector3(0, 2.5f, 0);
```

## Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| Model invisible | Scale - many files are in cm. Try `Scale = new Vector3(0.01f)`. Also check the camera isn't inside it. |
| Model pitch black | No lights in the scene, or normals missing (the importer generates them only when absent). Add a `DirectionalLight3D`. |
| Animation names empty | The exporter didn't include actions. In Blender, enable Animation in the glTF export options; for multiple clips use the NLA editor or "Group by NLA track". |
| Character deforms wrong | More than 96 bones, or more than 4 influences per vertex. Re-export with limits ("Limit bone influences: 4" in Blender). |
| Textures missing | Use `.glb` (embeds textures) instead of `.gltf` + separate files, or keep the texture files next to the `.gltf`. |
| Wrong orientation | Ensure "+Y up" in the exporter (glTF standard). The engine treats -Z as forward. |
