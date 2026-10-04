# Keeper spell Possession

Primary source: DKII.EXE v1.70, private PyGhidra reads on 2026-10-04;
shipped KeeperSpells, Shots, Creatures and LEVEL records. Function records
hold each read's scope and dated history. Stage7a implements the creature
lifecycle, modifiers and upkeep; Stage7b the possessed actions 25 and 26.

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
Action20, 0x00514560, assigns the third-person presentation bit from packet
bit0, preserving the other flags; it is unbuilt.
The ownership-conversion path 0x0049AC80 also calls the possession setters;
its full conversion semantics are outside this stage.

The full creature update, 0x00488930, continues the action runner, timed
effects, own-land regeneration and fear while possessed. It skips
ProcessFight, state/category dispatch and general AI steering. The earlier
physical pass remains; possession is not a blanket freeze of the creature.
The state-dispatch guard at0x0048A026–0x0048A030 also pauses retained Call to
Arms and Chicken handlers and their travel-arrival branches. Flag-object
recall and Chicken's timed countdown continue independently.
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
The final product is also narrowed to 32 bits before its signed integer
conversion: `0x0049505B–0x0049506D` and `0x004F7C3B–0x004F7C42` call the
multiply helper and then shift its stored result. See combat-damage.md §1
for that shared boundary; authored stat −101 with interval 100 returns −1.
The melee/spell readiness predicates are0x0048C9B0/0x0048C800, with zero
stamp ready and a strict stamp+interval<now comparison.
Possession bypasses hit/shot wind-up in0x00498E30,0x00498870 and0x004984D0;
influence does not. Actions continue independently of paused state dispatch.
The speed arms at0x0048990E–0x00489918/0x0048993F–0x00489949 use stat21
(shipped200). They multiply the current entity-unit speed by100+stat,
retain the low32-bit product, then divide unsigned by100 (possessed) or200
(influenced), truncating before the setter. Possession wins when both flags
are set. Setter0x0048D010 clamps unsigned to0xFFF before storing body speed
shifted4. The preceding setters also clamp before this arm reads the speed;
the simulation mirrors that boundary when composing its status cap.
The getter's supplementary primary read is recorded at
[0x0048D000](functions/0048D000.md).

## Upkeep — CONFIRMED, built (Q58)

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
0..65534. A nonzero DWORD returned by0x004BB0D0 for spell id2 applies variable226
UpgradedPossessionCostPercentage (70) with another truncation. A nonzero
creature+0x4A7 additionally adds variable116 (shipped0).

After ordinary mana payment, 0x004C10E0 compares the remaining mana minus
imp/trap commitments with this amount. Strictly less triggers immediate
action21/message53 and returns before warning/restamp; equality retains
possession. The caller is0x004BB2A0's keeper boundary0x004B9EC0.
Balance0x006F28E2 (5) had no direct reader in both code blocks with its setter
as positive control; this is not an assertion about all indirect aliases.
Spell2's25 is not the amount used by the audited charge branch.

The owner chose the binary-read rule (2026-10-04). An Imp's cost is 0, which
may explain the no-drain observation; the creature they possessed was not
identified. Re-read 2026-10-04 (claude): the stat-27 row is
`[0x006E4154]+0x2B5+2·level`, the shared per-level table at row
`+0xE1+(stat−1)·18`; the upgrade percentage is the byte at 0x006F29D3;
the payment's check precedes Call to Arms and the Reaper; the live test is
slot byte 0x006ED540 and class +0x12 = 0. A zero charge still compares, so a
possessed Imp ends when mana falls below the imp and trap upkeep.
Built in `PossessionRules.UpkeepPerSecond`/`AfterPayment`, summed in
`ManaRules.UpkeepPerSecondByPlayerIndex`, checked first in ManaSystem. LEVEL
bit2 is `LevelDef.Flags` (+0x638C) & 4; with no LEVEL record nothing is
charged. The +0x4A7 term (variable116, shipped 0) is not represented. The
forced end posts message 53 with Data1 5, Data2 0 (pushes at 0x004C13C8–0x004C13CE;
EDI is zeroed at 0x004C1385). GameDatabase drops a negative stat row, as for every
stat, so the signed product only reaches the clamp below 1 through a zero row.

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

