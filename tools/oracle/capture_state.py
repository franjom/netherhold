# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""capture_state: ticks of a full-state capture as plain values, for C# to rebuild our world from.

Usage: python -I capture_state.py <capture.dk2cap> <out folder> <tick>... [--dump <tick>.mem.z] [--state-only]

For each tick, writes <out>/<tick>.json and the matching memory image <out>/<tick>.mem
(capture_memory.py's format), so a test can run the original on exactly the state it rebuilds.
The JSON carries only fields whose meaning the decoder documents (reverse-engineering/
dk2capture.py's FIELDS, and the record offsets named below with the research they come from);
anything a test needs beyond them is added here, not guessed in C#.

With --dump (process_dump.py's whole-process image of the same tick), the memory image is the
dump itself, and the JSON also carries what only the dump holds:

    navBytes: w*h                         DK2's navigation-byte grid: [world+0x9F2] + 4 is the
                                          grid, width at +4, bytes at +0xC (0x004D16B0)
    creatures[].mover: {number, flags3E6, def686}
                                          the owner's Number (player +0x10FC), creature +0x3E6,
                                          definition (+0x370) +0x686: what the steering globals
                                          0x006EC9D0..E4 are set from (navigation-mesh-and-search.md §4)
    creatures[].body: {ptr, pos, vel, radius, b10E, b10F, window}
                                          the steering body at creature +0x124 (debug-dumpers.md):
                                          pos +0x84, vel +0x8C, radius +0xDC (signed word), the
                                          bytes +0x10E/+0x10F, and the eight window slots at +0xB4
                                          that (+0x10E >> 3) & 7 can index

    tick, width, height
    players: [{id, tag, ptr}]             id = array index + 1, tag = its thing-table slot
    tiles:   [terrain, owner, room] * w*h  terrain +0x0A, owner +0x08 & 0xFFF as a player id
                                          (0 when it names no player), room +0x02 & 0xFFF slot
    rooms:   [{slot, ptr, active, type, owner, minX, maxX, minY, maxY}]  active: the slot's byte
                                          the decoder's type_id and player; bounds +0x49/+0x4B/
                                          +0x4D/+0x4F (function 0x00460B50's record)
    doors:   [{slot, x, y, owner, open, lockMode}]   tile from the thing's x/y; open +0x10D, lock
                                          mode +0x111 (DoorState's DK2 offsets)
    creatures: [{slot, ptr, owner, x, y, type, state, health, target, targetClass}]
                                          the decoder's thing and creature fields; targetClass is
                                          the target's cls (0 a creature), null if none is stored
    creatures[].job: {resume, taskX, taskY, taskId, taskSide, taskSlot, lastKind, justDropped,
                      aimedKind, lastSearch, droppedTask, gold}
                                          the state object at creature +0x24E (creature-job-object.md):
                                          UtilityNewState +0xDF, ImpTaskPositionX/Y +0x8C/+0x90,
                                          ImpTaskID +0x8A, ImpTaskSurface +0x94, ImpTaskSlot +0x96,
                                          LastTaskType +0x98, JustDropped +0x99, the aimed kind +0x9D
                                          and the last search's turn +0x9E (functions/00463DF0.md),
                                          DroppedToDoJobId +0x6E (the dumper's name; not the aim);
                                          gold is the decoder's gold_held (+0x3FB). Absent on a
                                          record too short to hold them
    tileHealth: w*h                       tile +0x06 (runtime-map.md)
    objects: [{slot, type, owner, x, y, gold, flags}]
                                          the decoder's object fields (gold-on-the-floor.md 1)
    players[].money, players[].mana       player +0x7E Money, +0x82 CurrentMana (player-object.md)
    seed                                  the world random seed at world +0x15A0 (dk2-random.md)

With --state-only no memory image is written: for a resync replay (dk2tools replay --resync),
which rebuilds our world from the JSON alone.
"""
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import capture_memory  # noqa: E402

dk2capture = capture_memory.dk2capture


class Dump(object):
    """Reads a process_dump.py image."""

    def __init__(self, path):
        import bisect
        import zlib
        self._bisect = bisect.bisect_right
        self.mem = zlib.decompress(open(path, "rb").read())
        self.regions = []
        at = 0
        while at < len(self.mem):
            address, length = struct.unpack_from("<II", self.mem, at)
            self.regions.append((address, at + 8, length))
            at += 8 + length
        self.starts = [r[0] for r in self.regions]

    def read(self, address, n):
        i = self._bisect(self.starts, address) - 1
        if i < 0:
            raise KeyError(f"{address:08x}+{n:x} is not in the dump")
        start, offset, length = self.regions[i]
        if address + n > start + length:
            raise KeyError(f"{address:08x}+{n:x} is not in the dump")
        return self.mem[offset + address - start:offset + address - start + n]

    def u(self, address, fmt="<I"):
        return struct.unpack(fmt, self.read(address, struct.calcsize(fmt)))[0]


def body(r):
    b = 0x124
    return {"ptr": r.ptr + b, "pos": [r.u(b + 0x84, "<i"), r.u(b + 0x88, "<i")],
            "vel": [r.u(b + 0x8C, "<i"), r.u(b + 0x90, "<i")], "radius": r.u(b + 0xDC, "<h"),
            "b10E": r.u(b + 0x10E, "<B"), "b10F": r.u(b + 0x10F, "<B"),
            "window": [[r.u(b + 0xB4 + 8 * i, "<i"), r.u(b + 0xB8 + 8 * i, "<i")] for i in range(8)]}


def with_dump(out, snap, dump):
    world = snap.regions[dk2capture.REGION_WORLD][0]
    grid = dump.u(world + 0x9F2) + 4
    width, data = dump.u(grid + 4), dump.u(grid + 0xC)
    out["navBytes"] = list(dump.read(data, width * out["height"]))[:out["width"] * out["height"]]         if width == out["width"] else None
    for c in out["creatures"]:
        r = snap.record(c["slot"])
        player = r.u(0x2A)
        if not player or not r.f("def"):   # a pooled slot: no owner, no definition
            c["mover"] = c["body"] = None
            continue
        c["mover"] = {"number": dump.u(player + 0x10FC, "<B"), "flags3E6": r.u(0x3E6),
                      "def686": dump.u(r.f("def") + 0x686)}
        c["body"] = body(r)
    return out


def state(snap):
    table = snap.region(dk2capture.REGION_THING_TABLE)
    active = snap.region(dk2capture.REGION_ACTIVE)
    slot_of_ptr = {}
    for slot in range(len(table) // 4):
        p = struct.unpack_from("<I", table, 4 * slot)[0]
        if p:
            slot_of_ptr.setdefault(p, slot)
    by_tag = snap.player_by_tag()
    width, height = snap.map_size()
    tiles, health = [], []
    for y in range(height):
        for x in range(width):
            t = snap.tile(x, y)
            tiles += [t[0x0A], by_tag.get(struct.unpack_from("<H", t, 0x08)[0] & 0xFFF, 0),
                      struct.unpack_from("<H", t, 0x02)[0] & 0xFFF]
            health.append(struct.unpack_from("<H", t, 0x06)[0])
    paddr, _ = snap.regions[dk2capture.REGION_PLAYERS]
    players = [{"id": i + 1, "tag": dk2capture.field("player", "tag", snap.player(i)),
                "ptr": paddr + i * dk2capture.PLAYER_SIZE,
                "money": struct.unpack_from("<i", snap.player(i), 0x7E)[0],
                "mana": struct.unpack_from("<i", snap.player(i), 0x82)[0]}
               for i in range(dk2capture.PLAYER_COUNT) if snap.player(i) is not None]
    rooms, creatures, doors, objects = [], [], [], []
    for r in snap.records():
        if not r.raw:
            continue
        if r.kind == "room":
            slot = slot_of_ptr.get(r.ptr)
            rooms.append({"slot": slot, "ptr": r.ptr, "active": slot is not None and active[slot] != 0, "type": r.f("type_id"),
                          "owner": r.f("player"), "minX": r.u(0x49, "<H"), "maxX": r.u(0x4B, "<H"),
                          "minY": r.u(0x4D, "<H"), "maxY": r.u(0x4F, "<H")})
        elif r.kind == "door":
            doors.append({"slot": r.slot, "x": r.f("x") >> 12, "y": r.f("y") >> 12, "owner": r.f("owner"),
                          "open": r.u(0x10D, "<B"), "lockMode": r.u(0x111, "<B")})
        elif r.kind == "creature":
            target = r.f("target")
            trec = snap.record(target) if target else None
            creature = {"slot": r.slot, "ptr": r.ptr, "owner": r.f("owner"), "x": r.f("x"), "y": r.f("y"),
                        "type": r.f("creature_id"), "state": r.f("state"), "health": r.f("health"),
                        "target": target,
                        "targetClass": None if trec is None or trec.raw is None else trec.f("cls")}
            if len(r.raw) >= 0x3FF:
                job = 0x24E
                creature["job"] = {"resume": r.u(job + 0xDF), "taskX": r.u(job + 0x8C, "<i"),
                                   "taskY": r.u(job + 0x90, "<i"), "taskId": r.u(job + 0x8A, "<H"),
                                   "taskSide": r.u(job + 0x94, "<H"), "taskSlot": r.u(job + 0x96, "<H"),
                                   "lastKind": r.u(job + 0x98, "<B"), "justDropped": r.u(job + 0x99),
                                   "aimedKind": r.u(job + 0x9D, "<B"), "lastSearch": r.u(job + 0x9E),
                                   "droppedTask": r.u(job + 0x6E), "gold": r.f("gold_held")}
            creatures.append(creature)
        elif r.kind == "object":
            objects.append({"slot": r.slot, "type": r.f("type_id"), "owner": r.f("owner"),
                            "x": r.f("x"), "y": r.f("y"), "gold": r.f("gold"), "flags": r.f("obj_flags")})
    return {"tick": snap.tick, "width": width, "height": height, "players": players,
            "tiles": tiles, "tileHealth": health, "rooms": rooms, "doors": doors,
            "creatures": creatures, "objects": objects,
            "seed": struct.unpack_from("<I", snap.region(dk2capture.REGION_WORLD), 0x15A0)[0]}


def main():
    args = sys.argv[1:]
    dump = None
    state_only = "--state-only" in args
    args = [a for a in args if a != "--state-only"]
    if "--dump" in args:
        i = args.index("--dump")
        dump, args = args[i + 1], args[:i] + args[i + 2:]
    capture, out = args[0], args[1]
    os.makedirs(out, exist_ok=True)
    cap = dk2capture.Capture(capture)
    last = cap.ticks[-1]
    for tick in map(int, args[2:]):
        if tick > last:
            continue   # Capture.at would repeat the last frame
        snap = cap.at(tick)
        if snap is None:
            continue
        values = state(snap)
        if dump is not None:
            values = with_dump(values, snap, Dump(dump))
            values["memoryImage"] = os.path.abspath(dump)
        elif not state_only:
            capture_memory.write(os.path.join(out, f"{tick}.mem"), capture_memory.image(snap))
        with open(os.path.join(out, f"{tick}.json"), "w", encoding="utf-8") as f:
            json.dump(values, f, separators=(",", ":"))
        print(tick)


if __name__ == "__main__":
    main()
