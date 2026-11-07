---
systems: [combat, creatures, navigation, simulation-core]
---
# Captured room-selection order inputs

## Result and scope

Two missing turn-1400 restore inputs explain later destination differences in
the controlled Level 5 seed 1 continuation at hash/save 449. Supplying the
captured Lair slab chain corrects Warlock 437's bed destination. Supplying
player 3's captured room-list order corrects Imp 442's withdrawal destination.
These are diagnostic restore corrections; production simulation code is unchanged.

The ordered-site/preseed prefix remains 3180 draws through 1596. With both
inputs, the next boundary is 1597 draw 1: native ImpUtils.cpp:484 versus modern
Chicken ObjectState.cpp:2851, preseed 3845468488, modern bound 4. The old extra
State.cpp:1700 draw from Warlock 437 is gone. This does not establish full-state,
unconditional replay or Heart-outcome parity.

## Lair slab chain

CONFIRMED in the native capture: Lair 1116's sixteen-slab chain is unchanged
between 1400 and 1528. In chain order, its tiles are:

`(12,31), (12,30), (11,31), (12,29), (11,30), (10,31), (12,28), (11,29),
(10,30), (9,31), (11,28), (10,29), (9,30), (10,28), (9,29), (9,28)`.

The restored remake instead has zero SlabOrder on every Lair tile, yielding
row order. At 1528, preseed 4255120235 and bound 16 select index 2. Native
starts at (11,31), then searches to the accepted point (10,31); the remake
accepts its different index-2 tile (11,28).

STRONGLY INFERRED from a whole reread of the
[room point picker](functions/004E4C10.md): the random index starts a slab-chain
search, subject to floor-slot, wall-slot, navigation and obstacle guards. The
existing picker already implements that rule. The diagnostic input assigns
SlabOrder from the actual 1400 chain, preserving NextSlabOrder 303. It supplies
no later destination, route, action deadline or RNG draw.

Warlock 437's state/XY matches 87/87 samples from 1528 through 1614, versus
55/87 without the chain; the old first component difference was 1583.

## Owned-room chain

CONFIRMED in the native capture: player 3's room chain at 1400 is Bridge 1114,
Library 1115, Portal 1125, Lair 1116, Hatchery 1117, Training Room 1118, Heart
1124. The restored remake leaves the Lair last with RoomListPosition zero.
At 1576 the native chain has a new Bridge 1113 at its head. The withdrawal's
room draw selects index 6 of eight: native Training Room (15,30), modern Heart
(17,26). Both destinations use the room's stored centre words.

STRONGLY INFERRED from a whole reread of the
[room-list picker](functions/004C63D0.md): its draw indexes the owner's complete
linked list, including bridges. A focused reread of the room fallback in
[withdrawal](functions/004611D0.md) confirms that the picked room's centre feeds
the route. Existing whole-function confidence in the picker remains HYPOTHESIS.

The diagnostic input assigns descending positive RoomListPosition values to
the seven actual 1400 rooms, matched by owner, type and exact footprint.
NextRoomListPosition stays 15. The existing new-room insertion then supplies
the later Bridge's order. No 1576 room list or destination is restored. Imp
442's 1576 route target becomes native (15.5,30.5).

## Remaining arrival lead

CONFIRMED: Imp 442 reaches native position (63488,124928) at 1594. Native is
already in arrival state 202; modern is still travelling, with PathFinished
true and PathLastProgress 65536. Modern enters 202 at 1595. Native proceeds
through 1 at 1595, 10 at 1596 and a reinforcement walk at 1597. Modern remains
in 1 at 1597. Attribution of the missing native ring draw still needs checking.

STRONGLY INFERRED from a whole reread of
[state 6](functions/00470C80.md): it integrates an Imp before its whole-progress
arrival test and resumes the saved state in that same call. Modern's combat
withdrawal handler currently runs before CreatureMovementSystem, while the
ordinary worker arrival handlers run after it. The next bounded task is to
test that phase difference and the following idle-to-task handover. Neither
arrival timing nor worker selection is corrected by these order inputs.

## Verification and evidence

The Lair reader-only control preserves all 8542 baseline draw turn/seed/bound
tuples and byte-identical actors, seeds and events. With Lair order applied,
the owner-list reader-only control preserves all 8471 tuples and those same
three outputs. Each application changes only its reader's apply switch.
The paired non-Tools DLLs are byte-identical. Every successful build's source
hash map differs from frozen 449 only in ReplayCommands.cs: all 374 simulation
C# files, thirteen inherited bootstrap clauses and previous 24 input values
are unchanged. Lair-only runs add one actual-1400 input; owner-list controls
add a second. The final control therefore has 26 inputs. No production rule,
hash/save version, pin or regression suite changes in this task.

Study: `F:/DK2Evidence/studies/q712-lair-chain1528--2026-11-07--001`.
`verification-summary.json` owns component and observation-control receipts;
`native-chain-proof.json` and `native-owner-room-proof.json` own capture extracts.
Final replay: `owner-applied449/`, with its draw-verification receipt. Successful
builds are `build-read`, `build-apply`, `build-owner-read-v2`, `build-owner-apply`.
The first `build-owner-read` failed compilation and is retained as exploratory.
Frozen source, executable payload and SHA-256 manifest:
`F:/DK2Evidence/builds/q712-room-inputs--2026-11-07--001`.
