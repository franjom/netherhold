# Player messages consumed by the computer keeper

## Evidence and scope

Independent audit, 2026-09-28, of `DKII.EXE` v1.70, MD5
`2f3bbc21eae8c3aa468136baa303036d`, using Ghidra 12.1.4/PyGhidra in the
read-only project. Addresses are virtual. Layout and shipped records are
**CONFIRMED**; behavioural interpretations are **STRONGLY INFERRED** unless
stated otherwise. No live message sequence was captured. The keeper's reactions
are in `../KEEPER_AI.md` §5.3; this document owns their producers and lifetime.

## 1. Retained global queue

`0x004C4090` allocates 256 records of 56 bytes. There is one active linked list
and one free list at world `+0x2E54`, shared by all players. The iterators
`0x004C4B70`/`0x004C4B90` filter that list by recipient; they do not create
independent player queues. When full, insertion evicts the oldest active record,
even if it belongs to another player (`0x004C4600`).

The dumper `0x004C3F20` names the following fields; the writers and serializers
`0x004C4600`, `0x004C42E0`, `0x004C4470` establish widths and persistence.

| Offset | Width | Meaning |
|---|---|---|
| `+0x00` | u8 | pool slot |
| `+0x01` | u16 | recipient player tag |
| `+0x03` | u32 | message type |
| `+0x07` | u32 | subtype used for duplicate matching |
| `+0x0B` | u32 | unique message id |
| `+0x0F`, `+0x13` | u32 each | string and graphic ids |
| `+0x17`, `+0x1B` | u32 each | age and timeout in ticks |
| `+0x1F`, `+0x23` | u32 each | Data1 and Data2 |
| `+0x27` | u8 | NeedsTab |
| `+0x28` | u16 | ComputerPlayerFlags; bit 0 means handled |
| `+0x2A` | u16 | initialized to zero; purpose UNKNOWN |
| `+0x2C` | u32 | system-time stamp; behavioural use UNKNOWN |
| `+0x30`, `+0x34` | pointer each | next and previous active/free records |

`0x004C43B0` saves the queue header and all 256 records. Reading a message or
setting its handled bit does not remove it. The original debug sample
`samples/worldlog-envmapshowcase-gt231.log` contains 256 message records and a
type-41/Training-Room record aged 229 with timeout 1200: **CONFIRMED** sample
values, not a capture of its producer or the keeper's response.

### Insertion and expiration

`0x004C4600` rejects recipients whose player status is nonzero. This differs
from the keeper tick, which still runs at status 2. It also checks world
`+0x72A1` bit 1, with exceptions for types 14/21/48/49. Trigger action 45 writes
that bit at `0x0051972B`: only parameter byte 1 enables ordinary messages
(`trigger-dispatch-tables.md`). Our existing `TriggerState.NotificationsEnabled`
already models this gate; it must also govern keeper messages. Per-player
suppression is checked through `0x004C5530` before
allocation, including a Graveyard-needed suppression field at player `+0xF4E`.

The table at `0x006A0B00`, ten bytes per message type, supplies flags, a string
or subtype-table pointer, and a graphic. Low 13 flag bits give lifetime in
seconds, multiplied by the world's ticks per second. Bit 13 sets NeedsTab;
bit 14 permits duplicates; bit 15 selects a subtype table.

With duplicates disallowed, an unexpired record for the same recipient and type
is returned unchanged. For subtype messages, Data1 must also match the stored
subtype. Neither age, data nor handled state is refreshed. A successful new
insertion receives a new unique id, age zero and cleared handled state.
Type 6 has timeout zero, so `age < timeout` is false even immediately after
insertion: it does not suppress another type-6 record before the sweep.
This was rechecked at `0x004C4600`; the clear duplicate-permission bit alone
is insufficient to infer suppression.

`0x004C41E0` increments age and removes at `age >= timeout`. A zero-timeout
message therefore survives insertion until the next queue sweep. NeedsTab clears
at age 200. Types 17/18 can expire earlier when their referenced live creature
is no longer in mood 1/2 respectively. The handled bit is not an expiration test.
The world turn calls the player update at `0x0050A7F5`, then the queue sweep at
`0x0050A800`. The player update reaches the keeper tick (call chain in
`computer-keeper-tasks.md` §1). Thus the keeper can react before a zero-timeout
combat message is removed in that same turn. Both calls were checked in
instructions as well as the decompiled world routine `0x0050A450`.

