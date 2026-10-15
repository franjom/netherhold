"""Pilot: 0x00460B50 on captured state. Args: capture first last step"""
import os, struct, subprocess, sys, collections
sys.path.insert(0, r"D:\DungeonKeeperRemake-oracle\tools\oracle")
import capture_memory as cm
dk2capture = cm.dk2capture

cap_path, first, last, step = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
EXE = r"E:\GogGames\Dungeon Keeper 2\DKII.EXE"
ORACLE = r"D:\DungeonKeeperRemake-oracle\tools\oracle\dk2oracle.py"
NEUTRAL, HERO, G = 2, 1, 7      # [0x006F2548], [0x006F254C], variable 190's low byte
extra = [(0x006F2548, struct.pack("<H", NEUTRAL)), (0x006F254C, struct.pack("<H", HERO)), (0x006F2974, bytes([G]))]
import tempfile
tmp = os.path.join(tempfile.gettempdir(), "dk2-guard-room.mem")

cap = dk2capture.Capture(cap_path)
stats = collections.Counter()
diffs = []
for tick in range(first, last + 1, step):
    snap = cap.at(tick)
    if snap is None: continue
    tt = snap.region(dk2capture.REGION_THING_TABLE)
    active = snap.region(dk2capture.REGION_ACTIVE)
    ptr = {r.ptr: r for r in snap.records()}
    slot_of = lambda s: struct.unpack_from("<I", tt, 4 * s)[0]
    width, height = snap.map_size()
    # tile-derived bounds per room slot
    tiles = collections.defaultdict(list)
    for y in range(height):
        for x in range(width):
            t = snap.tile(x, y)
            tag = struct.unpack_from("<H", t, 2)[0] & 0xFFF
            if tag: tiles[tag].append((x, y))
    paddr, pdata = snap.regions[dk2capture.REGION_PLAYERS]

    creatures = [r for r in snap.things("creature") if r.raw and r.u(0x502, "<H")]
    if not creatures: continue
    requests, expect = [], []
    for c in creatures:
        owner = c.u(0x28, "<H"); target = c.u(0x502, "<H")
        why = {}
        def py(bounds_of):
            if owner in (NEUTRAL, HERO): why["z"] = "0: neutral or hero owner"; return 0
            if not active[target]: why["z"] = "0: target slot inactive"; return 0
            t = ptr.get(slot_of(target))
            if t is None or t.u(0x12) != 0: why["z"] = "0: target +0x12 set"; return 0
            player = c.u(0x2A); off = player - paddr
            room_slot = struct.unpack_from("<H", pdata, off + 0x36)[0]
            tx, ty = t.u(0x1A, "<i") >> 12, t.u(0x1E, "<i") >> 12
            seen, guards = 0, 0
            why["z"] = "0: no guard room"
            while slot_of(room_slot) and seen < 400:
                seen += 1
                room = ptr.get(slot_of(room_slot))
                if room is None: return "room not captured"
                if room.u(0x18, "<B") == 9:
                    why["z"] = "0: outside every guard room"
                    x0, x1, y0, y1 = bounds_of(room, room_slot)
                    if (abs(tx - (x0 - G)) <= 1 or abs(tx - (x1 + G)) <= 1) and y0 - G <= ty <= y1 + G                             or (abs(ty - (y0 - G)) <= 1 or abs(ty - (y1 + G)) <= 1) and x0 - G <= tx <= x1 + G:
                        why["edge"] = True
                    if x0 - G < tx < x1 + G and y0 - G < ty < y1 + G: return 1
                room_slot = room.u(0x08, "<H")
            return 0
        stored = lambda room, s: (room.u(0x49, "<H"), room.u(0x4B, "<H"), room.u(0x4D, "<H"), room.u(0x4F, "<H"))
        def from_tiles(room, s):
            ts = tiles.get(s) or [(0, 0)]
            return (min(x for x, _ in ts), max(x for x, _ in ts), min(y for _, y in ts), max(y for _, y in ts))
        requests.append(f"460b50 | ecx={c.ptr:x}")
        s_answer = py(stored)
        reason, edge = why.get("z", ""), why.get("edge", False)
        expect.append((c.slot, s_answer, py(from_tiles), reason, edge))
    cm.write(tmp, cm.image(snap, extra))
    out = subprocess.run([sys.executable, "-I", ORACLE, EXE, tmp], input="\n".join(requests) + "\n",
                         capture_output=True, text=True, env=os.environ).stdout.splitlines()
    for (slot, s, t, reason, edge), line in zip(expect, out):
        if line.startswith("fault"):
            stats["fault"] += 1; diffs.append((tick, slot, line)); continue
        o = int(line.split()[0], 16)
        stats[f"original {o}"] += 1
        if o == 0: stats[reason] += 1
        if edge: stats[f"target within a tile of an expanded edge, original {o}"] += 1
        if o != s: stats["emulated != stored-bounds rule"] += 1; diffs.append((tick, slot, f"orig {o} stored {s}"))
        if s != t: stats["stored != tile bounds"] += 1; diffs.append((tick, slot, f"stored {s} tiles {t}"))
    # bounds agreement over every Guard Room, independent of targets
    for r in snap.things("room"):
        if r.raw and r.u(0x18, "<B") == 9:
            stats["guard room samples"] += 1
            s = None
            for slot in range(4096):
                if slot_of(slot) == r.ptr: s = slot; break
            if (r.u(0x49, "<H"), r.u(0x4B, "<H"), r.u(0x4D, "<H"), r.u(0x4F, "<H")) != from_tiles(r, s):
                stats["guard room bounds differ"] += 1
                diffs.append((tick, s, f"bounds stored {(r.u(0x49,'<H'), r.u(0x4B,'<H'), r.u(0x4D,'<H'), r.u(0x4F,'<H'))} tiles {from_tiles(r, s)}"))
for k in sorted(stats): print(f"{stats[k]:6} {k}")
for d in diffs[:25]: print(d)
sys.exit(1 if diffs else 0)
