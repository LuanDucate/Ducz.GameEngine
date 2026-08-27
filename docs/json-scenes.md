# JSON Scenes (Data-Driven Levels)

Ducz Engine can build entire scenes from a JSON file: geometry, materials, textures, lights, terrain, physics props, particles, audio, a playable character and its camera. Author the file by hand, generate it, or use the visual **[Map Builder](map-builder.md)** - then load it with one line (or export it as GLB, see [GLB Export](glb-export.md)):

```csharp
using Ducz.Serialization;

game.Run(() => SceneLoader.LoadScene("Assets/level.json"));
```

Game *logic* stays in C#: load the scene, then grab nodes by name and drive them:

```csharp
var scene = SceneLoader.LoadScene("Assets/level.json");
var door = scene.FindNode<Node3D>("Door");
var player = scene.FindNode<PlayerController3D>("Player");
```

You can also work with the document object model directly (this is what the editor does):

```csharp
var doc = SceneDocument.Load("level.json");     // parse
doc.Nodes.Add(new NodeDef { Type = "crate", Position = new[] { 0f, 5f, 0f } });
doc.Save("level.json");                          // write
var root = SceneLoader.Instantiate(doc);         // build live nodes
```

## File structure

```json
{
  "name": "Level1",
  "environment": { ... },
  "input": { ... },
  "materials": { "stone": { ... }, "grass": { ... } },
  "nodes": [ { "type": "floor", ... }, { "type": "player", ... } ]
}
```

### environment

```json
"environment": {
  "background": "proceduralSky",      // or "solidColor" (+ "clearColor")
  "skyTop": "#2a4a8f",
  "skyHorizon": "#b8cfe8",
  "skyGround": "#4a4238",
  "sunDisk": true,
  "ambientColor": "#ffffff",
  "ambientIntensity": 0.3,
  "fog": { "color": "#b8cfe8", "start": 35, "end": 130 }
}
```

All colors everywhere are hex strings: `"#rrggbb"` or `"#rrggbbaa"`.

### input

```json
"input": {
  "defaultMovement": true,                       // WASD/arrows + jump + sprint
  "actions": { "shoot": ["MouseLeft", "F"], "interact": ["E"] }
}
```

Key names match the engine's `Key` enum (`Space`, `E`, `F1`, `LeftShift`...); mouse buttons are `MouseLeft`, `MouseRight`, `MouseMiddle`.

### materials

Named materials that nodes reference by key:

```json
"materials": {
  "grass": {
    "albedo": "#ffffff",
    "checkerboard": { "colorA": "#6faf50", "colorB": "#5c9a42", "cells": 16 },
    "uvScale": [6, 6],
    "specular": 0.05
  },
  "wood":  { "albedo": "#a9744f", "texture": "Assets/wood.png", "shininess": 12 },
  "lava":  { "albedo": "#ff5a1e", "emission": "#ff3c00", "emissionEnergy": 1.2, "unshaded": true },
  "glass": { "albedo": "#7fd4ff88", "transparent": true, "specular": 0.9, "shininess": 128 }
}
```

Fields: `albedo`, `texture` (file path), `filter` ("nearest" for pixel art), `checkerboard` (procedural), `specular`, `shininess`, `emission`, `emissionEnergy`, `transparent`, `unshaded`, `doubleSided`, `alphaCutout`, `uvScale`, `uvOffset`, `castShadows`, `receiveShadows`.

A node's `"material"` is either a key (`"material": "stone"`) or an inline object (`"material": { "albedo": "#ff0000" }`).

### Normal & roughness maps (high-quality texture packs)

```json
"tijolo": {
  "texture": "Assets/Textures/brick.png",
  "normalMap": "Assets/Textures/brick_normal.png",   // optional - see auto-detection below
  "normalStrength": 1.2,                              // 0 = flat, 1 = as authored, >1 exaggerated
  "roughnessMap": "Assets/Textures/brick_rough.png",  // grayscale: white = matte, black = glossy
  "uvScale": [0.5, 0.5]
}
```

