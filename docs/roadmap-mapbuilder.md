# Roadmap - Ducz Map Builder

> Project direction document (August 2026). The engine's focus shifts to
> **building simple maps visually and exporting them as GLB** for use in
> Godot, Blender, or any tool that reads glTF. The game engine still exists
> (it's what runs the editor and the play-test), but is no longer the product.

## Status (2026-08-18)

| Phase | Status | Where |
| --- | --- | --- |
| 1 - GLB export | **Done.** `MeshData` (CPU-side geometry), `GlbExporter` (SharpGLTF.Toolkit), PBR materials + embedded textures + `KHR_texture_transform`, lights, terrain, embedded props (glTF and Assimp), markers with `extras`, Godot suffixes, *merge by material* mode, button/Ctrl+E in the editor. Validated against SharpGLTF's strict validator (Godot/Blender: manual test pending). | `src/Ducz.GameEngine/Export/`, `Rendering/MeshData.cs`, `Rendering/PngEncoder.cs`, [glb-export.md](glb-export.md) |
| 2 - Map builder UX | **Done** (except multi-selection and per-face texturing, see below). RMB panel with a material/texture section (swatches, `Texture file...`, tiling, nearest filter), texture palette with thumbnails, `+ Texture file...`, drag & drop of images onto objects, Ctrl+LMB paints material, 0.25–2 m grid (G), paint by dragging, fill rectangle (Shift+drag), undo/redo, duplicate, arrow-key nudge, orthographic top-down view (T), world-scale UVs (`worldUv`). | `src/Ducz.Tools.SceneEditor/`, [map-builder.md](map-builder.md) |
| 3 - Reposition | **Done.** README and docs rewritten around the map builder (`map-builder.md`, `glb-export.md`, `launcher.md`), references to `samples/` removed, launcher renamed (templates "Empty Map / Arena / Terrain"), editor cleanups. | `README.md`, `docs/` |

**Done afterwards (08-18):** "Save as" dialog in Export GLB; Position X/Y/Z field + To origin / To spawn / Ground; importer accepts nodes with a matrix (Sketchfab); exact mesh collider (`MeshShape`) for imported models; "Split model into pieces" (`subNode`) to edit GLB maps piece by piece. Validated in Blender by the user.

**Quality improvement (08-18 to 08-19):** building shapes (wedge, stairs, 3 roofs, arch, curved wall, tube, prism, pyramid, chamfered box) with exact mesh collision; properties panel with per-shape numeric parameters (including ramp slope in degrees) and rotation on all 3 axes; grid from 0.05 to 4 m; normal/roughness maps with automatic detection of sibling files; per-face box material (`faceMaterials`) in the editor and in GLB export. The three old limitations (single face, no curves, simple materials) have been removed.

**Library and usability (08-19):**

- **Prefabs** - 37 ready-made pieces built from the engine's own shapes (houses, streets,
  buildings, structures, nature, urban). Panel **B**, the PREFABS section in the sidebar,
  **Ctrl+B** saves what you've built as a new piece. Installed alongside the engine in `src/Prefabs/`.
- **Prototype textures** - 11 one-meter grid tiles generated in code (`src/Textures/prototype/`),
  used as the default palette of every new map via `DefaultMaterials.Create()`.
- **Editing prefab pieces** - **Alt+RMB** selects a part (wall, window, sidewalk),
  **Ctrl+LMB** paints the part, **Alt+X** deletes it, "Ungroup parts" breaks the set apart.
- **Modular packs** - the **P** key cycles through the pieces of an imported kit; `subNodePivot: "base"`
  drops the piece where you click.
- **Multi-selection** - lasso in free-mouse mode, Shift+click, and move/scale/rotate/
  duplicate/copy/delete as a group.
- **Free mouse (Esc)** and **copy/paste (Ctrl+C/V)** through the system clipboard.
- **Scrollable sidebar** (`ScrollPanel` + scissor clipping in `SpriteBatch`).
- **`polygon` primitive** - any extruded outline, with exact mesh collision.
- **Robustness** - a NaN ray no longer crashes the engine, emergency save on crash,
  a default camera when the scene forgets `"current": true`, GLB export 10x faster (cache),
  "Open from folder..." in the launcher.

**Suggested next steps:** drag-to-move tool (gizmo); regroup objects
(the inverse of "Ungroup"); editable block-size presets; scrollable properties panel.

## 1. Current state of the repository

