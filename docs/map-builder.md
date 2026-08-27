# Map Builder

`Ducz.Tools.SceneEditor` is the **Ducz Map Builder**: a visual editor for simple, blocky
maps (greybox / kit-bash style). Place blocks and props, right-click anything to give it a
texture, then **export the whole map as `.glb`** and open it in Godot, Blender or any
glTF-aware tool. It edits the standard [JSON scene format](json-scenes.md), so what you
save can also be loaded by the engine itself (`SceneLoader.LoadScene`).

```bash
dotnet run --project src/Ducz.Tools.SceneEditor              # edits ./level.json
dotnet run --project src/Ducz.Tools.SceneEditor -- my.json   # edits a specific file
dotnet run --project src/Ducz.Tools.SceneEditor -- MyMap/    # opens a project folder (see launcher.md)
```

New files start with a ready-to-play template: sun light, textured 40x40 ground, spawn point,
player and a third-person camera.

## Controls

| Input | Action |
| --- | --- |
| **LMB** | Place the selected block on the hovered surface |
| **LMB + drag** | Paint a row of blocks (stays on the plane where the stroke started) |
| **Shift + LMB drag** | Fill a rectangle with the selected block |
| **Ctrl + LMB on an object** | Paint the selected material/texture onto the exact part under the cursor |
| **Alt + right-click** | Select one **part** inside a prefab (a wall, a window band, a pavement) and edit it |
| **Alt + X** | Delete the selected part of a prefab |
| **Right-click an object** | Open its properties panel: position X/Y/Z with **−/+** steppers, **shape parameters** (size, slope, steps, arc...), material & normal map, rotation on all three axes, collision, duplicate, delete |
| **Drag an axis handle** | Move the selected object with the mouse: three coloured arrows (X red, Y green, Z blue) appear on it - grab one and drag. Snaps to the grid; moves the whole selection at once |
| **X** or **Delete** | Delete the hovered object (or the selected one) |
| **R** | Rotate placement by 90° |
| **G** | Cycle the grid size (0.05 / 0.1 / 0.25 / 0.5 / 1 / 2 / 4 m - the small steps are for props and detail) |
| **T** | Toggle top-down map view / fly view |
| **B** | Open the **prefab browser** - ready-made houses, streets, trees, cars ([prefabs](prefabs.md)) |
| **Ctrl+B** | Save the selected object as a prefab |
| **P** | Open the **pack browser** for an imported modular kit (see below) |
| **Ctrl+Z / Ctrl+Y** | Undo / redo |
| **Ctrl+D** | Duplicate the selected object (beside it) |
| **Ctrl+C / Ctrl+V** | Copy the selection and paste it **under the cursor** - travels between open maps |
| **Ctrl+X** | Cut (copy, then delete) |
| **Esc** | Close the open panel; then **free the mouse** so clicking selects instead of placing |
| **LMB drag** (free mouse) | Rubber-band **select several objects** |
| **Shift + click** (free mouse) | Add or remove one object from the selection |
| **Arrows, PgUp / PgDn** | Nudge the selected object by one grid step / 0.25 m |
| **WASD** + **E/Q**, hold **RMB** | Fly the camera (hold **Shift** for speed). In top view: WASD pans, wheel zooms, RMB-drag pans |
| **+ / -** | Scale imported models up/down (while placing) |
| **Drop an image file** | Turn it into a texture material (applied to the object under the cursor, or added to the palette) |
| **Drop a .glb/.fbx/.obj file** | Import it as a placeable prop |
| **Tab** (or the Play button) | Toggle play mode |
| **Ctrl+S / Ctrl+O / Ctrl+N** | Save / Load / New |
| **Ctrl+E** | Export the map as GLB |

The left sidebar **scrolls with the mouse wheel** - blocks, materials and imported models
together are taller than any window, so put the pointer over it and roll to reach the rest.
A thin bar on its right edge shows where you are.

### Free mouse

