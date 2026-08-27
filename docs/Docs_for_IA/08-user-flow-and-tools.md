# 08 - User flow and tools (what happens to what the AI generates)

## Open the project

- Launcher (`Ducz.Tools.Launcher.exe`): project list → **Open**. Projects created outside the
  launcher show up if they are in `%AppData%\DuczEngine\launcher.json` (see [02](02-project-and-files.md)).
- Directly: `Ducz.Tools.SceneEditor.exe "<project folder>"` or
  `dotnet run --project src/Ducz.Tools.SceneEditor -- "<folder>"`.
- No console: messages/errors go to `%AppData%\DuczEngine\logs\mapbuilder.log`.

## What the user sees and does in the Map Builder

| Action | How |
| --- | --- |
| Fly through the scene | WASD + E/Q, hold right button to look; **T** top view |
| Place blocks | palette on the left + click; drag to paint a row; Shift+drag fills a rectangle; G changes the grid |
| Edit an object | **right click** → position X/Y/Z (with **−/+** buttons per axis, hold to repeat), shape parameters, material/texture, tiling, scale, rotation, collision, duplicate, delete |
| Texture | right click → *Texture file...*, or drag an image from Explorer onto the object, or Ctrl+click with a palette material selected |
| Test | **Tab** - walk the character through the map (real collision) |
| Save | Ctrl+S (writes `scenes/main.json` - the AI can re-read this file to continue a map) |
| Export | **Ctrl+E** → panel (scale, merge, Godot suffixes, embed models) → *Save as* `.glb` |
| Undo | Ctrl+Z / Ctrl+Y |

## Continue an existing map

If the user asks for changes ("add another platform"), the AI should **read the current
`scenes/main.json`** (the user may have moved/textured things), preserve everything and
add/change only the requested nodes. Don't regenerate from scratch without warning. Keep the names.

## GLB / Godot / Blender

- Export produces a `.glb` in meters (or at the chosen scale). Solid nodes get a `-col` suffix
  → on import Godot creates a `StaticBody3D` + collision; `spawn`/`player` become empty nodes with
  `ducz_type` metadata (useful for Godot scripts to find the spawn point).
- In Blender: *File → Import → glTF 2.0*.

## External models (ready-made props)

The user can drag `.glb/.fbx/.obj` into the editor. The AI can also reference them if the
files exist in `Assets/Models/`:
```json
{ "type": "model", "name": "Bus_01", "path": "Assets/Models/bus.glb", "position": [10, 0, -6],
  "rotationDegrees": [0, 90, 0], "collider": { "shape": "auto" } }
```
`collider: auto` = exact mesh collision. A whole map in GLB can be imported and then
split into pieces in the editor (*Split model into pieces*).

## Current limits (so the AI doesn't over-promise)

- No hand-sculpted terrain (there is procedural/heightmap terrain).
- No full PBR materials (metalness/AO); there is albedo + normal map + roughness map.
- No animated doors/elevators; no water.
- Character: 1.8 m, climbs up to ~0.35 m of step, jumps.
