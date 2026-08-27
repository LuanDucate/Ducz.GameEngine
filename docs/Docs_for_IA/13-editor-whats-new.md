# 13 - What the editor does today

> Reminder: **answer in the user's language**.

This is here so the AI doesn't promise what doesn't exist and, above all, so it can **tell the
user how to adjust what was generated**. Everything here belongs to the Map Builder
(`Ducz.Tools.SceneEditor`).

## Shortcuts worth mentioning in the response

| Key | What it does |
| --- | --- |
| **Tab** | Walk around the map as the character (real collision) |
| **Esc** | Closes the open panel; then **releases the mouse** (a click now selects instead of placing) |
| **B** | **Prefabs** panel (ready-made pieces) |
| **Ctrl+B** | Saves the selected object as a new prefab |
| **P** | Browse the **pieces** of an imported model pack |
| **Alt + right-click** | Selects **one part** of a prefab (wall, window, sidewalk) |
| **Ctrl + left-click** | Paints the part under the cursor with the selected material |
| **Alt + X** | Deletes that part |
| **Ctrl+C / Ctrl+V** | Copy and paste (pastes where the cursor is; works between two windows) |
| **Ctrl+D** | Duplicates alongside |
| **G** | Cycles the grid (0.05 to 4 m) |
| **T** | Top view |
| **Ctrl+E** | Exports GLB |
| **Ctrl+Z / Ctrl+Y** | Undo / redo |

With the mouse released (**Esc**), dragging the left button makes a **selection lasso**: it grabs
several objects and moves, scales, rotates, duplicates, or deletes them all together.
**Shift+click** removes or adds one.

## What this means for the map the AI delivers

- **Grouping is useful**: a `node` with `children` becomes **one object** in the editor - the user
  moves the whole house at once and can still edit a single wall with Alt. Group by element
  (one house, one stall, one lamp post), not the whole map into a single node.
- **Names matter**: the panel shows the node's and the part's `name`. `Casa_A_Parede_Norte` is much
  better than `Box_57`.
- **Materials are shared**: one material = every surface of that type. If the user is going to want
  different textures for two walls, use two materials.

## Default palette, already textured

Every new map is born with materials that use the **1 m prototype grid** that ships with the engine
(`Textures/prototype/`). Names: `stone`, `concrete`, `brick`, `wood`, `plaster`, `asphalt`,
`sidewalk`, `roof`, `grass`, `dirt`, `metal`, `glow`, `glass`, and the colored ones
`proto grey / orange / blue / green / red`.

Use these names when they fit - the map comes out with a legible scale on every surface. Only create
a new material when you need a color/type that doesn't exist:

```json
"telha_colonial": {
  "albedo": "#8f4a35",
  "texture": "Textures/prototype/grid_light.png",
  "uvScale": [1, 1],
  "specular": 0.12
}
```

`uvScale: [1,1]` with `"worldUv": true` = **one grid square per meter** at any block size.

## Available shapes (besides `box`)

`wedge` (ramp), `stairs`, `roofGable`, `roofHip`, `roofShed`, `arch`, `curvedWall`, `tube`,
`prism`, `pyramid`, `roundedBox` and **`polygon`** - any extruded outline:

```json
{ "type": "static", "name": "Galpao_Planta",
  "mesh": { "primitive": "polygon", "height": 7,
            "points": [0,0, 18,0, 18,9, 10,9, 10,14, 0,14] },
  "position": [20, 3.5, -12], "material": "concrete", "worldUv": true }
```

`points` are `x, z` pairs in meters around the piece's own origin, in any direction.
All these shapes automatically get **exact mesh collision**.

## Current limits (don't promise these)

- No hand-sculpted terrain (there is procedural/heightmap terrain).
- No full PBR materials (metalness/AO); there is albedo + normal map + roughness map.
- No animated doors/elevators; no simulated water.
- No regrouping objects ("Ungroup" is a one-way trip).
- Character: 1.8 m, climbs steps up to ~0.35 m, jumps.
