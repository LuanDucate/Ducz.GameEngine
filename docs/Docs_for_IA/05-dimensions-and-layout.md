# 05 - Reference dimensions and layout math

## Real-world measurements (use as defaults when the request doesn't specify)

| Element | Typical measurement |
| --- | --- |
| Character (player capsule) | 1.8 m tall, 0.7 m wide; climbs steps up to ~0.35 m; fits through gaps ≥ 0.9 m |
| Door | 0.9–1.2 m × 2.1–2.2 m |
| Ceiling height, residential / commercial / warehouse | 2.8 / 3.5 / 6–8 m |
| Corridor / sidewalk | 1.5–3 m wide |
| Street (single lane) / bus lane | 3–3.5 m; two-lane road 7 m |
| Urban bus / bi-articulated bus | 12 × 2.5 × 3.2 m / 28 × 2.5 × 3.2 m |
| Boarding platform | 0.3–0.9 m above the roadway, 4–8 m wide |
| Canopy / marquee | 4–5 m high, columns every 6–10 m |
| Tube station | tube Ø ~2.8 m, 12–20 m long, floor at ~0.9 m, access ramp |
| Footbridge | 3 m wide, 5–5.5 m of clear height |
| Stairs | step 0.18 high × 0.30 deep; width ≥ 1.2 m |
| Accessible ramp | slope ≤ 1:8 (0.125); ideal 1:12 |
| Wall / fence / guardrail | 2 m / 1.8 m / 1.1 m |
| Bench | 1.8 × 0.45 × 0.45 m (seat at 0.45 m) |
| Street lamp | 6–9 m; light ~0.3 m below the top |
| Traffic sign | 1.2 × 0.6 m at 2.2–2.5 m height |
| Tree (blockout) | trunk cylinder r 0.2 h 3; canopy sphere r 2 at y 4.5 |

Map size: a "deathmatch map" fits in 40×40 to 80×80 m; a large bus terminal is 150–250 m long by
40–80 m wide. Don't make a floor bigger than 300×300.

## Positioning math

Everything is centered (except `floor` = top; `ramp` is centered on the plane and `y` = the low end). For a block of height H resting on a
surface of height S: `y = S + H/2`.

- Wall of height 3 on the ground (S=0): y 1.5. On a 0.3 platform: y 1.8.
- A 0.25 slab on top of 4.5 columns: y = 4.5 + 0.125 = 4.625.
- Object of radius r on the ground (sphere / lying cylinder): y = r (plus the floor it sits on).

Y rotation (yaw): the geometry of a `wall`/`box` of length L along X rotates to Z with 90.
An object rotated 90 in Y keeps the same center - it just swaps X↔Z of its dimensions.

A repeated "block"/kit: pick a step (e.g. a column every 8 m) and generate positions
`x = x0 + k*8` for k = 0..N. Name them `Column_00 … Column_N`.

## Recommended build order (largest to smallest)

1. Base `floor` at the full size of the terrain.
2. Different floor zones: roadways, sidewalks, platforms (thin slabs).
3. Main volumes: buildings (walls + roof), perimeter walls.
4. Structures: canopies + columns, footbridges, ramps, stairs.
5. Openings: doors/windows (splitting walls).
6. Furniture and details: benches, lamps, signs, trash cans, trees, bus blockout.
7. Lights (`pointLight`) under canopies and interiors.
8. Spawn / player / camera at an open, representative spot (main entrance).

## Orientation: north is **−Z**

The engine is Y-up and right-handed, so `+X` = east, `+Y` = up and **`−Z` = north**
(`+Z` is south). The editor's top view (**T** key) draws `+X` to the right and `+Z`
downward - meaning if you build the map with north at `−Z`, the top view comes out exactly
like a normal map/radar (north up, east right).

If you build it "upside down" with north at `+Z`, the map ends up **mirrored** in the top view -
and a mirrored map has left/right swapped relative to the real place. Write the
coordinates like this:

| Direction | Axis |
| --- | --- |
| north / up on the map | `−Z` |
| south / down on the map | `+Z` |
| east / right | `+X` |
| west / left | `−X` |

## Every good map has levels

A map on a single plane, covered in props, is a "slab" - it looks like a bad prototype even when
textured. Give the zones different floor levels (0 / 1 / 1.5 / 2.5 / 3 m is enough) and connect
them with ramps, stairs and drops. See the full walkthrough in [14](14-image-reference.md).

## Elevation changes: ramps and stairs that actually connect

Three rules that avoid the most common mistake (an area that "exists" but nobody can reach):

1. **The ramp has to end exactly at the edge of the platform.** A 5 m ramp that rises
   1.6 m and stops 1 m short of the edge leaves a 0.32 m step - right at the limit of what the
   character can climb (~0.35 m). Make the ramp end at the same coordinate where the raised floor
   begins.
2. **Check the direction of the climb.** `wedge`/`stairs` climb toward `+Z` with `yaw 0`; use
   `yaw 180` to climb toward `−Z`, `90` for `+X`, `−90` for `−X`. Always point them toward the
   side where the higher floor is.
3. **A covered corridor needs headroom.** If the corridor has a ceiling at 4 m and the ramp rises
   3.5 m inside it, only 0.5 m of height is left: nobody gets through. Either remove the ceiling,
   or raise it above `ramp height + 2 m`.

## Verify the map is walkable (before delivering)

`tools/navcheck.py` (in this folder) reads `scenes/main.json`, assembles the walkable surfaces
(boxes, ramps, stairs, arches), respects the 0.35 m step and the character's 1.8 m height,
and does a flood-fill starting from the `spawn`:

```bash
python docs/Docs_for_IA/tools/navcheck.py "E:\Repos\Maps\MyMap\scenes\main.json"
```

Edit the `LANDMARKS` list with the important points of your map (name, x, z, expected height);
the output marks each as `OK` or `UNREACHABLE`. Passing an extra `x1 z1 x2 z2`, it prints
the surfaces along that line - that's how you find where the passage broke.

## Strategy for "recreating a real place"

The AI doesn't have the exact blueprint; the goal is to **capture the recognizable spatial
organization**:
- List 5–10 iconic elements of the place (for a terminal: long linear platforms,
  continuous canopy, tube stations, bus lanes on both sides, ticket office, footbridge).
- Define an orientation (long axis on X) and an overall rectangle.
- Distribute the elements into zones with plausible measurements; symmetry helps.
- Name everything for what it represents (`TubeStation_North`, `TicketOffice_Wall_South`) - the
  user will recognize and adjust it.
- Say in the response what was simplified/invented.
