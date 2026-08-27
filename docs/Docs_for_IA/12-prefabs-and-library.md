# 12 - Prefabs: the library of ready-made pieces

> Reminder: **answer in the user's language**. The prefab names below are identifiers
> and stay as they are.

The engine ships a library of **assembled pieces** in `src/Prefabs/*.duczprefab.json`. Each
file has a `node` (usually `type: "node"` with `children`) plus the `materials` it uses.

**For the AI this changes how you build**: instead of assembling a house from 15 boxes, you
copy a prefab's `node` and choose where it goes. Less JSON, a better result, and the user can
swap the whole piece later with two clicks.

## How to use it in a generator

```python
import json, io, glob, os

LIB = r"E:\Repos\Ducz.GameEngine\src\Prefabs"
biblioteca = {}
for arquivo in glob.glob(os.path.join(LIB, "**", "*.duczprefab.json"), recursive=True):
    p = json.load(io.open(arquivo, encoding="utf-8"))
    biblioteca[p["name"]] = p

nodes, materiais, contagem = [], {}, {}

def coloca(nome, x, z, yaw=0, y=0):
    p = biblioteca[nome]
    materiais.update(p.get("materials") or {})     # the map needs the piece's materials
    node = json.loads(json.dumps(p["node"]))       # deep copy
    contagem[nome] = contagem.get(nome, 0) + 1
    node["name"] = "%s_%d" % (nome.replace(" ", "_"), contagem[nome])
    node["position"] = [x, y, z]
    if yaw:
        node["rotationDegrees"] = [0, yaw, 0]
    nodes.append(node)

coloca("Straight street 12 m", 0, 0)
coloca("House 2-story 8x6", 0, 11, 180)
```

Three rules that always hold:

1. **Copy the `node`** (deep copy), don't reference the same object twice.
2. **Merge the prefab's `materials`** into the map's `materials`, otherwise the piece has no color.
3. **Give each instance a unique `name`**.

## What's available

| Category | Pieces |
| --- | --- |
| **Streets** | `Straight street 12 m`, `Plain street 12 m`, `Crossroads 12x12`, `Street corner 12x12`, `Plaza 16x16`, `Parking lot 12x6` |
| **Houses** | `House 1-story 8x6`, `House 2-story 8x6`, `House with porch 9x7`, `House with garage 8x6`, `Brick house 10x8` |
| **Buildings** | `Apartment block 4-floor 12x10`, `Office building 14x10`, `Tower 10-floor 12x12`, `Corner shop 10x10`, `Warehouse 20x12` |
| **Structures** | `Stairs 3 m`, `Ramp 1.5 m`, `Wall 8 m`, `Wall with gate 8 m`, `Wooden fence 6 m`, `Bridge 12 m`, `Gable roof 10x8`, `Hip roof 10x8` |
| **Nature** | `Medium tree`, `Tall tree`, `Bush`, `Flower bed 4x2`, `Tree cluster`, `Lawn 12x12` |
| **Urban** | `Street lamp` (with a real light), `Park bench`, `Bus stop`, `Trash can`, `Water tank`, `Construction barrier`, `Street sign` |

Read the folder before using it: the list may have grown, and the user may have their own prefabs
in `%AppData%\DuczEngine\prefabs\` or in `<project>/Prefabs/`.

## Modular fit (this is what makes the map close up)

- **Street and crossroads are 12 m modules** → use `x` and `z` as multiples of 12.
- The street has 8 m of asphalt + 2 m of sidewalk on each side → the facade of the houses starts
  at `z = ±11` from a street at `z = 0`.
- **A floor = 3 m** (the library's buildings use 3.2 m).
- `yaw` turns in 90-degree steps: `180` faces the house toward the lower street, `90`/`270` toward
  the vertical ones.

## Pieces from model packs (imported kits)

A modular kit comes in a single file with dozens of pieces. To use it piece by piece:

```json
{ "type": "model", "name": "Muralha_01", "path": "Assets/Models/wall_pack.glb",
  "subNode": "SM_MMW_017", "subNodePivot": "base",
  "position": [12, 0, -6], "rotationDegrees": [0, 90, 0],
  "collider": { "shape": "auto" } }
```

- **`"subNodePivot": "base"` is essential**: without it the piece goes to the spot it occupies
  *inside the file* and the whole kit ends up stacked in one corner.
- `"collider": {"shape": "auto"}` gives an exact mesh collision - needed to walk through arches
  and gates instead of bumping into a box.
- Find out the kit's module first (the medieval pack uses 3 m) and align everything to that step.

## File paths inside a prefab

A prefab that ships with the engine references things by a path **relative to the engine folder**
(`Textures/prototype/grid_light.png`). The engine looks for a file in this order:
as given → inside the open project → in the engine content and the folders above it. That's why
the piece works in any project without copying anything.

## What to say in the response

Tell the user **which prefabs** you used. The user can:

- swap any of them via the **B** panel;
- **Alt + right-click** to select a part (a wall, a sidewalk) and change its material,
  size, or delete it;
- **"Ungroup parts"** to break the piece into loose objects.