## Possessed actions (Stage7b)

Read 2026-10-04 from Codex's quarantine captures, re-checked with pyghidra-mcp:
listings of 0x005146B0 and 0x00514830–0x005153CC, bytes at 0x006BC3B0 and the
object jump table at 0x005153D0, and whole reads of the helpers named below.

**Action25 movement, 0x005146B0 — CONFIRMED.** The packet holds X (+0), Y (+4),
a packed word (+8) and the player tag (+0x10); player+0x134 is the possessed
thing, and zero does nothing. A nonzero packed word sets the position to X, Y and
its low 16 bits (unsigned) as Z, and the heading +0xF4 to its high 16 bits masked
0x7FF, for any class. A zero word acts only on a live creature (slot byte at
0x006ED540, class +0x12 = 0): position (X, Y, 0), the same unclamped point copied
to the last position +0xF6, heading untouched. Either branch then calls the fog
update 0x0048CC90 for a creature whose tile X or Y (bits 12 up) changed. The
setter 0x004B5020 stores a layer-2 thing raw. Otherwise it raises Z to the floor
height from 0x004545C0 (kept at +0x26). Above 0x3000 it sets Z to 0x2000, not
0x3000; exactly 0x3000 stays.

**Action26 packet, 0x00514830 — CONFIRMED.** Dword0 has X in bits 0–19 and yaw in
bits 20–30. Dword1 has Y in bits 0–19 and pitch in bits 20–30. Dword2 has Z in
bits 0–13, an 11-bit angle at 14 that no arm here reads, melee at bit 27 and the
slot in bits 28–31 (arithmetic shift). The aim vector, shared by every arm, is
sin yaw, −cos yaw and −cos pitch from the 16.16 tables 0x006C1488/0x006C1C88,
each entry times 4096 shifted right 16 (a floor), the cosines negated after the
shift. The horizontal part is not scaled by pitch. Normalising to 4096 sums the
32-bit squares shifted 12, takes Math_Isqrt of 16 times the sum, shifts it right
4 and divides each component times 16 by that, truncating; zero length skips it.

**Melee, bit 27 set — CONFIRMED.** (1) Gate 0x0048C9B0 or nothing. (2) Stamp
+0x427 = now **before** searching. (3) World +0x334, 0x00515420. Around the pose's
tile it visits (0,−1), (1,−1), (1,0), (1,1), (0,1), (−1,1), (−1,0), (−1,−1), then
the own tile (0,0) last. On each tile it walks the layer-0 chain (cell word 0 via
0x00508FC0, next +0xC), skipping the attacker itself. A candidate must pass
0x00516050, then the strict cone 0x00516950: the bearing from the pose, unwrapped
to within half a turn of yaw, strictly inside yaw ± 0x155. 0x00516670 then decides
replacement. The first survivor is taken. A later one replaces the best only if
it is strictly nearer horizontally from the pose **and** best·dot <
(candidate·dot × 300) / 256, truncating toward zero. The dots are the
4096-normalised 3D directions from the pose against the aim. This is not
nearest-distance, and it is not a total order. (4) SneakingAs +0x4A3 is the
possessed creature's: when it is nonzero and [target owner]->0x004BF3D0(sneakingAs)
is nonzero, the possessed creature's own +0x4A3 is zeroed (0x0051496E; the spell
arm repeats it at 0x00515156). Objects branch by type:
gold 1–3 is the pick-up, 9 the chicken arm, 14 process state 18, 15/16/46 a
shared arm at 0x00514C31; other picked types fall to the attack. A creature
in state 131 (on a torture device) is not struck but is stamped. Otherwise it
calls CreatureAction_Begin(17, target). On success, HypnotisedCreature +0x4A7
(creature targets only) is pointed at the target and NHypnotisedAttacks +0x4DB is
counted. PossessedTargetAttackTime +0x4D2 and PossessedTargetTag +0x4D0 are
written, and +0x427 is stamped again. A refused start keeps the first stamp. The
blow is action 17's swing 0x00498E30 into 0x00494D00. No target and def+0x686 bit
0 runs a dig/claim arm (actions 4/6 on the pose tile, 2/1 on the facing tile from
table 0x006BC350). It is LIKELY by listing, not built.