If you leave `normalMap` / `roughnessMap` out, the engine **looks for sibling files** next to the
albedo texture: `brick.png` → `brick_normal.png` / `brick_nrm.png` / `brick_n.png`, and
`brick_roughness.png` / `brick_rough.png`. Packs that name files `wood_albedo.png` +
`wood_normal.png` also work (the `_albedo`/`_basecolor`/`_diffuse` suffix is swapped). Set
`"autoMaps": false` to disable the lookup for one material.

Normal maps need no extra geometry data - the shader builds the tangent frame per pixel - so any
texture pack works, including on the built-in shapes.

### Per-face materials (one texture per side of a box)

```json
{
  "type": "static", "name": "Terreno", "mesh": { "primitive": "box", "size": [4, 2, 4] },
  "worldUv": true, "position": [0, 1, 0],
  "faceMaterials": { "all": "terra", "top": "grama", "front": "cartaz" }
}
```

Keys: `top`, `bottom`, `front` (+Z), `back` (−Z), `left` (−X), `right` (+X), plus `sides` (the
four vertical faces) and `all`. The most specific key wins (exact face → `sides` → `all`); faces
not listed fall back to the node's `material`. Works on `box`/`cube` meshes and is preserved by
the GLB export (one primitive per face).

## Node types

Every node supports: `name`, `position [x,y,z]`, `rotationDegrees [x,y,z]`, `scale [x,y,z]`, `visible`, `groups` (string array), `children` (nested node list) and `worldUv` (see below).

| Type | Creates | Key fields |
| --- | --- | --- |
| `node` | Empty `Node3D` (grouping) | - |
| `mesh` | Visual-only `MeshInstance3D` | `mesh`, `material` |
| `static` | `StaticBody3D` + mesh + collider | `mesh`, `material`, `collider` |
| `rigid` | `RigidBody3D` (simulated) | `mesh`, `material`, `collider`, `mass`, `restitution`, `friction` |
| `area` | Trigger volume | `collider`, optional `mesh` |
| `floor` | Floor prefab (top at position) | `size [x,z]`, `material` |
| `wall` | Wall prefab (centered) | `size [length,height,thickness]`, `material` |
| `ramp` | Walkable ramp, centered on `position` (low end at −len/2 in Z, high end at +len/2; `position.y` = low-end height). Rises toward +Z; rotate with `rotationDegrees` (180 = toward −Z, 90 = toward +X, −90 = toward −X) | `size [w,h,len]`, `material` |
| `crate` | Physics crate | `size [s]`, `material`, `mass` |
| `terrain` | Heightmap ground + collider | `terrain { mode, ... }`, `material` |
| `model` | Model file instance (glTF/GLB/FBX/OBJ/DAE/STL) | `path`, `subNode` (one node of the file only), `subNodePivot` (`"file"` keeps its place inside the model - the default; `"base"` puts its footprint centre on `position` with its base at y = 0, for building with a modular pack), `animation` (autoplay clip), `collider`, `material` (override) |
| `player` | Built-in third-person controller | `moveSpeed`, `jumpSpeed`, `gravity`, `color` |
| `spawn` | Spawn marker (group "spawn") | - |
| `camera` | `Camera3D` | `fov`, `near`, `far`, `current` |
| `flyCamera` | Free-flight debug camera | same as camera |
| `thirdPersonCamera` | Follow camera | `target` (node name), `distance`, `targetHeight`, `current` |

Set `"current": true` on the camera you want to look through. If no camera claims it, the first one in the document is activated and a warning goes to the log.
| `directionalLight` | Sun + shadows | `color`, `energy`, `shadows`, aim with `rotationDegrees` |
| `pointLight` / `spotLight` | Local lights | `color`, `energy`, `range`, `angle`, `softness` |
| `particles` | Billboard particle system | `particles { ... }` |
| `audio` / `audio3d` | Sound player (WAV) | `path`, `loop`, `volume`, `autoplay` |

### mesh

```json
"mesh": { "primitive": "box", "size": [2, 1, 2] }
```

