# Decision parity audit, hash 303

2026-10-25. Analysis and standalone diagnostics against the archived simulation
from `e96639742fa12c87eeb1407cdc13418f1b7931fe` (hash/save 303).
No simulation changes or additional modern campaign replay. One approved native
Level 1 playback supplies the capture below. This is a bounded audit, not a claim
that remaining outcome differences are solely RNG.

## Scope and evidence

The [saved replay failure analysis](../experiments/2026-10-25-packet-replay-hash303/analysis.md)
identifies the Level 1 Hatchery timing gate and Level 4 frontier interruptions.
This audit checks native-versus-modern decisions rather than tuning those rates.

New rig and outputs: `D:/DK2Evidence/parity-audit-2026-10-25-hash303/`.
The archived hash-303 assemblies remain at
`D:/DK2Evidence/tools/hash303-e9663974-2026-10-25/`.
Native inputs are the two `level4.dk2cap` files under
`D:/DK2Evidence/dk2runs/level4-live1/seed01/` and
`D:/DK2Evidence/dk2runs/level4-live1-seed2/seed02/`, plus the retained
`imp-strike-choice-20261018` live boundary under
`D:/DK2Evidence/imp-acceptance-worktree/reverse-engineering/`.
No existing evidence was overwritten. New diagnostic projects reference the
archived DLLs; they do not rebuild the simulation. Whole-turn observations use
raw native headers under [the calibrated clock limits](imp-observation-clocks.md).

## Level 1: captured construction window and trigger visibility

The owner approved one elevated `l1live` playback on the shipped 30×38 map,
without a seeder or packet recording. The completed capture is
`D:/DK2Evidence/parity-audit-2026-10-25-hash303/native-level1-20261025-130519-2260/level1.dk2cap`.
It contains all 850 headers, 1–850, with zero torn/missed frames, missing record
reads or unknown vtables. The driver closed only its own game and recorder.
`level1-analysis/` contains the reader, native worker/tile CSVs, comparison and
input hashes. The original 226-file audit seal remains unchanged.

**CONFIRMED observations:** the native footprint's last tile becomes claimed at
header 464; the recorded Hatchery wave starts at 466 and finishes at 470.
That leaves only two recorded ticks between claim completion and construction.
The modern values below come from existing hash-303 outputs, not new runs.

| Observation | Native seed 1 | Ours seed 1 | Ours seed 3 | Ours seed 7 |
|---|---:|---:|---:|---:|
| First/last Hatchery footprint tile claimed | 358 / 464 | 330 / 400 | 730 / 808 | 577 / 683 |
| Hatchery tiles built at 466–470 | 9 | 9 | 0 | 0 |
| Flag 18 first observed | 464 | 401 | 809 | 684 |

Native worker 455 is first observed with the footprint Claim `(14,32)` at
header 328, at tile `(15,23)` in that snapshot; it completes the first claim at
358 and follows the footprint. Its modern counterpart, initial worker 1, follows different work
histories in these seeds. A late claim alone therefore does not prove that the
chooser selected the wrong priority from equivalent inputs. The full failure
chain remains in the earlier replay analysis.

