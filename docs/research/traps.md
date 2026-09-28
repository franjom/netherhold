# Trap runtime

## Evidence and scope

**STRONGLY INFERRED:** DKII.EXE v1.70, read with PyGhidra on 2026-09-28.
The entry points are `0x004A7D10` (readiness), `0x004A84E0` (update),
`0x004A7FD0` (aim) and `0x004A80E0` (shot creation). Authored placement and
construction are in [doors-and-traps.md](doors-and-traps.md).
All binary descriptions below have this confidence; the shipped-table section
is **CONFIRMED from data**, not an original-game experiment.

## Definition loader

**STRONGLY INFERRED:** `0x004AA440` receives the file record behind a `0xA8`
prefix of field-presence words. Health at source `+0x298` becomes runtime
`+0xA2`, matching file `0x1F0`; shot at source `+0x2B5` becomes runtime
`+0xBB`, matching file `0x20D`. The file stride is 579 bytes.
`CopyPairs.py` assumes an EBP source and does not harvest this loader: here
ESI is the source and EBP the destination (`0x004AA457`–`0x004AA461`).
The pairs below were checked against its instruction listing and full body.

| File | Runtime definition | Meaning / conversion |
|---|---|---|
| `0x1C4` | `+0xA0` | idle-effect height, narrowed to a word |
| `0x1C8` | `+0x74` | recharge: low 32 bits of raw value × ticks/second, logically shifted right 12 |
| `0x1CC` | `+0x78` | charge threshold, same conversion |
| `0x1D0` | `+0x8E` | threat duration, same conversion; firing narrows it to a byte |
| `0x1D4` | `+0x9A` | mana per shot, signed dword |
| `0x1D8` | `+0x84` | idle-effect interval, same timing conversion |
| `0x1DC` | `+0x70` | detection range in whole tiles |
| `0x1E0`, `0x1E4` | `+0x7C`, `+0x80` | shot data words |
| `0x1EA` | `+0x8C` | threat magnitude, unsigned word |
| `0x1EC` | `+0x88` | definition flags, dword |
| `0x1F0` | `+0xA2` | initial health, unsigned word |
| `0x1F4`, `0x1F6`, `0x1F8` | `+0xB7`, `+0xB5`, `+0xB9` | no-mana, idle and dying effects |
| `0x20B` | `+0xBD` | automatic trigger selector |
| `0x20C`, `0x20D` | `+0xB4`, `+0xBB` | trap and shot ids |
| `0x231`, `0x235`, `0x239` | `+0xC1`, `+0xC5`, `+0xC9` | shot origin offsets in 1/4096 tiles |
| `0x241` | `+0xA4` | signed health gain per recovery application |

The flags actually read by these paths include `0x2` (reveal on fire), `0x20`
(initially hidden), `0x80` (turn towards a target), `0x100` (mana gate),
`0x200` (retain the firing presentation), and `0x800` (Guard Post branch).
Steering's `0x004D12B0` also reads `0x10000` and `0x20000` to enumerate a
completed trap as a half-tile obstacle when its query enables traps.

## State, readiness and charging

**STRONGLY INFERRED:** constructor `0x004A7940` seeds health from the definition
and zeroes the recharge deadline, charge byte, disarmer id, threat, threat
duration, Trigger countdown, Trigger target and cached target. Definition bit
`0x20` seeds the runtime hidden bit. The debug dumper `0x004AA130` independently
identifies those fields.

`0x004A7D10` returns, in order: 1 for construction; 0 when current turn is
strictly before recharge; 2 when definition bit `0x100` requires unaffordable
mana and the owner is not the hero player; 3 when a disarmer id is present;
otherwise 4. `0x004C1740` permits a zero cost, otherwise compares the signed
cost with current mana. The hero exemption reads `0x006F254C`.

The update decrements the threat-duration byte, then bypasses ordinary work
for construction, disarming or death. Targeting happens only at or after
recharge. No target resets the charge byte; a target increments it, wrapping
as a byte. After that increment, `charge >= definition threshold` checks
readiness and fires only for result 4. A zero threshold therefore fires on
the first successful detection. Insufficient mana does not reset charge.

## Target selection

**STRONGLY INFERRED:** the initialised registration table at `0x0066D430`
maps selector 1 to `0x004A9290`, 2 to `0x004A92C0`, and 3 to `0x004A9350`.
Selector 0 has no handler. `0x004A93A0` builds the runtime table.

