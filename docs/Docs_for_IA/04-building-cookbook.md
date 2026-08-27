# 04 - Building cookbook (JSON recipes)

All recipes assume the ground at y = 0, units in meters, `worldUv: true`. Replace the materials
with the ones from your map. Combine recipes: a building = floor + 4 walls + roof + door.

## Floor / courtyard / street

```json
{ "type": "floor", "name": "Chao", "size": [120, 60], "material": "asfalto", "worldUv": true }
```
One large `floor` as the base of everything. Areas of another material (sidewalk, platform) are
thin `static` slabs on top:

```json
{ "type": "static", "name": "Calcada", "mesh": { "primitive": "box", "size": [40, 0.15, 3] },
  "material": "concreto", "worldUv": true, "position": [0, 0.075, -10] }
```
(height 0.15 → center at y = 0.075; the slab runs from 0 to 0.15).

## Raised platform (0.3 m, with a curb)

```json
{ "type": "static", "name": "Plataforma", "mesh": { "primitive": "box", "size": [60, 0.3, 8] },
  "material": "concreto", "worldUv": true, "position": [0, 0.15, 0] }
```
To climb it on foot use a ramp (below) or leave it at 0.3 m - the player steps up to about 0.35 m.

## Straight wall

Length on the X axis; to turn it into Z, `rotationDegrees: [0, 90, 0]`.
```json
{ "type": "wall", "name": "Parede_Fundo", "size": [10, 3, 0.2], "material": "reboco", "worldUv": true, "position": [0, 1.5, -5] }
{ "type": "wall", "name": "Parede_Lado",  "size": [10, 3, 0.2], "material": "reboco", "worldUv": true, "position": [5, 1.5, 0], "rotationDegrees": [0, 90, 0] }
```

## Room / enclosed building (W × D, height H, thickness t)

Center at (cx, cz). The four walls:

| Wall | size | position | rotation |
| --- | --- | --- | --- |
| North (−Z) | `[W, H, t]` | `[cx, H/2, cz − D/2]` | - |
| South (+Z) | `[W, H, t]` | `[cx, H/2, cz + D/2]` | - |
| East (+X) | `[D, H, t]` | `[cx + W/2, H/2, cz]` | `[0, 90, 0]` |
| West (−X) | `[D, H, t]` | `[cx − W/2, H/2, cz]` | `[0, 90, 0]` |

Flat roof: `static box [W + 0.4, 0.25, D + 0.4]` at `[cx, H + 0.125, cz]`.
Different interior floor: `static box [W − t, 0.1, D − t]` at `[cx, 0.05, cz]`.

## Door (opening in a wall)

A wall with an opening = **two walls + a lintel**. A wall of length W along X, with a door
of width p (1.0–1.2 m) and height 2.2 m in the center:

```json
{ "type": "wall", "name": "Frente_Esq",  "size": [(W-p)/2, 3, 0.2], "position": [cx - (W+p)/4, 1.5, z] },
{ "type": "wall", "name": "Frente_Dir",  "size": [(W-p)/2, 3, 0.2], "position": [cx + (W+p)/4, 1.5, z] },
{ "type": "wall", "name": "Frente_Verga","size": [p, 0.8, 0.2],     "position": [cx, 2.6, z] }
```
(total height 3: lintel from 2.2 to 3.0 → height 0.8, center 2.6). Replace the formulas with numbers.

## Window

Same principle: a low wall (sill 0–1 m), a lintel (2.1–3 m) and, optionally, glass in the opening:
```json
{ "type": "static", "name": "Vidro_01", "mesh": { "primitive": "box", "size": [1.5, 1.1, 0.05] },
  "material": "vidro", "position": [x, 1.55, z], "collider": { "shape": "auto" } }
```

## Pillar / column

```json
{ "type": "static", "name": "Pilar_01", "mesh": { "primitive": "cylinder", "radius": 0.25, "height": 4.5 },
  "material": "concreto", "worldUv": true, "position": [x, 2.25, z] }
```
Square: `box [0.4, 4.5, 0.4]`. Repeat every 6–10 m under canopies.

## Roof (gable, hip, shed)

