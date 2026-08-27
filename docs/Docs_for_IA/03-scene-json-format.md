# 03 - Scene JSON format (`scenes/main.json`)

Complete, precise reference. All field names are camelCase; omitted fields take the default noted;
the reader ignores unknown fields and never breaks on an invalid node (it warns and creates an
empty node) - but the AI must generate valid JSON.

## Structure

```json
{
  "name": "NomeDoMapa",
  "environment": { ... },
  "materials": { "chave": { ... }, ... },
  "nodes": [ { ...node... }, ... ]
}
```

## `environment` (optional)

| Field | Default | Meaning |
| --- | --- | --- |
| `background` | `"proceduralSky"` | or `"solidColor"` (+ `clearColor`) |
| `skyTop`, `skyHorizon`, `skyGround` | blue/light/earth | procedural sky colors |
| `sunDisk` | `true` | draws the sun |
| `ambientColor`, `ambientIntensity` | white, `0.25` | ambient light (0.3–0.4 makes interiors less dark) |
| `fog` | none | `{ "color": "#b8cfe8", "start": 60, "end": 220 }` - use large start/end on large maps |

## `materials`

Dictionary name → material. Nodes reference it by name (`"material": "concreto"`) or use an
inline object (`"material": { "albedo": "#ff0000" }`).

| Field | Default | Meaning |
| --- | --- | --- |
| `albedo` | `"#ffffff"` | base color (multiplies the texture). Alpha < 1 → needs `transparent: true` |
| `texture` | - | image path relative to the project (`Assets/Textures/x.png`). **Do not use if the file does not exist** |
| `filter` | `"linear"` | `"nearest"` for pixel art |
| `checkerboard` | - | procedural texture: `{ "colorA": "#..", "colorB": "#..", "cells": 4, "size": 256 }` |
| `uvScale` | `[1,1]` | with `worldUv` on nodes = **repetitions per meter** (0.5 = one tile every 2 m) |
| `uvOffset` | `[0,0]` | UV offset |
| `specular` | `0.4` | glossiness: 0.05 matte (asphalt, grass), 0.4 default, 0.9 metal/glass |
| `shininess` | `32` | sharpness of the highlight: 8 matte … 128 glass |
| `emission`, `emissionEnergy` | - , `1` | self-illumination (signs, lamps) |
| `transparent` | `false` | glass: `"albedo": "#8fd0ff66", "transparent": true` |
| `unshaded` | `false` | ignores light (flat sign) |
| `doubleSided` | `false` | draws both sides |
| `alphaCutout` | `0` | alpha cutout (foliage) |
| `castShadows`, `receiveShadows` | `true` | shadows |
| `normalMap` | auto | normal map (relief). If omitted, it looks for `<texture>_normal.png` / `_nrm` / `_n` alongside it |
| `normalStrength` | `1` | relief strength (0 = flat, >1 exaggerated) |
| `roughnessMap` | auto | gray roughness map (white = matte). Looks for `_roughness` / `_rough` |
| `autoMaps` | `true` | turn off to stop looking for sibling maps |

## Fields common to every node

| Field | Meaning |
| --- | --- |
| `type` | node type (table below) - required |
| `name` | readable, unique name; appears in the editor and in Godot |
| `position` | `[x, y, z]` in meters (see semantics by type in [01](01-overview.md)) |
| `rotationDegrees` | `[pitch(X), yaw(Y), roll(Z)]` in degrees. Yaw 90 turns a wall from X to Z |
| `scale` | `[x, y, z]`; avoid it - prefer real sizes in `size`/`mesh` |
| `visible` | `true` |
| `groups` | `["name"]` tags (become metadata in the GLB) |
| `worldUv` | `true` → UVs in meters (recommended on ALL blocks/prefabs) |
| `faceMaterials` | material per face of a box: `{ "all": "terra", "top": "grama" }`. Keys: `top`, `bottom`, `front`, `back`, `left`, `right`, `sides`, `all` |
| `material` | material key or inline object |
| `collider` | see below; omit = automatic |
| `children` | list of child nodes (positions relative to the parent) - good for grouping (`type: "node"` as parent) |

## Node types

### Structure (what the AI uses most)

