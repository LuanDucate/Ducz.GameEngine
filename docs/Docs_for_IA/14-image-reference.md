# 14 - A map from a reference image (radar, floor plan, photo)

> Reminder: **answer in the user's language**.

The user can send an image - the radar of an FPS map, a floor plan, a satellite photo - and ask
"make a map that looks like this". The goal is **not to copy it pixel by pixel**: it's to reproduce
the **topology** (what connects to what), the **proportions**, and the **elevation**.

## Step 1 - Read the image as a graph

1. Identify the **zones**: spawns, objectives (bomb A/B), plazas, corridors. On a CS radar,
   the labels give it away (CT SPAWN, T SPAWN, A, B, MID).
2. Identify the **connections**: each drawn path (arrows, colors) becomes a corridor.
   Note it as a list: `T spawn → banana → B`, `T spawn → mid → arch → A`...
3. Orient it: on a radar, **north is the top of the image** → in Ducz, north is **−Z**. Top of the
   image = negative z.
4. Estimate the scale: a competitive CS map is about 90–120 m per side; a corridor 4–8 m;
   a bombsite plaza 15–25 m. Distribute the zones in the same proportion as the image.

## Step 2 - Zones as rectangles + portals

Model each zone as a rectangle with its own floor and wall height, and each connection as a
**portal** (an opening cut into the wall). Generate the walls per zone by subtracting the portals -
and build each wall line **only once** (keep track of the segments already built), otherwise the
walls of neighboring rooms overlap.

## Step 3 - ELEVATION (this is what separates a map from a "flat board")

A flat map with props isn't a map. Give each zone a **floor level** (`y`) and connect the levels:

| Connector | When to use | How |
| --- | --- | --- |
| `wedge` on the lower side | ramps, streets on a slope | ends **exactly at the portal**, its top aligned with the upper floor |
| `stairs` | building staircases, platforms | steps ≤ 0.3 m of rise per meter of run (the character climbs 0.35 m) |
| drop | balconies, windows | up to ~4 m is fine to descend; you can't come back up - make sure there's another way back |

Rules that avoid the classic mistakes:

- **A raised floor gets a "skirt"**: slab thickness = `y + 0.8`, to reach down to the ground - no
  floating platforms.
- **Corridor with a ceiling + a ramp inside**: headroom at the top of the ramp ≥ 2 m, otherwise
  nobody gets through.
- **Second floor** (apartments): ground-floor room with `stairs` climbing 3 m, the upper floor as
  its own zone at `y = 3`, and a **balcony** (a solid block + a guardrail with an opening) dropping
  into the neighboring zone.
- Run `tools/navcheck.py` with landmarks **including the expected height** - it is multi-level and
  catches an inverted ramp, a step that's too high, and an islanded area.

## Step 4 - Environment

- **Roofs** (`roofGable`) over the closed corridors: they become the map's silhouette in the top
  view - compare it with the radar.
- House/tree prefabs **outside the playable walls** so the map doesn't float in nothing.
- Palette materials with the grid (1 m) and colors per zone (site A warm, site B cool, spawns
  with the team's color): the player orients themselves without any texture at all.
- Lights (`Street lamp`, pointLights) in the closed corridors.

## A real example

`E:\Repos\Maps\de_infernato` was generated from an Inferno radar: 17 zones across 5 levels
(0 / 1 / 1.5 / 2.5 / 3 m), apartments on the 2nd floor with a balcony dropping into site A, banana
climbing 2.5 m up to B, mid with a ramp, 3 arches, roofs, and a village around it - 19/19 navigation
points reachable in `navcheck`.
