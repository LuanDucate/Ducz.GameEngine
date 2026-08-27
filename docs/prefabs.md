# Prefabs - ready-made pieces

A prefab is a whole piece of map in one click: a house with walls, windows and a roof, a
street segment with pavements and markings, a tree, a lamp post with a real light.
Press **B** in the Map Builder - or pick one straight from the **PREFABS** list in the left sidebar - and drop it like any other block - ghost preview,
grid snapping, **R** to rotate, **+/-** to scale.

The point is to make a hand-built map look good without assembling it box by box.

## Using them

| | |
| --- | --- |
| **B** | Open the prefab browser (tabs by category, paged) |
| Click a piece | It becomes the selected block; click in the map to place it |
| **R** | Rotate 90° before placing |
| **Ctrl+B** | Save the selected object as a new prefab |

A placed prefab is **one object**: right-click selects the whole thing, and you can move it,
rotate it, duplicate it (Ctrl+D) or delete it as a unit. Its materials are copied into the
map automatically, so you can retexture them afterwards from the palette like any other
material.

## Editing what came ready

A prefab is a starting point, not a sealed box. Hold **Alt** to reach the part under the
cursor - one wall, one window band, one pavement:

| | |
| --- | --- |
| **Alt + right-click** | Select that part. The properties panel then edits *it*: size, position, material, rotation, collision |
| **Ctrl + left-click** | Paint the part under the cursor with the selected material (no Alt needed) |
| **Alt + X** (or **Delete** in the panel) | Remove that part - the windows off a building, a pavement off a street |
| **Duplicate** | Copies the part inside the same prefab, one grid step across |
| **Ungroup parts** | Breaks the prefab into loose objects, each fully independent |

Positions shown for a part are **relative to the prefab**, which is what you want when
nudging a window along a wall. The prefab keeps working as a unit: a plain right-click still
selects the whole thing, so you can move the house after editing its windows.

Ungrouping is one-way (there is no re-group yet); it bakes the prefab's position, rotation
and scale into each child, so nothing moves when it happens.

## What ships with the editor

37 prefabs built from the engine's own shapes - no downloaded assets needed:

| Category | Pieces |
| --- | --- |
| **Streets** | Straight street, street without markings, 4-way junction, corner, square with fountain, parking strip |
| **Houses** | One-floor house, two-floor house, house with porch, house with garage, brick house |
| **Buildings** | 4-floor block, shop with awning and sign, 10-floor tower, corner shop, warehouse |
| **Structures** | Stairs, ramp, wall, wall with gate, wooden fence, stone bridge, gable roof, hip roof |
| **Nature** | Medium tree, tall tree, bush, planter, group of trees, grass patch |
| **Urban** | Street lamp (with light), bench, bus shelter, bin, water tank, roadworks barrier, street sign |

**They share a module so they line up:** streets are 12 m long and 8 m wide with 2 m
pavements, floors are 3 m, buildings sit on a 2 m grid. Place streets end to end on a 1 m
grid and everything meets.

## Making your own

Select an object in the map and press **Ctrl+B**. The object (with everything under it) and
the materials it uses are written to your library, and it shows up under the **Meus** tab.
Group several objects first if you want them saved together.

Prefabs are plain JSON, so you can also write or edit them by hand:

```json
{
  "name": "House 1-story 8x6",
  "category": "Houses",
  "description": "One floor, gable roof.",
  "materials": {
    "plaster": { "albedo": "#d8cfbe", "specular": 0.06, "uvScale": [0.4, 0.4] },
    "tiles":   { "albedo": "#8f4a35", "specular": 0.12 }
  },
  "node": {
    "type": "node", "name": "House",
    "children": [
      { "type": "static", "name": "Wall_N", "mesh": { "primitive": "box", "size": [8, 3, 0.25] },
        "position": [0, 1.8, -3], "material": "plaster", "worldUv": true },
      { "type": "static", "name": "Roof", "mesh": { "primitive": "roofGable", "size": [8.8, 2.2, 6.8], "overhang": 0.45 },
        "position": [0, 4.4, 0], "material": "tiles", "worldUv": true }
    ]
  }
}
```

The `node` is an ordinary scene node ([JSON scenes](json-scenes.md)) - anything the format
supports works, including lights, physics crates and imported models. Child positions are
relative to the prefab's own origin; keep the base at `y = 0` so it sits on the ground when
placed.

## Where they live

| Folder | What for |
| --- | --- |
| `<engine>/Prefabs/` | The library that ships with the Map Builder - **installed with it**, nothing to download |
| `%AppData%\DuczEngine\prefabs\` | Yours - where **Ctrl+B** saves, and where you can drop files |
| `<project>/Prefabs/` | Prefabs that belong to one map only |

Later folders win, so a project can override a stock prefab with its own version. File names
end in `.duczprefab.json`.

A shipped prefab references textures by a path relative to the engine folder
(`Textures/prototype/grid_light.png`); your own prefabs can also point at models you imported.
Asset paths are looked up in the open project first, then in the engine's own content, so the
stock pieces work in every project without copying anything.

## Turning a downloaded pack into prefabs

Model kits usually ship as one file with many pieces, and a single prop is often several of
them - a market stall is a counter, an awning and posts. Import the file, press **P** to browse
its pieces, place the ones you want, then select the assembly and press **Ctrl+B** to keep it
as one prefab. From then on the whole prop goes in with one click.