Picking a block arms the cursor: every click places another one. **Esc** (or the **Free mouse**
button at the top of the BLOCKS list) disarms it - the ghost disappears, nothing is placed, and
left-click selects the object under the cursor instead. Pick any block again to go back to
building. Handy when you have just dropped a big prefab and want to look around without
littering the map.

### Selecting several objects

In free-mouse mode (**Esc**), dragging the left button draws a box over the map and selects
everything whose centre falls inside it; **Shift+click** adds or removes one. The properties
panel then says *"N objects selected"* and every edit applies to the whole set:

- **Move** - the position **−/+** buttons and the arrow keys shift all of them by the same amount
- **Scale** - the scale **−/+** buttons grow or shrink all of them
- **Rotate** - *Yaw ±90* turns each around its own centre
- **Duplicate**, **Delete**, **Ctrl+C/Ctrl+V** - act on the whole selection

Clicking empty space (or a single object) clears the set again.

### Copy and paste

**Ctrl+C** copies the selected object - a whole prefab, or one part of it - together with the
materials it uses. **Ctrl+V** pastes it where the cursor points, resting on the surface under
it, and keeps the clipboard so you can paste again elsewhere. It goes through the system
clipboard as JSON, so it works between two open maps (and you can paste it into a text editor
to read it).

A translucent blue **ghost** previews exactly where the block will land. Placement snaps to the
grid horizontally and stacks precisely on top of whatever surface you point at.

## Prefabs: whole pieces in one click

The **PREFABS** section at the bottom of the sidebar lists the whole library, grouped by
category - scroll to it and click a piece to select it.

Press **B** for the prefab browser: houses, street segments, blocks of flats, trees, lamp
posts, benches, cars. Each one drops in fully assembled, as a single object you can move,
rotate and duplicate - the fastest way to a map that looks built rather than blocked out.
**Ctrl+B** turns anything you made into a new prefab. See **[Prefabs](prefabs.md)**.

## Blocks

**Boxes and prefabs:** Cube 1m, Block 2m, Slab, Floor 4x4, Wall 4x3, Wall 1x3, Ramp (prefab), Pillar, Sphere, Crate (physics), Point Light, Spawn Point.

**Building shapes:** Wedge/ramp, Stairs, Roof (gable), Roof (hip), Roof (shed), Arch, Curved wall,
Tube, Prism (hex), Pyramid, Rounded box. These cover the cases a plain box cannot: real slopes,
real roofs, curves and arches. Each one gets an **exact triangle collider**, so you walk up the
stairs and through the arch in play mode.

Every block is placed with `worldUv: true`, so textures keep the same
density on a 1 m cube and a 40 m floor (see *Textures* below).

- **Crates** are frozen while editing and become live physics objects in play mode.
- **Point lights** show as small glowing spheres and light the scene in both modes.
- **Spawn Point** is unique - placing a new one moves it (and keeps the player on it).
- The player and camera aren't shown in edit mode; they come to life in play mode.

## Textures and materials

The **MATERIALS / TEXTURES** palette shows every material of the map as a swatch. Click one to
make it the material for the blocks you place next.

Three ways to bring your own textures in:

1. **`+ Texture file...`** in the sidebar - native file dialog, the image becomes a new material
   named after the file and is selected.
2. **Right-click an object → `Texture file...`** - same dialog, but the material is applied to
   that object right away.
3. **Drag an image from the file explorer** onto the window: dropped over an object it is applied
   to it, dropped over empty space it is added to the palette.

In project mode the image is copied into `<project>/Assets/Textures/` and referenced by a
project-relative path; without a project the absolute path is stored.

The properties panel (right-click) also lets you:

- set the **Position**: one row per axis with a number box (Enter applies) and **−** / **+**
  buttons that move the object by one grid step (**Shift** = 4 steps). **Hold** a button and the
  object keeps sliding, so you can eyeball the right spot instead of guessing numbers. Also
  **To origin**, **To spawn**
  (moves the object onto the spawn point) and **Ground** (shifts an imported map/prop so its
  geometry is centered on the object's X/Z and its bottom sits at its Y - handy for downloaded
  models whose pivot is far from the geometry);