| Project | Lines | Role | Status for the new focus |
| --- | --- | --- | --- |
| `src/Ducz.GameEngine` | ~10.7k | Library: OpenGL 3.3 rendering, scene, physics, UI, audio, animation, AI, JSON scenes, glTF/Assimp importers | **Foundation of the editor.** Rendering, scene, physics (raycast/placement), UI, and serialization are essential. Audio, AI, animation, and player remain but become secondary. |
| `src/Ducz.Tools.SceneEditor` | 1,277 | Visual editor: block palette, placement ghost, 1 m snap, properties panel (RMB), model import, play-test (Tab) | **This is the Map Builder.** It already solves the hardest part (raycast → snap → place/stack). |
| `src/Ducz.Tools.Launcher` | ~630 | Project manager + templates | Useful as "open/create map". Templates need to become map templates. |
| `docs/` | 16 files | Complete engine manual | Still references `samples/` (removed folders). Needs to be reoriented. |

The solution builds clean (`dotnet build` → 0 warnings, 0 errors).

### What already exists and works in our favor

- **Building flow ready**: palette → translucent ghost → click places with 1 m snap
  on XZ and 0.05 m on Y (exact stacking). Delete with X, rotate with R, scale models with +/-.
- **Right-click already opens a properties panel** per object (scale, rotation, collision,
  delete) - exactly where "add texture" fits in.
- **Document format (`SceneDocument`/`NodeDef`/`MaterialDef`) is clean and complete**: it already has
  `texture`, `uvScale`, `uvOffset`, `filter`, `checkerboard`, `albedo`, `emission`, `transparent`,
  `doubleSided`. No need to invent a new format; the GLB is derived from it.
- **Model import (GLB/glTF/FBX/OBJ/DAE/STL)** with a real model preview in the ghost and
  an automatic collider - maps can mix blocks and external props.
- **SharpGLTF.Core 1.0.6 is already a dependency** (importer). `SharpGLTF.Toolkit` (same version)
  is already in the machine's NuGet cache - it's the simplest way to write GLB
  (`SceneBuilder`/`MeshBuilder`, embeds images, supports `KHR_texture_transform` and
  `KHR_lights_punctual`).
- **Compatible coordinate system**: the engine is Y-up, right-handed, meters - the same as glTF
  and Godot. There is no axis conversion.
- **The custom UI has an `ImageBox`** → texture thumbnails can be shown in the palette.
- **`Camera3D` already supports orthographic** → a top-down view for drawing the map is cheap to add.

### Concrete technical obstacles

1. **`Mesh` is GPU-only.** `MeshFactory` builds `Vertex[]`/`uint[]` and immediately uploads them to
   the VAO; the `Mesh` only keeps bounds (and optionally positions). To export we need the
   geometry on the CPU. → Introduce a `MeshData` (vertices + indices) that `MeshFactory`
   generates and `Mesh` consumes; the exporter uses `MeshData` straight from `NodeDef`, without touching
   the GPU.
2. **Prefabs (`floor`, `wall`, `ramp`, `crate`) have internal offsets** (floor with its top at Y,
   ramp with its base at Y and rotation on X). The exporter needs to reproduce exactly the
   same math as `SceneLoader`/`Prefabs` so the GLB matches what you see in the editor.
3. **Checkerboard is a procedural texture** - in the GLB it needs to become an embedded PNG. There is no
   PNG encoder in the current dependencies (StbImageSharp only reads). A minimal PNG encoder
   (zlib via `System.IO.Compression`, ~40 lines) solves it, or swap the checkerboard for a
   file texture in the default materials.
4. **External models in the map**: to include them in the GLB you need to re-read the source file
   on the CPU (SharpGLTF for .glb/.gltf; Assimp for the rest) and merge it in as a sub-tree.
   A simpler alternative for v1: export just an empty node with the name/path and
   let Godot instantiate the prop.
5. **Documentation and README** still describe `samples/...` and pitch a "code-first engine".
6. Detail: `SaveDocument` has leftover debug (`var teste`, `Console.WriteLine`).

## 2. Goal

A **simple map builder**, in the style of block/greybox editors:

- Build the map by placing blocks, walls, floors, ramps, and props;
- **Right-click an object → choose/add a texture** (image file), adjust
  tiling, scale, and rotation;
- **Export `.glb`** that opens directly in Godot (with generated collision) and in Blender.

## 3. Phased plan

### Phase 1 - GLB export (the core)

1. **`MeshData` on the CPU** (`Ducz.Rendering.MeshData`): `Vertex[] Vertices`, `uint[] Indices`.
   `MeshFactory` gains `BoxData/SphereData/...` (or a `Geometry` class) and the
   current methods become `new Mesh(XData(...))`. Zero behavior change in rendering.
