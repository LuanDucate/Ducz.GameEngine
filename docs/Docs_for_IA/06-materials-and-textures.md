# 06 - Materials and textures

## Principle: semantic "placeholder" materials

The user almost always says "I'll add the textures later." So the AI creates **one material per
surface type**, with a name that says what it is and a plausible color - no `texture`. In the
editor, the user right-clicks an object → *Texture file...* and the image goes into the whole
material (every object that uses `concreto` changes at once). Because of this:

- **One material per semantics**, not per object: `asfalto`, `calcada`, `concreto`, `reboco`,
  `metal`, `vidro`, `madeira`, `telha`, `piso_interno`, `faixa_amarela`, `grama`, `terra`, `placa`.
- If two walls should get different textures later, give them different materials
  (`reboco_externo`, `reboco_interno`).
- Realistic, matte colors so the greybox stays readable.

## The palette that ships with the map (prefer this one)

Every new map is born with materials that use the **1 m prototype grid** installed with the
engine. Use these names and the blockout comes out with a readable scale on every surface:

`stone`, `concrete`, `brick`, `wood`, `plaster`, `asphalt`, `sidewalk`, `roof`, `grass`,
`dirt`, `metal`, `glow`, `glass`, `proto grey`, `proto orange`, `proto blue`, `proto green`,
`proto red`.

One of these materials looks like this (the texture **multiplies** the color, so `albedo` still
controls the tone):

```json
"concrete": {
  "albedo": "#a5a29c",
  "texture": "Textures/prototype/grid_mid.png",
  "uvScale": [1, 1],
  "specular": 0.08
}
```

Create a new material only for what the palette doesn't cover - and follow the same format, with
`"texture": "Textures/prototype/grid_light.png"` and `uvScale: [1,1]`.

## Alternative semantic palette (flat colors)

```json
"materials": {
  "asfalto":      { "albedo": "#454545", "specular": 0.05, "shininess": 8 },
  "faixa_amarela":{ "albedo": "#e2b53a", "specular": 0.1 },
  "calcada":      { "albedo": "#b7b2a8", "checkerboard": { "colorA": "#bdb8ae", "colorB": "#aaa59b", "cells": 2 }, "uvScale": [0.5, 0.5], "specular": 0.08 },
  "concreto":     { "albedo": "#a9a59d", "specular": 0.1, "shininess": 8 },
  "reboco":       { "albedo": "#d8d2c4", "specular": 0.1 },
  "tijolo":       { "albedo": "#ffffff", "checkerboard": { "colorA": "#9c4a3c", "colorB": "#7c382e", "cells": 8 }, "uvScale": [0.5, 0.5] },
  "metal":        { "albedo": "#8f959c", "specular": 0.8, "shininess": 64 },
  "metal_escuro": { "albedo": "#3d4147", "specular": 0.6, "shininess": 48 },
  "vidro":        { "albedo": "#8fd0ff55", "transparent": true, "specular": 0.9, "shininess": 128 },
  "madeira":      { "albedo": "#9a6b45", "specular": 0.2, "shininess": 12 },
  "telha":        { "albedo": "#8a4a3a", "specular": 0.1 },
  "grama":        { "albedo": "#ffffff", "checkerboard": { "colorA": "#6faf50", "colorB": "#5c9a42", "cells": 16 }, "uvScale": [0.25, 0.25], "specular": 0.05 },
  "terra":        { "albedo": "#7d6448", "specular": 0.05 },
  "placa":        { "albedo": "#1f4f9c", "emission": "#1f4f9c", "emissionEnergy": 0.3 },
  "luz_teto":     { "albedo": "#fff4d6", "emission": "#fff0c0", "emissionEnergy": 0.9, "unshaded": true }
}
```

## `worldUv` and `uvScale`

With `"worldUv": true` on the node (always recommended), the texture is mapped in **meters**:
`uvScale` = repeats per meter. Good defaults: floor/sidewalk 0.5 (2 m tile), brick 0.5, grass 0.25,
detail textures 1. With no texture yet, `uvScale` only sets how dense the checkerboard appears.

## When the user ALREADY has textures

If they provided files: copy them to `Assets/Textures/` and reference
`"texture": "Assets/Textures/asfalto.jpg"` (`albedo` becomes a multiplier - leave it `#ffffff`).
Never point to a path outside the project or to a nonexistent file.

## Useful effects

- Glass: alpha in the albedo + `transparent: true`.
- Light fixture / sign: `emission` + `unshaded: true`.
- Decorative surface with no collision: `"collider": { "shape": "none" }` on the node (not on the material).

## High-quality textures (normal / roughness)

If the user's pack brings extra maps, **you don't need to do anything**: a material that uses
`brick.png` automatically loads `brick_normal.png` (or `_nrm`, `_n`) and `brick_roughness.png`
(or `_rough`) when those files are in the same folder. Names like `wood_albedo.png` +
`wood_normal.png` also work.

To declare them explicitly:

```json
"tijolo": {
  "texture": "Assets/Textures/brick.png",
  "normalMap": "Assets/Textures/brick_normal.png",
  "normalStrength": 1.2,
  "roughnessMap": "Assets/Textures/brick_rough.png",
  "uvScale": [0.5, 0.5]
}
```

- `normalStrength`: 0 = flat, 1 = as authored, 2+ = exaggerated relief.
- `roughnessMap`: grayscale, white = matte, black = glossy.
- `"autoMaps": false` turns off the automatic search on a specific material.

## One material per face (boxes)

For a floor with grass on top and dirt on the sides, or a wall with a poster only on the front:

```json
{ "type": "static", "name": "Terrain_Block", "mesh": { "primitive": "box", "size": [4, 2, 4] },
  "worldUv": true, "position": [0, 1, 0],
  "faceMaterials": { "all": "terra", "top": "grama" } }
```

Keys: `top`, `bottom`, `front` (+Z), `back` (−Z), `left` (−X), `right` (+X), `sides` (the four
verticals) and `all`. The most specific key wins. It only works on `box`/`cube`; the GLB export
keeps the material of each face.
