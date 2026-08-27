# Prototype textures

Every new map starts with a palette that is **already textured**: a clean 1 m grid, drawn by
the engine and shipped with it. The grid is *multiplied* by each material's colour, so
`brick` still looks like brick and `asphalt` still looks like asphalt - they just gain a
scale reference on every surface. That is the difference between a blockout that reads as
deliberate and one that reads as flat paint.

Drop your own image on a material at any time (right-click an object → *Texture file...*, or
drag a file onto it) and it replaces the grid. Nothing else changes.

## The set

`<engine>/Textures/prototype/` - 256 x 256 tiles, one square metre each at `uvScale: 1`.

| File | Use |
| --- | --- |
| `grid_light.png` | Near-white grid - the default under most colours |
| `grid_mid.png` | Slightly darker, for concrete and stone |
| `grid_dark.png` | Dark grid for asphalt and shadowed surfaces |
| `grid_fine.png` | Half-metre sub-grid, for detail work |
| `checker.png` | Two-tone check - pavements, floors |
| `proto_grey/orange/blue/green/red/purple.png` | The classic coloured prototype tiles, for blocking out by function (walkable / blocked / goal...) |

They are generated, not sampled from an asset pack, so they ship with the engine with no
licence strings attached.

## The starting palette

`DefaultMaterials.Create()` in the engine builds the palette used by new projects (both the
launcher templates and the editor's blank map), so the two never drift apart:

`stone`, `concrete`, `brick`, `wood`, `plaster`, `asphalt`, `sidewalk`, `roof`, `grass`,
`dirt`, `metal`, `glow`, `glass`, plus `proto grey / orange / blue / green / red`.

```csharp
var doc = new SceneDocument { Materials = DefaultMaterials.Create() };
```

In JSON a gridded material is just a normal material with a texture:

```json
"concreto": {
  "albedo": "#a5a29c",
  "texture": "Textures/prototype/grid_mid.png",
  "uvScale": [1, 1],
  "specular": 0.08
}
```

With `"worldUv": true` on the node, `uvScale: 1` means **one grid square per metre** on any
block size - a 1 m cube and a 40 m floor show the same grid.

## Where the paths point

`Textures/prototype/...` is relative to the engine's own content folder. Asset paths are
resolved in this order:

1. as given (absolute, or relative to the working directory),
2. inside the open project,
3. inside the engine's content folder and next to it.

So a project can override a stock texture simply by having a file with the same relative path
in its own folder.
