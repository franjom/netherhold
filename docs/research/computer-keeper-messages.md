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

Type 41's subtable at `0x006A03D8` gives Treasury 420 seconds, Lair/Hatchery
180, Graveyard/Prison 600, and the other listed room types 300. Type 42's table
at `0x006A04B8` gives Treasury/Hatchery 240, Lair 90, and the other listed room
types 300. These bytes are **CONFIRMED**. Unknown-subtype fallback is not a
general default lifetime: insertion only copies a matching subtype's lifetime;
a selected zero string discards the new message.

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

Initialization remains **UNKNOWN**. The constructor `0x004B8450` and initializer
`0x004B8640` do not explicitly clear these fields. `0x004C1E20` allocates the
seven-player array through `0x00634E80`; its allocator chain ends at
`0x00637C20`, which can call `HeapAlloc` with flags zero. No explicit clearing
of the allocation was found on this path. Direct-offset and interior-base
instruction sweeps found no reset, but do not exclude every indirect writer.
Do not turn unspecified initial contents into a random rule, or infer lifelong
type-41/room-14 suppression solely from the direct writer.

## 2. Producers and payloads

These are independently traced producers, not a claim that every call site of
the queue has been classified. Data2 is a tag where stated, not a stable array
index or a creature id inferred from the message number.

| Type | Producer | Recipient and payload | Result |
|---|---|---|---|
| 1 | `0x004DA200` | Heart owner; heart tile x/y | Negative heart-health delta while not invulnerable; see `win-and-defeat.md` §3.1 |
| 6 | `0x0045EB20` | Attacking creature's owner; its tile x/y | State-199 update with entry stamp equal to current turn, excluding ally-spell mode 3 |
| 12 | `0x00498BB0` | Hostile target tile's owner; target x/y | Terrain work calls `0x004508D0`, tests return zero, then emits when owner differs; exact helper-result semantics remain UNKNOWN |
| 17, 18 | `0x00478B80`, `0x00478C30` | Creature owner; caller's grievance code / creature tag | Positive anger input changes mood to unhappy/angry; never-angry creatures are excluded |
| 32, 35 | `0x004E5650` | New/old room owner; room type / room tag | Room ownership transfer; type 33 replaces 32 when the old owner is neutral |
| 32, 35 | `0x0044FC40` | New/old tile owner; 287 / zero | Mana-vault ownership transfer uses the same message family |
| 34 | `0x00466080` | Hostile room's owner; room type / room tag | Worker enters state 18 against the room; the keeper resolves the room's position |
| 41 | `0x00464400` | Imp owner; Treasury type 1 / zero | No Treasury exists and Treasury availability is already 3: a needed room, not an unlock notification |
| 42 | `0x004E5A70` | Room owner; room type / zero | Capacity failure, including Prison capacity; other room-search producers also emit it |
| 47 | `0x004AE120` | Shot owner; shot tile x/y | Shot process 13; shipped `Shots.kwd` record 31 is Turncoat, process flags `0x4` (hit event) |

For 47, `0x004AD1B0` installs the callback at `0x0069E91C` and its process
number at `0x0069E92C`. The shipped record's name/id/process/flags are
**CONFIRMED**; calling this a general damage notification would be incorrect.
See `shots.md` for process-event dispatch. The full Turncoat effect is outside
this message audit.

For 17/18, Data1 is the anger wrapper's third argument, not its bucket index.
The keeper recomputes the current largest bucket when choosing a remedy;
see `computer-keeper-management.md` section 13.

For 41, `0x004C6E70` independently confirms the Treasury-count test in the
producer. Existing storage with no capacity instead yields 42; storage with
capacity but no route yields 43. Room availability and room need are separate
conditions. Additional independently checked branches follow; this is not yet
an exhaustive classification of computed message types.