```json
{ "type": "static", "name": "Telhado", "mesh": { "primitive": "roofGable", "size": [6, 1.8, 8], "overhang": 0.3 },
  "material": "telha", "worldUv": true, "position": [cx, H + 0.9, cz] }
{ "type": "static", "name": "Telhado4", "mesh": { "primitive": "roofHip", "size": [6, 1.8, 8], "ridgeLength": 3, "overhang": 0.3 },
  "material": "telha", "worldUv": true, "position": [cx, H + 0.9, cz] }
{ "type": "static", "name": "TelhadoShed", "mesh": { "primitive": "roofShed", "size": [5, 1.2, 5], "thickness": 0.15 },
  "material": "telha_zinco", "worldUv": true, "position": [cx, H + 0.6, cz] }
```
On top of walls of height H, the roof sits at `y = H + roof_height/2`. Rotate with yaw 90 to
turn the ridge. `overhang` creates the eaves.

## Arch / curved passage

```json
{ "type": "static", "name": "Arco", "mesh": { "primitive": "arch", "size": [4, 4], "thickness": 0.5,
  "openingWidth": 2.2, "openingHeight": 2 }, "material": "pedra", "worldUv": true, "position": [x, 2, z] }
```
The opening is `openingWidth` wide, rises straight up to `openingHeight` and closes in a
semicircle. The player passes through (the collision is exact).

## Curved wall / arena / silo

```json
{ "type": "static", "name": "Muro_Curvo", "mesh": { "primitive": "curvedWall", "radius": 6, "height": 3,
  "thickness": 0.4, "arcDegrees": 120, "segments": 24 }, "material": "concreto", "worldUv": true, "position": [x, 1.5, z] }
```
`arcDegrees: 360` makes a hollow cylinder (silo, tower). Rotate in Y to choose which way the
curve opens.

## Canopy / awning on pillars

```json
{ "type": "static", "name": "Cobertura_A", "mesh": { "primitive": "box", "size": [48, 0.2, 10] },
  "material": "metal", "worldUv": true, "position": [0, 4.6, 0] }
```
Pillars of height 4.5 (center y 2.25) at the edges; the slab runs from 4.5 to 4.7. Sloped canopy:
`rotationDegrees: [8, 0, 0]` (tilts around X). Gable roof: two sloped slabs
`[±20, 0, 0]` meeting at the ridge.

## Access ramp (accessibility / platform)

Two options: the `ramp` prefab (thin, no sides) or the `wedge` shape (a **solid** ramp, with
closed sides - better for garages, embankments and platforms):
```json
{ "type": "static", "name": "Rampa_Solida", "mesh": { "primitive": "wedge", "size": [3, 1, 6] },
  "material": "concreto", "worldUv": true, "position": [x, 0.5, z] }
```
(the `wedge` is centered: it rises from `z−3` to `z+3`; `position.y` = height/2 when resting on the ground.)


The ramp is **centered** on `position` (X/Z) and `position.y` is the height of the low end. With no
rotation it rises toward **+Z**: low end at `z − length/2`, high end at `z + length/2`.
```json
{ "type": "ramp", "name": "Rampa_01", "size": [2, 0.3, 4], "material": "concreto", "worldUv": true, "position": [x, 0, z] }
```
To rise toward −Z: `rotationDegrees: [0, 180, 0]`; +X: `[0, 90, 0]`; −X: `[0, -90, 0]`.
Comfortable slope: height/length ≤ 1/8 (ramp) or ≤ 1/2 (a ramp replacing stairs).
**Recipe for meeting a slab**: if the slab edge is at `zBorda` and the ramp rises toward +Z
with length L, use `z = zBorda − L/2` (the high end lands exactly on the edge). E.g.: a slab that
starts at z = 24, an 8 m ramp rising toward −Z (yaw 180): center at `z = 24 + 4 = 28`.

## Stairs

**Use the ready-made shape** (one node, exact collision, adjustable in the editor):
```json
{ "type": "static", "name": "Escada_01", "mesh": { "primitive": "stairs", "size": [2, 2, 3], "steps": 10 },
  "material": "concreto", "worldUv": true, "position": [x, 1, z] }
```
(`position` is the center: the staircase runs from `y−height/2` to `y+height/2` and from `z−depth/2`
to `z+depth/2`, rising toward +Z; rotate with `rotationDegrees` in Y.)

