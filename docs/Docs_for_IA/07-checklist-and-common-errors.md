# 07 - Pre-delivery checklist and common errors

## Checklist

- [ ] `project.duczproj.json` with `mainScene: "scenes/main.json"`.
- [ ] `scenes/main.json` is valid JSON (commas, double quotes, no comments, no trailing comma).
- [ ] There is exactly **one** `directionalLight`, **one** `spawn`, **one** `player`, **one**
      `thirdPersonCamera` with `"target": "Player"` and `"current": true`.
- [ ] A base `floor` covers the entire built area (the player falls into the void outside it).
- [ ] `player.position` = spawn + `[0, 1.2, 0]`; spawn in a clear area, over floor/platform.
- [ ] Every resting wall/block has `y = surface + height/2` (not `y = 0`!).
- [ ] `floor` has `position.y` = the height of its top (normally 0).
- [ ] Ramps: `position` = center (the ramp runs from `z−len/2` to `z+len/2`), climbs toward +Z with no rotation (yaw 180 = toward −Z, 90 = toward +X, −90 = toward −X); the high end touches the slab edge exactly.
- [ ] Every material referenced exists in `materials`; no `texture` points to a nonexistent file.
- [ ] `worldUv: true` on the blocks/prefabs.
- [ ] Names are unique and readable; no problematic accents/spaces (use `_`).
- [ ] Nothing with size 0 or negative; nothing absurdly far away (|x|,|z| < 500).
- [ ] Doors/openings are ≥ 0.9 m wide and ≥ 2 m tall; passages the player must walk through
      have ≥ 1 m of clearance; nothing blocks the spawn.
- [ ] Interiors/canopies have `pointLight`s.
- [ ] Reasonable node count: dozens to a few hundred (a large map may have 300–600 nodes;
      above that the editor still works but gets slow to edit).
- [ ] The response explains: how to open it, what was built, what was left out, where to texture.

## Common errors (and the fix)

| Error | Symptom | Fix |
| --- | --- | --- |
| Wall at `y: 0` | half buried | `y = height/2` |
| `floor` at `y: -0.5` "to leave the top at 0" | floor sunk 0.5 | `floor.position.y` is already the top: use 0 |
| Ramp positioned as if `position` were the low end | ramp ends before the slab (gap) or overlaps it | `position` = center: high end at `center + len/2` in the climb direction |
| Ramp "climbing" the wrong way | player hits a wall | with no rotation it climbs toward +Z; yaw 180 → −Z; 90 → +X; −90 → −X |
| Ramp rotated with pitch (`[30,0,0]`) | comes out crooked | ramps are already inclined; rotate only in Y |
| Lying cylinder with `[90,0,0]` when you wanted it along X | tube on the Z axis | `[0,0,90]` lays it on the X axis; `[90,0,0]` lays it on the Z axis |
| Camera with no `target` or no `current` | black screen / stuck camera | `"target": "Player", "current": true` |
| Two `player`/`spawn` | strange behavior | keep one of each |
| Materials with a nonexistent `texture` | warnings, white object | remove `texture` or copy the file |
| 0.7 m doors | player can't pass | ≥ 0.9 m |
| 0.5 m steps | player can't climb | ≤ 0.3 m, or use the `stairs` shape / a ramp |
| Stacking 20 boxes to make stairs | heavy map, hard to edit | use `"primitive": "stairs"` (one node) |
| Making a curve with many straight walls | faceted and laborious | use `"primitive": "curvedWall"` |
| Building the map with north at `+Z` | top view comes out mirrored | north is **−Z** (see [05](05-dimensions-and-layout.md)) |
| Ramp stopping before the platform | step too high, area unreachable | the ramp ends at the edge of the raised floor |
| A 3.5 m ramp inside a corridor with a 4 m ceiling | nobody gets through | remove the ceiling or raise it |
| Forgetting `"current": true` on the camera | Tab doesn't switch the camera and the player seems stuck | the engine now activates the first camera and warns in the log, but declare it anyway |
| Delivering without testing walkability | islanded areas | run `tools/navcheck.py` |
| Roof made of tilted boxes | wrong seams | use `roofGable` / `roofHip` / `roofShed` |
| Floor too small | player falls | make the `floor` bigger than everything, with margin |
| Too many lights (100+) | slow | group them: 1 light every 8–10 m |
| Collision turned off on a floor | player falls | don't use `collider: none` on walkable surfaces |
| Trailing comma / comments in the JSON | read error | strict JSON |

## Numeric self-check

For each resting block, compute `base = y − height/2` and confirm that `base` is the height of the
surface where it should sit (0 on the ground, 0.3 on the platform...). For the walls of an
enclosure, confirm the ends meet: a wall on X of length W centered at cx runs from
`cx − W/2` to `cx + W/2`; the side wall on Z should be at `x = cx ± W/2`.
