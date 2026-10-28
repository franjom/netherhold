# The resync replay: continuing our simulation from a DK2 capture's state

2026-10-28, ADR-0556. Built for Q665: a full replay cannot isolate an Imp rule once the two
worlds have drifted, so this run starts from DK2's state at a tick and continues with the same
packets. Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/resync/` (the JSON, the map,
three runs and `analysis/resync_compare*.out`).

## How it runs

1. `python -I tools/oracle/capture_state.py <capture.dk2cap> <out> <tick> --state-only` writes
   `<tick>.json`: tiles (terrain, owner, room slot, health), players (money `+0x7E`, mana
   `+0x82`), creatures with the Imp job block (resume `+0xDF`, task tile `+0x8C`/`+0x90`, task
   id `+0x8A`, side `+0x94`, slot `+0x96`, last kind `+0x98`, `JustDropped` `+0x99`, the aimed
   kind `+0x9D` and the last search's turn `+0x9E`, all at the state object creature `+0x24E`;
   gold in hand `+0x3FB`), objects (type, point, gold) and the world seed `+0x15A0`.
2. `python -I tools/oracle/resync_marks.py <tick.json> <ours tiles.csv> <native tiles.csv>
   <player>` adds DK2's marks. A capture holds none (they are the task pool, a heap block outside
   the recorded regions, and tile flag `+0x0B` bit 0 is zero on every tile at 2340). A mark is a
   packet both sides apply, so our tile log says when each tile was marked and DK2's when it was
   dug: a tile marked at or before the tick, not removed by the player (our log clears it while
   the tile is still solid), and still solid in DK2's log at the tick, is marked.
3. `dk2tools replay level4 - out.csv --packet ... --entity-map ... --resync <tick.json>
   --resync-at <tick> [--resync-map slot,ours.csv] --jobs jobs.csv`. After our turn `tick`,
   `WorldResync.Apply` copies the state (ADR-0556 lists what it copies and what it leaves) and the
   replay goes on. `--resync-map` names our creature for native slots the replay's entity map gets
   wrong for this build; the map here was built from the packet imp casts' turns, which match.
4. `analysis/resync_compare.py` counts job starts per window and kind from `jobs.csv` and the
   capture's creature records.

## Level 4 seed 1, resynced at 2340, to 3400

| run | mine starts | claim | reinforce | Imps mining / claiming / reinforcing |
|---|---|---|---|---|
| native | 110 | 66 | 83 | 4.4 / 0.8 / 4.6 |
| aim read from `+0x6E`, no marks | 139 | 68 | 37 | 5.8 / 0.9 / 1.7 |
| aim from `+0x9D`, no marks | 119 | 37 | 85 | 4.4 / 0.4 / 4.1 |
| aim from `+0x9D`, DK2's marks | 118 | 67 | 88 | 4.3 / 0.8 / 4.6 |

- The dumper's `DroppedToDoJobId` (`+0x6E`) is 0 on DK2's three aimed reinforcers at 2340; the
  aim state 10 retries first is `+0x9D` (functions/00463DF0.md), 6 on all three. With the aim
  copied, our mapped Imps reinforce again as DK2's do.
- Without the marks, the Imp DK2 had digging (9,57) dropped the dig at once: ours had dug that
  tile at 2291 and its mark was gone; DK2 dug it at 2344. With the marks it digs (9,57) and then
  (11,57), as DK2's slot 431 does, at the same ticks.
- After both, the job mix matches over the 1,060 ticks. The seams still finish in another order:
  DK2's crew for (29,34) came from the Workshop at 2375-2378 (door-crate trips for blueprints our
  replay refused at 2006 and 2229, since those tiles were not ours yet), and the resync leaves
  blueprints ours. Copying DK2's doors and traps under construction from the capture is the next
  extension.

## Limits

The resync copies state, not history: routes, action phases, the task pool's holders, room
occupancy and the manufacture queue are ours, rebuilt from the same packets. A creature that is
not an Imp keeps its state and is moved only when it stands free. Treasury gold is tile stores
here (ADR-0015), so DK2's money is reported, not copied. The run drifts again after the resync;
compare the first few hundred ticks, and resync again later for the next window.