The pick predicate 0x00516050 — CONFIRMED. Equal owners branch at 0x00516065.
Same owner: objects only, within reach 0x005162E0, typed through 0x005162A0:
gold 1–3 while carried gold +0x3FB is strictly below the level-scaled def+0x69C,
or types 9, 14, 15, 16, 46, 64; every other type is refused. Another owner:
objects whose definition lacks +0xF4 bit 20, within reach, typed through
0x00516228: the same gold check, the same types and 94; traps unless +0x10C
bit 4; creatures not dying (+0x3E6 bit 21, vtable +0x28); any other class.
Reach uses the creature's heading +0xF4, not the aim: within 0x80 (no wrap),
and horizontal distance² ≤ 1.2 × the summed vtable +0x3C sizes.

**Spell, bit 27 clear — CONFIRMED.** Slot 3 is the follower arm, not a spell.
0x00515D50 scores friendly creatures within 7 tiles as below. Below variable 223
(MaximumGroupNumberOfFollowers, 8), a successful 0x00461980 counts +0x488. It
never stamps (unbuilt). Other slots: refused by 0x0048C800 (strict, so equality
refuses) → nothing. The spell id from 0x004943F0 is zero, or the record is
missing → return, no stamp. The target is world +0x338, 0x00515520, friendly = flags
bit 2; flags bit 7 replaces it with self after the scan. Bit 5 (teleport) runs
Player_NearestRoomOfType(keeper, 5 Dungeon Heart) from the creature, and on success
action25 with a zero packed word at that tile's centre, between effects 0x141/0x142.
Bit 6 runs 0x0048CAD0(data1, data2) and sets SpeedBlurTimer +0x4DD = data1 when
data1 > 100. Otherwise a target means Begin(16, slot, target). No target means
BeginAt(15, pose + unnormalised aim, slot, pitch, range). range = 0x3000 when
spell Range +0x31 plus SniperMode (+0x3E6 bit 31) is nonzero, else 0. Shot type
30 without a target starts nothing. Every path past the record lookup, refused
starts included, then stamps +0x41B + 4·(slot & 0xFF) = now.

**Spell target scan 0x00515520 — CONFIRMED.** The best score starts at variable 187
FirstPersonAccuracyAngleDegrees (0x006F296E, 45) × 2048 / 360 = 256 and is replaced
on a signed strictly lower score = 4096 − dot(normalised(thing − pose), aim).
Players come in list order (world +0x7A, next +0x3A). One qualifies by
Player_IsEnemy(owner, player), inverted for friendly spells. Per player, in order:
creatures (+6), traps (+0xC), doors (+0xE), then objects (+0xA, type 94 only),
each list linked at +0x10. Distance is horizontal from the creature's own position, not
the pose: < 0x5B000 (91 tiles²) for creatures and objects, < 0x31000 (7 tiles)
for traps and doors. Every candidate needs the cone from the pose and a sight line
0x004512F0 from the creature. A door is sighted at a point one tile (its 4096-length
direction from the pose) nearer the pose and is skipped when +0x109 (under
construction) or the cell's byte +0xB bit 0 is set. Traps skip +0x10C & 0x30,
creatures dying ones (bit 21 or the death query). The caster is not excluded. The
threshold is unsigned (variable × 0x800) / 360.

**Free-aim launch, 0x004984D0 — CONFIRMED.** Possessed, the shot has no wind-up
and no horizontal muzzle offset. Z adds the slot entry's +0xE. 0x004B7CD0
spawns spell +0x44 at heading +0xF4. The data words are ShotData1/2, and +0x151 is
the possessed flag. +0x165 is set under SniperMode, which doubles contact damage in
0x004AC420. Range goes to +0x155, the start to +0x159. 0x004AB830/0x004AB8A0 aim
and launch at the action's point.