| `type` | Creates | Key fields | `position` = |
| --- | --- | --- | --- |
| `floor` | solid floor (0.5 m thick, downward) | `size: [x, z]` | top of the floor |
| `wall` | solid wall | `size: [length(X), height(Y), thickness(Z)]` | center (y = height/2 if resting on the ground) |
| `static` | solid block with any primitive | `mesh: { primitive, size/radius/height }` | center |
| `ramp` | walkable ramp; with no rotation it rises toward **+Z** (low end at `z−len/2`, high at `z+len/2`) | `size: [width, height, length]` | center in the plane; `y` = height of the low end |
| `crate` | box with physics (falls/pushes) | `size: [side]`, `mass` | center |
| `mesh` | visual only, no collision | `mesh` | center |
| `node` | empty grouper | `children` | origin of the group |
| `terrain` | terrain by function/heightmap | `terrain: { mode: "flat"/"hills"/"heightmap", sizeX, sizeZ, amplitude, frequency, resolution }` | center |
| `model` | GLB/FBX/OBJ file as a prop | `path`, `subNode`, `subNodePivot`, `collider: {"shape":"auto"}` | origin of the model (or the base of the piece, with `"subNodePivot": "base"`) |

### `mesh` (primitives for `static`/`mesh`/`rigid`)

```json
"mesh": { "primitive": "box", "size": [x, y, z] }
"mesh": { "primitive": "cylinder", "radius": 0.3, "height": 4 }     // Y axis (pillar). Lay it down with rotationDegrees [0,0,90] or [90,0,0]
"mesh": { "primitive": "sphere", "radius": 0.5 }
"mesh": { "primitive": "cone", "radius": 0.5, "height": 1 }
"mesh": { "primitive": "capsule", "radius": 0.35, "height": 1.8 }
"mesh": { "primitive": "torus", "radius": 0.5, "thickness": 0.15 }
"mesh": { "primitive": "plane", "size": [x, z] }                    // no thickness, facing up
"mesh": { "primitive": "quad", "size": [width, height] }          // vertical, facing +Z (sign/board)
```

A `static` without a `collider` gets automatic collision matching the primitive (box/sphere/capsule;
cylinder and cone become an enclosing box).

### Construction shapes (new - use them!)

Beyond the basic primitives, there are ready-made construction shapes. All are **centered on the
`position`** (like a box) and get **exact mesh collision** automatically:

| `primitive` | What it is | Fields |
| --- | --- | --- |
| `wedge` | solid ramp: low at −Z, high at +Z, walkable top | `size: [width, height, length]` |
| `stairs` | staircase rising toward +Z (no need to stack steps anymore!) | `size: [width, height, depth]`, `steps` (default 8) |
| `roofGable` | gable roof (ridge along X) | `size: [width, height, depth]`, `overhang` |
| `roofHip` | hip roof (`ridgeLength` 0 = pyramidal) | `size`, `ridgeLength`, `overhang` |
| `roofShed` | shed roof (sloped slab) | `size: [width, height, depth]`, `thickness` |
| `arch` | wall with an arched opening | `size: [width, height]`, `thickness`, `openingWidth`, `openingHeight` |
| `curvedWall` | curved wall / ring (360 = hollow cylinder) | `radius`, `height`, `thickness`, `arcDegrees`, `segments` |
| `tube` | hollow tube on the Y axis | `radius`, `height`, `thickness`, `segments` |
| `prism` | N-sided prism | `radius`, `height`, `sides` |
| `pyramid` | rectangular-base pyramid | `size: [width, height, depth]` |
| `roundedBox` | box with beveled corners (props look much better) | `size`, `bevel` |

```json
{ "type": "static", "name": "Escadaria", "mesh": { "primitive": "stairs", "size": [3, 2.4, 3.6], "steps": 12 }, "material": "concreto", "worldUv": true, "position": [0, 1.2, 0] },
{ "type": "static", "name": "Rampa_Garagem", "mesh": { "primitive": "wedge", "size": [3, 1, 6] }, "material": "concreto", "worldUv": true, "position": [10, 0.5, 0] },
{ "type": "static", "name": "Telhado_Casa", "mesh": { "primitive": "roofGable", "size": [6, 1.8, 8], "overhang": 0.3 }, "material": "telha", "worldUv": true, "position": [0, 3.9, 0] },
{ "type": "static", "name": "Portico", "mesh": { "primitive": "arch", "size": [4, 4], "thickness": 0.5, "openingWidth": 2.2, "openingHeight": 2 }, "material": "pedra", "worldUv": true, "position": [0, 2, -10] }
```

