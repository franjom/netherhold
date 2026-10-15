# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""capture_memory: one tick of a full-state capture as a memory image for dk2oracle.py.

Usage: python -I capture_memory.py <capture.dk2cap> <tick> <out.mem> [<address>=<hex bytes>...]

Writes every region the capture stored (the thing table and its active bytes, the world, the
players, the tiles, the task pools) and every stored record at the address it had in the game,
as `<u32 address> <u32 length> <bytes>`. What the capture does not store (definitions, path
buffers, the globals outside the world object) is not in the image; a function that reads it
faults, unless it is supplied as an extra `<address>=<hex bytes>` region. The decoder is the
local reverse-engineering/dk2capture.py (ADR-0046), found through DK2_CAPTURE_DECODER or the
repository's own reverse-engineering folder.
"""
import os
import struct
import sys

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.environ.get("DK2_CAPTURE_DECODER")
                or os.path.join(here, "..", "..", "reverse-engineering"))
import dk2capture  # noqa: E402


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
    with open(path, "wb") as out:
        for address, data in regions:
            out.write(struct.pack("<II", address, len(data)))
            out.write(data)


def main():
    capture, tick, path = sys.argv[1], int(sys.argv[2]), sys.argv[3]
    extra = [(int(a, 16), bytes.fromhex(b)) for a, b in (x.split("=") for x in sys.argv[4:])]
    snapshot = dk2capture.Capture(capture).at(tick)
    if snapshot is None:
        sys.exit(f"{capture} has no frame at or before tick {tick}")
    write(path, image(snapshot, extra))
    print(f"tick {snapshot.tick}: {len(snapshot.regions)} regions, {sum(1 for _ in snapshot.records())} records -> {path}")


if __name__ == "__main__":
    main()