| Producer | Room / messages | Discriminating gate |
|---|---|---|
| `0x004DED00` | Hatchery / 41, 42, 43 | Availability 3; no room gives 41, unreachable stocked room gives 43, otherwise an empty reachable room more than four seconds past `room+0x9F` gives 42. Pending build or sale suppresses all three. |
| `0x004E0470` | Lair / 41, 42, 43 | Availability 3; absent/full/unreachable branches. Pending build or sale suppresses only the absent-room 41 branch; 42/43 have no such gate. The full-room branch uses the same strict four-second comparison. |
| `0x004D8870` | Combat Pit / 41, 42, 43, 44 | Availability 3 and no pending build/sale for each message. Four-second timing applies to the unusable-place branches. Message 44 carries the room tag; the others carry zero. |
| `0x0046CF40`, `0x0046D3C0` | Graveyard / 41, 42 | Both require availability 3 for absent-room 41. Only `0x0046CF40` rechecks availability 3 on the failed-capacity 42 branch. |
| `0x004BA420` | Treasury / 41, 42 | Positive gold left after storage attempts: availability other than 1 permits a request; Treasury count zero selects 41, otherwise 42. This differs from the imp's state-3-only absent-room gate. |

The pending-work predicates `0x004C7340` and `0x004C7360` scan three room-type
bytes, stride `0x19`, at player `+0x1C2` and `+0xE97`. Their writers are the
build/sell queues `0x004C6EF0`/`0x004C70A0`; see `room-slab-chain.md` sections
2 and 4. Any occupied slot suppresses the gated requests, regardless of room type.
The route helper `0x00471740` obtains a room position and tests a creature route;
the producer calls above use options `0x80` for the first search and 2 for the
unreachable-room distinction. These are not merely room-count checks.

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
Training (`0x004F3560`) and Workshop (`0x004F6200`) finders also gate messages
on availability 3 and pending build/sale. After the no-room 41 branch, the
message precedence is unreachable 43, capacity 42, room-tagged 44. Capacity
failures wait strictly more than four seconds after `room+0x9F`; a zero
`room+0xA3` redirects that failure to 44. Workshop's existing-room failures
also require nonzero player byte `+0xA1C`. Torture substitutes type 99 for 41
when creature byte `+0x36F` is 4 and its owner equals the searched player.
The captive Pit finder `0x004D8510` only posts 42/43/44 when a Pit already exists;
it does not request a missing Pit. The shared capacity predicate is in
`state-category-hooks.md` §3.5; Library storage and Workshop crate gates are
in `library-and-research.md` §7 and `workshop-crates.md` §3.

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
`+0x59` nonzero; that field's meaning is outside this sender audit.
The foreign scan first prioritizes the keeper's imprisonment-mask bit, then
repeats without that bit test after the own-creature rescue scan. Other rescue
eligibility and task-reservation effects belong to the worker system.

The ownership transfer's argument order was checked in instructions:
`0x004E5658` reads the old owner; `0x004E58F4` sends 35 to it;
`0x004E58F9` compares it with the neutral tag; `0x004E5931` sends 32 or 33 to
the new owner. Neutral-room acquisition therefore does not take the keeper's
type-32 Portal/Lair branch.

## 3. Reaction quirk and implementation requirements

At `0x004FA668`, the type-42 branch calls the gold-untag helper `0x004FA7C0`
**before** checking whether the requested room is a Treasury. Every room-space
message attempts to remove the player's task-list-2 tags. If a refused untag
still targets gold, the helper reports failure. Only for Treasury does that
failure stop expansion; `0x004FA676` marks that message handled anyway.
For other rooms the normal expansion policy continues despite failure.

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

Scheduling and the preserved observation window are ADR-0245. The keeper consumes
1 and 6 (ADR-0278): `KeeperThreatResponder` walks the recipient's retained entries
in queue order, skips handled ones and acknowledges by command. No live original
message trace has been captured. Room/mood/subtype messages, their suppression
and remaining producers are unimplemented; unsupported kinds fail explicitly.