2. **`GlbExporter`** (new, in `Ducz.GameEngine/Serialization` or `Ducz.GameEngine/Export`),
   input `SceneDocument` + base folder, output `.glb`. Uses `SharpGLTF.Toolkit`:
   - Walks `Nodes` recursively, keeping hierarchy and names;
   - `static`/`mesh`/`rigid`/`area`(with mesh)/`floor`/`wall`/`ramp`/`crate` → primitive with
     material, applying the same offsets as `SceneLoader`;
   - `terrain` → heightmap mesh generated on the CPU (same function as `Terrain`);
   - `model` → sub-tree imported from the source file (v1 can be an empty node + `extras`);
   - `pointLight`/`spotLight`/`directionalLight` → `KHR_lights_punctual`;
   - `spawn`/`player`/`camera` → empty node with `extras` (`{"ducz":{"type":"spawn"}}`); Godot
     exposes `extras` as node metadata;
   - **Godot import suffixes**: static geometry gets the name `Name-col`
     (automatic StaticBody3D + collision on import), `crate`/`rigid` → `-rigid`,
     areas → `-colonly`. Blender ignores the suffix, so it costs nothing.
   - `MergeByMaterial` option (fewer draw calls in Godot) vs. one node per object (editable).
3. **Materials → glTF PBR**: `albedo` → `baseColorFactor`; `texture` → embedded image
   (`baseColorTexture`); `uvScale/uvOffset` → `KHR_texture_transform`; `emission`+
   `emissionEnergy` → `emissiveFactor`; `transparent` → `alphaMode BLEND`;
   `alphaCutout` → `MASK`; `doubleSided`; `specular/shininess` → `metallic 0` +
   derived `roughness`; `checkerboard` → generated, embedded PNG; `filter nearest` →
   `NEAREST` sampler.
4. **"Export GLB" button (Ctrl+E)** in the editor; writes to
   `<project>/Export/<Name>.glb` (or next to the `.json` outside a project).
5. **Validation**: open the `.glb` in Godot 4 (check collision via `-col`, textures, tiling)
   and in Blender; compare visually with the editor.

### Phase 2 - Map builder UX (textures via right-click)

1. **The properties panel gains a "Texture/Material" section**:
   - a list of the document's materials (with a thumbnail via `ImageBox`);
   - **"Choose image..."** → native dialog (same technique as `FolderPicker`, with
     `OpenFileDialog`) → creates/updates `MaterialDef { Texture = path }`, copies the image
     into the project's `Assets/Textures/`, applies it to the object (`def.Material = key`);
   - **dragging an image onto an object** does the same (the editor already receives `FileDropped`);
   - tiling (`uvScale` +/-), *nearest* filter (pixel art), tint color (`albedo`).
2. **Texture palette in the sidebar** replaces the text buttons in MATERIALS: a grid of
   thumbnails; an image dropped in the window with no object under the mouse becomes a new texture in the palette.
3. **Building tools**: selectable grid size (0.5 / 1 / 2 m); paint by
   dragging (holding LMB places several); fill rectangle (LMB-drag for floor/wall);
   duplicate (Ctrl+D); move with arrows/simple gizmo; **undo/redo** via JSON snapshot
   (cheap: the document is small); multi-selection.
4. **Camera**: besides fly, orbit mode and orthographic top-down view (`Camera3D` already supports it).
5. (v2) Per-face texture: boxes exported as 6 primitives with distinct materials.
   `MeshInstance3D` already accepts multiple `Surface`s.

### Phase 3 - Reposition the repository

1. README and docs: fix `samples/` → `src/`; rewrite the opening around the map
   builder + GLB; `scene-editor.md` becomes `map-builder.md`; add `docs/glb-export.md`
   (what comes out in the file, Godot suffixes, how to import into Godot/Blender).
2. Launcher: map templates (Empty, Room, Arena) instead of "games"; an "Export GLB" button
   directly from the launcher is optional.
3. The "Export Game" button and the `Ducz.Player` runtime were removed - the engine is a map builder, not a game packager.

## 4. Risks and assumed decisions

- **SharpGLTF.Toolkit** becomes an engine dependency (same family/version as the Core already used).
- Keep **JSON as the source format** of the map and the GLB as *output*: this allows reopening and
  editing at any time; the GLB is regenerated whenever needed.
- glTF materials are PBR metallic-roughness; the engine is Blinn-Phong. The conversion is
  approximate and sufficient for greybox/diffuse textures (which is the use case).
- External props (`model`) merged into the GLB can bloat the file; that's why the option to
  export just a marker is kept.
