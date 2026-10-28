# The resync replay: continuing our simulation from a DK2 capture's state

2026-10-28, ADR-0556 and ADR-0558. Built for Q665: a full replay cannot isolate an Imp rule once the two
worlds have drifted, so this run starts from DK2's state at a tick and continues with the same
packets. Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/resync/` (the JSON, the map,
three runs and `analysis/resync_compare*.out`).

## How it runs

1. `python -I tools/oracle/capture_state.py <capture.dk2cap> <out> <tick> --state-only` writes
   `<tick>.json`: tiles (terrain, owner, room slot, health), players (money `+0x7E`, mana
   `+0x82`, and the task pool), creatures with the Imp job block (resume `+0xDF`, task tile
   `+0x8C`/`+0x90`, task id `+0x8A`, side `+0x94`, slot `+0x96`, last kind `+0x98`, `JustDropped`
   `+0x99`, the aimed kind `+0x9D` and the last search's turn `+0x9E`, all at the state object
   creature `+0x24E`; gold in hand `+0x3FB`), objects (type, point, gold) and the world seed
   `+0x15A0`; doors and traps (type `+0xF6`, construction, health); each player's manufacture
   ring (ADR-0558, below). The marks are the pool's kind-1 and kind-2 entries, each kind's list walked from
   its head (player-task-pool.md); the capture records the pool as region `REGION_POOL` + player
   index, 18 KB for player 3 at 2340. Tile flag `+0x0B` bit 0 is the Secret Door flag, not a mark.
2. `dk2tools replay level4 - out.csv --packet ... --entity-map ... --resync <tick.json>
   --resync-at <tick> [--resync-map slot,ours.csv] --jobs jobs.csv`. After our turn `tick`
   (the replay's turn numbers are DK2's, so the capture's tick `T` is applied after turn `T`),
   `WorldResync.Apply` copies the state (ADR-0556 and ADR-0558 list what it copies and what it
   leaves) and the replay goes on. `--resync-map` names our creature for native slots the replay's entity map gets
   wrong for this build; the map here was built from the packet imp casts' turns, which match.
3. `analysis/resync_compare.py` counts job starts per window and kind from `jobs.csv` and the
   capture's creature records.

Before the pool read, the marks were derived from the packet tags in our tile log and DK2's tile
log (a tile marked at or before the tick, not untagged, still solid in DK2's log). At 2340 the
derivation gave the same seven tiles as the pool: (9,57), (11,57), (11,55), (21,34), (20,34),
(20,33), (19,33).

## Level 4 seed 1, resynced at 2340, to 3400

| run | mine starts | claim | reinforce | Imps mining / claiming / reinforcing |
|---|---|---|---|---|
| native | 110 | 66 | 83 | 4.4 / 0.8 / 4.6 |
| aim read from `+0x6E`, no marks | 139 | 68 | 37 | 5.8 / 0.9 / 1.7 |
| aim from `+0x9D`, no marks | 119 | 37 | 85 | 4.4 / 0.4 / 4.1 |
| aim from `+0x9D`, derived marks, mine slots without their face | 118 | 67 | 88 | 4.3 / 0.8 / 4.6 |
| aim from `+0x9D`, the pool's marks, dig and mine slots with their face | 122 | 46 | 90 | 4.3 / 0.6 / 4.4 |
| the same, with DK2's doors, traps, crates, manufacture list and crate trips, and ADR-0557 (final) | 112 | 57 | 90 | not measured |

Without the blueprints the windows matched native's through 3100 (claims 20/44 by then are the
chains both sides run); after 3100 native claims the corridor (34,34)-(39,26), 37 claims, and ours
none. That corridor opens from an on-the-way claim of a miner walking to the store at 2861
(README, the seam-delay chase), whose crew came from Workshop trips. With them (the last row) the
east seam (29,34) is first struck at 2482 against native's 2476 (3232 with blueprints alone, 3072
with neither); this run's corridor claims are not recorded here.

- The dumper's `DroppedToDoJobId` (`+0x6E`) is 0 on DK2's three aimed reinforcers at 2340; the
  aim state 10 retries first is `+0x9D` (functions/00463DF0.md), 6 on all three. With the aim
  copied, our mapped Imps reinforce again as DK2's do.
- Without the marks, the Imp DK2 had digging (9,57) dropped the dig at once: ours had dug that
  tile at 2291 and its mark was gone; DK2 dug it at 2344. With the marks it digs (9,57) and then
  (11,57), as DK2's slot 431 does, at the same ticks.
- After the aim and the marks, the job mix matched for the first 750 ticks, but the seams still
  finished in another order: DK2's crew for (29,34) came from the Workshop at 2375-2378 (door-crate
  trips for blueprints our replay refused at 2006 and 2229, since those tiles were not ours yet).

## Doors, traps, crates and the manufacture list (ADR-0558)

- `capture_state.py` writes each door's and trap's type, construction flag and health, and each
  player's manufacture ring: points, the orders (cost, kind, type, the blueprint's point), the
  tail and slot 0's cost. `WorldResync` removes a door or trap DK2 lacks, lays one ours lacks
  (no gold, no order), takes DK2's construction flag and health, rebuilds each keeper's
  blueprint list in thing-slot order, replaces every crate with DK2's floor crates and copies the
  manufacture list, points, tail and slot-0 cost.
- An Imp on a crate trip (resume 34/36: task tile the blueprint, side word the crate's object;
  35/37 carrying) walks to the keeper's nearest crate of that object, or is given one and walks to
  the blueprint (`ImpTaskSystem.ReissueCrateJob`).
- Limits: the side word as the crate's object is STRONGLY INFERRED for 34/36 (four Imps at 2340,
  all 52, the Wooden Door crate) and a HYPOTHESIS for 35/37, where no carrying Imp is captured.
  "Nearest" is ours, so several reissued Imps may go to one crate where DK2 may have paired them
  otherwise. The blueprint list's order is DK2's thing-slot order, an assumption while slots are
  reused.
- The copy found Q618: with DK2's state in, our Imp met a trap crate waiting at 2380 where DK2's
  walked to an empty Workshop, because ours paid for a Sentry Trap at its own 1500 and DK2 at
  slot 0's 3000 (ADR-0557). The tiles that differ at the resync fall from 140 to 101.
- Job starts 2340-3400, native/ours: mine 110/112, claim 66/57, reinforce 83/90.

## The death window (resyncs at 3900 and 3496)

Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/README.md`, section "The death window".