**Basic primitives:** `cube` (size[0]), `box` (size[x,y,z]), `sphere` (`radius`), `plane` (size[x,z], `uvTiling`), `quad`, `cylinder`/`cone` (`radius`, `height`), `capsule`, `torus` (`radius`, `thickness`).

**Building shapes** (all centered on the origin like a box, so they drop into the grid the same way):

| `primitive` | Shape | Fields |
| --- | --- | --- |
| `wedge` | solid ramp, low at −Z, high at +Z, walkable slope | `size [width, rise, run]` |
| `stairs` | flight of steps climbing toward +Z | `size [width, height, depth]`, `steps` (default 8), `solidSide` |
| `roofGable` | two slopes meeting at a ridge along X | `size [width, height, depth]`, `overhang` |
| `roofHip` | four slopes; `ridgeLength` 0 = pyramid roof | `size`, `ridgeLength`, `overhang` |
| `roofShed` | single tilted slab (low −Z → high +Z) | `size [width, rise, depth]`, `thickness` |
| `arch` | wall with a round-topped opening | `size [width, height]`, `thickness`, `openingWidth`, `openingHeight`, `segments` |
| `curvedWall` | ring segment around Y (360 = full shell) | `radius`, `height`, `thickness`, `arcDegrees`, `segments` |
| `tube` | hollow pipe along Y | `radius`, `height`, `thickness`, `segments` |
| `prism` | N-sided prism | `radius`, `height`, `sides` |
| `pyramid` | rectangular-base pyramid | `size [width, height, depth]` |
| `roundedBox` | box with chamfered edges | `size [x,y,z]`, `bevel` |
| `polygon` | any footprint extruded upwards (concave is fine) | `points` (`x,z` pairs in metres), `height` |

These shapes get an **exact triangle collider** automatically (walk up the stairs, through the
arch, along the curved wall). Use `"collider": { "shape": "box" }` for the cheaper bounding box.

`curvedWall`, `tube` and `prism` are described by `radius`/`height`/`thickness`, but they also
accept the bounding `size` array (`[diameter, height, thickness]`) - any entry greater than 0
overrides the matching field, so both notations work.

```json
{ "type": "static", "name": "Escada", "mesh": { "primitive": "stairs", "size": [2, 2.4, 3.2], "steps": 12 }, "material": "concreto", "worldUv": true, "position": [0, 1.2, 0] },
{ "type": "static", "name": "Torre_Curva", "mesh": { "primitive": "curvedWall", "radius": 5, "height": 4, "thickness": 0.4, "arcDegrees": 180 }, "material": "tijolo", "worldUv": true, "position": [0, 2, 0] }
```

### worldUv (texture density in meters)

```json
{ "type": "static", "mesh": { "primitive": "box", "size": [4, 3, 0.3] }, "material": "brick", "worldUv": true }
```

With `"worldUv": true` box, plane and cylinder geometry is UV-mapped in **meters** instead of
0..1 per face, so a material's `uvScale` means *tiles per meter* and a texture looks the same
on a 1 m cube and a 40 m floor. The map builder sets it on everything it places. Applies to
`mesh`/`static`/`rigid`/`area` meshes and to the `floor`, `wall`, `ramp` and `crate` prefabs.
Without the flag (default) each face spans UV 0..1 - the classic behaviour.

### collider

Omit it for **auto** (derived from the mesh primitive), or specify:

```json
"collider": { "shape": "box", "size": [2, 1, 2], "layer": 1, "mask": 1 }
```

Shapes: `auto`, `mesh`, `box`, `sphere` (`radius`), `capsule` (`radius`, `height`), `none`.

For `model` nodes, `"collider": { "shape": "auto" }` (or `"mesh"`) gives the model an **exact
triangle-mesh collider** (`MeshShape`) - characters walk on its floors and hit its walls. Use
`"shape": "box"` for the old bounding-box behaviour (cheaper, fine for small props):

```json
{
  "type": "model", "name": "House", "path": "Assets/Models/house.obj",
  "position": [-14, 0, 2], "rotationDegrees": [0, 35, 0],
  "collider": { "shape": "auto" }
}
```