| Type | Table flags | Lifetime |
|---|---|---|
| 1 | `0x2078` | 120 seconds |
| 6 | `0x2000` | zero; next sweep |
| 12, 17, 18, 47 | `0x2032` | 50 seconds |
| 32, 34, 41, 42 | `0xA000` | selected by Data1; duplicates suppressed per subtype |
| 35 | `0xE000` | selected by Data1; duplicates allowed |
| 55 | `0xA000` | subtable `0x006A0690`: 1–4 20 s, 5 240 s; both Training hooks pass 5 (read 2026-10-01) |

A type's row is at `0x006A0B00 + 10 × type`. Rows 41, 42 and 43 are `0xA000`
with subtables `0x006A03D8`, `0x006A04B8` and `0x006A0528`; each entry is
eight bytes, u16 room type, u16 seconds and an i32 string id, ending in a
type-0 row. Type 41 gives Treasury 420 seconds, Lair/Hatchery 180,
Graveyard/Prison 600 and room types 6, 7, 9, 10, 12, 13, 15, 16 300. Type 42
gives Treasury/Hatchery 240, Lair 90 and types 6, 7 and 9–16 300. Type 43
gives Prison/Torture 360 and types 1–4, 6, 7, 9, 10 and 13–16 300. These bytes
are **CONFIRMED** (re-read 2026-10-02). The record starts with timeout zero; a
matching row sets subtype, string and lifetime. An unmatched type takes the
terminator's string with timeout and subtype zero: 41 and 43 end in string 0, so
the record is freed again after any eviction it caused; 42's terminator string
is nonzero, so the record stays until the next sweep. The duplicate test
compares Data1 with the stored subtype (`+0x07`), not with Data1.

### Player suppression state

`0x004C5530` checks the following player dwords before duplicate lookup.
`0x004C5650` updates them on the accepted-message path at `0x004C4A00`, even
when the caller did not request a retained record. Duplicate-return paths do
not reach that update. Deadlines compare current turn strictly below the stored
value; equality permits another message. Durations use `0x006F2590` ticks/second.

| Types / Data1 | Player offset | Suppression / update |
|---|---|---|
| 41 or 99 / 14 | `+0xF4E` | reject when 1; write 1 |
| 53 / 1 | `+0xF52` | deadline; current turn + 300 seconds |
| 55 / 1 | `+0xF56` | deadline; current turn + 120 seconds |
| 75 / any | `+0xF5A` | deadline; current turn + 600 seconds |
| 89 / 27 | `+0xF62` | reject when 1; write 1 |
| 89 / 10 | `+0xF66` | reject when 1; write 1 |

Save persistence is **STRONGLY INFERRED**: `0x004B8D40` writes and `0x004B9250`
reads `0x1028` bytes beginning at player `+0x76`, covering every suppression
field. Queue expiration therefore does not reset these values on a save/load.

**Initialization: none in the code (re-read 2026-10-09, Q544).** Neither the constructor
`0x004B8450`, its sub-objects, nor `0x004B8640` writes `+0xF4E`, and `Players_Create` `0x004C1E20`,
called at each level load, takes the seven records from an uncleared `HeapAlloc`. Its one writer is
`0x004C5650` (1, at `0x004C5693`), its one reader the gate (`0x004C5575`); only a saved-game load
refills it (`0x004B9250`). So 41/99 Graveyard is admitted at most once per player per level, and a
save keeps the flag. The first value is the heap's: typically 0 (HYPOTHESIS; no code decides it).
Ours starts it at 0, a modelling choice, and saves it (`GraveyardWantedPosted`, ADR-0414).

## 2. Producers and payloads

### The census (2026-10-04)

`0x004C4600` takes five stack arguments (`RET 0x14`): type, recipient, Data1, Data2
and an allocate flag. A rel32 scan of both executable blocks (`.text` and `cseg`)
finds **151 call sites** and no absolute reference; every known producer is among
them (the positive control). Each site pushes its type as an immediate; shared call
tails were read for every function holding a type the walk reads. The only
wrappers that pass a type on are the anger pair below. Data2 is a thing tag where
stated, not a stable index. Every type the keeper's walk (`0x004FA400`, §3) reads:

| Type | Producer | Recipient; Data1 / Data2 | Gate | Ours |
|---|---|---|---|---|
| 1 | `0x004DA200` | Heart owner; heart x / y | Negative heart-health delta, not invulnerable (`win-and-defeat.md` §3.1) | built |
| 6 | `0x0045EB20` | Attacker's owner; its x / y | State 199 entered this turn, attack mode not 3 | built |
| 12 | `0x00452F60` (`0x0045313E`) | Tile owner; x / y | Work brings to zero a tile whose terrain is Solid and Ownable and whose owner is not the worker's player; then player events `0x38`/`0x39` | built (`TileWorkSystem.PostThreat`); events `0x38`/`0x39` not |
| 12 | `0x00498BB0` | Tile owner; x / y | Creature action 9, the breach check (`trigger-walkers.md`) | action 9 unbuilt |
| 17, 18 | `0x00478B80`, 60 sites | Creature owner (`+0x28`); selector / creature tag | Not never-angry; the add changed the band and the amount is positive; band 1 posts 17, band 2 posts 18 and fires trigger event 11 | posts §4; event 11 not fired |
| 17, 18 | `0x00478C30`, one site (`0x004D6A68`) | as above | The percentage adder `0x004870E0`, same tests | site unbuilt |
| 32 or 33, 35 | `0x004E5650` | New owner 32 (33 when the old owner is neutral), old owner 35; room type / room tag | Whole-room transfer | §4 |
| 32, 35 | `0x0044FC40` | New / old owner; 287 / 0 | The tile set to terrain 31 or 32 (it makes object 101 or 108 there) changes owner; skipped under world `+0x7239` bit 6 | unbuilt |
| 34 | `0x00466080` | Room owner; room type / room tag | A worker takes state 18 against an enemy room (job 11, Q523) | job 11 unbuilt |
| 34 | `0x00497DF0` (`0x00497F29`) | Tile owner; room type / room tag | A Good creature's action-11 blow that leaves the tile a room (`room-health.md` §4) | built (`TileWorkSystem.PostThreat`) |
| 41, 42, 43 | rows below | Room's owner; room type / 0 | Per producer | §4 |
| 47 | `0x004AE120` | Shot owner; x / y | Shot process 12, the Alarm trap's shot (`shots.md`; entries of 17 bytes from `0x0069E84F`) | built |

