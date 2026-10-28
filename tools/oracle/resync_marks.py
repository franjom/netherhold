# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
# See LICENSE in the project root for the full license text.
"""resync_marks: DK2's dig marks at a tick, added to a capture_state.py JSON as "marks".

Usage: python -I resync_marks.py <tick.json> <ours tiles.csv> <native tiles.csv> <player>

A full-state capture holds no mark: DK2 keeps them in the keeper's task pool (player-task-pool.md),
a heap block the capture does not cover. The marks are derived instead. Every mark is a packet the
replay also applies, so our tile log (dk2tools replay --tiles, the 'tagged' column) says when each
tile was marked; DK2's tile log (dk2capture.py tiles) says when DK2 dug it. A tile DK2 still had
marked at the tick is one marked at or before it, whose terrain in DK2's log is still rock, dirt or
gold at the tick. A mark our log shows cleared while the tile was still solid is one the player
removed (an untag packet), so DK2 lost it too; one cleared as the tile fell is our dig, which says
nothing about DK2's. Marks the replay refused (a tile DK2 owned and we did not) are missed.

Writes "marks": [{"x", "y", "player", "order"}] into the JSON, order being the marking tick, so the
dig walk's newest-first tie-break (TileState.TagOrder) can be restored.
"""
import csv
import json
import sys

SOLID_DIGGABLE = {2, 6, 9, 7}   # dirt, gold, reinforced wall, gems: what a mark can still be on


def main():
    path, ours, native, player = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
    with open(path, encoding="utf-8") as f:
        state = json.load(f)
    tick = state["tick"]
    bit = 1 << player
    marked_at, cleared_at = {}, {}
    with open(ours, encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            t = int(row["tick"])
            if t > tick:
                break
            k = (int(row["x"]), int(row["y"]))
            if int(row["tagged"]) & bit:
                marked_at[k] = t
                cleared_at.pop(k, None)
            elif k in marked_at and k not in cleared_at:
                cleared_at[k] = (t, int(row["terrain"]))
    terrain = {}
    with open(native, encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            if int(row["tick"]) > tick:
                break
            terrain[(int(row["x"]), int(row["y"]))] = int(row["terrain"])
    marks = []
    for k, t in sorted(marked_at.items(), key=lambda kv: kv[1]):
        if k in cleared_at and cleared_at[k][1] in SOLID_DIGGABLE:
            continue   # the player removed it
        if terrain.get(k) in SOLID_DIGGABLE:
            marks.append({"x": k[0], "y": k[1], "player": player, "order": t})
    state["marks"] = marks
    with open(path, "w", encoding="utf-8") as f:
        json.dump(state, f, separators=(",", ":"))
    print(f"{len(marks)} marks of player {player} at tick {tick}: "
          + " ".join(f"({m['x']},{m['y']})@{m['order']}" for m in marks))


if __name__ == "__main__":
    main()
