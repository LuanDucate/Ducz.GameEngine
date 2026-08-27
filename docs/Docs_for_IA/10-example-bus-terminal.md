# 10 - Large complete example: Terminal Boqueirão (Curitiba)

This example shows how a "vague and large" request becomes a coherent map. The actual result is
in `E:\Repos\Maps\de_testeIA` (802 nodes, 22 materials) - open it to study; here is the
reasoning and the numbers.

## Request

> "In `E:\Repos\Maps\de_testeIA`, create a map similar to a terminal, ideally the Terminal do
> Boqueirão in Curitiba. Build as much of the structure as you can; I'll add the textures."

## 1. Iconic elements chosen

A Curitiba integration terminal has: a **long central platform under a continuous canopy**
with pillars; **secondary platforms** running parallel; **dedicated bus lanes on both sides**;
**tube stations** (glass tubes raised 0.9 m with a ramp and a turnstile) on the **bi-articulated**
line; a **ticketing/administration building**; a **footbridge** to cross to the other side of the
lanes; sidewalks with trees and lamps; buses parked in the bays; tactile paving, line totems,
benches, trash cans.

## 2. Terrain and zones (long axis on X)

Terrain `floor` 240 × 100 m (asphalt). Zones along Z, from north (−) to south (+):

| z | Zone | How |
| --- | --- | --- |
| −45..−40 | north sidewalk + wall | `static box` 240×0.15×5 at y 0.075; `wall` 240×1.2 at z −45 |
| −40..−34 | BRT lane (bi-articulated) | yellow/white markings (`collider: none`) |
| −30 (±2.25) | **tube stations** (2×, at x −45 and +45) | platform 20×0.9×4.5; cylinder lying down `[0,0,90]` r 1.55 length 20 at y 2.15, no collision; rings; glass walls at the ends; cabin + turnstile; 8 m ramp (1:9) rising toward +X; 5 steps at the other end |
| −23..−17 | north outer lane | markings |
| −17..−11 | north platform 150×0.3×6 + canopy 154×0.2×7 at y 4.6, pillars r 0.25 every 8 m | 3 m ramps at the ends |
| −11..−5 | north inner lane | markings + pedestrian crossings |
| −5..+5 | **central platform** 170×0.3×10 + canopy 176×0.25×14 at y 5.525, 2 rows of pillars r 0.3 every 8 m at z ±3.8, cross beams, lights + `pointLight` every 16 m | benches every 14 m, trash cans, totems |
| +5..+11 / +11..+17 / +17..+23 | mirror of the north side (south platform, lanes) | |
| +40..+45 | south sidewalk + wall (with a gap for the footbridge) | |
| x −113..−95 | **building** 18×14×4: 4 walls, 3 m door toward the platform side (2 walls + lintel), roof, counter, queue | interior lights |
| x 70 | **footbridge** from z −3 to 40 at 5.3 m: 3 m slab, railings, 4 pillars, 26–27 steps of ~0.19 m at each end | lights |

Block-bus (12×3×2.5, wheels = cylinders lying down `[90,0,0]`) in 8 bays; a 26 m bi-articulated
in the BRT lane next to tube station 1. Trees (cylinder trunk + sphere canopy, no collision) and
lamps with a light every ~22 m along the sidewalks.

Spawn at `[-80, 0.3, 0]` (west end of the central platform, facing the building); player at
`y 1.5` (0.3 + 1.2).

## 3. Calculations worth copying

- 0.3 m platform: `box [L, 0.3, W]` at `y 0.15`. Everything sitting **on** it adds 0.3:
  bench (seat 0.08 at y 0.3+0.46), 5.1 m pillar at `y 0.3+2.55`.
- Canopy over 5.1 m pillars (top 5.4): 0.25 slab at `y 5.525`; 0.4 beams at `y 5.2`.
- Platform ramp: `ramp [3, 0.3, 3]` at the west end, center at `x = edge − 1.5` with `[0,90,0]`
  (rises toward +X; the high end lands exactly on the edge).
- Tube station ramp: `ramp [2, 0.9, 8]`, center at `x = tube − 14`, `[0,90,0]` → high end at
  `x = tube − 10` (edge of the tube platform) at 0.9 m.
- Step k of a staircase with N steps and rise H: height `H/N·k`, center `base + H/N·k/2`,
  depth 0.3 m, shifting 0.3 m per step in the descent direction.
- 3 m door in a 14 m wall: two 5.5 m walls centered at `cz ± 4.25` + lintel
  `[3, 1.4, t]` at `y 3.3` (4 m wall, 2.6 m opening).
- Cylinder lying down on the X axis: `rotationDegrees [0,0,90]`; wheels (Z axis): `[90,0,0]`.

## 4. What was left out (told to the user)

- Textures (all surfaces use placeholder materials: `asfalto`, `calcada`, `plataforma`,
  `piso_tatil`, `concreto`, `reboco`, `cobertura`, `estrutura`, `metal`, `vidro`, `madeira`, `placa`…).
- The exact floor plan of the real terminal (the layout is typical, not faithful to the survey).
- The curved/arched canopy of the real terminal → flat slab; tube station → cylinder + rings.
- Written signage, detailed turnstiles, people, real vehicles (use GLB props later).

## 5. How it was validated

Opened in the Map Builder: top view confirms the zones; a fly-through shows the canopy, pillars,
benches, totems, lanes, buses, trees, and the tube station with the bi-articulated. Play (Tab)
spawns on the central platform. GLB export works (the file has ~800 nodes; for Godot, consider
"Merge static geometry" on export to reduce draw calls).

## 6. Lesson for similar requests

Break it down into **parallel zones with measurements** (the table above), generate repeated
elements with a fixed step (pillars every 8 m, benches every 14 m), and name them by zone. A
terminal, train station, airport, or mall all follow the same logic: base floor → zones → volumes →
structure (canopy/pillars) → openings → furniture → lights → spawn.

## 7. Note on how the example was produced

The `main.json` in `E:\Repos\Maps\de_testeIA` was generated by a small program that applies the
formulas above (which is why it has 802 nodes, with dashed markings and repeated steps). An AI
writing JSON by hand should **use the same numbers**, but may reduce the decorative repetition
(fewer dashes, pedestrian crossings as a single white slab, staircases replaced by ramps) to fit
within the response - the recognizable structure comes from the zones, canopies, platforms, and
tube stations, not from the repeated details.
