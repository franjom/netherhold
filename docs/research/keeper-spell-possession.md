# Keeper spell Possession

Primary source: DKII.EXE v1.70, private PyGhidra reads on 2026-10-04;
shipped KeeperSpells, Shots, Creatures and LEVEL records. Function records
hold each read's scope and dated history. Stage7a implements the creature
lifecycle and modifiers; Stage7b actions and the owner's upkeep choice remain.

## Cast and target — CONFIRMED

Keeper spell2 supplies shot32 for both research levels, cost500, upkeep25,
cooldown0, location5 and target6. Shot32 uses process26, flags6,
process flags4, health1 and speed2048. The spell record's upkeep is not the
amount charged by the possession branch below.

The shared cast path is 0x004BB8A0/0x004BBA40/0x004BBF90; the selector
accessor is 0x005177B0 and target6's validator is 0x00517AB0. It requires
a live owned creature, not held, stunned (209/255/256/257), in193 or
278/279/280, a pit fighter, chicken-timed, IsHorny or actively rebellious.
Frozen alone does not refuse. Owned chicken objects are a separate accepted
branch, currently unbuilt in the creature-command interface.

Process26 is 0x004AEEA0, registered by 0x004AD1B0. It checks the actual
owner's possession pointer before its creature branch, refuses type46 and
an already possessed target, sets possession through 0x0048DEA0 and installs
the pointer through 0x004BE090. These downstream refusals occur after ordinary
cast payment and clocks. Process24, 0x004AEE50, is an owner-changing callback
and is not the possession handler. The chicken arm calls 0x004AF000 and
0x0049D080; its object control and presentation remain unbuilt.

## Entry, exit and update — CONFIRMED

0x0048DEA0 sets creature+0x3E6 bit7. Entry leaves state, job, claim and
current action intact; it resets follower count+0x488 and influence
deadline+0x3EE. Exit clears the flag, requests idle through the state setter,
clears the owner's influence flags and resets the deadline. The setter's
held-state refusal still applies. Speed is recomputed on exit in the binary.

Action21, 0x00514590, exits immediately; 0x0050E5C0 invokes its action-table
handler inline. Frozen exit additionally ends the current action and asks
for218. 0x004BE090 clears carried/dragged things through 0x0048B3A0 and
0x0048B500, resets the old creature through 0x0048EE70 and updates player+0x134.
When entering a live creature it also saves its gold into player+0xE93.
Linked hypnosis/follower cleanup, the follower count, that gold bookkeeping
field and presentation notifications are not represented by Stage7a.
Action20, 0x00514560, toggles the third-person presentation bit; it is unbuilt.
The ownership-conversion path 0x0049AC80 also calls the possession setters;
its full conversion semantics are outside this stage.

The full creature update, 0x00488930, continues the action runner, timed
effects, own-land regeneration and fear while possessed. It skips
ProcessFight, state/category dispatch and general AI steering. The earlier
physical pass remains; possession is not a blanket freeze of the creature.
0x00487C70 exits possession before body conversion and owner unlinking.
The death gate at 0x004B2F37 in 0x004B2CE0 skips the dying window.

0x004BFD00 visits every other creature with the same actual owner and sets
creature+0x3EA bit3 from line of sight. There is no radius condition: a squared
distance is calculated but does not gate the update. 0x00488930 invokes it
only when unsigned now is strictly greater than +0x3EE, then stores now+20.
With initial deadline0 the first refresh is turn1; equality keeps the old
influence flags. Exit clears the whole owner's list. Sight uses
0x004512F0/0x004511A0: the start tile is not tested, solid navigation and
ordinary doors block, barricades do not. See combat-acquisition.md and
door-blocking.md for the shared walker.

## Modifiers — CONFIRMED

0x005099A0 initializes32 first-person stat words to100. Variable224's
0x0051DC70 stores the low16 bits at global+0x313+2*StatId, ignores Level and
lets the last record win. Accessor0x00495080 reads a signed word.

