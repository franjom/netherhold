# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""capture_export: a full-state capture's ticks as plain values (for C#) or as memory images (for dk2oracle.py).

Usage:
    python -I capture_export.py state <capture.dk2cap> <out folder> <tick>... [--dump <tick>.mem.z] [--state-only]
    python -I capture_export.py memory <capture.dk2cap> <tick> <out.mem[.z]> [<address>=<hex bytes>...]

Merged on 2026-11-16 from capture_state.py (now `state`) and capture_memory.py (now `memory`); tools/oracle/README.md.
The decoder is chosen from the capture: a record_native.py capture's frozen sources (identity checked against its
sha256-final.json; DK2_CAPTURE_DECODER may only name identical code), else DK2_CAPTURE_DECODER, the capture's sources
or the F:/DK2Evidence archive. All outputs are created exclusively; existing evidence is never replaced.

memory: one tick as a memory image. New record_native.py captures supply their verified full-memory timeline; oracle
region boundaries are preserved; the .z suffix selects compression. A legacy capture without a complete-memory
declaration gives an explicitly PARTIAL structured image. Extra address=bytes arguments are analysis overlays.

state: for each tick, writes <out>/<tick>.json and the matching memory image <out>/<tick>.mem (the `memory` format),
so a test can run the original on exactly the state it rebuilds. The JSON carries only fields whose meaning the decoder documents (reverse-engineering/
dk2capture.py's FIELDS, and the record offsets named below with the research they come from);
anything a test needs beyond them is added here, not guessed in C#.

New complete captures automatically provide full-memory fields through their verified timeline.
For legacy captures, --dump explicitly supplies a matching-clock memory image. These add:

    navBytes: w*h                         DK2's navigation-byte grid: [world+0x9F2] + 4 is the
                                          grid, width at +4, bytes at +0xC (0x004D16B0)
    creatures[].mover: {number, flags3E6, def686}
                                          the owner's Number (player +0x10FC), creature +0x3E6,
                                          definition (+0x370) +0x686: what the steering globals
                                          0x006EC9D0..E4 are set from (navigation-mesh-and-search.md §4)
    creatures[].body: {ptr, pos, vel, radius, b10E, b10F, window}
                                          the steering body at creature +0x124 (debug-dumpers.md):
                                          pos +0x84, vel +0x8C, radius +0xDC (signed word), the
                                          bytes +0x10E/+0x10F, and the five window slots at +0xB4
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
    creatures[].level                     Level +0x403 (creature-object.md)
    creatures[].fear: {temp, current, counter}
                                          TempFear +0x524, CurrentFear +0x526, OverThreatCounter
                                          +0x528 (words handed over in 0x0045E4F0, the byte counter in 0x0045EC80; combat-fear.md)
    creatures[].fightReturn: {x, y, tick}
                                          OldFightPos +0x52A/+0x52C (the withdraw state writes the x
                                          tile to both) and OldFightTick +0x52E (ADR-0225)
    flags: 128, timers: 16                the trigger banks on the world object, +0x3F70 bytes and
                                          +0x3FF0 dwords (trigger-state.md 2); a timer is the tick it
                                          was started, 0 when not
    tileHealth: w*h                       tile +0x06 (runtime-map.md)
    objects: [{slot, type, owner, x, y, gold, flags}]
                                          the decoder's object fields (gold-on-the-floor.md 1)
    players[].money, players[].mana       player +0x7E Money, +0x82 CurrentMana (player-object.md)
    players[].pool: [{kind, x, y, id, workers}]
                                          the player's task pool (player-task-pool.md), the capture's
                                          region REGION_POOL + player index: each kind's list from its
                                          head (pool +0x4656 + kind*2) by Next (+0x1F), the 36-byte
                                          entries' Position +0x00/+0x02, Id +0x21 and the twelve
                                          Worker slots +0x05; absent when the region is not captured
    marks: [{x, y, player, order}]        the pool's kind-1 (rock) and kind-2 (gold) entries, the
                                          keeper's dig marks; order counts up from the tail, so the
                                          head, raised last, has the largest (0x00507BB0 files at
                                          the head; TileState.TagOrder)
    seed                                  the world random seed at world +0x15A0 (dk2-random.md)

With --state-only no memory image is written; full-memory JSON fields still load automatically.
This is for a resync replay (dk2tools replay --resync),
which rebuilds our world from the JSON alone.
"""
import hashlib
import json
import os
import struct
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from native_memory import capture_header, for_capture  # noqa: E402

dk2capture = None


def decoder_path(capture_path):
    """The folder holding the capture's decoder: its frozen sources for a complete capture (identity checked),
    else DK2_CAPTURE_DECODER, the capture's sources or the F: archive."""
    explicit = os.environ.get('DK2_CAPTURE_DECODER')
    capture = Path(capture_path).resolve() if capture_path else None
    captured_sources = capture.parent / 'sources' if capture else None
    declared = False
    if capture is not None and capture.is_file():
        with capture.open('rb') as stream:
            is_capture = stream.read(8) == b'DK2CAP\x01\n'
        declared = is_capture and capture_header(capture).get('complete_memory') is not None
    if declared:
        source = captured_sources / 'dk2capture.py'
        manifest = json.loads((capture.parent / 'sha256-final.json').read_bytes())
        if not source.is_file() or manifest.get('sources/dk2capture.py') != hashlib.sha256(source.read_bytes()).hexdigest():
            raise ValueError('Declared complete capture decoder is missing or has a different identity')
        candidates = [Path(explicit)] if explicit else [captured_sources]
        if explicit:
            override = Path(explicit) / 'dk2capture.py'
            if not override.is_file() or hashlib.sha256(override.read_bytes()).hexdigest() != manifest['sources/dk2capture.py']:
                raise ValueError('Decoder override differs from the complete capture source')
    else:
        candidates = [Path(explicit)] if explicit else ([captured_sources] if captured_sources else [])
        if not explicit:
            candidates += [Path('F:/DK2Evidence/archive/main-clone--unknown-date--001/payload/reverse-engineering')]
    for candidate in candidates:
        if (candidate / 'dk2capture.py').is_file():
            return candidate
    raise RuntimeError('No retained capture decoder found; expected capture/sources or the F:/DK2Evidence archive')


_decoder_folder = None


def load_decoder(capture_path):
    """Imports the capture's decoder as the module global dk2capture. The folder is chosen (and a complete
    capture's frozen decoder identity-checked) on every call; a second capture that needs another decoder is refused,
    since one process can hold only one module named dk2capture."""
    global dk2capture, _decoder_folder
    folder = decoder_path(capture_path)
    if _decoder_folder is not None:
        if Path(folder).resolve() != Path(_decoder_folder).resolve():
            raise ValueError(f'{capture_path} needs the decoder in {folder}; this process already loaded {_decoder_folder}')
        return dk2capture
    sys.path.insert(0, str(folder))
    import dk2capture as decoder  # noqa: E402
    dk2capture, _decoder_folder = decoder, folder
    return dk2capture


def image(snapshot, extra=()):
    """(address, bytes) for every stored region and record, then the extras."""
    for _rid, (address, data) in sorted(snapshot.regions.items()):
        if data:
            yield address, data
    for record in snapshot.records():
        if record.raw:
            yield record.ptr, record.raw
    yield from extra


def write(path, regions):
    with open(path, "xb") as out:
        for address, data in regions:
            out.write(struct.pack("<II", address, len(data)))
            out.write(data)


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


def creature_health(r):
    """Health +0x40 is signed, as for doors and traps: a dead pool slot holds a negative value, which the
    decoder's unsigned field turned into a number the resync loader cannot read (Level 4 at 5000)."""
    return r.u(0x40, "<i")


def body(r):
    # 004D5C00: five vectors end before the radius and limit fields at body+DC.
    b = 0x124
    return {"ptr": r.ptr + b, "pos": [r.u(b + 0x84, "<i"), r.u(b + 0x88, "<i")],
            "vel": [r.u(b + 0x8C, "<i"), r.u(b + 0x90, "<i")], "radius": r.u(b + 0xDC, "<h"),
            "b10E": r.u(b + 0x10E, "<B"), "b10F": r.u(b + 0x10F, "<B"),
            "window": [[r.u(b + 0xB4 + 8 * i, "<i"), r.u(b + 0xB8 + 8 * i, "<i")] for i in range(5)]}


def with_dump(out, snap, dump):
    world = snap.regions[dk2capture.REGION_WORLD][0]
    if dump.u(dump.u(world + 4) + 4) != snap.tick:
        raise ValueError('Complete memory clock does not match the structured frame')
    grid = dump.u(world + 0x9F2) + 4
    width, data = dump.u(grid + 4), dump.u(grid + 0xC)
    if width != out['width']:
        raise ValueError('Navigation grid width does not match the structured frame')
    out["navBytes"] = list(dump.read(data, width * out["height"]))
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


ENTRY = 36


def pool_entries(data):
    """A pool region's live entries, kind by kind, each list from its head."""
    out = []
    for kind in range(1, 16):
        head = struct.unpack_from("<H", data, 0x4656 + kind * 2)[0]
        seen = set()
        while head and head not in seen and (head + 1) * ENTRY <= len(data):
            seen.add(head)
            e = head * ENTRY
            x, y = struct.unpack_from("<HH", data, e)
            out.append({"kind": kind, "x": x, "y": y, "id": struct.unpack_from("<H", data, e + 0x21)[0],
                        "workers": list(struct.unpack_from("<12H", data, e + 5))})
            head = struct.unpack_from("<H", data, e + 0x1F)[0]
    return out


def manufacture(player):
    """The manufacture list (the push 0x005066E0): points the word at +0x217, then a ring of 128
    16-byte entries from +0x219 (cost word +0, kind +2 with 0 a door and 1 a trap, type +3, the
    blueprint's point +4/+8), its tail index at ring +0x802 and count at +0x803."""
    ring = 0x219
    tail, count = player[ring + 0x802], player[ring + 0x803]
    orders = []
    for n in range(count):
        e = ring + ((tail - count + n) % 128) * 16
        orders.append({"kind": player[e + 2], "type": player[e + 3], "cost": struct.unpack_from("<H", player, e)[0],
                       "x": struct.unpack_from("<i", player, e + 4)[0], "y": struct.unpack_from("<i", player, e + 8)[0]})
    # 0x00506B50 pays against the cost word at ring slot 0, not the head's (Q618)
    return {"points": struct.unpack_from("<H", player, 0x217)[0], "orders": orders, "tail": tail,
            "slotZeroCost": struct.unpack_from("<H", player, ring)[0]}


def player_id(tag, by_tag):
    if not tag:
        return 0
    if tag not in by_tag:
        raise ValueError('unresolved player tag %d' % tag)
    return by_tag[tag]


def player_terminal(raw, by_tag):
    if raw is None or len(raw) < 0xEE6:
        raise ValueError('terminal state requires a full player region')
    return dict(status=struct.unpack_from('<I', raw, 0x19A)[0],
                killedByPlayer=player_id(struct.unpack_from('<H', raw, 0x132)[0], by_tag),
                heartDiedTick=struct.unpack_from('<I', raw, 0xEE2)[0],
                keeperFrameAfterDefeat=bool(struct.unpack_from('<I', raw, 0x76)[0] & (1 << 22)))


def active_player_tags(snap):
    # World_TickPlayers: world+7A, then player+3A. Allocated slots need not be initialized players.
    players = {}
    for index in range(dk2capture.PLAYER_COUNT):
        raw = snap.player(index)
        if raw is not None:
            players[struct.unpack_from('<H', raw, 4)[0]] = raw
    tag = struct.unpack_from('<H', snap.region(dk2capture.REGION_WORLD), 0x7A)[0]
    tags = set()
    while tag:
        if tag in tags or tag not in players:
            raise ValueError('incomplete or cyclic active player list')
        tags.add(tag)
        tag = struct.unpack_from('<H', players[tag], 0x3A)[0]
    return tags


def player_heart(snap, raw, records, by_tag):
    # Original Heart site: player+B43. Follow its room's list, not the first owned Heart.
    x, y = struct.unpack_from('<HH', raw, 0xB43)
    width, height = snap.map_size()
    if x >= width or y >= height:
        return None
    room_tag = struct.unpack_from('<H', snap.tile(x, y), 2)[0] & 0xFFF
    rooms = [r for r in records if r.kind == 'room' and r.f('tag') == room_tag and r.f('type_id') == 5]
    if not rooms:
        return None
    if len(rooms) != 1:
        raise ValueError('ambiguous Heart room')
    room = rooms[0]
    active = snap.region(dk2capture.REGION_ACTIVE)
    if not room_tag or room.slot is None or room.slot >= len(active) or not active[room.slot]:
        return None
    objects = {r.slot: r for r in records if r.kind == 'object' and r.slot is not None}
    tag, seen, hearts = room.u(0x0E, '<H'), set(), []
    while tag:
        if tag in seen or tag not in objects or tag >= len(active) or not active[tag]:
            raise ValueError('incomplete or cyclic Heart room object list')
        seen.add(tag)
        item = objects[tag]
        if item.u(0x138, '<H') != room_tag:
            raise ValueError('Heart object list has mismatched room membership')
        if item.f('type_id') == 13:
            hearts.append(item)
        tag = item.u(0x12A, '<H')
    if len(hearts) > 1:
        raise ValueError('ambiguous Heart objects in room')
    heart = hearts[0] if hearts else None
    return dict(health=heart.u(0x40, '<i') if heart else 0,
                lastAttacker=player_id(heart.u(0x36, '<H'), by_tag) if heart else 0,
                roomRuined=bool(room.u(0x9A, '<B') & 1), regenCountdown=room.u(0x56, '<B'), roomTag=room_tag)


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
    players, marks = [], []
    records = list(snap.records())
    active_players = active_player_tags(snap)
    for i in range(dk2capture.PLAYER_COUNT):
        if snap.player(i) is None:
            continue
        player = {"id": i + 1, "tag": dk2capture.field("player", "tag", snap.player(i)),
                  "ptr": paddr + i * dk2capture.PLAYER_SIZE,
                  "money": struct.unpack_from("<i", snap.player(i), 0x7E)[0],
                  "mana": struct.unpack_from("<i", snap.player(i), 0x82)[0],
                  "manufacture": manufacture(snap.player(i)),
                  "terminal": player_terminal(snap.player(i), by_tag) if dk2capture.field("player", "tag", snap.player(i)) in active_players else None,
                  "heart": player_heart(snap, snap.player(i), records, by_tag) if dk2capture.field("player", "tag", snap.player(i)) in active_players else None}
        pool = snap.regions.get(dk2capture.REGION_POOL + i)
        if pool is not None and pool[1] is not None:
            player["pool"] = pool_entries(pool[1])
            digs = [e for e in player["pool"] if e["kind"] in (1, 2)]
            marks += [{"x": e["x"], "y": e["y"], "player": i + 1, "order": len(digs) - n}
                      for n, e in enumerate(digs)]
        players.append(player)
    rooms, creatures, doors, traps, objects = [], [], [], [], []
    for r in records:
        if not r.raw:
            continue
        if r.kind == "room":
            slot = slot_of_ptr.get(r.ptr)
            rooms.append({"slot": slot, "ptr": r.ptr, "active": slot is not None and active[slot] != 0, "type": r.f("type_id"),
                          "owner": r.f("player"), "minX": r.u(0x49, "<H"), "maxX": r.u(0x4B, "<H"),
                          "minY": r.u(0x4D, "<H"), "maxY": r.u(0x4F, "<H")})
        elif r.kind == "door":
            # type +0xF6 (the factory 0x004B7B50), construction +0x109 (0x004A5070), health +0x40
            doors.append({"slot": r.slot, "x": r.f("x") >> 12, "y": r.f("y") >> 12, "owner": r.f("owner"),
                          "type": r.u(0xF6, "<B"), "constructing": r.u(0x109) != 0, "health": r.u(0x40, "<i"),
                          "open": r.u(0x10D, "<B"), "lockMode": r.u(0x111, "<B")})
        elif r.kind == "trap":
            # type +0xF6 (0x004B7C10), construction +0x10C bit 5 (0x004A7940), health +0x40
            traps.append({"slot": r.slot, "x": r.f("x") >> 12, "y": r.f("y") >> 12, "owner": r.f("owner"),
                          "type": r.u(0xF6, "<B"), "constructing": (r.u(0x10C, "<B") & 0x20) != 0,
                          "health": r.u(0x40, "<i")})
        elif r.kind == "creature":
            target = r.f("target")
            trec = snap.record(target) if target else None
            creature = {"slot": r.slot, "ptr": r.ptr, "owner": r.f("owner"), "x": r.f("x"), "y": r.f("y"),
                        "type": r.f("creature_id"), "state": r.f("state"), "health": creature_health(r),
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
            if len(r.raw) >= 0x404:
                creature["level"] = r.u(0x403, "<B")   # creature-object.md: Level +0x403
            if len(r.raw) >= 0x529:
                # TempFear +0x524, CurrentFear +0x526, OverThreatCounter +0x528 (0x0045E4F0, 0x0045EC80)
                creature["fear"] = {"temp": r.u(0x524, "<H"), "current": r.u(0x526, "<H"),
                                    "counter": r.u(0x528, "<B")}
            if len(r.raw) >= 0x532:
                # OldFightPos +0x52A/+0x52C (both the x tile) and OldFightTick +0x52E (0x004617FB-0x00461820)
                creature["fightReturn"] = {"x": r.u(0x52A, "<H"), "y": r.u(0x52C, "<H"), "tick": r.u(0x52E, "<i")}
            creatures.append(creature)
        elif r.kind == "object":
            objects.append({"slot": r.slot, "type": r.f("type_id"), "owner": r.f("owner"),
                            "x": r.f("x"), "y": r.f("y"), "gold": r.f("gold"), "flags": r.f("obj_flags")})
    return {"tick": snap.tick, "width": width, "height": height, "players": players,
            "tiles": tiles, "tileHealth": health, "rooms": rooms, "doors": doors, "traps": traps,
            "creatures": creatures, "objects": objects, "marks": marks,
            "flags": list(snap.flags_bank() or b""), "timers": snap.timers() or [],
            "seed": struct.unpack_from("<I", snap.region(dk2capture.REGION_WORLD), 0x15A0)[0]}


def main_state(args):
    if args[:1] in (['-h'], ['--help']):
        sys.exit(__doc__)
    dump = None
    state_only = "--state-only" in args
    args = [a for a in args if a != "--state-only"]
    if "--dump" in args:
        i = args.index("--dump")
        dump, args = args[i + 1], args[:i] + args[i + 2:]
    if len(args) < 3:
        sys.exit(__doc__)
    capture, out = args[0], args[1]
    load_decoder(capture)
    os.makedirs(out, exist_ok=True)
    cap = dk2capture.Capture(capture)
    if dump is not None and cap.header.get('complete_memory') is not None:
        raise ValueError('--dump is for legacy captures; complete capture memory is bound automatically')
    last = cap.ticks[-1]
    for tick in map(int, args[2:]):
        if tick > last:
            raise ValueError(f'Requested tick {tick} is past the last captured frame {last}')
        snap = cap.at(tick)
        if snap is None or snap.tick != tick or snap.torn:
            raise ValueError(f'No exact untorn frame for requested tick {tick}')
        values = state(snap)
        from native_memory import for_capture
        complete = for_capture(capture, tick, cap.header) if dump is None else None
        if dump is not None:
            values = with_dump(values, snap, Dump(dump))
            values["memoryImage"] = os.path.abspath(dump)
            values['captureCompleteness'] = 'explicit-memory-image'
        elif complete is not None:
            values = with_dump(values, snap, complete)
            values['memoryTimeline'] = str(complete.folder)
            values['captureCompleteness'] = 'complete-readable-process-memory'
            if not state_only:
                memory_path = os.path.join(out, f'{tick}.mem.z')
                complete.export(memory_path)
                values['memoryImage'] = os.path.abspath(memory_path)
        elif not state_only:
            write(os.path.join(out, f"{tick}.mem"), image(snap))
        if complete is None and dump is None:
            values['captureCompleteness'] = 'partial-legacy-structured-frame'
            print(f'PARTIAL legacy frame {tick}: no complete memory timeline.', file=sys.stderr)
        with open(os.path.join(out, f"{tick}.json"), "x", encoding="utf-8") as f:
            json.dump(values, f, separators=(",", ":"))
        print(tick)



def main_memory(args):
    if len(args) < 3 or args[0] in ('-h', '--help'):
        sys.exit(__doc__)
    capture, tick, path = args[0], int(args[1]), args[2]
    extra = [(int(a, 16), bytes.fromhex(b)) for a, b in (x.split("=") for x in args[3:])]
    load_decoder(capture)
    cap = dk2capture.Capture(capture)
    snapshot = cap.at(tick)
    if snapshot is None or snapshot.tick != tick or snapshot.torn:
        sys.exit(f"{capture} has no exact untorn frame at tick {tick}")
    complete = for_capture(capture, tick, cap.header)
    if complete is not None:
        complete.export(path, compressed=path.endswith('.z'), extra=extra)
    else:
        print('PARTIAL legacy image: this capture has no complete memory timeline.', file=sys.stderr)
        write(path, image(snapshot, extra))
    print(f"tick {snapshot.tick}: {len(snapshot.regions)} regions, {sum(1 for _ in snapshot.records())} records -> {path}")



def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if not argv or argv[0] in ('-h', '--help') or argv[0] not in ('state', 'memory'):
        print(__doc__)
        return 0 if argv and argv[0] in ('-h', '--help') else 2
    (main_state if argv[0] == 'state' else main_memory)(argv[1:])
    return 0


if __name__ == "__main__":
    sys.exit(main())
