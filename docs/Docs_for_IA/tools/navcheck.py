# -*- coding: utf-8 -*-
"""Walkability check for a Ducz map: can the player actually reach every area?

Samples the map on a grid, finds the walkable floor height of each cell (boxes,
wedges and stairs), then flood-fills from the spawn using the engine's real
limits (step 0.35 m, player height 1.8 m) and reports which landmarks connect.
"""
import json, io, math, sys, collections

PATH = sys.argv[1] if len(sys.argv) > 1 else r"E:\Repos\Maps\Dust2\scenes\main.json"
CELL = 1.0
STEP_UP = 0.42          # the controller climbs ~0.35 m; allow a little tolerance
PLAYER_H = 1.75
X0, X1, Z0, Z1 = -50, 46, -54, 46

doc = json.load(io.open(PATH, encoding="utf-8"))

solids = []
for n in doc["nodes"]:
    if n["type"] not in ("static", "rigid", "floor"):
        continue
    if (n.get("collider") or {}).get("shape") == "none":
        continue
    mesh = n.get("mesh") or {}
    prim = mesh.get("primitive", "box")
    size = mesh.get("size") or [1, 1, 1]
    pos = n.get("position") or [0, 0, 0]
    yaw = math.radians((n.get("rotationDegrees") or [0, 0, 0])[1])
    solids.append((n["name"], prim, pos, size, yaw))


arch_opening = {}
for n in doc["nodes"]:
    m = n.get("mesh") or {}
    if m.get("primitive") == "arch":
        arch_opening[n["name"]] = (m.get("openingWidth", 2.0), m.get("openingHeight", 2.0))


def surface(sx, sz):
    """(top, bottom) spans of every solid covering this column, as a list."""
    spans = []
    for name, prim, p, s, yaw in solids:
        c, si = math.cos(yaw), math.sin(yaw)
        dx, dz = sx - p[0], sz - p[2]
        lx = dx * c - dz * si          # inverse yaw rotation
        lz = dx * si + dz * c
        w, h, l = s[0], s[1], s[2]
        if abs(lx) > w / 2 or abs(lz) > l / 2:
            continue
        bottom = p[1] - h / 2
        if prim in ("wedge", "ramp"):
            t = (lz + l / 2) / l                       # rises toward local +Z
            spans.append((bottom, bottom + h * t, name))
        elif prim == "stairs":
            steps = 8
            t = (lz + l / 2) / l
            spans.append((bottom, bottom + h * math.ceil(t * steps) / steps, name))
        elif prim == "arch":
            ow = 5.0 if "opening" not in str(name) else 5.0
            ow = arch_opening.get(name, (2.0, 2.0))[0]
            oh = arch_opening.get(name, (2.0, 2.0))[1]
            if abs(lx) <= ow / 2 - 0.2:
                clear = bottom + oh + ow / 2          # flat part + the arc above it
                if clear < p[1] + h / 2:
                    spans.append((clear, p[1] + h / 2, name))
            else:
                spans.append((bottom, p[1] + h / 2, name))
        else:
            spans.append((bottom, p[1] + h / 2, name))
    return spans


nx = int((X1 - X0) / CELL)
nz = int((Z1 - Z0) / CELL)

# Every column can have several standing surfaces (a corridor floor and the roof
# above it), so the graph is multi-level: one node per (cell, surface).
levels = [[[] for _ in range(nx)] for _ in range(nz)]
for iz in range(nz):
    for ix in range(nx):
        sx = X0 + ix * CELL + CELL / 2
        sz = Z0 + iz * CELL + CELL / 2
        spans = surface(sx, sz)
        for bot, top, name in spans:
            if top > 12:
                continue
            blocked = any(b < top + PLAYER_H - 0.05 and t2 > top + 0.05
                          for b, t2, _ in spans)
            if not blocked:
                levels[iz][ix].append(round(top, 3))
        levels[iz][ix] = sorted(set(levels[iz][ix]))


