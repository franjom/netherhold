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
starts creature action 49 (`0x31`, Entrance), sets `+0xF4=0x400`, enters state 193 for a keeper
Portal (278 for a Hero Portal), and stores the originating room id at
`+0x4EB`. It emits arrival notifications `0x0D/0x0E` through `0x004C4600`
under its type-seen/mode checks; it also has mode-specific sound/effect calls.

CONFIRMED: handler 193 at `0x00459C20` waits for `0x00496D00` to return
zero, clears its motion vector, displaces the creature using `0x00494650(0)`,
sets state 1, then calls `0x0046F390`. UNKNOWN: that final call's complete
effects.

## State 193's wait and step (built 2026-09-28, ADR-0271)

- **The wait.** The creator starts action 49 (`0x004DCBE2`). Action record 49's
  `+20` byte is 30 (filler `0x00495EA6`), so it plays animation block 30,
  Entrance: the creature record's 84-byte art blocks from `0x20`, frames at
  `+0x44` and rate at `+0x48`. The Warlock has 147 frames at 30, the Goblin 197
  at 30. STRONGLY INFERRED from two exact fits: the recording holds every
  Warlock in 193 for 21 ticks and every Goblin for 28, which is
  `ceil(frames × 4 / rate)` plus the tick the handler sees the action end.
- **The step.** `0x00494650(0)` returns runtime `+0x6EC`, which the loader fills
  from source `+0x1284` (`0x00493B37`), file offset `0xE94`. Handler 193 adds it
  to the position unrotated and takes the floor height there. The Warlock's is
  (0, 2929), the Goblin's (43, 3139): from the centre (12.5, 38.5) both land on
  (12,39), as all eleven recorded Level 2 arrivals do.
- **Why it matters.** The centre is the portal's hole. A creature left there has
  no route, so every one of our portal arrivals stood on it for good.

Built: `PortalRules.JoiningState`, `JoiningTicks` and `StillJoining`;
`CreatureJobSystem` holds 193, steps and chooses in the same tick. `WorldView`
plays the Entrance clip once from the state-entry stamp; `ArtSourceMap` selects
file block 30 (catalogue index 29, which omits the initial unrelated block).
The creator now also sets the original heading `0x400`, so authored motion
faces the same direction as the eventual step. The existing classic clock
retains its final pose until state 193 ends. Verified
against all 46 shipped creature descriptors and the Warlock/Goblin view lifecycle.
The classic handoff treats the authored exit displacement as a relocation:
its visible previous XY becomes the exit XY for that frame, preventing the new
clip from replaying the Entrance's movement from the portal centre. Native
previous XY remains unchanged. Endpoint mesh probes (Goblin, Warlock, Troll,
Mistress and Salamander) found no material foot-height gap: below 0.016 tile.
Deviates: height is not taken, and 193 reads as idle to other simulation systems.

CONFIRMED negative scope: no direct `0x0049AA30` created-event call occurs
in the complete bodies of `0x004DC8F0`, `0x004DC9A0`, `0x004B78D0`,
`0x004B7900` or initializer `0x004872A0`. No synthetic event 3 was added.
UNKNOWN: indirect events deeper
in initializer callees; this scan does not claim their absence.

CONFIRMED: `0x0048B120` increments one level, stopping at 10 or a finished
creature. Health retains its fixed-point fraction of the old maximum;
experience is not reset there. `CreatureLevelRules.LevelUp` already models
that arithmetic. Its existing unmodelled adjustment is covered by Q427.

## The portal's type pick (built 2026-09-28, ADR-0272)

Read with Ghidra 12.1.4. STRONGLY INFERRED from the binary; the order it predicts
matches the Level 2 recording.

- **The weight.** `0x004DBF90` walks the 46 types. For each type the keeper may
  receive (the same gates as ours), the weight is taken from its first attraction.
  A Lair gives a flat 1.0, or 3.0 when the definition's lair object (`+0x771`) is
  `0x16`, the Goblin's. Any other room gives each of the keeper's rooms of that
  type's contribution (`0x004DCE70`), less 1.0 per creature of the type held.
  A type whose weight is 0 here is not a candidate.
- **The modifiers.** The player's `CreatureEntranceInfo` table (`+0xB57`, 18-byte
  rows, named by the player dumper `0x004BCF10`) holds a boost at row `+0x06` and a
  sack block at `+0x0E`, both in 1/4096. A boost of exactly 10.0 multiplies the
  weight by 200, any other by itself, at least 1.0. A block of exactly 10.0 zeroes
  it, any other divides by itself, at least 1.0.
