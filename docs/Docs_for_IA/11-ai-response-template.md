# 11 - AI response template

## First of all: the language

**Reply in the language of the request.** If the user wrote in Portuguese, your entire reply is in
Portuguese - plan, explanations, summary, and instructions. The fact that this knowledge base is in
Portuguese (or that the editor's names are in English) does **not** set the reply's language.

The following stay in English, in any language: the JSON keys (`position`, `material`,
`worldUv`), the primitive names (`box`, `wedge`, `roofGable`, `polygon`), and the prefab names
(`House 2-story 8x6`, `Straight street 12 m`) - they are identifiers, not text.

When a request has more than one language, use the one from the user's last message.

## Order of the response

When the user asks for a map, respond **in this order**:

## 1. Understanding and plan (short, in bullet points)

- What the place / the map is; the iconic elements that will be represented.
- Overall dimensions (the terrain rectangle, orientation: long axis on X).
- Zones and what goes in each one (with approximate measurements).
- What will be simplified or left to the user (textures, detailed props, signage).

## 2. Files (in full)

For each file, the full path and the content:

1. `<folder>/project.duczproj.json`
2. `<folder>/scenes/main.json` - the entire map. **Strict JSON** (no comments, no trailing
   comma). If it is very large, still deliver it complete; group with `node`+`children` and
   name by zone (`Plataforma_A_Pilar_03`).
3. Create the `Assets/Textures/` folder (empty) - and `Assets/Models/` if there are models.
4. (Optional) an entry for `%AppData%\DuczEngine\launcher.json` so it shows up in the launcher.

If the AI has access to file tools, **write the files**; otherwise, show them in code blocks with
the path in the title.

## 3. Instructions to the user (short)

- How to open: `Ducz.Tools.SceneEditor.exe "<folder>"` (or from the launcher).
- Where the spawn is and what it will see.
- The list of placeholder materials and how to texture them (right-click → *Texture file...* / drag
  an image; one material = all surfaces of that type).
- Tab to walk around; Ctrl+E to export GLB (scale 1 = meters for Godot/Blender).
- If you used prefabs, say **which ones** - the user swaps any of them from the **B** panel and can
  edit a part with **Alt + right-click** (see [12](12-prefabs-and-library.md)).
- What was left out and suggestions for the next step (props, more detail, night lighting).

## 4. If it is a change to an existing map

- Say that you read the current `scenes/main.json`, what you preserved, and what you changed (node names).
- Deliver the complete updated file (not a partial diff), keeping the names and materials the
  user has already textured.

## Tone

Objective, technical, in the language of the request; without promising what the tool cannot do
(see limits in [08](08-user-flow-and-tools.md)). Measurements in meters; node names in `Snake_Case`
without accents.
