# AI knowledge base - Ducz Map Builder

This folder teaches an AI to **generate 3D maps for the Ducz Map Builder** from a request in
natural language. A typical scenario:

> User: "Create the project in `E:\Repos\Maps\de_testeIA`. I want a map that looks like a bus
> terminal, the Boqueirão Terminal in Curitiba. Build the whole structure; I'll add the textures."

The AI must then **create the project folder with the JSON files** that the Map Builder opens. The
user opens the project, adjusts it, applies textures, and exports it as GLB for Godot/Blender.

## ⚠ Language of the response

**Always answer in the language the user wrote in.** They asked in Portuguese, answer in
Portuguese; they asked in English, answer in English; in Spanish, in Spanish.

This documentation is in Portuguese for the author's convenience - **that is not a language
instruction**. The same goes for the English names that appear in the editor (`Export GLB`, `Free
mouse`), in the JSON fields (`position`, `material`), and in the code comments: they are technical
names and stay as they are in any language.

What changes with the language: the text of your response, the explanations, and the summary of what
was created. What does **not** change: the JSON keys, the names of the primitives (`box`, `wedge`,
`roofGable`), and the names of the library prefabs (`House 2-story 8x6`).

You don't need to write any C# code: a map is **a scene JSON file** (`scenes/main.json`) plus a
project descriptor (`project.duczproj.json`).

## Suggested reading order

| File | What it teaches |
| --- | --- |
| [01-overview.md](01-overview.md) | What the Map Builder is, units, axes, what the AI delivers |
| [02-project-and-files.md](02-project-and-files.md) | Folder structure, `project.duczproj.json`, how the user opens it |
| [03-scene-json-format.md](03-scene-json-format.md) | Complete reference for `scenes/main.json` (materials, node types, fields) |
| [04-building-cookbook.md](04-building-cookbook.md) | Ready-made recipes: floor, wall, room, door, window, stairs, roof, pillar, bench, fence, ramp, light |
| [05-dimensions-and-layout.md](05-dimensions-and-layout.md) | Real reference measurements and the math of positioning (centers, heights, rotations) |
| [06-materials-and-textures.md](06-materials-and-textures.md) | Default palette, semantic "placeholder" materials, worldUv/tiling, leaving textures to the user |
| [07-checklist-and-common-errors.md](07-checklist-and-common-errors.md) | Validation before delivering; errors that break the map |
| [08-user-flow-and-tools.md](08-user-flow-and-tools.md) | Open in the editor/launcher, play-test, export GLB, collision, external models |
| [09-example-bus-stop.md](09-example-bus-stop.md) | A complete small example (request → plan → JSON) |
| [10-example-bus-terminal.md](10-example-bus-terminal.md) | A complete large example: how a bus terminal was decomposed and built |
| [11-ai-response-template.md](11-ai-response-template.md) | The response format the AI should follow (plan → files → instructions) |
| [12-prefabs-and-library.md](12-prefabs-and-library.md) | **Ready-made pieces**: how to use the prefab library, modular fit, model packs |
| [13-editor-whats-new.md](13-editor-whats-new.md) | What the user can do in the editor (matters for what the AI promises in the response) |
| [14-image-reference.md](14-image-reference.md) | **A map from an image** (radar/plan/photo) and the **elevation** rules that avoid flat-board maps |

## Golden rules (summary)

1. **Units in meters, Y axis up, −Z is "front".** A 1 m block is 1 m in Godot/Blender.
2. **`position` is the CENTER of the object** for `static`/`wall`/`mesh`; it's the **TOP** for `floor`
   and the **center on the plane / low tip in Y** for `ramp` (it rises toward +Z; rotate with yaw 180
   to rise toward −Z). A 3 m tall wall resting on the ground sits at `y = 1.5`.
3. Every map needs: 1 `directionalLight`, 1 floor, 1 `spawn`, 1 `player`, 1 `thirdPersonCamera`
   (with `"current": true`).
4. Prefer **ready-made prefabs** (`src/Prefabs/`) for houses, streets, buildings, and trees - see
   [12](12-prefabs-and-library.md). A loose block only for what the library doesn't have.
5. North is **−Z**; before delivering, run `tools/navcheck.py` to prove you can walk from one end
   to the other.
6. Use `"worldUv": true` on every block/prefab so textures have a uniform density.
7. Materials: use the **default palette with a 1 m grid** (`stone`, `concrete`, `brick`, `asphalt`,
   `grass`...) described in [06](06-materials-and-textures.md) - it comes textured and makes the
   blockout look intentional. The user swaps it for their own textures later.
8. Name the nodes (`"name"`) in a readable and unique way (`Plataforma_Central`, `Pilar_03`): the user
   sees these names in the editor and in Godot.
7. Prefer **many simple blocks** over complex geometry: boxes, cylinders, ramps.
8. Deliver: `project.duczproj.json`, `scenes/main.json`, an empty `Assets/Textures/` folder, and a
   short text saying how to open it and what's still missing (textures, details).