- Resynced at 3900, ours already differs in who is alive, and 12 native slots map to nothing (a
  stale entity map, Imps cast after 2340); ours then loses 27 Imps by 4800, native 0.
- With slot reuse handled, the hero parties match native in composition. Native arrives at
  2990, 3470, 3830, 4190; ours 30 ticks later, because timers 4-9 start at 2029 natively.
- Ours also raises parties 6 (4460) and 8 (4940). A party spawns only while flag 11 is 0; native's
  Imps reach (8,25) in action point 8 at 4378 and set it, ours mines (9,26) at 4572 against
  native's 4320 and never does.
- Goblin slot 435 flees at 3500 natively on the intake its stun piled up (ADR-0559). In the 3496
  resync ours still does not flee: its frighteners, party 4, are not spawned yet.
- Full seed 1 replays to 6804: Imp deaths 45 with and without ADR-0559; the hash-316 run had 32,
  one seed, not attributed to ADR-0557.
- Next: resync after both sides' parties exist, with non-Imp creature state copied.

## Limits

The resync copies state, not history: routes, action phases, the task pool's holders and room
occupancy are ours, rebuilt from the same packets. Our loader queues no order for an authored
trap where DK2's does (Q667). A creature that is
not an Imp keeps its state and is moved only when it stands free. Treasury gold is tile stores
here (ADR-0015), so DK2's money is reported, not copied. The run drifts again after the resync;
compare the first few hundred ticks, and resync again later for the next window.
