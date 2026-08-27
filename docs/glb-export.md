# GLB Export

`GlbExporter` turns a [scene document](json-scenes.md) into a binary glTF 2.0 file (`.glb`) -
the format Godot, Blender, Unity, three.js and every glTF viewer read natively. The Map Builder
calls it from **Export GLB / Ctrl+E**; you can also call it from code:

```csharp
using Ducz.Export;
using Ducz.Serialization;

var doc = SceneDocument.Load("scenes/main.json");
var result = GlbExporter.Export(doc, "Export/main.glb");
Console.WriteLine(result);            // "42 nodes, 20 meshes, 7 materials, 6584 triangles"
foreach (var warning in result.Warnings)
    Console.WriteLine(warning);
```

Geometry is generated on the CPU from the same definitions the engine renders
(`MeshFactory.*Data`, `Terrain.BuildMeshData`), so no window / OpenGL context is needed and the
export matches what the editor shows. Coordinates are unchanged: the engine, glTF and Godot all
use meters, Y-up, -Z forward.

## What goes into the file

| Scene node | glTF output |
| --- | --- |
| `mesh`, `static`, `rigid`, `area` (with mesh) | mesh + material on a node with the scene transform |
| `floor`, `wall`, `ramp`, `crate` | the prefab geometry, including the floor's downward offset and the ramp's slope |
| `terrain` | the heightmap mesh (flat / hills / image), vertex colors kept |
| `model` | the model file embedded as a sub-tree (glTF/GLB read directly; FBX/OBJ/DAE/STL/... via Assimp), material override honoured; with `subNode` only that part (placed where it sits in the file) |
| `pointLight`, `spotLight`, `directionalLight` | `KHR_lights_punctual` lights |
| `camera`, `flyCamera` | glTF cameras |
| `spawn`, `player`, `thirdPersonCamera`, `particles`, `audio` | empty nodes with `extras` metadata (`ducz_type`, `path`, `target`) |
| `node` / `group` + `children` | the same hierarchy |
| `groups` | `extras.groups` array on the node |
| `faceMaterials` on a box | one primitive per distinct face material inside the same mesh |

Nodes with `"visible": false` are skipped unless `IncludeHiddenNodes` is set.

### Materials

`MaterialDef` → PBR metallic-roughness:

| Scene material | glTF |
| --- | --- |
| `albedo` | `baseColorFactor` (sRGB → linear) |
| `texture` | embedded `baseColorTexture` (PNG/JPEG as-is; BMP/TGA/GIF re-encoded to PNG) |
| `checkerboard` | generated PNG, embedded |
| `uvScale` / `uvOffset` | `KHR_texture_transform` |
| `filter: "nearest"` | NEAREST sampler |
| `specular` | roughness ≈ `1 - specular * 0.85`, metallic 0 |
| `emission` + `emissionEnergy` | `emissiveFactor` + `KHR_materials_emissive_strength` |
| `transparent` / alpha < 1 | `alphaMode: BLEND`; `alphaCutout` → `MASK` |
| `doubleSided`, `unshaded` | `doubleSided`, `KHR_materials_unlit` |
| `normalMap` / `roughnessMap` | not exported (baked lighting maps stay in the engine); the base color and factors are |

Blinn-Phong to PBR is an approximation - fine for greybox and diffuse textures, which is the
map builder's use case.

## Godot import hints

By default (`GodotSuffixes = true`) node names get Godot's import suffixes, which Godot 4 turns
into physics on import and every other tool ignores:

| Suffix | Applied to | Godot creates |
| --- | --- | --- |
| `-col` | solid static geometry (blocks, floors, walls, ramps, terrain, models with a collider) | `StaticBody3D` + trimesh collision |
| `-rigid` | `crate` / `rigid` | `RigidBody3D` with collision |
| `-colonly` | `area` without a mesh | collision only, no visual |

Objects whose collider is `"none"` get no suffix. Node `extras` show up as node **metadata**
in Godot (`get_meta("ducz_type")`), so a script can find the spawn point or player marker.

Importing in Godot: drop the `.glb` into the project, double-click it, adjust the import
options if needed (e.g. *Meshes → Light Baking*), then **Instantiate** it in a scene or use
**Scene → New Inherited Scene**. In Blender: *File → Import → glTF 2.0*.

## Options

```csharp
GlbExporter.Export(doc, "map.glb", new GlbExportOptions
{
    Scale = 100f,             // export in centimeters (1 = meters, the default)
    MergeByMaterial = true,   // one "Map-col" mesh with one primitive per material (fewest draw calls)
    IncludeModels = false,    // keep external props as marker nodes only (smaller file)
    IncludeLights = true,
    IncludeMarkers = true,    // spawn/player/camera/particles/audio as empty nodes with extras
    GodotSuffixes = true,
    IncludeHiddenNodes = false
});
```

`MergeByMaterial` keeps physics props, models, terrain, lights and markers as separate nodes;
only static blocks are merged. Use it for large maps that Godot should treat as one static mesh.

## Notes & limits

- Lights inside embedded model files are not carried over (their meshes and materials are).
- Skinned/animated models are exported as static geometry.
- glTF only stores PNG/JPEG images; other formats are converted, which can enlarge the file.
- Warnings (missing textures or model files, unknown node types) are collected in
  `GlbExportResult.Warnings` and logged; the export still succeeds.
