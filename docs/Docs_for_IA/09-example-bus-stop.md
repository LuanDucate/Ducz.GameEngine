# 09 - Small complete example: bus stop

## Request

> "In `D:\Mapas\ponto_onibus`, create a bus stop on a street: sidewalk, a shelter with a glass roof,
> a bench, a trash can, a lamp, and the street with a bus lane. I'll add textures later."

## Plan (the AI writes this before the JSON)

- Terrain 60 × 30 m: street down the middle (X axis), sidewalks on both sides.
- Street: `floor` asphalt 60×30; sidewalks: 3 m slabs on the north (z −10) and south (z +10) edges.
- Painted bus lane: a thin 0.3 m yellow slab running along the street at z = −5.
- Shelter on the north sidewalk at x = 0: 4 × 1.6 m, glass roof at 2.5 m, 2 pillars, glass back panel.
- Bench under the shelter, trash can beside it, 6 m lamp with a light, stop sign.
- A block-bus parked in the lane for scale.
- Spawn on the sidewalk in front of the shelter.

## Files

`D:\Mapas\ponto_onibus\project.duczproj.json`
```json
{ "name": "ponto_onibus", "mainScene": "scenes/main.json", "engineVersion": "0.2.0", "created": "2026-08-18 15:00" }
```

`D:\Mapas\ponto_onibus\scenes\main.json`
```json
{
  "name": "PontoDeOnibus",
  "environment": { "skyTop": "#2a4a8f", "skyHorizon": "#b8cfe8", "ambientIntensity": 0.35,
                   "fog": { "color": "#b8cfe8", "start": 60, "end": 200 } },
  "materials": {
    "asfalto":       { "albedo": "#454545", "specular": 0.05, "shininess": 8 },
    "faixa_amarela": { "albedo": "#e2b53a", "specular": 0.1 },
    "calcada":       { "albedo": "#b7b2a8", "checkerboard": { "colorA": "#bdb8ae", "colorB": "#aaa59b", "cells": 2 }, "uvScale": [0.5, 0.5], "specular": 0.08 },
    "metal":         { "albedo": "#8f959c", "specular": 0.8, "shininess": 64 },
    "vidro":         { "albedo": "#8fd0ff55", "transparent": true, "specular": 0.9, "shininess": 128 },
    "madeira":       { "albedo": "#9a6b45", "specular": 0.2, "shininess": 12 },
    "placa":         { "albedo": "#1f4f9c", "emission": "#1f4f9c", "emissionEnergy": 0.3 },
    "onibus":        { "albedo": "#c8352c", "specular": 0.5, "shininess": 32 }
  },
  "nodes": [
    { "type": "directionalLight", "name": "Sol", "rotationDegrees": [-50, 35, 0], "energy": 1.1 },
    { "type": "floor", "name": "Rua", "size": [60, 30], "material": "asfalto", "worldUv": true },

    { "type": "static", "name": "Calcada_Norte", "mesh": { "primitive": "box", "size": [60, 0.15, 4] }, "material": "calcada", "worldUv": true, "position": [0, 0.075, -13] },
    { "type": "static", "name": "Calcada_Sul",   "mesh": { "primitive": "box", "size": [60, 0.15, 4] }, "material": "calcada", "worldUv": true, "position": [0, 0.075, 13] },
    { "type": "static", "name": "Faixa_Onibus",  "mesh": { "primitive": "box", "size": [60, 0.01, 0.3] }, "material": "faixa_amarela", "worldUv": true, "position": [0, 0.006, -7.5], "collider": { "shape": "none" } },

    { "type": "node", "name": "Abrigo", "position": [0, 0.15, -12.5], "children": [
      { "type": "static", "name": "Abrigo_Teto",  "mesh": { "primitive": "box", "size": [4, 0.08, 1.8] }, "material": "vidro", "position": [0, 2.54, 0] },
      { "type": "static", "name": "Abrigo_Viga",  "mesh": { "primitive": "box", "size": [4, 0.1, 0.1] }, "material": "metal", "position": [0, 2.45, -0.85] },
      { "type": "static", "name": "Abrigo_Pilar_E", "mesh": { "primitive": "box", "size": [0.1, 2.5, 0.1] }, "material": "metal", "position": [-1.95, 1.25, -0.85] },
      { "type": "static", "name": "Abrigo_Pilar_D", "mesh": { "primitive": "box", "size": [0.1, 2.5, 0.1] }, "material": "metal", "position": [1.95, 1.25, -0.85] },
      { "type": "wall",   "name": "Abrigo_Fundo", "size": [3.8, 2.4, 0.04], "material": "vidro", "position": [0, 1.2, -0.85] },
      { "type": "static", "name": "Banco_Assento", "mesh": { "primitive": "box", "size": [1.8, 0.08, 0.45] }, "material": "madeira", "position": [0, 0.46, -0.5] },
      { "type": "static", "name": "Banco_Pe_E", "mesh": { "primitive": "box", "size": [0.08, 0.42, 0.45] }, "material": "metal", "position": [-0.8, 0.21, -0.5] },
      { "type": "static", "name": "Banco_Pe_D", "mesh": { "primitive": "box", "size": [0.08, 0.42, 0.45] }, "material": "metal", "position": [0.8, 0.21, -0.5] }
    ]},

    { "type": "static", "name": "Lixeira", "mesh": { "primitive": "cylinder", "radius": 0.25, "height": 0.9 }, "material": "metal", "position": [3, 0.6, -12.5] },
    { "type": "static", "name": "Poste", "mesh": { "primitive": "cylinder", "radius": 0.08, "height": 6 }, "material": "metal", "position": [-4, 3.15, -11.5] },
    { "type": "pointLight", "name": "Poste_Luz", "position": [-4, 5.9, -11.5], "color": "#ffe2b0", "energy": 2.5, "range": 14 },
    { "type": "static", "name": "Placa_Poste", "mesh": { "primitive": "cylinder", "radius": 0.04, "height": 2.6 }, "material": "metal", "position": [3, 1.45, -11.2] },
    { "type": "static", "name": "Placa", "mesh": { "primitive": "box", "size": [0.5, 0.5, 0.03] }, "material": "placa", "position": [3, 2.5, -11.2] },

    { "type": "static", "name": "Onibus_Bloco", "mesh": { "primitive": "box", "size": [12, 3.0, 2.5] }, "material": "onibus", "worldUv": true, "position": [8, 1.7, -8.5] },

    { "type": "spawn",  "name": "SpawnPoint", "position": [-2, 0.15, -12] },
    { "type": "player", "name": "Player", "position": [-2, 1.35, -12] },
    { "type": "thirdPersonCamera", "name": "MainCamera", "target": "Player", "distance": 6.5, "targetHeight": 1.4, "current": true }
  ]
}
```

Teaching notes:
- The children of the `Abrigo` group are relative to `[0, 0.15, -12.5]` (on top of the 0.15 m sidewalk).
- The player is at `y = 0.15 + 1.2 = 1.35` because the spawn sits on the sidewalk.
- The yellow lane is decorative (`collider: none`) and almost flat.
- No `texture` on any material: the user applies those later.

## Reply text to the user (template)

"I created `D:\Mapas\ponto_onibus` with a street, sidewalks, a bus lane, a shelter with a glass roof,
a bench, a trash can, a lamp with a light, a sign, and a reference block-bus. Open it with
`Ducz.Tools.SceneEditor.exe D:\Mapas\ponto_onibus`. Placeholder materials: asfalto, calcada,
metal, vidro, madeira - right-click an object → *Texture file...* to apply your own images
(all objects sharing the same material change together). Tab to walk around; Ctrl+E exports the
GLB."