### Applying a texture/material to a model

When a model file has no usable material (or you want a different look), set `material`
on the model node - it overrides **every surface** of the instance:

```json
"materials": {
  "katanaSteel": { "texture": "Assets/Models/swords_basecolor.jpeg", "specular": 0.8, "shininess": 64 }
},
"nodes": [
  {
    "type": "model", "name": "Katana", "path": "Assets/Models/Katana.fbx",
    "position": [0, 1, 0], "material": "katanaSteel",
    "collider": { "shape": "auto" }
  }
]
```

Note that the importer already tries hard on its own: it resolves the texture path stored
in the file, then the same file name next to the model, then **any image with the same base
name** (handles `texture.png` re-saved as `texture.jpeg`), and FBX embedded textures.

### terrain

```json
"terrain": { "mode": "hills", "sizeX": 160, "sizeZ": 160, "amplitude": 3, "frequency": 0.07 }
"terrain": { "mode": "heightmap", "heightmap": "Assets/height.png", "maxHeight": 25 }
"terrain": { "mode": "flat", "sizeX": 100, "sizeZ": 100 }
```

### particles

```json
"particles": {
  "amount": 60, "lifetime": 1.4, "speed": 2.5,
  "direction": [0, 1, 0], "spread": 30, "gravity": [0, -2, 0],
  "startSize": 0.15, "endSize": 0.02,
  "startColor": "#ffb400", "endColor": "#ff3c0000",
  "additive": true, "shape": "box", "shapeRadius": 1.8
}
```

## The player + camera pair

The usual recipe for a playable scene:

```json
{ "type": "spawn",  "name": "SpawnPoint", "position": [0, 0, 6] },
{ "type": "player", "name": "Player", "position": [0, 1.2, 6], "moveSpeed": 7 },
{ "type": "thirdPersonCamera", "name": "MainCamera",
  "target": "Player", "distance": 6.5, "targetHeight": 1.4, "current": true }
```

`target` is resolved by node name after the whole tree is built. The `player` type creates a `PlayerController3D`: capsule physics, camera-relative WASD, jump, sprint, fall respawn and a default capsule visual.

### Animated character models (skeleton + animation files)

Replace the capsule with a rigged character and separate animation files - the
Unreal-style workflow (one skeleton mesh FBX + one FBX per animation) works directly:

```json
{
  "type": "player", "name": "Player", "position": [0, 1.2, 0],
  "visual": {
    "path": "Assets/Characters/SKM_Manny_UE5.FBX",
    "scale": 0.01,                          // UE FBX is in centimeters
    "offset": [0, -0.85, 0],                // feet at the capsule bottom
    "rotationDegrees": [-90, 0, 0],         // UE is Z-up; stand the model upright
    "animations": {
      "idle": "Assets/Characters/Idle.FBX",
      "walk": "Assets/Characters/Walk_F.FBX",
      "run":  "Assets/Characters/Run_F.FBX"
    }
  }
}
```

- Clips named **idle / walk / run** switch automatically based on movement speed
  (run kicks in while sprinting); missing clips fall back gracefully.
- Any extra clip names are simply registered - play them from code:
  `player.Animator!.Play("attack")`.
- The same works in code: `Model.LoadAnimationClips("Walk_F.fbx", renameTo: "walk")`
  plus `player.SetVisualModel(instance, animator)`.
- The built-in skinned shader supports up to 128 bones per mesh.

## Complete example

Create a project from the **Arena** template in the [Launcher](launcher.md): its `scenes/main.json` is a full example with materials, walls, ramps, a platform, pillars with lights, physics crates, spawn, player and camera - open it in the map builder or load it with `SceneLoader.LoadScene`.

## Notes & limits

- Unknown node types produce a warning and a plain `Node3D` (the loader never crashes on bad data; check the console).
- The JSON format holds *content*; behavior (enemy AI, triggers reacting, score) is C# on top. `groups` + `FindNode` are the bridge.
- Environment settings apply globally when the scene is instantiated.
- File paths inside the JSON resolve like all engine assets (relative to `Assets.BasePath`).
