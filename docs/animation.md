# Animation

Three complementary tools cover animation in Ducz Engine:

1. **`AnimationPlayer` + `AnimationClip`** - keyframed animation of bones and node transforms (imported from glTF or built in code)
2. **`Skeleton3D`** - bone hierarchies driving skinned meshes on the GPU
3. **Tweens** - quick one-off property animation (doors, UI, knock-backs)

## AnimationPlayer

An `AnimationPlayer` is a node that plays named clips on its parent's subtree. Targets are resolved by name: **skeleton bones first, then `Node3D` descendants**.

```csharp
var anim = hero.FindNode<AnimationPlayer>()!;   // created automatically by Model.Instantiate()

anim.Play("Run");                     // starts (or keeps playing) "Run"
anim.Play("Attack", fadeSeconds: 0.15f);  // cross-fades from whatever is playing
anim.Stop();
anim.Seek(0.5f);                      // jump to a time
anim.Speed = 1.5f;                    // playback rate (negative = backwards)

anim.CurrentAnimation                 // "Attack"
anim.IsPlaying
anim.AnimationFinished += clipName => { ... };   // non-looping clips only
```

Calling `Play` with the already-active clip does nothing, so state-machine code can call it every frame safely.

### Looping

Looping is a property of the clip: `clip.Loop = true` (imported clips default to looping). For a one-shot attack:

```csharp
anim.GetClip("Attack")!.Loop = false;
anim.Play("Attack");
anim.AnimationFinished += _ => anim.Play("Idle", 0.2f);
```

## Building clips in code

You don't need a DCC tool for simple motion - clips are plain data:

```csharp
// A platform that patrols between two points, 4-second loop.
var clip = AnimationClip.FromPositionKeys("patrol", targetName: "Platform", new[]
{
    (0f, new Vector3(0, 1, 0)),
    (2f, new Vector3(8, 1, 0)),
    (4f, new Vector3(0, 1, 0)),
});

var player = platformRoot.AddChild(new AnimationPlayer());
player.AddClip(clip);
player.Play("patrol");
```

`AnimationClip.FromRotationKeys` does the same for rotations. For full control, construct `AnimationTrack`s directly - each track targets one property (`Position`, `Rotation`, `Scale`) of one named node/bone, with `Linear` or `Step` interpolation.

## Skeletons and skinning

Imported character models handle all of this automatically. The pieces, should you want to build or manipulate them by hand:

```csharp
var skeleton = hero.FindNode<Skeleton3D>()!;

int index = skeleton.FindBone("Head");
var bone  = skeleton.Bones[index];

// Bones expose an animated local pose you can override after animation runs
// (e.g. procedural head-look):
bone.LocalRotation = lookRotation * bone.LocalRotation;

skeleton.ResetToRestPose();
Matrix4x4 headPose = skeleton.GetBoneGlobalPose(index);  // skeleton-space
```

- The built-in skinned shader supports **96 bones** and 4 influences per vertex.
- A skinned `MeshInstance3D` is connected to its skeleton through a `SkinBinding` (joint remap + inverse bind matrices) - `Model.Instantiate()` wires this up.

### BoneAttachment3D

A node that follows a bone every frame. Use it to put weapons in hands, hats on heads:

```csharp
var attachment = skeleton.AddChild(new BoneAttachment3D("mixamorig:RightHand"));
attachment.AddChild(sword);
```

## Tweens

For quick "animate this one value" jobs, tweens beat clips:

```csharp
// Punch-scale a pickup when collected
Tree!.CreateTween()
    .To(s => gem.Scale = new Vector3(s), 1f, 1.4f, 0.1f, Ease.OutQuad)
    .To(s => gem.Scale = new Vector3(s), 1.4f, 0f, 0.25f, Ease.InCubic)
    .Call(() => gem.QueueFree());

// Camera shake, door slides, UI fades... same pattern.
```

Tweens are managed by the `SceneTree` and clean themselves up. Chain `.To`, `.Wait`, `.Call`, mark `.SetLooping()`, kill with `tween.Kill()`.

## Which tool when?

| Need | Use |
| --- | --- |
| Character run/attack cycles from Blender/Mixamo | `AnimationPlayer` (imported clips) |
| Moving platform, rotating fan, opening gate | `AnimationClip.FromPositionKeys` / tween |
| Hit flash, punch scale, camera shake, UI slide | Tween |
| Procedural aiming, head tracking | Write to `Skeleton3D` bones in `OnUpdate` |
| Blending walk → run smoothly | `anim.Play("Run", fadeSeconds)` |