- **The draw.** The weights become shares of 100.0 and a running total. One random
  number below the total picks the first entry whose running total reaches it. The
  creature is made (`0x004DC9A0`) and the portal's tally goes up.
- **The decay.** After every pick, every non-zero boost and block drops by 1.0.
- **Noticing.** `0x004DBD90` runs once at level start (`0x0050BDD6`) and before each
  pick. It gives each admitted type without a row one. After tick 1, a type not
  attracted by the Lair starts with a boost of 10.0. So a type that becomes
  available mid-game is almost certain next, then weighted 9, 8, 7 and so on.
- **The block.** `0x004DC7E0` sets 10.0 on the type named as last sacked
  (`+0xB47`, `EntranceLastSackedCreatureTypeId`). Its caller `0x0045A0D0` is
  unread. Ours has no sacking, so the block is never set.

In Level 2 the Library makes Warlocks available at 635. Their boost puts four
Warlocks through our portal before the first Goblin, against DK2's five. With
this built, the whole recording replays and our keeper wins, at 3508 where DK2
won at 3263.

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
STRONGLY INFERRED: admission does not guarantee the next ordinary arrival's type. The
weighted pick is above.

## Implementation boundaries

Action 28 now uses shared `PortalRules.CreateArrival` placement/tally and
`CreatureLevelRules.LevelUp`, preserving the ordinary clock. It descends with
no Portal and delegates from a creature to its owner. World and action-point
contexts refuse it.

The existing arrival representation remains an approximation: derived tile
order instead of a persistent player room list; bounding-box centre for
malformed portals; a fitted state-193 wait instead of the full original action runner;
no original 448-slot allocator, unique-type owner override, mutable pool consumption, arrival body bit,
origin room id, or audiovisual notifications. Q473/Q474 track the missing
work. These are not evidence that the corresponding DK2 rules are absent.

## The entrance effect (read 2026-10-14, built as ADR-0476)

After the notifications and before `CreatureAction_BeginAt(0x31)`, at `0x004DCB8F`, the
creator reads the creature definition (`creature+0x370`) at `+0x776`; when that is nonzero
and the dword at `0x00759AEC` is at least 3 (that it is the effects detail setting is
INFERRED) it requests that effect through the view slot `+0x38C` with five arguments: the
effect id, the room owner's tag (`room+0x1D`), a pointer to the spawn position (the room's
`+0x2D`/`+0x2E` shifted up 12 plus `0x1800`, z a literal 0), a pointer to the creature's
heading `+0xF4` (0 then; `0x400` is stored after the call) and a pointer to a zeroed
local. The field is the creature record's **Portal Entrance effect**: the loader
`0x00492320` copies staging `+0x1326` to `def+0x776`, file byte 3894 (`0xF36`), which
holds 181 for the Goblin, 178 for the Dark Elf, 180 for the Troll, 185 for the Rogue,
179 for the Firefly and 184 for the Black Knight (CONFIRMED). Each of those effects
chains (`NextEffectId`) to a Portal Beams effect, 159, 161, 166 or 168, whose element
draws the `Lightbeams` animation (art type 6, 60 frames, HP 120) at scale 3 from 3200
below the request point, with a white disc sprite over the hole. That the descriptor's
second dword (10) is its frame rate is a HYPOTHESIS fitted to the life: 60 frames at 10 a
second is the element's 6 seconds; no reader of type-6 elements has been read.
Built: `CreatureDef.EntranceEffectId`, `PortalRules.EntranceEffect`, posted by
`PortalSystem` after `CreateArrival`; the detail gate is taken as satisfied.

### Arrival rendering and the reported colour tail (2026-10-23)

The adapter now dispatches animated elements (type6) to its mesh-frame renderer;
the existing branch had been unreachable. Flat sprites, including the white disc,
and additive effect meshes share the terrain's transparent triangle stream, so
depth rather than renderer priority decides whether the chasm covers an effect. The isolated Level8
probe verifies sixty beam frames and captures the Goblin arrival through7500ms.
The ten-frames-per-second interpretation above remains a HYPOTHESIS.

CONFIRMED shipped data: Goblin181 chains to161, generating smoke354,
sparkle355 and white-disc326. Their art flags are0x84,0 and0x101; none has
keeper-colour bit2. The owner reports a keeper-coloured central cloud after the
bright phase, beyond the supplied reference frames. Its producer is UNKNOWN;
no transition is invented. The generator has no post-birth RGB override
([004386F0](functions/004386F0.md)), and action49 has no tick callback
([004950C0](functions/004950C0.md)). This tail is Q619; sprite lighting is Q609.
