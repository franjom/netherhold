"""Answer M7TrapFights2's three owner checks from a dk2record.py capture, so nothing rests on watching.

    py tools/levelgen/m7trapfights_report.py <capture.dk2cap> [--log]

Q362 (chambers x 24..36, rows 3..19): does a brave creature ever take a Hero Sentry as its target
    (creature +0x502 equal to the trap record's tag, +0x04), and in which state?
Q513a (corridors x 3 and x 7, rows 3..11): which of the two hero Boulder traps, (3,4) on neutral
    Dirt Path and (7,4) on hero ground, lets a boulder (object type 74) out, and when?
Q729 (Prison x 2..4, rows 30..32): when the Prison's terrain stops being Prison (sold), what the
    captive Dwarf (type 14) is doing and where, and where it ends: dead body, state, tile.

--log also prints every change as it happens: creature state (named from creature-state-table.md),
target, health; traps, boulders, shots and dead bodies appearing and going.
Reads the capture through reverse-engineering/dk2capture.py (gitignored, local).
"""
import argparse
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
sys.path.insert(0, os.path.join(ROOT, "reverse-engineering"))
from dk2capture import Capture, field  # noqa: E402

BOULDER, PRISON_TERRAIN, DWARF, IMP = 74, 20, 14, 1
BOULDER_TRAPS = {(3, 4): "neutral Dirt Path", (7, 4): "hero Claimed Path"}
PRISON = [(x, y) for x in range(2, 5) for y in range(30, 33)]


def state_names():
    path = os.path.join(ROOT, "docs", "research", "creature-state-table.md")
    names = {}
    with open(path, encoding="utf-8") as f:
        for m in re.finditer(r"\| (\d+) `0x[0-9a-f]+` \| ([^|]+?) \|", f.read()):
            names[int(m.group(1))] = m.group(2)
    return names


def zone(x, y):
    if x >= 24 and y <= 19:
        return "Q362"
    if x <= 8 and y <= 11:
        return "Q513a"
    if x <= 14 and y >= 29:
        return "Q729"
    return "-"


def tile_of(rec):
    return rec.f("x") >> 12, rec.f("y") >> 12