If you need loose steps (irregular shape), stack slabs - 18 cm tall, 30 cm deep, rising toward −Z:
```json
{ "type": "static", "name": "Degrau_1", "mesh": { "primitive": "box", "size": [1.5, 0.18, 0.3] }, "material": "concreto", "position": [x, 0.09, z] },
{ "type": "static", "name": "Degrau_2", "mesh": { "primitive": "box", "size": [1.5, 0.36, 0.3] }, "material": "concreto", "position": [x, 0.18, z - 0.3] },
{ "type": "static", "name": "Degrau_3", "mesh": { "primitive": "box", "size": [1.5, 0.54, 0.3] }, "material": "concreto", "position": [x, 0.27, z - 0.6] }
```
Rule: step k (1-based) has height `0.18*k`, center y `0.09*k`, z `z0 − 0.3*(k−1)`. The
character climbs steps up to about 0.35 m; real stairs work. A **ramp** of the same
rise is simpler and works too.

## Walkway / elevated bridge

Two staircases or ramps + a high slab + guardrails:
```json
{ "type": "static", "name": "Passarela_Piso", "mesh": { "primitive": "box", "size": [3, 0.25, 30] }, "material": "concreto", "worldUv": true, "position": [x, 5.125, z] },
{ "type": "wall", "name": "Passarela_Guarda_E", "size": [30, 1.1, 0.1], "material": "metal", "position": [x - 1.45, 5.8, z], "rotationDegrees": [0, 90, 0] },
{ "type": "wall", "name": "Passarela_Guarda_D", "size": [30, 1.1, 0.1], "material": "metal", "position": [x + 1.45, 5.8, z], "rotationDegrees": [0, 90, 0] }
```

## Fence / railing / guardrail / curb

A low, thin `wall`: guardrail `[L, 1.1, 0.08]` (center y 0.55 above the surface);
fence `[L, 2, 0.05]`; curb `static box [L, 0.15, 0.2]`.

## Bench, trash can, post, sign

```json
{ "type": "static", "name": "Banco_01", "mesh": { "primitive": "box", "size": [1.8, 0.08, 0.45] }, "material": "madeira", "position": [x, 0.46, z] },
{ "type": "static", "name": "Banco_01_Pe_E", "mesh": { "primitive": "box", "size": [0.08, 0.42, 0.45] }, "material": "metal", "position": [x - 0.8, 0.21, z] },
{ "type": "static", "name": "Banco_01_Pe_D", "mesh": { "primitive": "box", "size": [0.08, 0.42, 0.45] }, "material": "metal", "position": [x + 0.8, 0.21, z] },
{ "type": "static", "name": "Lixeira_01", "mesh": { "primitive": "cylinder", "radius": 0.25, "height": 0.9 }, "material": "metal", "position": [x, 0.45, z] },
{ "type": "static", "name": "Poste_01", "mesh": { "primitive": "cylinder", "radius": 0.08, "height": 6 }, "material": "metal", "position": [x, 3, z] },
{ "type": "pointLight", "name": "Poste_01_Luz", "position": [x, 5.8, z], "color": "#ffe2b0", "energy": 2.5, "range": 14 },
{ "type": "static", "name": "Placa_01", "mesh": { "primitive": "box", "size": [1.2, 0.6, 0.04] }, "material": "placa", "position": [x, 2.4, z] }
```

## Tunnel / horizontal tube (e.g. Curitiba's "tube stations")

