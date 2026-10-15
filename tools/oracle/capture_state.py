# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""capture_state: ticks of a full-state capture as plain values, for C# to rebuild our world from.

Usage: python -I capture_state.py <capture.dk2cap> <out folder> <tick>...

For each tick, writes <out>/<tick>.json and the matching memory image <out>/<tick>.mem
(capture_memory.py's format), so a test can run the original on exactly the state it rebuilds.
The JSON carries only fields whose meaning the decoder documents (reverse-engineering/
dk2capture.py's FIELDS, and the record offsets named below with the research they come from);
anything a test needs beyond them is added here, not guessed in C#.

    tick, width, height
    players: [{id, tag, ptr}]             id = array index + 1, tag = its thing-table slot
    tiles:   [terrain, owner, room] * w*h  terrain +0x0A, owner +0x08 & 0xFFF as a player id
                                          (0 when it names no player), room +0x02 & 0xFFF slot
    rooms:   [{slot, ptr, active, type, owner, minX, maxX, minY, maxY}]  active: the slot's byte
                                          the decoder's type_id and player; bounds +0x49/+0x4B/
                                          +0x4D/+0x4F (function 0x00460B50's record)
    creatures: [{slot, ptr, owner, x, y, type, state, health, target, targetClass}]
                                          the decoder's thing and creature fields; targetClass is
                                          the target's cls (0 a creature), null if none is stored
"""
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import capture_memory  # noqa: E402

dk2capture = capture_memory.dk2capture


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
    tiles = []
    for y in range(height):
        for x in range(width):
            t = snap.tile(x, y)
            tiles += [t[0x0A], by_tag.get(struct.unpack_from("<H", t, 0x08)[0] & 0xFFF, 0),
                      struct.unpack_from("<H", t, 0x02)[0] & 0xFFF]
    paddr, _ = snap.regions[dk2capture.REGION_PLAYERS]
    players = [{"id": i + 1, "tag": dk2capture.field("player", "tag", snap.player(i)),
                "ptr": paddr + i * dk2capture.PLAYER_SIZE} for i in range(dk2capture.PLAYER_COUNT)
               if snap.player(i) is not None]
    rooms, creatures = [], []
    for r in snap.records():
        if not r.raw:
            continue
        if r.kind == "room":
            slot = slot_of_ptr.get(r.ptr)
            rooms.append({"slot": slot, "ptr": r.ptr, "active": slot is not None and active[slot] != 0, "type": r.f("type_id"),
                          "owner": r.f("player"), "minX": r.u(0x49, "<H"), "maxX": r.u(0x4B, "<H"),
                          "minY": r.u(0x4D, "<H"), "maxY": r.u(0x4F, "<H")})
        elif r.kind == "creature":
            target = r.f("target")
            trec = snap.record(target) if target else None
            creatures.append({"slot": r.slot, "ptr": r.ptr, "owner": r.f("owner"), "x": r.f("x"), "y": r.f("y"),
                              "type": r.f("creature_id"), "state": r.f("state"), "health": r.f("health"),
                              "target": target,
                              "targetClass": None if trec is None or trec.raw is None else trec.f("cls")})
    return {"tick": snap.tick, "width": width, "height": height, "players": players,
            "tiles": tiles, "rooms": rooms, "creatures": creatures}


def main():
    capture, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    cap = dk2capture.Capture(capture)
    last = cap.ticks[-1]
    for tick in map(int, sys.argv[3:]):
        if tick > last:
            continue   # Capture.at would repeat the last frame
        snap = cap.at(tick)
        if snap is None:
            continue
        with open(os.path.join(out, f"{tick}.json"), "w", encoding="utf-8") as f:
            json.dump(state(snap), f, separators=(",", ":"))
        capture_memory.write(os.path.join(out, f"{tick}.mem"), capture_memory.image(snap))
        print(tick)


if __name__ == "__main__":
    main()