1. **Sight:** refuses while the nearby-Trigger count is nonzero, then calls
   `0x004A9400(0)`. This scans hostile players' creatures, requires the
   attackable bit `+0x3EA & 0x40`, excludes definition flag `0x4000`, and
   chooses the strictly nearest visible candidate inside the range. Squared
   distance is the sum of each squared axis shifted right 12.
2. **Tile:** also refuses while a nearby Trigger is counted. `0x00453AC0`
   selects the first creature on the trap's tile whose owner differs from the
   trap's, is not neutral, and whose own disarm-target word is zero. It does
   **not** consult alliances. The caller then rejects that selected creature
   if dying, flying (virtual `+0x88`), or definition flag `0x4000` is set.
   It does not continue to the next creature after rejection. Trap id 16
   additionally requires the selected creature to be a worker.
3. **Trigger:** uses the same first-creature helper, without selector 2's
   additional dying, flying, flag or worker tests.

The sight cache at runtime `+0x119/+0x11D` returns immediately on a second
call in the same turn. The previous turn's target may seed the scan after
visibility, attackable-bit, special-creature and range checks; its boundary
is inclusive, whereas a new candidate's boundary is strict. Equal-distance
candidates do not replace it. The retained candidate is not rechecked for
hostility in this arm. Turning calls the same finder with range **plus two**
before the trigger calls it with ordinary range, so selector 1 can inherit
the larger-range result from that turn's cache. See Q452 for observation.

## Firing and recharge

**STRONGLY INFERRED:** `0x004A7FD0` aims at the target's position plus half its
height. With no target it aims one tile along the trap's heading.
`0x004A80E0` refuses shot id zero or a failed allocation without the later
mana/recharge updates. It rotates the definition's horizontal offsets by the
trap heading and adds the vertical offset to the origin. The new shot receives
the target id, the two definition data words, and its aim. Selectors other than
1 and 3 force the shot's immediate-hit field when a target exists. Id 16
overwrites shot data2 with the target id after creation.

After successful creation: deduct mana under the same flag/hero condition as
readiness; clear hidden when flag `0x2` is set; zero charge; copy the narrowed
duration and set threat to the definition word if the full converted duration
is nonzero, otherwise zero; set recharge to **current turn + converted recharge**.
The authored THINGS `NumberOfShots` is not a shot budget on this path.

Selector 3 additionally retains the triggering creature and starts a byte
countdown derived using global `0x006F2966`. `0x004A8E70` walks nearby own traps
with an expanding radius and invokes their firing gates. Construction helpers
`0x004A9720/0x004A9890/0x004A9A00` maintain a four-bit count of nearby own Trigger
traps within squared distance `<= 0x1000`. This suppresses automatic targeting
for selectors 1 and 2. Trigger propagation needs its own implementation.

## Disarm, reveal and destruction

**STRONGLY INFERRED:** states 204–206 are `0x00458DF0`, `0x00458EB0`,
`0x00458F60`. State 204 travels to the trap tile at fractions `(0x7FF, 0x2AA)`
with resume 205; a failed walk clears both ids. State 205 faces the tile centre,
then sets a deadline to current turn + 50 + a random draw bounded by 200 and
enters 206. These are ticks, with no ticks/second multiplication.
State 206 waits for the action to finish and for the turn to be strictly past
the deadline. A roll below the level-scaled byte at creature definition `+0x5E0`
attempts firing, then enters state 220 with a 50-tick retry deadline; the other
arm destroys the trap. The attempted firing occurs **before**
clearing the trap's disarmer; readiness therefore refuses firing. Q451 asks the
owner to check that visible consequence. Acquisition must be carried with
these states before enabling automatic disarming. Exit hook `0x00459130`
clears both references when the tag still names a trap, otherwise only the
creature reference.

Hidden is one runtime bit, not an alliance mask. `0x004A9F10` hides it from a
foreign local player; completed visible traps are rendered, and construction
is shown only to its owner. Firing with flag `0x2` clears hidden. `0x004AA110`
also clears it. A direct caller in `0x00488930` reaches it after a creature
changes tile: definition flag `0x04000000` enables a neighbourhood scan via
table `0x006BC3B0`, revealing hidden enemy traps. The scan's tile validity
predicate and table extent need checking before porting it.