A cylinder laid on its side: the Y axis becomes X with `rotationDegrees: [0, 0, 90]` (length = `height`).
```json
{ "type": "static", "name": "Tubo_01", "mesh": { "primitive": "cylinder", "radius": 1.5, "height": 12 },
  "material": "vidro", "position": [x, 1.6, z], "rotationDegrees": [0, 0, 90], "collider": { "shape": "none" } }
```
Since the cylinder is only hollow in appearance (the collision is a box), leave `collider: none` on
the tube and put a **floor slab** inside (`static box [12, 0.1, 2.4]` at the y of the tube's floor)
and a **pair of low invisible walls** if you want to block the sides. Raise the tube floor 0.3–0.9 m
+ an access ramp.

## Parked vehicle (bus as a block)

`static box [12, 3.2, 2.5]` at `[x, 1.6 + 0.2, z]` (0.2 m of clearance under the chassis: optional) -
good as a scale reference; the user swaps it for a model later.

## Grouping with `node` + `children`

Child positions are relative to the parent - useful for repeating a "kit" (bus shelter,
kiosk) in several places:
```json
{ "type": "node", "name": "Abrigo_01", "position": [20, 0, -8], "children": [
  { "type": "static", "name": "Abrigo_01_Teto", "mesh": { "primitive": "box", "size": [4, 0.1, 1.6] }, "material": "metal", "position": [0, 2.55, 0] },
  { "type": "static", "name": "Abrigo_01_P1", "mesh": { "primitive": "box", "size": [0.08, 2.5, 0.08] }, "material": "metal", "position": [-1.9, 1.25, -0.7] },
  { "type": "static", "name": "Abrigo_01_P2", "mesh": { "primitive": "box", "size": [0.08, 2.5, 0.08] }, "material": "metal", "position": [1.9, 1.25, -0.7] },
  { "type": "wall",   "name": "Abrigo_01_Fundo", "size": [4, 2.5, 0.05], "material": "vidro", "position": [0, 1.25, -0.75] }
]}
```
To repeat, copy the group changing `name` and `position`.

## Building with a modular pack (kit of ready-made pieces)

A modular kit comes in a single file with dozens of pieces (rampart, gate, tower, barrel).
To use them piece by piece:

```json
{ "type": "model", "name": "Muralha_01", "path": "Assets/Models/wall_pack.glb",
  "subNode": "SM_MMW_017", "subNodePivot": "base",
  "position": [12, 0, -6], "rotationDegrees": [0, 90, 0],
  "collider": { "shape": "auto" } }
```

- **`"subNodePivot": "base"` is essential**: without it the piece goes to the place it occupies
  *inside the file* (the whole kit ends up stacked in a corner). With it, the center of the piece's
  base lands exactly on `position`.
- `"collider": {"shape": "auto"}` gives exact mesh collision - important for passing through the
  arches and gates instead of bumping into a box.
- Find out the kit's module before positioning (the medieval pack uses **3 m**): align everything
  to that step and the pieces fit with no gap.
- Not every piece with a "hole" is passable: an arch might start 2.5 m off the ground. In the editor,
  the **P** key opens the pack's piece browser so you can see each one.


## Using ready-made prefabs (the fastest way to get quality)

The engine ships a library of assembled pieces in `src/Prefabs/*.duczprefab.json` (houses,
streets, buildings, trees, lamps...). **For the AI, this is gold**: instead of assembling a house
from 15 boxes, copy the prefab's `node` and position it.

```python
import json, io, glob, os
LIB = r"E:\Repos\Ducz.GameEngine\src\Prefabs"
biblioteca = {}
for f in glob.glob(os.path.join(LIB, "*.duczprefab.json")):
    p = json.load(io.open(f, encoding="utf-8"))
    biblioteca[p["name"]] = p

def coloca(nome, x, z, yaw=0):
    p = biblioteca[nome]
    materiais.update(p.get("materials") or {})      # the map needs its materials
    node = json.loads(json.dumps(p["node"]))        # copy
    node["name"] = nome.replace(" ", "_") + "_" + str(len(nodes))
    node["position"] = [x, 0, z]
    if yaw:
        node["rotationDegrees"] = [0, yaw, 0]
    nodes.append(node)

coloca("Straight street 12 m", 0, 0)
coloca("House 2-story 8x6", 0, 11, 180)
```

**Snapping rules** (this is what makes the map line up perfectly):

- Street and crossroads are **12 m** modules: `x` and `z` in multiples of 12.
- A street has 8 m of asphalt + 2 m of sidewalk on each side, so the facade of the houses sits
  from **z = ±11** for a street at `z = 0`.
- A floor = **3 m**; the library's buildings use 3.2 m per floor.
- `yaw` rotates in steps of 90: `180` turns the house toward the street below, `90`/`270` toward
  the vertical streets.

Mix in loose blocks for what the library does not have, and say in your answer which prefabs you
used - the user can swap any of them via the editor's **B** panel.


## Interior/canopy lighting

Under canopies the sun does not reach: place a `pointLight` every 8–10 m (`energy 2`, `range 12`,
y ≈ canopy height − 0.3) and `ambientIntensity` 0.35–0.45 in the `environment`.