**Tile chain order — CONFIRMED.** When a thing not on layer 2 changes tile,
0x004B5020 unlinks it (prev +0xA, next +0xC) and pushes it on the **head** of the
new tile's chain for its layer +0x16. One chain mixes creatures, objects, traps and
doors, so melee meets the latest arrival first across classes. Because 0x00516670
is order-dependent, the simulation needs a persisted per-tile link order. Every
constructor pushes at the head of layer 0 (0x004B4E70 with a zero argument):
creatures 0x00487350, objects 0x0049B272, shots 0x004AAD53, doors 0x004A50C0, traps
0x004A7999 and dead bodies 0x004B0F9B. Hand_Drop and the carry drop relink on
0x004B5580's layer, 0 for a thing off the map; pick-up and the death bookkeeping
(0x0048846F) unlink through 0x004B4F60. A body (class 6, 0x004B0F20) is linked to the
neutral player (0x004B0FAA). The pick has no class filter, so a shot or body in the
cone is a candidate. The spell scan's per-player lists are newest first
(0x004B4C30 prepends).

Action begin 0x00496700 refuses only while another action than 0 or 21 runs whose
interruption byte (0x006E41B4 + 0x1D·id) is zero; shipped bytes are all 1. It ends
the current action first. Neither action 26 nor the begin tells the target: the
answer 0x0045AA20 has only spFighting and trap callers. An object's radius
(vtable 0x0066D654 slot +0x3C, 0x00402AD0) is 0. The gold arm's last call,
0x0050FB70, forwards to the world's sink (presentation, LIKELY).

UNREAD: arm 0x00514C31; the dig/claim arm's detail; the SniperMode toggle and its
command; the dword2 angle; 0x00516050's third argument (melee passes 1);
0x00508FC0 has no bounds test at the map edge; the caster as its own friendly
candidate; +0x155's comparison units; action 17 on an object, shot or body.

**Build boundary (2026-10-04).** Stage7b1 builds action25 as
`MovePossessedCreatureCommand` (`PossessionRules.Move`): both packed-word
branches, the 0x2000 height cap, the 0x7FF heading mask, the previous-position
copy and the tile-chain relink. A possessed thing is always a creature here, so
the zero branch's class test always passes. The setter's floor raise has no
counterpart, because the floor is height 0 (M9/Q398). Nothing but possession
(and a save restore) writes PosZ/LastPosZ, so a creature leaving possession keeps its last Z (an M9
note). Fog is not called directly: FogOfWarSystem covers 0x0048CC90's circle
pass (PerceptionRange) every tick for every player; the line-of-sight square of
radius def+0x77B is not modelled (fog-reveal-sources.md §3.1).

Stage7b2 builds action26 as `ActPossessedCreatureCommand` (`PossessionRules.Act`).
The link order is `ThingTileLink` on creatures, objects, traps, doors and shots,
kept by `WorldState` from the position, hand and carry setters, so every tile
change relinks; a corpse is relinked as a new body. Melee builds the gate, the
stamps, the pick, the swing on creatures, traps and doors, state131's stamp-only
case, the gold pick-up and types 15/16/46 (impact only). Spells build the gates,
the scan, self, teleport, speed (`StatusEffectRules.ModifySpeed`), action16 and
action15 (`FightRules.BeginFreeAim`/`FireFreeAim`; the shot flies flat at the
point's X/Y), shot type30's refusal and the slot stamp. Unbuilt (Q535): the
follower arm (no state246), dig/claim, the chicken, type14, action17 on an object,
shot or body, and action16 at a type-94 object; each stamps as DK2 does and starts
nothing. Not represented (Q536): SneakingAs, hypnosis, +0x4D0/+0x4D2, SniperMode,
the secret-door cell bit and heights. A slot outside the creature's spells is
refused by `SpellOf`'s bound; spell level is not tested, as in the listing.

## Stage7a verification

Release and Unity compile: zero warnings/errors. No-install suite:3460 passed,
0 failed,396 skipped. Ledger242, KB771 and coverage checks pass.
All42 scenarios completed with178 artifacts. Against both Stage0 and Stage6:
94 byte-identical event/world/command artifacts,84 hash-only changes, no
other differences. This matches ADR-0350's composition prediction.
Pin changes174→175, 0x8213FCEC0D1B3EA0→0x6F9C6A3B4D22F803.
Original Level1/2 seeds1–20 remain unavailable as established in Stage0.