def where(rec):
    return "(%5.2f,%5.2f)" % (rec.f("x") / 4096.0, rec.f("y") / 4096.0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("capture")
    ap.add_argument("--log", action="store_true", help="print every change, not only the answers")
    a = ap.parse_args()
    names = state_names()
    sname = lambda s: "%d %s" % (s, names.get(s, "?"))
    log = print if a.log else (lambda *_: None)

    creatures = {}          # ptr -> dict(id, owner, zone, state, target, health)
    others = {}             # ptr -> (kind, label, tile)
    trap_tags = {}          # tag -> (tile, owner)
    q362 = {}               # creature ptr -> {(state, trap tile): [first tick, frames]}
    q513 = {}               # trap tile -> first boulder tick
    sold = None
    prison_seen = False
    dwarf_at_sale = None
    dwarf_last = None
    dwarf_body = None
    last = None

    for snap in Capture(a.capture).frames():
        last = snap.tick
        seen = set()
        for rec in snap.things():
            if rec.raw is None or rec.kind in ("room", "player", "unknown"):
                continue
            if rec.kind == "trap":
                trap_tags[rec.f("tag")] = (tile_of(rec), rec.f("owner"))
            if rec.kind == "creature":
                if not rec.f("creature_id"):
                    continue
                seen.add(rec.ptr)
                tx, ty = tile_of(rec)
                now = dict(id=rec.f("creature_id"), owner=rec.f("owner"), state=rec.f("state"),
                           target=rec.f("target"), health=rec.f("health"))
                old = creatures.get(rec.ptr)
                if old is None:
                    now["zone"] = zone(tx, ty)
                    log("t%5d %-5s creature %08X type %d owner %d NEW at %s state %s health %d" % (
                        snap.tick, now["zone"], rec.ptr, now["id"], now["owner"], where(rec), sname(now["state"]), now["health"]))
                else:
                    now["zone"] = old["zone"]
                    what = []
                    if old["state"] != now["state"]:
                        what.append("state %s -> %s" % (sname(old["state"]), sname(now["state"])))
                    if old["target"] != now["target"]:
                        t = trap_tags.get(now["target"])
                        what.append("target %d -> %d%s" % (old["target"], now["target"], " (TRAP at %s)" % (t[0],) if t else ""))
                    if old["health"] != now["health"]:
                        what.append("health %d -> %d" % (old["health"], now["health"]))
                    if what:
                        log("t%5d %-5s creature %08X type %d at %s %s" % (snap.tick, now["zone"], rec.ptr, now["id"], where(rec), "; ".join(what)))
                creatures[rec.ptr] = now
                if now["zone"] == "Q362" and now["target"] in trap_tags:
                    hit = q362.setdefault(rec.ptr, {}).setdefault((now["state"], trap_tags[now["target"]][0]), [snap.tick, 0])
                    hit[1] += 1
                if now["id"] == DWARF and now["zone"] == "Q729":
                    dwarf_last = (snap.tick, now["state"], where(rec), now["health"])
                continue
            kind = rec.kind
            if kind == "object":
                if rec.f("type_id") != BOULDER:
                    continue
                kind = "boulder"
            elif kind not in ("trap", "shot", "dead_body"):
                continue
            seen.add(rec.ptr)
            if rec.ptr not in others:
                t = tile_of(rec)
                others[rec.ptr] = (kind, t)
                log("t%5d %-5s %s %08X owner %d NEW at %s" % (snap.tick, zone(*t), kind, rec.ptr, rec.f("owner"), where(rec)))
                if kind == "boulder":
                    near = min(BOULDER_TRAPS, key=lambda p: abs(p[0] - t[0]) + abs(p[1] - t[1]))
                    if abs(near[0] - t[0]) + abs(near[1] - t[1]) <= 2:
                        q513.setdefault(near, snap.tick)
                if kind == "dead_body" and zone(*t) == "Q729" and dwarf_body is None:
                    dwarf_body = (snap.tick, t)
        for ptr in [p for p in creatures if p not in seen]:
            c = creatures.pop(ptr)
            log("t%5d %-5s creature %08X type %d GONE (last state %s)" % (snap.tick, c["zone"], ptr, c["id"], sname(c["state"])))
        for ptr in [p for p in others if p not in seen]:
            kind, t = others.pop(ptr)
            log("t%5d %-5s %s %08X GONE (placed at %s)" % (snap.tick, zone(*t), kind, ptr, t))
        if sold is None and snap.map_size()[0]:
            terrains = [field("tile", "terrain", snap.tile(x, y)) for x, y in PRISON]
            prison_seen = prison_seen or PRISON_TERRAIN in terrains
            if prison_seen and PRISON_TERRAIN not in terrains:
                sold = snap.tick
                dwarf_at_sale = dwarf_last
                log("t%5d Q729  Prison sold (terrain now %s)" % (snap.tick, sorted(set(terrains))))

    print("capture ends at tick %s" % last)
    print("\nQ362: does a brave creature target a revealed Hero Sentry?")
    if not q362:
        print("  no creature in the chambers ever had a trap as its target")
    for ptr, hits in q362.items():
        for (state, t), (tick, frames) in sorted(hits.items(), key=lambda h: h[1][0]):
            print("  creature %08X targets the trap at %s in state %s: from t%d, %d frames" % (ptr, t, sname(state), tick, frames))
    print("\nQ513a: which hero Boulder trap rolls?")
    for t, ground in BOULDER_TRAPS.items():
        print("  %s on %s: %s" % (t, ground, "boulder at t%d" % q513[t] if t in q513 else "no boulder"))
    print("\nQ729: the captive when its Prison is sold during the carry (state 44's exit 4)")
    if sold is None:
        print("  the Prison was not sold in this capture")
    else:
        print("  sold at t%d; the Dwarf then: %s" % (sold, "t%d state %s at %s health %d" % (
            dwarf_at_sale[0], sname(dwarf_at_sale[1]), dwarf_at_sale[2], dwarf_at_sale[3]) if dwarf_at_sale else "not seen"))
    if dwarf_last:
        print("  the Dwarf last seen t%d state %s at %s health %d" % (
            dwarf_last[0], sname(dwarf_last[1]), dwarf_last[2], dwarf_last[3]))
    if dwarf_body:
        print("  a dead body appeared in the zone at t%d on %s" % dwarf_body)


if __name__ == "__main__":
    main()
