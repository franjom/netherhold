---
systems: [creatures, rooms, tooling-process]
---
# Q710: fighter decisions after the Level 4 restore

## Verdict

Q710 remains OPEN. The existing recordings expose unequal fighter inputs before
the first post-restore decision, and different later birth populations. They do
not isolate a causal AI-rule difference or explain the complete Guard-room gap.
This is a bounded continuation of the [post-Q714 comparison](resync-heart-terminal.md#level-4-recheck).
No native game was launched and no simulation behavior was changed.

## Evidence and controls (2026-11-14)

The frozen hash-516 packet, enriched turn-5000 inputs and eight native captures
come from `F:/DK2Evidence/studies/q714-restore--2026-11-14--001`.
The following additive studies retain their scripts, results and SHA-256 manifests:

| Study under `F:/DK2Evidence/studies/` | What it verifies |
|---|---|
| `q710-room-decisions--2026-11-14--002` | Raw native resume fields against the carried CSV rows; all prior state totals reproduced for eight native and 48 modern runs |
| `q710-population-census--2026-11-14--001` | The same totals split by initial/retained actors and post-restore birth events |
| `q710-room-observer--2026-11-14--002` | Actual restore pairings and an immediate post-restore sidecar for native seed 7 / generator 101; observer off and on each reproduce all five archived telemetry files byte-for-byte |
| `q710-current-h518--2026-11-14--001` | Two diagnostic replays, generators 101 and 202, at examined source `7d6c318a`, hash 518 |
| `q710-decision-summary--2026-11-14--002` | Selected raw native states/bed links, paired observer rows and old/current metrics, with hashes of the preceding manifests |

Ordinary CSV rows at turn 5000 precede `WorldResync.Apply`. The sidecar records
the actual `ResyncReport.Pairs` and the state immediately after that call; it
does not rematch creatures or re-run their decisions. The census uses native
observation labels without shifting them to presumed execution turns. All
native labels 5000–5450 used by the resume census were present and untorn.

Rejected setup attempts remain outside the accepted manifests: room-decisions
001 failed a total-count assertion, room-observer 001 failed source-provenance
validation, and decision-summary 001 stopped on a non-tick sidecar header.

## The first observed difference is already in the restored inputs

CONFIRMED in the seed-7 capture at label 5000: Dark Elf 428 is in state 6,
resume 98, with bed 2356 pointing back to creature 428. Troll 453 is already
walking in state 7, resume 80 (make a home). By label 5300, Troll 453 owns bed
3916 and walks in state 7, resume 63 (Workshop).

In the hash-516 generator-101 replay, the actual paired modern ids are 75 and
83 respectively. Immediately after restore, both are idle state 1, resume 0,
without a bed or active route. The same is true of all seven restored keeper
fighters in this sample. At replay label 5002, all seven are walking in state
7 with resume 80. In particular, actor 75 is seeking a home although native
428 already owns one.

The code accounts for the unequal inputs: `WorldResync.Apply` restores fighter
pose and health, retains some modern free states, and otherwise can reset to
idle. It does not reconstruct their native bed links or complete job/route and
needs history. This differs from saying that the same fighter, given the same
inputs, chose a different job. The existing [lair acquisition rule](lair-acquisition.md#5-what-starts-the-search-q150)
checks bed ownership before departure; a fresh binary read of
[0046F390](functions/0046F390.md) leaves that reading unchanged.

## Late home-seeking includes different actors

For labels 5300–5450, native Goblins and Trolls contribute zero state-7/resume-80
exposure. The 40 frozen default-Xoshiro replays average 39.125 such actor-turns
per run. This counts time walking toward home acquisition, not time executing
state 80 or a number of decisions.

| Modern population class | Goblin actor-turns/run | Troll actor-turns/run |
|---|---:|---:|
| Initial or retained | 13.3 | 1.325 |
| Post-restore unmatched birth | 3.25 | 21.25 |

In seed 7 / generator 101 specifically, the extra Troll home-walking exposure
belongs to new modern actors 87 (151 turns) and 88 (112 turns), both unmatched
by the existing birth-pair heuristic. It does not belong to initial Troll 453.
New Dark Elf 89 contributes another 123 home-walking turns and is paired to
native 419, with modern birth label 5320 versus recorded native CSV birth label
5274. The pair log's `native_turn=5273` is its matching convention.

An unmatched birth means the existing same-type/owner, 100-turn-window matcher
found no partner; it does not prove that native could never produce that actor.
“Initial or retained” likewise does not prove exact matched identity. The
population split reduces a confounder but does not produce matched histories.

## Examined hash-518 snapshot and the next controlled comparison

The old comparison predates [Q807's portal rules](portal-creature-action.md#the-portals-type-pick-built-2026-09-28-adr-0272)
and subsequent changes. Reusing seed-7 inputs at hash 518 gives:

| Generator | Fighter actor-turns, 516 → 518 | State-7 actor-turns, 516 → 518 | Goblin/Troll Guard-target actor-turns, 516 → 518 |
|---|---:|---:|---:|
| 101 | 1621 → 1339 | 909 → 587 | 77 → 142 |
| 202 | 1490 → 1923 | 886 → 1094 | 115 → 79 |

Fighter/state exposure uses 5300–5450; target exposure uses 5000–6000 and the
prior scorer's living owner-1 Guard definition. Birth mixes also change. These
two runs describe examined source `7d6c318a`, not the latest master. They are
not a replacement cohort, an isolated Q807 intervention or evidence of a general
improvement/regression.

The next bounded task is to inventory the capture's coverage of actor 428's
state/resume, route and needs inputs and the mapping of its bed 2356. Legacy
captures may lack route allocation or history. A diagnostic restore on current
code must assert the inputs it actually restores immediately at the boundary,
then trace the first bed-ownership decision with remaining mismatches explicit.
Keep later birth populations separate. Bed restoration alone cannot establish
matched inputs; Q719 and unrecovered history still limit outcome claims.
