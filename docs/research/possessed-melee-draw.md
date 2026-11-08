---
systems: [combat, creatures, simulation-core]
---
# Possessed melee animation draw at 1647

## Attribution

The [hash-455 control](store-gold-steering.md) remains the last verified
continuation. Its first mismatch names Warlock 437 in the modern stack;
that identifies the modern consumer, not the native draw's owner.

CONFIRMED in the Level 5 seed-1 capture: possessed Imp 440 changes from
no action/standing slot 39 at 1646 to action 17/slot 5 at 1647. The action
start stamp is 1647; its phase/rate are 1365/1365, then phase 2730 at 1648.
The packet has one action-26 melee request over 1645 through 1648, on playback turn
1647. The modern event also reports possessed_swing at 1647. Its stored
SwingStartTick is 1646; this alone does not establish earlier playback.

Warlock 437 arrives in state 53 and changes walk slot 1 to standing 39 at
1647. The shipped standing descriptor at file +0x13E3 has flags 1, type 5
for both Warlock and Imp. Therefore that install cannot supply the coin.
The separate descriptor mapping is recorded in [the loader](functions/00492320.md).
Imp melee slot 5 has file flags 0x28011, including 0x8000 but not 0x400.

STRONGLY INFERRED from fresh whole reads of
[the possessed dispatcher](functions/00514830.md),
[the animation chooser](functions/0048BE50.md) and
[the installer](functions/004B37D0.md): the melee request starts action 17
through the existing action starter. Its slot-5 install qualifies for the
world-stream random(2) at RenderInfo.cpp:201 even without the alternate bit.
Imp 440's start is the eligible source for the missing coin. The native draw
log has no per-call creature tag, so this attribution is not a tagged capture.

## Implementation boundary

PossessionRules.Melee reaches FightRules.BeginPossessedAction, which records
the swing and requests its sound. It does not install or advance the shared
action animation. ActionAnimationClock already implements the installer coin,
but the possessed-melee path does not reach it. This is an animation/action
bridge omission; adding a draw to the Library search would conceal it.

No gameplay change or new continuation control was made in this lead.
The native action target and retained target word are 1463; that record is
absent from the captured frame. The modern event targets creature 20, tagged
436. Target selection therefore also differs; the native target kind remains
UNKNOWN from this capture. Resolve that mapping before claiming a matched attack.

Before implementing, connect action 17 to the shared starter/runner with
replacement, ownership and completion tests; distinguish the command stamp
from playback scheduling. Then repeat the same-input control. Do not insert
a turn-1647 or creature-440 draw guard into production.

Evidence: `F:/DK2Evidence/studies/q712-possessed-melee1647--2026-11-08--001`.
`observations.json` contains the native components, packet excerpt and modern
events; the copied packet is SHA-256 checked and its source remains in place.
The two raw shipped-data windows and source hashes bind the code/data reading.
The controlled prefix and all restore/full-state/Heart limits remain unchanged.