**Practical rules:**
- `wedge` replaces the `ramp` prefab when you want a **solid** ramp (with closed sides).
- `stairs` replaces stacking N boxes: a single node, with correct collision.
- Roofs: `position.y` = base of the roof box + height/2. On top of a 3 m wall with a
  1.8 m roof: `y = 3 + 0.9 = 3.9`.
- `curvedWall` solves plazas, arenas, silos and street curves without workarounds.
- `curvedWall`, `tube` and `prism` use `radius`/`height`/`thickness`, but they also accept
  `size: [diameter, height, thickness]` - whichever is filled in (> 0) wins. Use one of the two
  ways, don't mix them.
- All of these shapes get **exact mesh collision** on their own: the AI does not need to declare
  a `collider` (only if you want the cheapest one: `"collider": { "shape": "box" }`).

### `collider` (optional)

```json
"collider": { "shape": "auto" }                       // default
"collider": { "shape": "none" }                       // decorative, passable
"collider": { "shape": "box", "size": [2, 1, 2] }
"collider": { "shape": "mesh" }                        // for model: exact mesh (auto already does this)
```

### Lighting

| `type` | Fields | Notes |
| --- | --- | --- |
| `directionalLight` | `rotationDegrees: [-50, 35, 0]`, `energy: 1.1`, `color`, `shadows` | the sun; **exactly 1 per map**; points along the node's −Z, so pitch −50 = 50° downward |
| `pointLight` | `position` (center), `color: "#ffe2b0"`, `energy: 2`, `range: 10` | lamps, posts (place at y 3–5). Don't overdo it: dozens, not hundreds |
| `spotLight` | `energy`, `range`, `angle: 45`, `softness`, aim with `rotationDegrees` | spotlight |

### Player and camera (required for the play-test)

```json
{ "type": "spawn",  "name": "SpawnPoint", "position": [x, chao, z] },
{ "type": "player", "name": "Player", "position": [x, chao + 1.2, z] },
{ "type": "thirdPersonCamera", "name": "MainCamera", "target": "Player", "distance": 6.5, "targetHeight": 1.4, "current": true }
```

Exactly one of each. `player.position.y` = ground height at the point + 1.2 (center of the 1.8 m
capsule). The spawn must be in an open area (not inside a wall).

### Others (rarely needed in maps)

`camera` / `flyCamera` (`fov`, `near`, `far`, `current`), `particles` (fire, smoke),
`audio`/`audio3d` (WAV), `area` (invisible trigger), `rigid` (generic physics body).

## Commented example of a node of each structural type

```json
{ "type": "floor",  "name": "Patio",       "size": [80, 40], "material": "asfalto", "worldUv": true, "position": [0, 0, 0] },
{ "type": "wall",   "name": "Muro_Norte",  "size": [80, 2, 0.3], "material": "concreto", "worldUv": true, "position": [0, 1, -20] },
{ "type": "wall",   "name": "Muro_Leste",  "size": [40, 2, 0.3], "material": "concreto", "worldUv": true, "position": [40, 1, 0], "rotationDegrees": [0, 90, 0] },
{ "type": "static", "name": "Pilar_01",    "mesh": { "primitive": "cylinder", "radius": 0.3, "height": 4.5 }, "material": "concreto", "worldUv": true, "position": [-10, 2.25, 0] },
{ "type": "static", "name": "Cobertura",   "mesh": { "primitive": "box", "size": [30, 0.25, 8] }, "material": "metal", "worldUv": true, "position": [0, 4.625, 0] },
{ "type": "ramp",   "name": "Rampa_Acesso","size": [2, 0.4, 4], "material": "concreto", "worldUv": true, "position": [5, 0, 6] },
{ "type": "pointLight", "name": "Luz_01",  "position": [-10, 4.2, 0], "color": "#ffe2b0", "energy": 2, "range": 12 }
```