The 41 and 42 producers: the carrier `0x00464400`, `0x004BA420` (below), the Lair,
Hatchery, Combat Pit, captive Pit, Graveyard, Library, Casino, Temple, Torture,
Training and Workshop finders (below), the rescue selector `0x0046D810`, the claim
failure `0x004E5A70` (Data1 room type; also a Prison past `+0x4B` after a
successful claim, `0x004E5C72`, Data1 11; no availability, pending-work or timer
gate), a Lair occupant with no bed slot (`0x0046F4F0`: 42 Lair, then state 1) and the
Graveyard count `0x004DDCA0` (42 Graveyard when its places do not exceed the
owner's flagged creatures).

Rows 32, 33 and 35 are `0xA000`/`0xA000`/`0xE000` with subtables `0x006A0060`,
`0x006A00F8` and `0x006A0220`: room types 1–4, 6–17, 22 and 287 at 20 seconds. Row
34 (`0x006A0190`) lists the same types without 287 at 30 seconds. Each ends in a
terminator with a nonzero string, so an unlisted type, a Hero Portal (25) among them,
is kept until the next sweep. 17 and 18 are `0x2032` (fifty seconds, no subtype, no
duplicates): one unexpired 17 for a player suppresses another creature's.
Their string is computed from the creature (`-9999 - (def word + 1 + Data1)`),
so they are retained. Every other type reaches the walk's default arm.

For 17/18, Data1 is the anger wrapper's third argument, not its bucket index.
The keeper recomputes the current largest bucket when choosing a remedy;
see `computer-keeper-management.md` section 13.

### The gold carrier and gold paid in

Rechecked in instructions 2026-10-02. The carrier's store chooser `0x00464400`
walks its owner's Treasuries and unruined Hearts (`+0x9A` bit 0 clear). A store
whose `+0x49` gold is below its capacity (`0x004F4700`) clears a "none has space"
flag; one whose first slab `0x00471630` reaches with options `0x80` sets a
"reachable" flag and is a candidate. With no candidate: no Treasury
(`0x004C6E70(1)`, all Treasury rooms, compared unsigned below 1) posts 41 only
at availability 3; else no space posts 42; else no reachable store posts 43.
The Heart counts as a store for 42 and 43 but not for 41. No pending-work or
timer gate applies; the point test is the route test (`0x00471630`'s record).

`0x004BA420` with a positive amount, flag 0 and a nonzero tile spreads the gold
over the Treasuries and unruined Heart in equal shares (`0x004F4090` returns
what a room did not take) until a pass stores nothing. Gold left over then
posts 41 with no Treasury, 42 with one, when the Treasury availability row
(`0x004BAB80(1)`, no row reading 1) is not 1, and is dropped as a floor pile.
State 2 therefore posts here and not in the carrier. The branch for a Treasury
count of 255 is unreachable. Such calls come from the room-tile sale
`0x004C5DB0` (its refund at the tile), `0x004C3670`, `0x004C3A20`, action `0x77`
(`0x0051415F`) and `0x00499E50`/`0x00499F86` (at the Heart); only the sale is
classified here.

Room availability and room need are separate conditions. The finders' branches:

| Producer | Room / messages | Discriminating gate |
|---|---|---|
| `0x004DED00` | Hatchery / 41, 42, 43 | Availability 3. Stocked means a non-empty `ObjectList` (`+0x0C`), which holds chickens and no coop (`hatchery.md` §3); a stocked room the `0x80` route reaches is chosen. No room gives 41; else a stocked room route option 2 cannot reach gives 43; else an empty reached room with `ChickensGenerated` (`+0x51`) non-zero, more than four seconds past `room+0x9F`, gives 42. Pending build or sale suppresses all three (rechecked 2026-10-02). |
| `0x004E0470` | Lair / 41, 42, 43 | Availability 3. A Lair the `0x80` route reaches with a slot is chosen. No Lair gives 41 unless a build or sale is pending; else a Lair with a slot that route option 2 cannot reach gives 43; else a reached full Lair more than four seconds past `room+0x9F` gives 42 (rechecked 2026-10-02). |
| `0x004D8870` | Combat Pit / 41, 42, 43, 44 | Availability 3 and no pending build/sale for each message. Four-second timing applies to the unusable-place branches. Message 44 carries the room tag; the others carry zero. |
| `0x0046CF40`, `0x0046D3C0` | Graveyard / 41, 42 | Both require availability 3 for absent-room 41. Only `0x0046CF40` rechecks availability 3 on the failed-capacity 42 branch. Built (ADR-0414). |

The pending-work predicates `0x004C7340` and `0x004C7360` scan three room-type
bytes, stride `0x19`, at player `+0x1C2` and `+0xE97`. Their writers are the
build/sell queues `0x004C6EF0`/`0x004C70A0`; see `room-slab-chain.md` sections
2 and 4. Any occupied slot suppresses the gated requests, regardless of room type.
The route helper `0x00471740` takes a room point (`0x004E5000`) and tests the
creature's route (`0x004D0E60`) to it. Option bits 1, 2 and 7 become creature
`+0x3E6` bits 17–19, `IgnoringDoors`, `IgnoringLockedRegion` and
`CheckingAllDoors`: the first search respects every door, the unreachable test
does not. `room+0x9F` is `GameTickLastOnChangeSize`, the game turn written by
`Room_Construct` (`0x004E3965`) and by `0x004E4480` (`0x004E4491`), which all
three tile rebuilds call; "four seconds" is `4 × ticks per second`, compared
unsigned and strictly. These are not merely room-count checks.

The Library finder `0x004E2500` checks **Lair availability (2)** before posting
Library (6) messages: `0x004E26A6` pushes 2, `0x004E26AA` calls the availability
lookup, and `0x004E26AF` compares with 3. This is not Library availability.
An existing Library also requires nonzero player research points (`+0x213`)
before its failure messages. Missing room gives 41; otherwise the precedence is
unreachable 43, missing storage position 45, capacity 42, then room-tagged 44.
All branches test pending build/sale. The storage-position and capacity failures
wait strictly more than four seconds after `room+0x9F`; `room+0xA3 == 0`
selects 44 instead. This quirk needs a test with differing Lair/Library availability.

The Casino (`0x004D75F0`), Temple (`0x004F1A20`), Torture (`0x004F2B80`),
Training (`0x004F3560`) and Workshop (`0x004F6200`) finders (read 2026-10-04)
return a room the `0x80` route reaches with a place before any message. Otherwise,
at availability 3 and behind the pending build/sale tests: no room 41; else a room
with a place that route option 2 cannot reach 43; else a reached room without a
place, more than four seconds past `room+0x9F`, 42 when its `+0xA3` is set and 44
naming the last such room when not. `+0xA3` is written by each type's
after-tile-change pass: a Training Dummy (14) or Target (29) attached
(`0x004F2EA0`), a Torture Wheel or Electric Chair (`0x004F1F10`), an Anvil, Steel
Bucket or Work Bench (`0x004F5530`), any interior tile for a Library, Combat Pit or
(inferred) Casino; a Temple's is only zeroed, so a full Temple posts 44, never 42.
Ours reads it live, which agrees: the furniture follows the room's size (owner, 2026-10-05).
Torture's place is two creatures' units (one unit free is silent) and its missing
room is 99 when creature type `+0x36F` is 4, the Mistress (row 99: `0xA000`, subtable
`0x006A0448`, 41's lifetimes under one string). A Casino with a lever (`+0x53`) is a
candidate without a place, and its 43 wants both; while the keeper's jackpot-party count
`+0xF4A` is set, its first loop meets only Casinos with a party on (`+0x41`, `casino.md`).
The Workshop's 43/42/44 need player byte `+0xA1C`, the manufacture queue's count
(`state-category-hooks.md` §3.4; ours `ManufactureQueue.Count`). The Library's
(`0x004E2500`) asks each reachable Library for a shelf (`0x004E1AC0`, a draw) before a
place: missing, it flags 45 (row `0xA000`, subtable `0x006A08C8`: Library and Workshop,
120 s) when `+0xA3` is set, else 44; then 41, the research-target return, 43, 45, 42, 44.
The Temple's and Combat Pit's capacity is their rim (`0x004E3ED0`, the cells
`0x00454110` rejects), so a full Temple posts 44. The spectator finder `0x004D8510`
counts only pits with a fight on and never posts 41; the fighter's `0x004D8870`, called by
`Job_StartTrain` for a creature between the Training and Combat Pit level caps, posts 41,
43, 42 or 44; its drop test `0x004D8430` fails a pit whose floor slots are all stood on. Row 44's subtable `0x006A08E0`: 1, 2, 4 at 120 s, 6, 7, 9–16 at 240 s. Built: ADR-0413.

The rescue selector `0x0046D810` separately caches Lair and Prison searches
on the worker state. Missing room plus availability 3 produces result 1;
an existing room with no capacity produces 3; capacity without a worker route
(`0x80`) produces 2; a usable room produces 4. When an eligible collapsed
creature is encountered, results 1/2/3 emit 41/43/42 respectively, with room
type in Data1 and zero Data2. Foreign creatures request Prison; own creatures
without a bed request Lair. An existing bed bypasses that Lair request branch.
These senders have no pending-build/sale check or four-second room timer.
The capacity search `0x004C6510` uses Prison's `+0x4B` limit rather than
generic capacity. The routed search `0x004C6610` additionally requires Prison
`+0x59` nonzero: the door's `Activation` (`jail-break.md` §2, read 2026-10-09 for Q542). The Prison's
after-tile-change slot `0x004E2B30` sets it when the room is one tile (`+0xA7` = 1), so a hand-built
Prison has it on from its first tile; the owner's door arm `0x004E29E0` also sets it, the toggle
`0x004E3680` is the only clear in play, and a restore (`0x004ED440`) copies a saved word. A map
Prison has it too: the loader inserts its tiles one by one (`0x004C5C30` → `0x004ED1A0`, which runs
the slot after linking each tile). Built as ADR-0412, without carrying a bedless own creature to a Lair.
The foreign scan first prioritizes the keeper's imprisonment-mask bit, then
repeats without that bit test after the own-creature rescue scan. Other rescue
eligibility and task-reservation effects belong to the worker system.

The ownership transfer's argument order was checked in instructions:
`0x004E5658` reads the old owner; `0x004E58F4` sends 35 to it;
`0x004E58F9` compares it with the neutral tag; `0x004E5931` sends 32 or 33 to
the new owner. Neutral-room acquisition therefore does not take the keeper's
type-32 Portal/Lair branch.

## 3. The room-message arms and implementation requirements

Read in instructions 2026-10-02 (`0x004FA5B8`–`0x004FA70F`). Both arms first
clear `NextRoomToBuildSet` when `NextRoomToBuild` is the Portal placeholder 3.
The request is Data1's low byte; availability is `0x004BAB20` on the keeper's
player for the request without bit 7.

- **41.** Availability other than 3: handled. A request already set: left
  unhandled, so the walk retries it each turn while it is retained. Otherwise,
  unless `CurrentRoomToBuild` names the same type (low seven bits), the request
  becomes `NextRoomToBuild` with the set bit; handled either way.
- **42.** `0x004FA7C0` runs first, for every room type: action 41 through the
  dispatcher `0x0050E5C0` on each kind-2 task, the gold digs. It reports
  failure only when an action returns 0 on a tile whose terrain (world slot
  `+0x6C`, `0x00509450`) has `+0x17B` bit 1, Impenetrable (Gems in the shipped
  data). A failure with Data1's byte equal to 1 marks the message handled and
  stops. Otherwise `BuildNewRoomWhenMoreSpaceRequired` (`+0x4B2` bits 7–8,
  loaded as `(byte & 3) << 7`): 0 sets bit 7, expand; 2 marks handled; 1 and 3
  ask for a new room. The availability, set-bit and same-room tests then follow
  as for 41, storing the byte with bit 7.

The untag failure branch is unreachable in 1.70, **CONFIRMED** statically: the
dispatcher returns the handler's result, and the startup constructor `0x00511A80`
registers `0x005129E0` for id `0x29` in the `{id, handler}` list at `0x006A54B8`,
which the world-init routine `0x005099A0` copies into the table (cleared at
`0x00509A90`, copied at `0x00509ABD`–`0x00509AED`); `GameAction_LoadLevel` clears and
copies the same list again (`0x005139BC`–`0x005139EC`), and the handler always returns 1. The
second availability call in each arm repeats the first, so its call to the room
chooser `0x004F93B0` cannot run. The arms spend no allowance.

The other arms (read 2026-10-04). 1, 6, 12 and 47 run the position reader
`0x004F9B90` when `Flags` bit 3 is set, 34 the room reader `0x004FA000`; a
nonzero return marks the message handled, and keeper `+0` bit 3
(`FightGoingOn`) is set even when the flag is clear. 17 calls make-happy
(`0x004F9710`) when `UnhappyCreaturesHappy` (`+0x4B6` bit 15) is set, else is
marked handled. 18 calls dispose (`0x004F9520`, Data2) when
`AngryCreaturesDisposeOf` (bit 17) is set, else make-happy when
`AngryCreaturesHappy` (bit 16), else is marked handled; either remedy's nonzero
return marks it. 32 acts only when `AutomaticallyBuildBiggerLairWhenPortalTakenOver`
(`+0x4B2` bit 1) is set and Data1 is 3 or 25; otherwise it is **left unhandled**,
not marked. Then `BuildNewRoomWhenMoreSpaceRequired` 2 marks it handled, and the
request is a Lair, with bit 7 (expand) when that parameter is 0: Lair availability
other than 3 marks it handled, a request already set leaves it unhandled, and
otherwise it becomes `NextRoomToBuild` unless `CurrentRoomToBuild` names the same
type, handled. 32 does not clear the Portal placeholder. 35 clears it as 41 does,
then marks the message handled when the chooser `0x004F93B0` returns nonzero.
0, 13, 22 and 26 are left unhandled; every other type, 33 among them, is marked.
The remedies are in `computer-keeper-management.md` §13.

The keeper skips handled records but can retry unsuccessful reactions while
their messages remain retained (`0x004FA400`). Consequently a read-and-drain
event list would change both retry behavior and duplicate suppression. A typed
implementation needs retained simulation state, lifetime/eviction policy and
explicit acknowledgments; the observation sink must remain observational.
An AI host that observes only after all simulation systems, including message
expiry, would miss type 6 entirely. The input-source boundary must expose the
pre-expiration observation while preserving the chosen command delay. Suppression
initialization and unclassified producer branches still prevent
claiming complete queue fidelity.

## 4. Implementation coverage

`PlayerMessageQueue` implements types 1/6 and the mana-pressure types 52/53,
including admission, duplicate retention, shared eviction, age/tab expiry and
explicit handled state. For 52/53 the lifetime is the subtype row's 20 seconds,
a duplicate must match Data1, warning subtype 0 (no string) is discarded and a
subtype off the table is refused; their producers are `ManaSystem` and
`ManaRemovalLifecycle` (`imp-mana-removal.md`, ADR-0277). 53 subtype 1, possession
ended for want of mana, has no producer here. These types do not use the
unresolved suppression fields. `CombatMessageRules` posts
during the combat update and on the melee/shot Heart-damage paths. The state-199 handler
`0x0045EB20` was rechecked: it posts only when its entry stamp equals the current
turn and attack mode is not 3; it is not a notification on each hit.
`0x00476D30` writes the entry stamp on every accepted transition, including a
same-state transition. A transition alone does not emit; if the state changes
again before its handler runs, no type 6 is sent. Travel arrival only stamps.
`CombatSystem` runs the message prologue after acquisition on that turn and at
state-199 update; acquisition precedes state dispatch in the original
(`combat-acquisition.md` section 2). General state-hook dispatch remains open.

Scheduling and the preserved observation window are ADR-0245. Type 84, a creature that cannot reach the Call to Arms flag, is posted by states 190
and 89 with a fifty-second lifetime and no duplicates (`call-to-arms.md`). The keeper consumes
1 and 6 (ADR-0278): `KeeperThreatResponder` walks the recipient's retained entries
in queue order, skips handled ones and acknowledges by command. No live original
message trace has been captured.

Types 41–43 are retained with their subtype rows (ADR-0327); a type the rows do
not list, and 41 for a Graveyard (the `+0xF4E` suppression), are refused.
`RoomNeedRules` posts from the carrier's store chooser, the room sale's refund,
the Lair finder and the Hatchery finder (ADR-0335); `room+0x9F` is
`TileState.RoomSizeChangedTick` and the Hatchery's `+0x51` is
`TileState.ChickensGenerated`. The pending
build/sale gate has nothing to test, since a wave is laid in its command's tick.
`KeeperRoomRequests` runs both arms. Unsupported kinds fail explicitly.

Coverage of the walk's types (2026-10-04, ADR-0353; "ours" means the producing
event exists in our simulation):

| Type | Producers built | Consumer | Consumer built | What blocks the rest |
|---|---|---|---|---|
| 1, 6 | all | position reader | yes (ADR-0278) | — |
| 12 | `0x00452F60`'s post | position reader | yes (`KeeperThreatResponder`) | action 9 (not ours) |
| 17, 18 | every anger path of ours (`CreatureAngerRules`, `AngerComplaint`) | make happy, dispose | yes (`KeeperMoodRemedies`) | — (`0x004D6A40`, the percentage site, is not ours) |
| 32/33, 35 | the room transfer and the defeat handover (`RoomHealthRules.PostTransfer`) | Lair request, chooser | yes (`KeeperRoomRequests`) | — (`0x0044FC40`'s terrains 31/32 are not ours) |
| 34 | the hero blow | room reader `0x004FA000` | yes (`EvaluateRoom`) | job 11 (Q523, not ours) |
| 41, 42 | carrier, sale refund, Lair, Hatchery, the claim's 42, the Training, Temple, Torture and Casino finders, Library and Workshop at 41 (`RoomNeedRules.NoJobRoomFor`) | room request | yes (ADR-0327) | Q542 rescue, Q543 finder gates, Q544 Graveyard; Lair-bed state 79 has no entry; door and trap sales, action 0x77 and `0x00499E50` are not ours |
| 47 | Alarm | position reader | yes (`KeeperThreatResponder`) | — |

The suppression fields in section 1 remain unimplemented.