- edit the **shape parameters** of the selected object under *SHAPE* - the fields change with what
  is selected: a box shows Width/Height/Depth, a ramp adds **Slope** in degrees (type 20 and the
  rise is recomputed), stairs show **Steps**, a curved wall shows **Radius / Arc / Segments**, a
  roof shows **Overhang / Ridge**, a light shows **Energy / Range**. Type a number and press Enter,
  or use **−/+** (hold the button to repeat, **Shift** for a bigger step). The line under the fields explains the result
  ("12 steps of 0.2 m rise / 0.27 m tread", "slope 18.4° (33%)");
- pick any palette material for the object (swatch grid) or `Use palette mat.` (the one currently
  selected in the sidebar);
- change **Tiling** - tiles per meter of that material (world-space UVs), affects every object
  using it;
- toggle **Pixel filter** (nearest-neighbour sampling for pixel-art textures);
- assign a **Normal map** and set its **Bump** strength (see below);
- type a **Rotation** on each axis (X / Y / Z), or use **Yaw ±90** / **Reset**.

Materials live in the JSON's `materials` section, so you can also add or tweak them by hand
(colors, emission, transparency, `uvScale`...) - the editor picks them up on load.

### High-quality texture packs (normal / roughness maps)

Drop in a pack that ships extra maps and the engine picks them up **automatically**: a material
using `brick.png` also loads `brick_normal.png` (or `_nrm`, `_n`) and `brick_roughness.png`
(or `_rough`) when those files sit next to it. Names like `wood_albedo.png` + `wood_normal.png`
work too. The result is real surface relief under moving light, with no extra geometry.

To attach one by hand, right-click the object → **Normal map...**, and tune **Bump** (0 = flat,
1 = as authored, up to 4 for a strong effect). The material's own row shows `Normal: auto` when a
sibling map was detected. `"autoMaps": false` in the JSON turns the lookup off for a material.

### One texture per face

A cube usually needs the same material everywhere, but ground blocks (grass on top, dirt on the
sides) or a wall with a poster do not. In the JSON give the node `faceMaterials`:

```json
"faceMaterials": { "all": "terra", "top": "grama", "front": "cartaz" }
```

Keys: `top`, `bottom`, `front`, `back`, `left`, `right`, `sides` (the four vertical faces) and
`all`; the most specific one wins. GLB export keeps each face's material.

### World-space UVs

Blocks placed by the editor carry `"worldUv": true`: box/plane/cylinder faces are UV-mapped in
meters instead of 0..1 per face. A material's `uvScale` therefore means **tiles per meter**
(`0.5` = one repetition every 2 m) and looks identical on every block size. Hand-written scenes
without the flag keep the classic per-face mapping.

## Importing props (FBX / OBJ / glTF)

Drop the file onto the window, use **Browse model file...**, or type/paste a path in the MODELS
box and press Enter. Supported: `.fbx`, `.obj`, `.gltf`, `.glb`, `.dae`, `.stl`, `.3ds`, `.ply`.

The model becomes a palette button with a translucent preview of the *actual model*; its base
sits on the surface you point at, and an **exact triangle-mesh collider** is added automatically
(you walk on its floors and bump into its walls in play mode; blocks snap onto its real surfaces).
Use **+/-** to adjust the placement scale (FBX files are often authored in centimeters) and
**R** to rotate. Saved scenes reference the model by path (`"type": "model"`); loading a scene
re-imports its models into the palette automatically. Props are embedded in GLB exports.

### Building with a modular pack

Modular kits (a medieval wall kit, a dungeon set, a city pack) ship as **one file holding
dozens of separate pieces**. Import it once and press **P**: the pack browser lists every
piece, paged, and clicking one selects it for placement like any other block - ghost preview,
grid snapping, **R** to rotate, **+/-** to scale. Each piece lands **where you click**, with its
footprint centred on the cursor and its base on the surface, instead of jumping to wherever it
happens to sit inside the file.

In the JSON that is `"subNodePivot": "base"`:

```json
{ "type": "model", "name": "Muralha_01", "path": "Assets/Models/wall_pack.glb",
  "subNode": "SM_MMW_017", "subNodePivot": "base", "position": [12, 0, -6],
  "rotationDegrees": [0, 90, 0], "collider": { "shape": "auto" } }
```

Without `subNodePivot` (the default, `"file"`) the piece keeps the place it has inside the
model - which is what *Split model into pieces* needs to keep a map assembled.

### Editing a downloaded map piece by piece

A whole map (e.g. a `.glb` from Sketchfab) imports as **one** object: you can move it, scale it,
put it on the spawn point (**To spawn** / **Ground** in the properties panel) and build on top of
it, but not touch its parts. Right-click it and press **Split model into pieces**: every mesh node
of the file becomes its own object (a `model` with `subNode`), so each wall, roof or prop can be
selected, moved, re-textured (material override), duplicated or deleted. Pieces keep their exact
mesh collision and export back to GLB individually.

## Exporting the map as GLB

**Export GLB** (or **Ctrl+E**) opens the export options panel:

- **Scale** - 1 = meters (Godot, Blender, Unity, glTF default); presets `x100 (cm)` and `x0.01`, or
  type any factor. Geometry, positions and light ranges are scaled in the file itself (no scaled
  root node), so a 1 m block measures exactly *scale* units in the target tool.
- **Merge static geometry** - one mesh with one primitive per material (fewest draw calls).
- **Godot collision suffixes** - `-col` / `-rigid` names so Godot builds physics on import.
- **Embed imported models** - include props/maps geometry (off = marker nodes only).

**Export...** then opens a native *Save as* dialog (it remembers the last folder you used; the
suggested name is `<MapName>.glb`). All choices are remembered between sessions. The file contains every block, prop, terrain, light and
material of the map, ready for Godot or Blender - see **[GLB Export](glb-export.md)** for what
exactly goes into the file, Godot import hints (`-col` suffixes) and options such as merging
all static geometry into one mesh.

## If the editor ever crashes

Errors go to `%AppData%\DuczEngine\logs\mapbuilder.log`, and the editor writes whatever you had
open to `<scene>.recovered.json` next to the scene file before it closes - the crash dialog tells
you the path. Rename that file over your scene (or open it directly) to get the work back.

## Moving objects with the mouse

Select an object (right-click it, or click it in free-mouse mode) and a **move gizmo** appears:
three arrows along X (red), Y (green) and Z (blue). Grab an arrow and drag to slide the object
along that axis; it snaps to the current grid (**G** cycles the grid size) and the exact figures
update live in the properties panel. With several objects selected, the gizmo moves them all
together. The typed **Position** boxes and the arrow keys still work for precise nudges.

## Autosave

Your map is **saved automatically**: a few seconds after you stop making changes (and at least
every 90 seconds during a long session), the file is written to disk - so a forgotten **Ctrl+S**
never costs you work. A `.autosave.json` backup is kept next to the scene as well. The status
line shows *"Autosaved"* with the time whenever it happens. **Ctrl+S** still saves on demand.

## Play mode

Press **Tab** and the editor instantiates your document through the real `SceneLoader`: the
player spawns, crates fall, lights and particles run. Press **Tab** again to jump back to
editing, with the camera where you left it. Handy to check scale and walkability before
exporting.

## Using the JSON in your own Ducz game

```csharp
using Ducz;
using Ducz.Serialization;

var game = new Game(new GameSettings { Title = "My Game" });
game.Run(() => SceneLoader.LoadScene("level.json"));
```

## Extending the editor

`src/Ducz.Tools.SceneEditor/EditorScene*.cs` is intentionally hackable:

- Add palette entries to the `Palette` list (a label, a ghost mesh and a `NodeDef` factory).
- Default materials live in `CreateDefaultDocument`.
- Material/texture UI is in `EditorScene.Materials.cs`, undo/redo in `EditorScene.History.cs`.
- Anything the [JSON format](json-scenes.md) supports (terrain, models, particles, audio) can be
  wired into the palette the same way.
