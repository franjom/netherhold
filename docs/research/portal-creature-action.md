# Scripted portal creatures (action 28)

## Evidence and scope

CONFIRMED (binary inspection, 2026-09-28): Ghidra 12.1.4, Codex's
`codex-thunder/DK2_RE` copy, read-only. `ScanAll.py 006F2590` found 226 hits
across all initialized blocks before this investigation. Addresses below refer
to the shipped v1.70 executable. Exports remain under `reverse-engineering/`.

## The scripted call

CONFIRMED: player dispatcher `0x004C8620`, arm `0x004C8851`, passes body
bytes 0 and 1 (creature type and requested level) to `0x004DC8F0` and returns
success. The latter temporarily sets player `+0x76` bit `0x400`, preserving
an already-set bit. It walks head `+0x36`, next `+0x08`, stopping at the first
room of type 3. No Portal produces nothing; failure at that Portal does not
try another. Success increments that room's `+0x4D`, then calls `0x0048B120`
once per missing level. This routine does not write the portal clock `+0x49`.

CONFIRMED: `0x004DC9A0` immediately refuses when the owner's bit `0x400`
is clear (`0x004DC9AB`–`0x004DC9C0`). This explains the wrapper's temporary
enable. Its complete body contains no ordinary attraction, availability,
keeper-capacity or pool-quantity eligibility check. Those belong to the
ordinary selector `0x004DBF90`. The creator calls `0x005064B0` on player
`+0x1109` to subtract one from the type's pool when possible, ignoring failure.
The simulation's immutable pool remains a standing limit; see Q474.

CONFIRMED: creation uses room coordinate bytes `+0x2D/+0x2E`, each converted
to fixed point with an added `0x1800` (`0x004DC9F5`–`0x004DCA43`). For a
normal 3×3 Portal these locate its centre. It calls `0x004B78D0` →
`0x004B7900`. A unique type substitutes its definition's owner byte before
owner resolution. The routine refuses an unresolved owner or an allocated-creature
count at least `0x1C0` (448). Neither body searches for a vacant walkable
tile. Occupying the Portal therefore does not itself refuse creation.

CONFIRMED: after initialization the creator sets body `+0x3E6` bit 1,
applies effect `0x31`, sets `+0xF4=0x400`, enters state 193 for a keeper
Portal (278 for a Hero Portal), and stores the originating room id at
`+0x4EB`. It emits arrival notifications `0x0D/0x0E` through `0x004C4600`
under its type-seen/mode checks; it also has mode-specific sound/effect calls.

CONFIRMED: handler 193 at `0x00459C20` waits for `0x00496D00` to return
zero, clears its motion vector, displaces the creature using `0x00494650(0)`,
sets state 1, then calls `0x0046F390`. UNKNOWN: the exact wait duration and
that final job-selection call's complete effects. These must be read before
assigning a simulation duration. The ordinary path currently starts idle too.

CONFIRMED negative scope: no direct `0x0049AA30` created-event call occurs
in the complete bodies of `0x004DC8F0`, `0x004DC9A0`, `0x004B78D0`,
`0x004B7900` or initializer `0x004872A0`. No synthetic event 3 was added.
UNKNOWN: indirect events deeper
in initializer callees; this scan does not claim their absence.

CONFIRMED: `0x0048B120` increments one level, stopping at 10 or a finished
creature. Health retains its fixed-point fraction of the old maximum;
experience is not reset there. `CreatureLevelRules.LevelUp` already models
that arithmetic. Its existing unmodelled adjustment is covered by Q427.

## Warlocks and a Library

CONFIRMED (shipped data): Warlock 5 has attraction `(Present=1, RoomId=6,
RoomSize=0)` and an empty second slot. Level 2 pools and unlocks it. The ten
action-28 records in seven levels are pinned, including ids and levels, by
`PortalCreatureActionOnRealDataTests.ShippedAction28CreatureAndLevelPairsDoNotDrift`.

CONFIRMED: loader `0x0049355E`–`0x004935CA` compacts enabled attractions
into runtime triples: room byte, size byte, type byte. `0x00569C60` reads
the field currently named Present; 1 selects the room comparison in
`0x004DC810`. `0x004C6EB0` sums room `+0xA7` across the keeper's room list.
The comparison itself permits zero tiles when the required size is zero.
Our extra existence check does not alter Warlock admission because its
separate budget is zero without Library capacity.

CONFIRMED: selector `0x004DBF90` uses the first attraction's room type,
sums `0x004DCE70` contributions and subtracts one per owned creature of that
type. A Library contributes room `+0x67`, its furniture capacity; the Lair
has a separate branch. Existing capacity evidence: `binary-entrance.md` §4.1.

STRONGLY INFERRED: a furnished 16-tile Library at turn 635 can attract a
Warlock in our engine, assuming the keeper is below its overall cap and
has unused Warlock budget. The shipped-data test establishes that setup and
also proves refusal when existing Warlocks exhaust the budget. Tile count
alone does not establish how much furniture the original had at that turn.
No attraction change is justified for this Level 2 case.
STRONGLY INFERRED: admission does not guarantee the next ordinary arrival's type. Our picker is
uniform, while the binary's weighting remains the separate Q90 limitation
documented in `portals.md`.

## Implementation boundaries

Action 28 now uses shared `PortalRules.CreateArrival` placement/tally and
`CreatureLevelRules.LevelUp`, preserving the ordinary clock. It descends with
no Portal and delegates from a creature to its owner. World and action-point
contexts refuse it.

The existing arrival representation remains an approximation: derived tile
order instead of a persistent player room list; bounding-box centre for
malformed portals; idle instead of the animation-driven state 193 sequence;
no original 448-slot allocator, unique-type owner override, mutable pool consumption, arrival body bit,
origin room id, or audiovisual notifications. Q473/Q474 track the missing
work. These are not evidence that the corresponding DK2 rules are absent.