Melee damage0x00494D00 now derives stance from actual creature flags at the
combat consumers. The possession arm wins over influence and the pit arm;
see [combat-damage.md §2](combat-damage.md#2-the-multiplier-and-all-three-flags-are-already-named-in-our-own-notes)
for the independently corrected influence numerator and the damage pipeline.

Melee recharge0x00494EF0 and spell recharge0x004F7AB0 use stat31
(shipped25): possessed (100+a)/100, then variable210's unsigned byte/100;
influenced (100+a)/200. Each 12.12 operation truncates separately, before
the existing work-rate division. Percentage70 produces3583, not a combined
rational result. 0x00441990 zero-extends its32-bit numerator before shifting
into64 bits; authored negative numerators retain this behavior.
The melee/spell readiness predicates are0x0048C9B0/0x0048C800, with zero
stamp ready and a strict stamp+interval<now comparison.
Possession bypasses hit/shot wind-up in0x00498E30,0x00498870 and0x004984D0;
influence does not. Actions continue independently of paused state dispatch.
The update's speed arm uses stat21 (shipped200), giving possessed×3 and
influenced×1.5 after its preceding modifiers. The body cap mirrors this arm.

## Upkeep — CONFIRMED path, owner decision pending (Q58)

0x004C17A0 charges a live possessed creature only while global0x006F2957
bit0 is set. There is no camera, local-player or actual-owner equality gate
in this branch. 0x0056C100 reads LEVEL+0x638C and ORs its configuration;
0x0051E6D0 installs config bit2 as the global bit. 64 of66 shipped LEVEL
files set it; Secret2/3 omit it. Omission alone is not proof that the final
configuration is zero: its initialization/other writers were not fully audited.

Base cost is unsigned creature definition+0x764, loaded by0x00492320 from
CREATURES+0xEE8. The parser's signed short must be reinterpreted as unsigned
at consumption. Imp0; Firefly/Skeleton/Dwarf50; Goblin/Warlock/Troll150;
Bile Demon/Mistress300; Vampire600; Horny500. Level1 keeps the base;
higher levels multiply the signed level-stat word, truncate/100 and clamp
0..65534. Upgraded availability, through0x004BB0D0, applies variable226
UpgradedPossessionCostPercentage (70) with another truncation. A nonzero
creature+0x4A7 additionally adds variable116 (shipped0).

After ordinary mana payment, 0x004C10E0 compares the remaining mana minus
imp/trap commitments with this amount. Strictly less triggers immediate
action21/message53 and returns before warning/restamp; equality retains
possession. The caller is0x004BB2A0's keeper boundary0x004B9EC0.
Balance0x006F28E2 (5) had no direct reader in both code blocks with its setter
as positive control; this is not an assertion about all indirect aliases.
Spell2's25 is not the amount used by the audited charge branch.

The owner was asked to choose the binary-confirmed LEVEL/creature rule or
leave upkeep unbuilt pending an original-game observation, and to identify
the creature/level in the no-drain observation. No answer has been received.
Stage7a currently makes no possession upkeep charge; this is an explicit
unbuilt boundary, not evidence of free possession in DK2.

## Regeneration and implementation boundary

CONFIRMED: 0x00488930's own-land regeneration block has no possession or
combat exclusion; it excludes dying and compares tile owner with actual
owner. The loader converts shipped67 to unsigned16 per creature update.
Positive health changes through0x004B2CE0 do not take possession's damage
multiplier. Q152's possession continuation is settled; the hand/scheduling
observation remains open.

Stage7a preserves the existing modern own-land cadence (67 once per second,
rather than16 each original turn), explicitly a pre-existing deviation.
It continues that healing while possession pauses needs and sleeping-state
healing. This does not claim exact original health timing.

STRONGLY INFERRED integration: simulation systems reproduce the researched
branches independently; their registration order is ADR-0013/Q398, not a
claim of the original's whole thing-pass schedule. New creature flags and
the typed influence deadline are hashed and saved together (version175).
Camera, input devices and Unity remain outside this task. Pending actions
must carry pose/aim/slot semantics rather than an AI target-tag shortcut.

## Stage7a verification

Release and Unity compile: zero warnings/errors. No-install suite:3460 passed,
0 failed,396 skipped. Ledger242, KB771 and coverage checks pass.
All42 scenarios completed with178 artifacts. Against both Stage0 and Stage6:
94 byte-identical event/world/command artifacts,84 hash-only changes, no
other differences. This matches ADR-0350's composition prediction.
Pin changes174→175, 0x8213FCEC0D1B3EA0→0x6F9C6A3B4D22F803.
Original Level1/2 seeds1–20 remain unavailable as established in Stage0.
