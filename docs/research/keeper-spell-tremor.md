# Tremor — effect and recovery traced, integration awaiting verification

Read 2026-10-03 from DKII.EXE v1.70 through isolated PyGhidra MCP project
copies. Disassembly checks the callback arguments, slot selection, bounds,
expiry comparison and update order. Binary behavior is STRONGLY INFERRED;
the shipped descriptor and the dumper's field names are CONFIRMED.
The integer action clock and Tremor integration are committed. Independent
review found remaining corrections; the boundary below records binary evidence.

## 1. Descriptor and cast

KeeperSpells.kwd identifies Tremor as spell 8: cost 30,000 mana, zero
cooldown and upkeep, location selector 5, target selector 4, flags 0x14.
Both variants use shot 24; ordinary data words are 5 and 3, upgraded 7 and 3.
The shipped shot has process 27, first-tick event 0x40, health 10,
countdown flag 4, damage type 0 and collision class 2.

The position permission path is the existing `0x004BB8A0`: mana, cooldown,
location and target. Location table initializer `0x005174A0` maps selector 5
to `0x00517F60`, which returns true; `0x005176B0` dispatches it. Unlike
Create Gold, Tremor's location selector does not require allied floor.
Target selector 4 still applies its own-room exclusions; see
[Create Gold](keeper-spell-create-gold.md#1-descriptor-and-cast).

Process table initializer `0x004AD1B0` maps process 27 to `0x004AF330`.
That callback requests the existing radius-2 threat helper, then creates a
world effect at the shot's position through `0x00510290`. Its duration is
the low 16 bits of data1 times variable 19; its radius is data2 shifted 12,
also narrowed to 16 bits. It returns zero. The ordinary values therefore
request 20 turns and a radius of 12,288 position units; upgraded 28 turns.
The shot's ten-turn health is not the world effect's duration. The first-tick
event's zero return is ignored at `0x004AB27D`–`0x004AB290`; the shot tick
continues and the callback is not repeated on later turns.

## 2. World record and area

`0x0050EA70` names the eight 20-byte records beginning at world +0x9BB0:

| relative offset | width | original name |
|---|---|---|
| +0 | dword | WorldX |
| +4 | dword | WorldY |
| +8 | dword | StartedAt |
| +12 | word | Duration |
| +14 | word | Radius |
| +16 | dword | effect handle; the dumper does not name it |

`0x00510290` takes the first record with zero radius, otherwise the record
with greatest unsigned age (strictly greater comparison, so the earliest
slot wins ties). It caps the unsigned radius at 0x5000, stamps StartedAt and
requests an effect-service handle. No owner is stored in this record.

The world frame `0x0050A450` calls the Tremor update `0x00510440` after
entities and players. For every nonzero radius, age below Duration is active;
at equality or later it clears Radius and replaces the display handle with
the closing display's returned handle. WorldX, WorldY, StartedAt and Duration
are retained. Active slots are visited in slot order.

Each active turn calls `0x00510370` once. It draws from the world random
stream over the square `(2r+1)^2`, where r is Radius shifted right 12,
then checks the sampled tile and its neighbors in the stored order
N, NE, E, SE, S, SW, W, NW, center. It chooses the first in-bounds tile whose
navigation solid bit is clear, or returns failure. Bounds are `0x00509090`;
the update's solid getter is `0x00509200`, the same navigation bit.
This is a square sampler
with neighbor fallback, not a circular area sweep. No chosen floor means
no creature/wall pass that turn.

## 3. Creature and wall branches

Around that chosen floor the update visits those same nine offsets. An
in-bounds non-solid tile walks its thing chain and calls `0x004764A0` only
for class 0 (creatures), passing the original cast tile and radius. There is
no owner test at this call. The creature helper, not a health routine, decides
whether and where to flee. Exact head/next fields and the post-reaction next
load are in [the updater record](functions/00510440.md). Chain insertion
and relinking make creature reaction order newest-linked-first within each
tile, rather than stable creature-list order. Existing ChainOrder can
represent that order; deferred relinking cannot capture every intra-turn
crossing. Snapshot traversal is STRONGLY INFERRED safe for normal reactions;
prior-category leave hooks were not transitively audited.

The solid branch requires terrain id 9 and a non-neutral tile owner. It
draws `random(variable168 >> 1)` and applies the negative sum of that draw
and the half value through `0x00452F60` on the tile's owner. Thus the shipped
variable value 436 requests 218–435 work damage per visited wall per active
turn, before the existing tile-health clamp/conversion. Other solid types
and neutral walls do not take this branch. Despite the variable's name,
this caller does not divide by turns per second.

The update also invokes world slot +0x388 with id 43 and the neutral tag.
`0x0050E490` forwards six arguments to context +0x10, slot +0x90, or returns
zero if that component is absent. The session attach window in `0x00525370`
passes component 0x006D3CC8 to `0x00527040`. Its static initializer
`0x0043A5E0` calls `0x0043A740`, installing vtable 0x0066CC94; the destructor
`0x0043AA30` corroborates that table. This establishes the concrete receiver.

Slot +0x90 is `0x00440420`: bounds, solidity and view-visibility gates precede
slot +0x98 (`0x00440350`). That allocator creates separate 0x159-byte effect
objects through `0x00434700` and `0x004347E0`, linked into component +0x336.
The definition getter `0x00440BC0` uses a separate 0x94-byte record array;
the initializer uses the presentation random seed 0x006D6450, rather than
the world seed. Shipped Effects.kwd identifies effect 43 as **Tremor Rocks**,
checked by `TremorOnRealDataTests`. This is the presentation effect interface,
not the shot-43 Disruption interface. The allocator's transitive rendering
helpers were not all audited; this read does not assert they have no other
side effects. Flame supplied structural lookup addresses only; the call,
instance, vtable and allocation facts above were checked in DKII.EXE.

## 4. Build boundary and next read

The integration is awaiting verification. This read establishes the original
ordinary recovery path and the intended engine boundary; it does not certify
the implementation, saved-state shape or scenario predictions. Descriptor
and clock checks remain in `TremorOnRealDataTests` and `AnimationTimingTests`.

`TremorRules` now mirrors allocation, sampling, wall work and creature response;
the world pass follows entities and players. `TremorCreatureSystem` advances
ordinary recovery before steering. It preserves the retained route and holds
it until a state permits general steering; the original physical impulse
pass remains unmodelled here. Traversal uses the existing deferred tile-chain
links and snapshots each tile's descending link order. Multiple crossings
within one turn, corpse equivalence and arbitrary category-hook relinking
remain limits of that shared approximation.

The focused simulation tests cover casts/refusals, slot allocation,
field/hash/save symmetry, RNG order, walls, creature routing and recovery.
Both complete 42-scenario runs (`build/keeper-spells-tremor-a` and `-b`)
match byte-for-byte. All hash series differ from the Stage 0 series listed
in `docs/plans/2026-10-03-keeper-spells-baseline.md`: version 170 and the fixed
records/false creature flags change composition. Every event log and final
world dump remains identical; none of those scenarios casts Tremor.

Q533 tracks integration verification and the remaining original-game comparison. `0x004764A0` was decompiled whole and
its branch ranges disassembled: it gates dying, unattackable, captive and
state-table cases, manipulates the dumper-named Fleeing bit, routes with
resume state 1/options 0x80, and can enter state 210 on a five-percent branch.
The function record holds those details and the scoped Fleeing writer census.
No combat-flee setter was identified; do not equate CreatureActivity.Fleeing
with this bit. Idle `0x004773D0` clears it when its handler executes, not at
every transition into 1. Broad initialization, record copies and indirect
writes prevent an absolute-absence conclusion from the census.
The state dispatcher runs needs before selecting the current handler; a
successful hunger selection therefore bypasses idle's flag clear. The
simulation clears at its guarded idle-role handlers after needs, not in the
earlier recovery pass. See [the dispatcher](functions/00476FC0.md).
State 210's `0x00481520` ends the current action, starts action 64 and enters
263. Its `0x004815A0` waits for completion, optionally consumes the
dumper-named ShouldDelayGetUp (+0x3E6 bit 27) as one handler delay, then
starts get-up action 53 and enters 264. The setter found outside Tremor is
shot process 17; see [its scoped record](functions/004AE990.md). These
handlers supply no fixed duration; they wait for the action clock below.
Full assembly resolves the route arithmetic and stack aliases; the exact
formulas and retry order are in the `0x004764A0` function record. Preserve
its repeated X normalization, logical shifts and asymmetric search bounds.
The owner was asked what Tremor visibly does to friendly and enemy creatures.
No experiment result has been supplied; the creature behavior remains
STRONGLY INFERRED from the binary pending an original-game comparison.

### Animation clock

Action table initializer `0x004950C0` selects slot 24 for action 64 and slot
27 for action 53; both have null handlers, mode 3 and no fixed timer.
`0x00494860`/`0x00494880` resolve missing slots to 4 and 1 respectively,
without recursively checking the fallback. `0x00496700` starts the resolved
resource in the embedded animation object at body +0x44; `0x004969D0` with
variant 1 clears looping. It retains the requested slot number even when
the resource comes from a fallback. Missing resources can still start an
action: their zero rate completes it at the next action-runner poll.

`0x00496AA0` mode 3 waits for body +0xDC (animation object +0x98), a normalized
advance rate, not an instance pointer. `0x00496D00` reports the current action.
The creature updater `0x00488930` polls actions, dispatches the state, then
runs the animation chooser `0x0048BE50`. Its ordinary path advances the
embedded object in the same turn that the state handler starts an action.
[The chooser record](functions/0048BE50.md) owns the call addresses,
active-action protection and exceptional status/art bypasses.

Type-6 resource compaction in `0x00504D80` narrows the file frame count and FPS
at art +0x44/+0x48 to words. Flags come from +0x40; segment lengths from bytes
+0x51/+0x52. `CreatureAnimationTiming` carries these definition values without
a rendering handle. Parsing metadata assumes a present descriptor loads,
as the existing entrance/angry metadata do; failed asset loads are not modeled.

`0x004B37D0` resets playback, `0x004B41E0` computes its rate, and `0x004B3AD0`
advances type-6 resources through `0x004B3C10`. `AnimationTimingRules` models
ordinary immediate nonlooping playback at the original four turns/second.
For an unsegmented clip, duration = trunc(frames*4096/FPS),
step = min(4095, trunc(16777216/(duration*4))). Phase advances to 4096;
the following update zeroes the rate. For positive step the hold is
ceil(4096/step)+1 turns. If the action starts at T and needs H advances to
zero its rate, the first advance is on T, zero is installed at T+H-1, and
the action poll observes completion at T+H. An initial zero step completes
at T+1. A segment transition installing zero step is observed at the next
turn's earlier action poll.
Resource flags 8/16 enable start/end segments. Segment transitions divide
overshoot by next-duration/previous-duration using fixed-point division at
each step. The function record preserves equality and carry-clamp branches;
the implementation simulates them rather than adding rounded durations.
The action clock does not model arbitrary rate changes or queued clips.
An endworld Tremor entering 210 on C therefore starts 64 on C+1; ordinary
get-up starts on C+1+H64, plus one if ShouldDelayGetUp is consumed, and
finishes H53 turns after that start.

Derived get-up holds match all six independent Q527 captures: Warlock 8,
Goblin 11, Troll 5, Firefly 13, Dwarf 4, Lord of the Land 4. Corresponding
knockdown holds are 5, 7, 5, 3, 3, 5. Horny's missing knockdown and fallback
slot 4 complete at the next poll; its get-up falls back to segmented slot 1
(30 frames/30 FPS, start 6/end 14) and derives nine turns. These descriptor
and timing checks extend `TremorOnRealDataTests`; pure clock regressions
are in `AnimationTimingTests`. Neither the existing drop clock nor victory
clock is changed by this integration prerequisite.

### Recovery movement

The original gates ordinary route steering on the current state's bit 0,
after its handler. Recovery 210/263/264 and idle 1 lack that bit: finishing
264 into 1 does not resume the retained route. Neither the starter nor that
transition supplies a steering route reset. The separate physical movement
pass can still move the creature. [The creature updater record](functions/00488930.md)
owns the gate, initialization and commit addresses; [the physical updater](functions/004B2030.md)
records its limited audit. Preserve this distinction when integrating
recovery; do not invent a stop in the knockdown starter. The general choice
of mover for every other state remains outside this focused read.
