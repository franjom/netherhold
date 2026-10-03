# Tremor — world effect traced, creature control unfinished

Read 2026-10-03 from DKII.EXE v1.70 through isolated PyGhidra MCP project
copies. Disassembly checks the callback arguments, slot selection, bounds,
expiry comparison and update order. Binary behavior is STRONGLY INFERRED;
the shipped descriptor and the dumper's field names are CONFIRMED.
This is a research checkpoint, not a completed simulation implementation.

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
The shot's ten-turn health is not the world effect's duration.

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
at equality or later it clears Radius and requests the closing display.
The remaining fields are retained. Active slots are visited in slot order.

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
whether and where to flee; its complete recovery behavior is not yet settled.

The solid branch requires terrain id 9 and a non-neutral tile owner. It
draws `random(variable168 >> 1)` and applies the negative sum of that draw
and the half value through `0x00452F60` on the tile's owner. Thus the shipped
variable value 436 requests 218–435 work damage per visited wall per active
turn, before the existing tile-health clamp/conversion. Other solid types
and neutral walls do not take this branch. Despite the variable's name,
this caller does not divide by turns per second.

The update also invokes world slot +0x388 with id 43 and the neutral tag.
`0x0050E490` forwards all six arguments to the object at world-context +0x10,
slot +0x90, or returns zero if that object is absent. That receiver has not
been identified: the call must not yet be classified as either simulation
damage or presentation. The shipped shot 43 is Disruption, but the numeric
id alone does not establish what this forwarding interface creates.

## 4. Build boundary and next read

Tremor remains unsupported by KeeperSpellRules; no fields, commands, state
hashes or snapshot layout changed. Its research upgrade is described by the
data but is not built. The existing 42-scenario prediction therefore remains
unchanged. Read-only descriptor validation is in `TremorOnRealDataTests`.

Q531 tracks the remaining core paths. `0x004764A0` was decompiled whole and
its branch ranges disassembled: it gates dying, unattackable, captive and
state-table cases, manipulates the dumper-named Fleeing bit, routes with
resume state 1/options 0x80, and can enter state 210 on a five-percent branch.
The function record holds those details. Before implementation, follow the
Fleeing readers and recovery states, validate its route arithmetic against
the instructions, and identify the +0x90 receiver. The owner was asked what
Tremor visibly does to friendly and enemy creatures. No experiment result
has been supplied; no health-damage or recovery rule is assumed.