**CONFIRMED ordering difference:** Flag 18 is written by action 65464 after
condition 74 (type 41) of action point 21's root 67 accepts this footprint.
The captured native list puts that point (thing 1145) in class list 5 and the
four Imps in list 0. [The sweep](functions/004B7830.md) completes list 0 before
list 5; [the point's frame](functions/004B05E0.md) runs its root then. Our
`GameSimulation.Tick` instead runs `TriggerExecutor.Run`, including area roots,
before its systems and their tile work. The observed same-header native flag
and next-turn modern flags agree with this difference in visibility of work.
This boundary does not require a random choice to explain the different timing.
It is downstream of claiming, so it does not explain why those claims began late.

The first recorded state difference is already header 1: native placed workers
are in state 1 while our post-turn-1 workers are in 10 at the same tiles. Tile
positions first differ at header 6. These streams use different RNG algorithms
and startup observation phases; neither difference alone establishes a wrong
state handler, nor licenses a universal one-turn shift of later observations.

The worker-455 boundary first seen at header 328 has now been captured at
execution clock 327, with two neighbouring choices. See the
[Imp selection evidence](imp-selection-evidence.md) for same-input task/RNG
matches, navigation reconstruction differences, and remaining eligibility gaps.

## A captured chooser matches through its RNG calls

**CONFIRMED, bounded computation:** the retained execution-turn-171 boundary
for native Imp 453 is reproduced by hash 303 with both a rebuilt navigation mesh
and the retained imported mesh. [The original boundary](imp-strike-choice-boundary.md)
owns the captured inputs, route and historical result. The current diagnostic
restores its exact tick, terrain, marks, ordered task data, worker poses,
reservations and starting LCG state; holder relocation remains forbidden.

Unicorn instruction hooks observe the actual original chooser's RNG entries
and returns on its retained before-image. Only its presentation cache is cleared
in emulator memory, as in the retained oracle; the generator is not replaced.
The real modern chooser runs with an `IRandomAlgorithm` logger delegating to
`Dk2Lcg`. Both perform exactly these two calls, in order:

| Call | Range | Result | Seed before | Seed after |
|---:|---:|---:|---:|---:|
| 1 | 4 | 0 | 1768782011 | 4284192478 |
| 2 | 4 | 0 | 4284192478 | 1843400869 |

The native calls return to `0x00468D59`, the ring scanner's draw site. Modern
callers are the loose-object search and claim ring search. Chosen task, state,
position, route target, route points/flags and final RNG match the captured
result. This validates this decision through its RNG boundary; it does not
advance the intervening simulation or validate omitted rooms/general objects.

The historical checker initially reconstructed this state without name/blood
fields. Since ADR-0521, simulation initialization allocates those fields and
draws twice per worker. Restoring all four workers' actual captured identities
removes the eight setup draws; no gameplay rule or seed was changed to obtain
the match. See [checker reconstruction limits](imp-decision-checker.md).

## Combat scan timing matches the tested branch

**CONFIRMED, controlled computation:** 128 executions of original
[ProcessFight](functions/0045E9F0.md) match the real hash-303
`CombatRules.ScanIsDue` on whether selection is called and the resulting counter.
The matrix covers both Imp/non-Imp types, all 16 counter values, no movement,
within-tile movement, and x/y tile crossings. Counter wrap and unrelated flag
preservation are checked. No RNG state advances in these cases.

This isolates the scan counter and movement test. Eligibility, resolved category,
prior-position accessor, logging and victim selection are controlled dependencies
in the native harness. It does not validate sight, candidate order, fear,
withdrawal, or the caller's observation of earlier creatures' movement.

## Recorded-drop boundary: a replay adaptation changes the encounter

**CONFIRMED capture observations:** native seed 1's northern Guard Room first
appears converted at raw header 6671. Native seed 2 has no observed conversion
through 6804: tile `(20,5)` is terrain 18, owner Good in every saved frame.
Nevertheless its frame 6674 contains a creature there in landing state 76;
five living creatures occupy
`(20,5)` at 6685, with landing/stun/get-up states. Its Lord is later damaged,
and has 4758 health at the last recorded header, 6804. Its keeper never reaches status 2
in this window. Native seed 1 reaches status 2 at 6743.

Our sealed Level 4 seed-2 run instead relocates its final successful drops to
`(15,15)` and leaves the Lord at full health. Seeds are not matched RNG streams;
this comparison establishes a differing treatment of the recorded coordinates,
not a matched-state combat defect or a predicted win gain.

**CONFIRMED source and binary distinction:** `ReplayCommands.DropAt` explicitly
relocates a recorded creature drop to a nearby tile accepted by our current
drop rules, following the owner's 2026-09-29 adaptation. `HandSystem.Drop` also
applies the interface drop gate when executing the command. The native
[stored-drop handler](functions/004C0EE0.md) calls
[Hand_Drop](functions/004BC710.md) without an ownership/placement gate;
the ordinary interface gate is [separate](placement-cursor.md#the-drop-cages-colour).
The actual native seed-2 playback shows landing states on enemy-owned ground.

Thus the existing rates measure an adapted replay, not strict execution of
identical drop coordinates after the worlds diverge. The adaptation is deliberate;
this audit does not remove it. Exact packet compatibility needs a separate
command-execution comparison that preserves recorded coordinates and keeps live
cursor validation in the input layer. Native solid-tile, disposal and ownership
edge cases still need their own checks before implementing such a path.

The prior inference that timely worker access explains our Level 4 outcomes
remains useful for this adapter. It must not be promoted into a native rule
requiring Guard Room conversion before troops can receive recorded drops.

## Known deterministic gaps still prevent a full parity claim

**Closed (ADR-0544, hash 308):** original [withdrawal](functions/004611D0.md),
with failed Heart and room pickers and no lair, returns 0 before local random
choices. The room picker ([`0x004C63D0`](functions/004C63D0.md)) returns no room
without a draw on an empty list, and none when its one draw lands on a bridge; a
failed pick ends the withdrawal. Ours now does the same
(`RoomLists.RandomNonBridgeRoom`, `FearRules.Withdraw` returns false); before
hash 308 it skipped the failed pick and took the local fallback even with no rooms.
The campaign effect is unmeasured.

**Closed (ADR-0545, hash 309):** the away-from-target stage DK2 runs when the
thing staged in `+0x502` is a live creature or trap is built
(`FearRules.AwayFromTarget`); its point is overwritten by the local tries, so
only its draws are consumed. Each caller now supplies what DK2 leaves staged.
Campaign effect unmeasured. Not mirrored: the flee back-off keeps an
unattackable live creature staged where ours drops it, and `spFighting`'s two
rarer drops of its target.

Two other limits are already established in native research and rechecked here:

- The NonFighter hero arm temporarily sets its candidate target before calling
  withdrawal/alarm. The withdrawal half is built (ADR-0545, hash 309); the alarm
  still takes the candidate's position directly.
  [The function record](functions/0045BDF0.md) owns that gap.
- Native completes a creature's update before the next; our combat, movement and
  worker-job passes remain separate. The established ordering is in
  [combat acquisition](combat-acquisition.md#1-where-in-the-tick) and
  [the entity-sweep record](functions/004B7830.md). The chooser's matching local
  result does not validate the state those passes present to it in a full turn.

## Level4 captured withdrawal and room order, hash304

**CONFIRMED, bounded decision mismatch.** A new native `l4live` playback captured
the first keeper Imp withdrawal entry/return at execution3473: slot427, health750,
state203 to6, staged target0. The earlier flee observation around3023 is a
different boundary. Both complete stopped-memory images are retained under
`D:/DK2Evidence/level4-interruption-hash304-2026-10-25-restart1/` (R).
Modern tools remain the archived clean `5716560c` Release build, HashVersion304.

The captured room picker draws index6 from15 rooms. Native list slot1108 is the
Library at `(18,48)`; our derived list slot1107 is the Hatchery at `(38,49)`.
There are no bridges in this list. Both fixtures contain exactly the same room
slots and centre words, and the captured Heart is unruined. Only room order
changes between modern controls; the latter supplies the captured native chain.

| Boundary result | Live native | Modern derived order | Modern native-order control |
|---|---|---|---|
| RNG start/end |2030929326 /3162508482|same|same|
| Return / state |1 /6|true /6|true /6|
| Destination tile |18,48|38,49|18,48|
| Route count / flags |14 /13|18 /13|14 /13|
| First five route points |captured|different|all five exact|

The fixture restores captured terrain/owners, raw creature/body poses, target,
utility state, navigation bytes and retained mesh/free lists/locate cache.
Seven navigation stamps supply missing reconstruction bytes. Door/lair objects,
combat lists and other state are not completely imported: this is an order-only
handler control, not a complete-world equivalence claim. The selected Imp has no
lair. Its captured return supplies the native result independently of emulation.

Original-byte emulation observes RNG ranges/results `(1,0),(2,1),(2,0),(15,6)`
and the selected native rooms, then faults fetching `0028C46E`. This is a picker
prefix observation, **not** a completed handler oracle pass. Preserve that failed
output. The live capture and modern order-only control establish this mismatch;
neither proves that room ordering caused any of the17 campaign losses.

Reproduce into fresh paths: `python -B R/capture_withdrawal.py`, then
`python -B R/export_boundary.py`; the former launches one native playback and
closes its own child after the pair. `Analysis.dll R/boundary.json <new-output>
<derived|native-room-order>` runs the preserved standalone fixture. The v2
results include the route window. `python -B R/verify_withdrawal_v2.py` checks
the live pair, controls,19 sealed artifacts and pinned inputs.

## Next verification boundary

1. Isolate the route/deadline effect of the actual Level1 seed7 write-order
   reversal established in the [navigation follow-up](navigation-partial-rebuild.md#actual-update-history-hash304),
   retaining the immediate pre-refresh cache. Pool/pose history and trigger order
   remain separate from the still-unexplained late claiming histories.
2. Treat recorded-command application separately from live cursor validation.
   Preserve the existing adaptive rate as its own measurement; do not silently
   change its policy or relabel it as strict replay.
3. Room creation/list order is now retained by the combat cache in
   [hash305](room-list.md#persistent-player-list-order-hash305), with a production-history
   boundary check. The [hash305 campaign comparison](../experiments/2026-10-25-packet-replay-hash305/README.md)
   records outcomes separately. Capture acquisition inputs at the earlier interruption:
   scan counter, poses, candidate/attacker order, sight, target, state/resume and
   RNG. The new withdrawal result does not validate that earlier decision.
4. Correct only an established mismatch, with native-backed focused fixtures,
   then verify the next boundary. No such gameplay correction was made here.

## Reproduction

Run each diagnostic only into fresh output paths. The rig retains the actual
requests, JSON results, TSVs and source. `capture-input-manifest.json` hashes
both captures and their reader; `chooser-identity-inputs.json` hashes the native
chooser input and memory used to restore identities. The new source files are:

- `capture_metrics.py`, `native_drop_audit.py`: saved captures only.
- `scan_contract.py`, `ScanProbe/`: 128 original-byte/real-method controls.
- `enrich_fixture.py`, `ChooserIdentityProbe/`: the two navigation controls.
- `trace_native_chooser.py`, `ChooserTraceProbe/`: call-by-call RNG comparison.
- `withdraw_no_room.py`, `WithdrawProbe/`: the failed-room-pick control.

The un-enriched `ChooserProbe/` outputs are retained as an explicit negative
fixture control. `native_capture_audit.py` is an unused draft; the executed
capture extractor is `capture_metrics.py`. The live capture driver was
subsequently run once with owner approval; its capture and `level1-analysis/`
outputs supplement the original audit seal.

The Level 1 supplement's `level1-analysis/verify_capture.py --seal` independently
checks the native headers/record integrity, footprint and flag observations,
captured class-list membership, modern comparisons and input hashes. It seals
11 new files and verifies the original 226 hashes without changing them.
Omit `--seal` for subsequent read-only verification of this supplement.

`verify_audit.py --seal` verified the saved results, six pinned input hashes and
24 copied DLL hashes against the archive, then sealed 226 files in
`audit-artifact-manifest.csv`. It reproduces the two expected withdrawal
disagreements rather than treating them as passing fidelity checks. Run
`python -B D:/DK2Evidence/parity-audit-2026-10-25-hash303/verify_audit.py`
without `--seal` for subsequent read-only result and manifest verification.

Independent read-only review re-read the cited native branches, both Level 4
captures, all 850 Level 1 frames, diagnostic controls and the 226 original plus
11 supplemental hashes. It verified the new timing/list observations; the
subsequent worker-455 comparisons are recorded in the linked selection study.
`dk2tools docs`, `dk2tools kb check` (2315 records) and `git diff --check` pass.
This audit changes documentation only.
