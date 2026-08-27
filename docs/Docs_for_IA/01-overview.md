# 01 - Overview

## What the Ducz Map Builder is

A simple 3D map editor ("greybox / kit-bash"): the user places blocks, walls, ramps,
pillars and props on a grid, applies textures with the right mouse button, tests by walking
through the map (Play) and **exports a `.glb`** that opens in Godot (with collision generated
automatically), in Blender, Unity, etc. The editor reads and writes a **scene JSON file**;
that JSON is what the AI produces.

## Language

This whole knowledge base is written in Portuguese, but that is just the author's convenience:
**always answer in the language the user asked in**. Technical names (JSON keys, primitives,
prefab names) stay as they are in any language.

## The AI's role

Input: a folder path + a request ("I want a map of X").

The fastest way to a good result is to **build with the library prefabs**
(houses, streets, buildings, trees, lamps - see [12](12-prefabs-and-library.md)) and use loose
blocks only for what is missing.
Output: the project folder ready to open:

```
<path>/
  project.duczproj.json      descriptor (name, main scene)
  scenes/main.json           THE MAP (materials + node list)
  Assets/Textures/           empty; the user adds textures later
```

The AI does not render or run anything - it **decomposes the request into blocks** (floors, walls,
roofs, pillars, ramps...) with coherent positions and sizes, and writes the JSON.

## Essential conventions

| Item | Rule |
| --- | --- |
| Unit | meters. A door is about 1 m × 2.1 m; ceiling height 2.8–3.5 m; a bus is 12 m × 2.5 m × 3.2 m |
| Axes | Y up. X to the right, Z "toward the viewer" (−Z is the front/north of the map) |
| Rotation | `rotationDegrees: [x, y, z]` in degrees. Rotating in Y (yaw) is the most common: 90 turns a wall from X to Z |
| Origin | (0,0,0) on the ground. Spawn point near the origin |
| Colors | hex `"#rrggbb"` or `"#rrggbbaa"` |
| Vectors | arrays `[x, y, z]`; `size` values are TOTAL measurements (not half-measurements) |
| Grid | place things at multiples of 0.5 m or 1 m - the editor works on a grid |

## `position` semantics by type (the most common mistake!)

| Type | `position` means |
| --- | --- |
| `static`, `mesh`, `rigid`, `wall`, `crate`, `pointLight`, `model` | **center** of the object |
| `floor` | **top** of the floor (the floor extends 0.5 m below that Y) |
| `ramp` | **center** of the ramp in the plane (X/Z) and the height of the **low end** (Y). With no rotation the low end is at `z − length/2` and the high end at `z + length/2` (rising toward +Z) |
| `spawn`, `player` | point on the ground / center of the capsule (player at `y = ground + 1.2`) |

Examples: a 3 m tall wall standing on the ground at y=0 → `"position": [x, 1.5, z]`.
A 2×2×2 box resting on the ground → `[x, 1, z]`. A 0.3 m thick floor slab on the ground → `[x, 0.15, z]`.

## What the user does next

Opens the folder in the Map Builder (launcher or command line), sees the map in 3D, adjusts it with
the mouse, applies textures to surfaces (right click → Texture file / drag an image),
presses Tab to walk through the map and Ctrl+E to export the GLB. So: **correct, well-named
structure is worth more than fine detail**.