The update requests destruction when the tile owner differs from the trap
owner and clears the associated creature's disarm target. Common health
routine `0x004B2CE0` adds a signed delta directly to a trap's health, without
creature damage multipliers. Cleanup `0x004A7B20` removes ownership/list links
and undoes a completed Trigger's neighbour counts. Recovery uses signed
definition `+0xA4`, capped by missing health, when turn is strictly after
`+0x107`, then advances that deadline by one game second.

## Shipped definitions and shot coverage

**CONFIRMED from shipped `Traps.kwd` and `Shots.kwd`:** all sixteen records
have zero charge except Spare Door 1 (raw 2048, hence two ticks). These are
global definitions; a level override must still be read through GameDatabase.

| Trap | Selector | Recharge ticks | Mana/shot | Shot | Existing shot handler |
|---|---:|---:|---:|---:|---|
| Gas | 2 | 16 | 0 | 58 | process 31 unbuilt (Q395) |
| Boulder | 1 | 4 | 0 | 26 | process 22 unread |
| Sentry | 1 | 24 | 250 | 44 | contact damage 7 |
| Alarm | 1 | 4 | 0 | 13 | process 12 presentation arm read, unbuilt |
| Fireburst | 1 | 48 | 250 | 14 | area damage 2 |
| Lightning | 1 | 48 | 375 | 59 | process 9 |
| Freeze | 2 | 24 | 250 | 45 | process 11 |
| Fear | 1 | 24 | 125 | 25 | process 21 unread |
| Trigger | 3 | 16 | 0 | 39 | no shot process; trap propagation above |
| Spike | 2 | 16 | 0 | 27 | damage type 1 unread |
| Guard Post | 2 | 4 | 0 | 0 | separate room/creature dispatch in update |
| Magic Door | 2 | 32 | 500 | 59 | process 9; DOORS reference is separate |
| Spare Door 1 | 2 | 30 | 0 | 38 | process 4 (speed); trap integration unbuilt |
| Hero Sentry | 1 | 24 | 100 | 44 | contact damage 7 |
| Hero Alarm | 1 | 4 | 50 | 13 | process 12 |
| Jack In The Box | 2 | 4 | 0 | 64 | process 39 unread |

Existing shot rules and their limits: [shots.md](shots.md),
[combat-spells.md](combat-spells.md), [creature-status-effects.md](creature-status-effects.md).
Reading a trap's firing call does not establish the unread shot's behavior.

## Built subset and remaining reads

`KwdTraps` exposes the gameplay definition fields, and `TrapActivationRules`
implements readiness and charge arithmetic. `SimulationTiming` owns the loader's
fixed-point conversion. These are stateless rules; no automatic trap update or
firing is dispatched. Hash/snapshot layouts are unchanged (ADR-0251).

**UNKNOWN at integration boundaries (Q450):** finish selector 3's countdown
source `0x006F2966` and `0x004A8E70` propagation, then lifecycle callers of
`0x004A9720/0x004A9890/0x004A9A00`. All sight and tile traps can depend on those
counts. Preserve `0x00453AC0`'s tile-chain ordering before enabling selectors
2/3. Shot creation also needs the immediate-hit reader of `shot+0x14D`, vertical
origin/aim and collision membership; Q398 tracks the existing shot limitations.
Unread per-shot work is tabled above. Guard Post's update branch needs its
room lookup `0x004C61C0` and linked creature dispatch read together.

**UNKNOWN acquisition (Q455):** who starts disarming and where both references
are assigned. The full 13-block scan, including `cseg`, found 56 occurrences
of little-endian `0x0000043D`, including unrelated classes and instruction
displacements. Its positive control `0x006F2590` returned 226 hits. This does
not prove acquisition absent: indirect writes and wider copies remain possible.
Trace state 204's entry registrations and the writers of trap `+0x115` with
class identity retained. `0x00481610`, called after disarm success, is an
ability/action attempt, not evidence of selecting another trap.

**STRONGLY INFERRED threat:** `0x004B32A0` counts visible hostiles strictly
inside its outer radius, divides threat by that count, and applies it only
inside a radius one tile smaller (at least one). The update's duration-byte
decrement does not itself clear the threat word. This trap-specific distribution
must be integrated before firing; creature combat fear radiation is not a
drop-in replacement.
