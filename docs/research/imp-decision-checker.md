# Limits of the Imp decision checker

Checked 2026-10-17 on master `5735d000`, with unchanged gameplay.
Source: `src/DK2.Tools/DecisionCheck.cs`, the local `decisions.py` extractor,
and `level4-live1/seed01/level4.dk2cap`. Frame numbers below are capture
frames, not simulation turns. The extractor SHA256 is
`629f6387a8874550d84561a7584f370614479b4a37a7a5fb64eb2138960253cf`.

## Result — CONFIRMED controls, bounded scope

Through frame 450 the checker reports 82/82 choices equal, including the
cast Imp's claim at `(30,54)` at frame 396. Through frame 1100 it reports
347 rows: 7 task-loss rows, 337 equal choices and 3 disagreements. All three
disagreements disappear in the targeted input controls below, without
changing the chooser. This is not a proof of complete behavioral parity:
the checker neither reproduces the intervening simulation nor restores
every input to an individual native update.

| Frame / Imp | Extracted result versus DK2 | Input control | Controlled result |
|---|---|---|---|
| 828 / 452 | Dig `(28,49)` versus mine `(29,49)` | Restore only the seam's pre-turn terrain and health | Mine `(29,49)` |
| 904 / 454 | Claim `(27,43)` versus `(28,56)` | Load the earlier Imp 453's new claim and pool reservation | Claim `(28,56)` |
| 1061 / 452 | Claim `(25,60)` versus `(25,61)` | Load only the earlier Imp 443's updated position | Claim `(25,61)` |

At frame 827 the seam is terrain 6 with health 96. At 828 it is terrain 3
with health 1. The extractor copies the end-of-turn tile whenever an earlier
Imp had that task, even if later workers also worked it. It therefore gives
452 the collapsed seam before the original chose to mine it. Restoring the
pre-turn seam is a counterfactual control, not a reconstruction of its exact
health at selection. Adding captured work sides also preserves the match.

At 904, Imp 453 has already taken `(27,43)` before 454 chooses. The fixture
leaves 453 in its preceding state 10 and omits its new reservation. At 1061,
443 has moved from `(88455,225018)` to `(90341,225054)` before 452 chooses;
the fixture retains the old point, changing the stealing comparison.
Both claim mismatches persist for each forced ring-start draw 0–3 before
their worker-input corrections. RNG start side does not explain these cases.

The captured creature linked list, not numeric-id inference, places the Imps
in order `442,443,452,453,454,455` at frames 827, 903 and 1060. A reread of
[the entity sweep](functions/004B7830.md) confirms one entity update finishes
before the next. [The ring scan](functions/00468D10.md) draws from the world
stream, which the ordinary checker does not restore.

## What the checker actually establishes

Its extractor mixes previous-frame workers and reservations with selected
end-of-turn tile values and current-frame kinds 1, 2 and 14. It assumes slot
order for earlier workers. The C# checker additionally starts each choice at
tick zero with xoshiro seed zero, omits rooms and unexported objects, and may
move holders to approximate working faces when their routes fail. It keeps
one mutable world across rows. These are reconstruction limits; they do not
each establish a defect in the tested gameplay rules.

Historical agreement percentages remain observations of those fixtures.
They must not be described as exact native-state equivalence, nor may a
mismatch alone justify changing job priorities. Conversely, 337 agreements
cannot validate work duration, movement, task creation or interruption timing.
The [failed candidate acceptance](imp-candidate-acceptance.md) remains valid
as a replay result; this checker audit does not reverse that verdict.

## Required boundary for the next behavioral comparison

Capture the native state immediately before the chosen worker's selection,
after earlier entities and its own preceding action have run. Include the
actual list order, tick and RNG state; task ids, kinds, order and worker slots;
exact worker positions, routes and resume states; action id, epoch and phase;
hands, relevant piles, terrain, ownership and room inputs. Compare the chosen
task and route, then advance to the next action boundary. Whole-turn snapshots
alone cannot reconstruct shared-tile writes or task stealing at that boundary.

No new gameplay defect is established by this sample. It identifies why the
existing checker can falsely label an Imp decision as wrong. Investigate the
first discrepancy from the correctly captured boundary before another fix.

## Reproduction artifacts (local, ignored)

- `reverse-engineering/imp-first-choice-{450,1100}-20261017.{jsonl,tsv}`.
- `reverse-engineering/imp-decision-controls-20261017.py` generates the three
  frame-828 fixtures, both earlier-worker controls and the ring-control input.
- `imp-decision-828-{extracted,previous-seam,previous-seam-and-sides}-20261017`
  and `imp-decision-{904,1061}-earlier-worker-20261017`: JSONL inputs and TSV
  outputs under `reverse-engineering/`.
- `reverse-engineering/imp-decision-diagnostic-20261017/`: a copy of the
  checker with only its RNG replaced by a logged fixed draw. `IMP_RING_SIDE`
  selects 0–3; outputs are `imp-decision-ring-sideN-20261017.tsv`.

Run each JSONL through `dk2tools decide Level4 INPUT OUTPUT`. The extractor
is the `4f7164e7-51c8-4d0e-a8b2-f06401724714/scratchpad/decisions.py` beneath
the local Claude project temp directory. Its default earlier-worker patch
was enabled. No campaign sweep or gameplay modification was needed.
