---
systems: [simulation-core, campaign-triggers, rooms, tooling-process]
---
# Heart and terminal-state resync (Q714)

## Scope and representation

`capture_state.py` exports optional `players[].terminal` and `players[].heart`.
`WorldResync.Apply` restores them under `ResyncParts.Players`; absent sections
retain modern values and report that limitation. Old JSON remains compatible.
A present section requires all its fields: incomplete JSON is an input error,
not an invented Playing status or dead Heart. The CLI returns exit 2.
This changes the replay adapter without adding hashed fields, ordinary simulation
rules or snapshot shape; it does not itself advance hash/save version. The frozen
comparison below uses version 516, before the separate Q807 portal change.

The exporter follows the active player list (world `+0x7A`, player `+0x3A`;
[player walk](functions/004C2310.md)). Allocated but inactive player slots can
contain uninitialized status/killer bytes and supply no terminal/Heart section.
Full-player-region bytes supply status `+0x19A`, killer word `+0x132`, death turn
`+0xEE2` and flags `+0x76` bit 22. The short thing-table head cannot supply them.
Killer/source tags are translated through the captured player map; unresolved
nonzero tags are rejected. Zero remains the no-player sentinel.

The recorded Heart position (`player+0xB43`) selects its room through the native
tile's room tag. The room's object list (`+0x0E`, next `object+0x12A`) selects type
13 and validates `object+0x138` membership, following the
[Heart frame](functions/004D9A60.md). Another owned ruined Heart cannot replace
this site; a claimed ruin at the original site remains eligible despite its owner.
The capture supplies signed object health `+0x40`, source word `+0x36`, room ruin
bit `+0x9A` and regeneration countdown `+0x56`. A captured list with no Heart
means absent/zero health; an unavailable site means unavailable metadata.

Restore assigns fields directly, including the matching native room-tag tiles'
ruin flags. It calls neither health-change methods nor verdict handlers: restoring
data must not credit damage, sample results, scatter creatures or emit verdicts.
It never derives status from health or forces Playing. The native death turn is
converted to modern tick by subtracting one, retaining zero's unset sentinel.

## Result-history limits

Any supplied terminal section clears the stale sampled fields and `AtVerdict`;
running counters remain modern. This cannot undo counter adjustments made by a
prior modern verdict, and does not reconstruct native historical results.
Playing clears the stale status stamp. A native terminal status is retained with
`WorldResync.UnknownVerdictTick` (-1), `Statistics.Sampled=false` and unknown
`EndTurn`; replay prints `tick=unknown`. The Unity result writer already selects
only sampled results, so it cannot record this unavailable result as a native one.

The [native end routine](functions/004BE630.md) stores whole-second `TimeTaken`,
not an exact verdict tick. The separate profile copy is historical state outside
this bounded restore. Campaign/finale side effects, counters, creature histories
and Q719's other partial inputs remain limits; this is not full-state parity.

## Verification

Evidence: `F:/DK2Evidence/studies/q714-restore--2026-11-14--001`.
The frozen build derives from `5961ce12`; its committed simulation/tool sources
match the [earlier measured build](level4-outcomes.md#q710-recheck-at-hash-516-2026-11-14)
before this adapter change. Existing native captures were read; no game was launched.
Eight enriched turn-5000 inputs add only these two sections to the earlier JSON;
removing them reproduces every old value. Raw capture identities are in `inputs.json`.

The old restore logic fails seven regression cases and passes fifteen controls;
the fixed full simulation suite passes 5418 tests, Tools passes 231 and the Python
exporter passes 17. Fixtures cover terminal statuses, Playing after stale loss,
unknown verdict time, legacy/skipped/unavailable sections, signed health, ruin,
regeneration phase, tag translation, claimed ruins and active-player selection.
One unchanged-input replay matches all five earlier telemetry files byte for byte.
Nine missing-field regressions and two dispatch cases fail before their fixes;
the latter now return exit 2 with a capture-specific error. The final parser's
legacy and enriched controls each match all five frozen-build telemetry files;
its simulation assembly is byte-identical (`contract-validation/verification.json`).

## Level 4 recheck

`analysis.json` scores 40 default-Xoshiro runs (eight native states times generator
seeds 101/202/303/404/505) and eight DK2-LCG controls, completing turn 6000 after
restore at completed turn 5000. Two original-build controls and two final-parser
controls bring the archived run total to 52; the repeated enriched pilot matches
all five telemetry files. This uses the earlier packet/offset 69 and native births.

| Cohort | Runs | Imp exits/deaths per run | State 7 share | State 8 share | Melee Guard-target turns per run |
|---|---:|---:|---:|---:|---:|
| Native | 8 | 6.5 | 0.1855 | 0.0695 | 274 |
| Before, default Xoshiro (includes eight pre-restore losses) | 40 | 10.025 | 0.1915 | 0.0273 | 156.25 |
| After, default Xoshiro | 40 | 8.825 | 0.2494 | 0.0305 | 189.425 |
| DK2 LCG, before and after | 8 each | 9.375 | 0.2216 | 0.0316 | 180 |

Definitions match the [prior baseline](level4-outcomes.md#q710-recheck-at-hash-516-2026-11-14):
deaths in 5001..6000 (native slot exits versus modern events), fighter state shares
in 5300..5450, and Goblin/Troll target turns against any living owner-1 Guard in
5000..6000. All modern Imp death durations here are zero; backdating changes none.
The post-fix default cases have no retained pre-restore result. Two lose later:
state 6/generator 101 at stamp 5731 and state 8/generator 101 at 5756.

Q710 stays OPEN; the [fighter-decision study](q710-fighter-decisions.md) continues the input comparison. The state-7/state-8 and fighter-exposure gap remains,
without a causal AI rule. Default Xoshiro ignores captured RNG state; LCG controls
still lack matched initialization/history. Legacy polling labels, the stale-row
exclusion heuristic, unrecovered counters and other Q719 inputs limit both cohorts.
The restore fixes an identified adapter omission; it is not evidence that this
omission caused every excess death or that either cohort establishes parity.