def cell_of(x, z):
    return int((x - X0) / CELL), int((z - Z0) / CELL)


def nearest_level(ix, iz, y):
    """The standing surface of this cell closest to y (used for landmarks)."""
    if not levels[iz][ix]:
        return None
    return min(levels[iz][ix], key=lambda h: abs(h - y))


def blocked_between(ix, iz, jx, jz, walk_y):
    """Thin walls fall between cell centres, so sample the gap itself."""
    mx = X0 + (ix + jx + 1) / 2 * CELL
    mz = Z0 + (iz + jz + 1) / 2 * CELL
    for bot, top, _ in surface(mx, mz):
        if bot < walk_y + PLAYER_H - 0.1 and top > walk_y + 0.15:
            return True
    return False


spawn = next(n for n in doc["nodes"] if n["type"] == "spawn")["position"]
sx0, sz0 = cell_of(spawn[0], spawn[2])
start_h = nearest_level(sx0, sz0, spawn[1])
if start_h is None:
    print("!! the spawn cell has no floor")
    sys.exit(1)

seen = {(sx0, sz0, start_h)}
queue = collections.deque(seen)
while queue:
    ix, iz, here = queue.popleft()
    for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        jx, jz = ix + dx, iz + dz
        if not (0 <= jx < nx and 0 <= jz < nz):
            continue
        for there in levels[jz][jx]:
            if there - here > STEP_UP or here - there > 4.0:
                continue
            key = (jx, jz, there)
            if key in seen:
                continue
            if blocked_between(ix, iz, jx, jz, max(here, there)):
                continue      # a thin wall sits between the two cells
            seen.add(key)
            queue.append(key)

reached = {(ix, iz) for ix, iz, _ in seen}

LANDMARKS = [
    ("CT spawn",      -27,  30, 3.5), ("B corridor",   -35,   0, 0.0),
    ("B site",        -33, -24, 0.0), ("lower tunnels", -16, -20, 0.0),
    ("tunnel ramp",   -16, -34, 1.0), ("upper tunnels",   2, -40, 2.0),
    ("T spawn",        28, -42, 2.0), ("T mid",          12, -31, 2.0),
    ("mid (north)",     5, -20, 2.0), ("mid doors",       5,  -8, 0.0),
    ("mid (south)",     5,  10, 0.0), ("CT mid",         -8,  20, 0.0),
    ("catwalk",        15,   2, 2.5), ("A short",        18,  12, 0.0),
    ("A site",         30,  24, 0.0), ("A plat",         33,  29, 1.6),
    ("long doors",     36, -30, 2.0), ("A long",         36, -10, 0.0),
    ("pit",            26,   9, -1.2),
]
print("reachable cells: %d (%d surfaces)" % (len(reached), len(seen)))
bad = 0
for name, x, z, y in LANDMARKS:
    ix, iz = cell_of(x, z)
    h = nearest_level(ix, iz, y)
    ok = h is not None and (ix, iz, h) in seen
    if not ok:
        bad += 1
    print("  %-15s %-11s floor=%s (expected ~%s)" % (
        name, "OK" if ok else "UNREACHABLE", "none" if h is None else round(h, 2), y))
print("unreachable landmarks:", bad)


# debug: print the standing surfaces along a line of cells
if len(sys.argv) > 3:
    ax, az, bx, bz = [float(v) for v in sys.argv[2:6]]
    n = int(max(abs(bx - ax), abs(bz - az))) + 1
    print("surfaces from (%s,%s) to (%s,%s):" % (ax, az, bx, bz))
    for i in range(n + 1):
        x = ax + (bx - ax) * i / n
        z = az + (bz - az) * i / n
        ix, iz = cell_of(x, z)
        marks = ["%.2f%s" % (h, "*" if (ix, iz, h) in seen else "") for h in levels[iz][ix]]
        print("   x=%6.1f z=%6.1f  %s" % (x, z, ", ".join(marks) or "-- void --"))
