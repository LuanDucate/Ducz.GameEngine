# 02 - Project and files

## Folder structure

```
E:\Repos\Maps\de_testeIA\            <- path given by the user
  project.duczproj.json
  scenes\
    main.json
  Assets\
    Textures\                        <- empty; images the user applies are copied here
  Export\                            <- created by the editor when exporting (do not create)
```

## `project.duczproj.json`

```json
{
  "name": "de_testeIA",
  "mainScene": "scenes/main.json",
  "engineVersion": "0.2.0",
  "created": "2026-08-18 15:00"
}
```

- `name`: displayed name (window title). Use the folder name or the map name.
- `mainScene`: always `"scenes/main.json"` (relative path, forward slashes).
- `created`: informational, `"yyyy-MM-dd HH:mm"`.

## `scenes/main.json`

The map. Full format in [03-scene-json-format.md](03-scene-json-format.md). Minimal valid
skeleton:

```json
{
  "name": "MeuMapa",
  "environment": {
    "skyTop": "#2a4a8f", "skyHorizon": "#b8cfe8", "ambientIntensity": 0.35,
    "fog": { "color": "#b8cfe8", "start": 60, "end": 220 }
  },
  "materials": {
    "asfalto": { "albedo": "#4a4a4a", "specular": 0.05 },
    "concreto": { "albedo": "#b8b4ac", "checkerboard": { "colorA": "#c2beb6", "colorB": "#aca8a0", "cells": 2 }, "uvScale": [0.5, 0.5], "specular": 0.1 }
  },
  "nodes": [
    { "type": "directionalLight", "name": "Sol", "rotationDegrees": [-50, 35, 0], "energy": 1.1 },
    { "type": "floor", "name": "Chao", "size": [60, 60], "material": "asfalto", "worldUv": true },
    { "type": "spawn", "name": "SpawnPoint", "position": [0, 0, 0] },
    { "type": "player", "name": "Player", "position": [0, 1.2, 0] },
    { "type": "thirdPersonCamera", "name": "MainCamera", "target": "Player", "distance": 6.5, "targetHeight": 1.4, "current": true }
  ]
}
```

## How the user opens the project

- **Command line** (always works):
  `dotnet run --project src/Ducz.Tools.SceneEditor -- "E:\Repos\Maps\de_testeIA"`
  or the already-compiled executable: `Ducz.Tools.SceneEditor.exe "E:\Repos\Maps\de_testeIA"`.
- **Launcher**: only lists projects it created. For the project to appear in the list, the AI can
  add an entry to `%AppData%\DuczEngine\launcher.json`:

```json
{
  "projects": [
    { "name": "de_testeIA", "path": "E:\\Repos\\Maps\\de_testeIA", "lastOpened": "2026-08-18T15:00:00" }
  ],
  "defaultLocation": "E:\\Repos\\Maps"
}
```
  (if the file already exists, just add the object to the `projects` array; keep the rest).

## Rules for paths

- Inside the scene JSON, texture/model paths are **relative to the project folder**
  (`Assets/Textures/parede.png`) with forward slashes `/`.
- Do not reference files that do not exist: if the user said they will add textures later, use
  solid-color/checkerboard materials (see [06](06-materials-and-textures.md)) - **without** a `texture` field.
- If the user provided a `.glb`/`.fbx` to use as a prop, place it in `Assets/Models/` and
  reference it as `"path": "Assets/Models/arquivo.glb"`.
