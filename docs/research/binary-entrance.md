# What the binary says about the entrance

**Evidence class: the executable.** Everything here was read out of `DKII.EXE`
with Ghidra on 2026-09-04, under **ADR-0046** — facts may cross into this
document, decompiler output may not, and a fact from the binary ranks **below**
an owner observation of the game running.

Tagged per claim. Where my reading of the decompiler is shaky I say so rather
than rounding it up, and one section below is **wrong somewhere on purpose** —
it records a contradiction I could not resolve, because a half-read control flow
that reads as authoritative is the most dangerous thing this project could
produce.

| | |
|---|---|
| Binary | `DKII.EXE`, 2,987,568 bytes, GOG v1.70 |
| Ghidra | 11.0.1, JDK 26, auto-analysis 107 seconds. `application.java.max` is empty, so 26 is accepted despite 21 being the documented ceiling |
| Route in | The retail build kept its `__FILE__` assert strings, so `Entrance.cpp` names its own translation unit (see `executable-strings.md`) |
| The unit | 20 functions in `[0x004db000, 0x004de000)`, one of which asserts from `Entrance.cpp` |

**EA's own build path, for the record:**
`D:\Dev\DK2\Projects\Source\Game\Entrance.cpp`.

---

## 1. A creature's own flags decide whether a portal may produce it — CONFIRMED (binary), and we had it wrong

**This is the finding that changed code.** Before any question of levels, pools
or rooms, the candidate builder rejects a creature type unless its flag word
passes two tests:

- it **must** carry bit 10 (`0x400`) — the flag the community already named
  `AvailableViaPortal`, which turns out to mean exactly what it says;
- it **must not** carry bit 27 (`0x8000000`) — `IsUnique`;
- and a **hero** portal (room type 25) tests a *different* bit in a *second*
  flag word instead, which this project does not parse.

The room type is read off the room object and compared against 3 (Portal) and
25 (Hero Portal), so one piece of code serves both entrances and the creature
flag decides which one it may come out of.

### Why this mattered

`PortalRules` had four gates — availability, pool, attraction, capacity — and
**none of them is this one**. The shipped campaign contains the case:

> On **Frosty's Castle**, **Frosty's Lair** and **Frosty's Realm**, every keeper
> from player 3 to player 6 has **nine hero creatures marked AVAILABLE _and_
> listed in its creature pool**: Knight, Giant, Guard, Royal Guard, Monk,
> Wizard, Thief, Fairy, Elven Archer. Not one of them carries
> `AvailableViaPortal`.

The level gate passes and the pool gate passes, so before this fix **a keeper's
portal on those levels would have produced Knights and Fairies.** DK2 will not
do that. Those creatures are obtainable there by other means; the entrance is
simply not one of them — the same reason **Vampire** and **Skeleton** lack the
flag, being the graveyard's and the prison's business (`Crypt.cpp`,
`prison.cpp`), and **Horny** lacks it, being a temple sacrifice.

ADR-0047. Now `PortalRules.CanComeFromPortal`, tested both ways in
`PortalTests` and asserted as an exact nine-name set across all 66 levels in
`PortalOnRealDataTests`.

### The IsUnique half never fires on shipped data

No campaign level makes a unique creature available or pools one — checked
across all 66. So that half is **unfalsifiable from the files** and rests on the
binary alone. It is implemented anyway, because **all thirteen unique creatures
do carry `AvailableViaPortal`** — which is precisely why DK2 needs a second
test. Without it, a level that pooled a Unique Dark Angel would get one out of a
portal. Both premises are now checked tests rather than remarks.

---

## 2. The capacity formula — CONFIRMED (binary), with two divergences

```text
portals = countRoomsOfType(player, 3)     // Portal
gates   = countRoomsOfType(player, 25)    // Hero Portal
capacity = (portals + gates == 0)
         ? 0
         : (portals + gates - 1) * perAdditional + first
```

`first` and `perAdditional` are the low two bytes of a single global, which fits
the balance table being loaded into a packed struct rather than kept as
`VARIABLES` records — and fits the strings pass finding **no balance variable
names in the binary at all**.

**What this confirms:** the formula
`CreaturesSupportedByFirstPortal + (n-1) * CreaturesSupportedPerAdditionalPortal`
is right, and the **zero-entrance special case is explicit in DK2 too**. It has
to be, because `(0-1) * 5 + 15` is 10 rather than 0 —
`PortalRules.CreatureCapacity` already returns early on no portals for exactly
that reason, and now that is corroborated rather than merely tidy.

**Two divergences were recorded here and both are now closed** (Q98). The
implementation reproduces the routine: `PortalSystem` increments its portal
count *before* the `CanProduce` test and counts both room types, so a broken
portal and a claimed Hero Portal each raise the cap exactly as DK2's does. The
quirk is stated on `PortalRules.CreatureCapacity`.

The room-count helper is a plain walk of the player's room list comparing a
room-type byte, with **no health or functionality test** — that part is
unambiguous.

---

## 3. The creature count behind the cap is flag-filtered, not imp-compared — STRONGLY INFERRED

The count the cap is compared against walks the player's creature list and
includes an entry only if a **bit in the creature instance is set**. It does not
compare against the imp's type id.

That supports the choice `PortalRules.CreatureCount` already made — filter on
`CreatureFlags.IsWorker` rather than on "is it the imp" — over OpenKeeper's,
which removes the imp *type* by identity. The two differ only for a keeper
holding a Dwarf. **Which bit it is remains UNKNOWN:** the offset is in the
creature *instance*, not the definition, so it is not one of the flags this
project parses.

---

## 4. The pick is weighted, not uniform — HYPOTHESIS, and my reading contradicts itself

`PortalSystem.Pick` chooses uniformly at random among eligible types, and its
own doc comment admits this is a placeholder for something the data says exists:
`DecreaseEntrancePersentageOfLastSackedCreatureType` and
`IncreaseEntrancePersentageOfNewCreatureTypes`, both 33 (Q90).

The binary does carry a weighting. The candidate builder fills a **table of nine
bytes per creature**: a type id, and a value in **12-bit fixed point**
(`0x1000` = 1.0 — the same scale as `Fp32`). The value is built up from the
keeper's **attracting rooms**, then **decremented once per creature of that type
already held**, and a type is a candidate only while its value is **> 0**.

The per-room contribution comes from a helper that is **special-cased by room
type**, which matches the strings pass finding sixteen per-room source files
rather than one parameterised room:

| Room | Contributes | status |
|---|---|---|
| General furniture rooms | the room's **`TotalCapacity`**, `room+0x67`: matching auto furniture, with a conditional +1 for Library, Training, Workshop and Casino but not Torture | **CONFIRMED** |
| Temple (13) | **2** if it holds **object 66, the Temple Hand**, else 0 | **CONFIRMED** |
| Prison (11), Combat Pit (16) | the room's **slab count**: it walks the slab chain from `room+0x2D`/`+0x2E` and counts each slab `FUN_00454110` accepts, and **both types land on that function's arm 9, which returns 1 unconditionally** | **STRONGLY INFERRED**, one guard unread |
| Lair (2) | **flat 1.0**, or **3.0** when a byte at `+0x771` on the creature record is **22**; the room walk is skipped entirely | **CONFIRMED**, the 22 unexplained |

**The Prison and Combat Pit arm, read 2026-09-14.** `FUN_004DCE70` dispatches on
the room's type byte `+0x18`: Prison and Combat Pit take the branch at
`0x004DCEDD`, which walks the room's slab chain (next at `slab+0x04`) and
increments a counter for every slab `FUN_00454110` returns non-zero for.

That function is the same predicate the room-placement scan uses as a
**rejection** test. It guards on the slab belonging to a room
(`word[slab+0x02] & 0xFFF`) and on **`byte[slab+0x0C] == 0xFF`**, then
dispatches on room type through a 20-entry table at `0x0045421C`. **Prison (11)
and Combat Pit (16) both index arm 9**, and arm 9 is `0x004541E2` —
`MOV EAX,1; RET`, true for every slab.

**So the contribution is the room's size in slabs**, not its occupants as this
page guessed while the row read "shape only". What stops it being CONFIRMED is
the `+0x0C == 0xFF` guard: that field is undocumented, so how often it excludes
a slab is unknown. Reading one prison's slabs live would settle it, and
`runtime-map.md` already has the addressing to do that.

### Corroborated by the owner, 2026-09-11

**A 3x5 Temple never produced a Dark Angel; taken to 5x5 it did**, with the
pool otherwise unchanged (creatures were being sacked back through the portal
throughout). The Dark Angel's attraction record is **room 13, size 0** — *no*
size requirement — so a `(roomId, minSize)` boolean, which is what
`PortalRules.IsAttracted` implements, predicts she arrives at 3x5. She does not.

The table above predicts it exactly: the Temple contributes **2 if it holds a
particular object and 0 otherwise**, and a type is a candidate only while its
value is **> 0**. This is the first evidence *for* the weighted model from
outside the binary, and it answers Q89. It also lines up with Q270 — room
capacity counts **auto-placed furniture, not tiles**.

**The object is the Temple Hand, object 66** (owner, same day: the hand in the
centre pool of a 5x5 Temple). The shipped table names it exactly that and gives
it `Obstacle`. Note the Temple's room record carries **no `ObjectIds` at all**,
unlike the ten rooms that do, so the hand is placed by Temple-specific code —
which is what "special-cased by room type" above predicts.

**Open, and the owner's question:** the contribution is a *weight*, so whether
2 means **at most two Dark Angels** depends on the decrement reading that the
next section says contradicts itself. Plausible, not established — and
settleable by keeping a 5x5 Temple, sacking nobody, and seeing whether a third
ever arrives.

### The contradiction, and how the owner resolved it

**Stated first.** Read literally, a Lair attraction contributes a **flat 1.0**
and each goblin held subtracts 1.0. That would cap level 1 at **one** creature
— flatly false; level 1 gives fifteen. The reading was wrong somewhere and
the 2026-09-07 sitting stopped without finding where.

**The Temple's 2 is a cap of two, and it was predicted before it was seen.**
The number was read off the binary on 2026-09-07. On **2026-09-11** the owner
built a 5x5 Temple, took a first Dark Angel, a second **1 m 44 s** later, and
then nothing for three and a half minutes. Two, as written. A prediction met,
not a curve fitted.

**The decrement-per-creature-held reading is right.** Both offsets it turns on
are confirmed by our own files, not by this reading: `creature+0x36F` is the
creature's type id and `object+0x12C` the object's, both from ADR-0115.

**The first attempt at a resolution, that lair tiles are the budget, was wrong
and stays withdrawn.** `0x004DC153` tests the attraction's room id for **2**
and on a Lair stores a **flat 1.0** (3.0 in one branch).

**RESOLVED, and it is one instruction.** The Lair branch ends
`JMP 0x004DC223` — which lands **past** the draw-down loop at
`0x004DC1F5`–`0x004DC221`. A Lair-attracted type is therefore given 1.0 and
**never decremented**: owning a Lair is a plain yes, for ever. The draw-down
applies to the room-walk path only.

So both of the owner's observations fall out of one mechanism. **Goblins are
Lair-attracted (creature 7, room 2) and uncapped**, which is why level 1's
fifteen were never in conflict with anything; **Dark Angels are Temple-attracted
and walk the room path**, so 2 minus one per angel held stops at two. The
contradiction was never in the mechanism — it was a branch this document had
not followed to its jump.

**One caveat, recorded rather than waved away.** A portal's interval grows as
`n² + EntranceGenerationSpeedSeconds` (ADR-0086), so a silence can also be a
stretched interval. What would remove the doubt is whether **other** creature
types kept arriving across those three and a half minutes: if they did, the
portal was firing and declining to pick an angel, which is the cap.

Still unimplemented, and now worth implementing. What it means:

> **It is a second, independent explanation for the owner's observation that
> arrivals slow down** — not an interval that stretches, but eligibility
> draining as the dungeon fills, room *capacity* on one side and creatures held
> on the other. That would also match a well-known DK2 behaviour: a keeper needs
> more lair space to attract more creatures.

Q86's slowdown keeps two candidate mechanisms, one implemented and one not.
See Q97.

### 4.1 The builder, read whole — and why `IsAttracted` was not rewritten

`Entrance.cpp`, `0x004DC0F0`-ish. The candidate table is at **`0x00746EC0`**,
**9 bytes** an entry, indexed by creature type: `u8 typeId`, `i32 value`,
`i32 copy`. `value` is **12-bit fixed point** (`0x1000` = 1.0).

1. Zero both dwords.
2. **Build up.** Lair takes the flat path above. Otherwise walk the keeper's
   room list (`player+0x36`, entries through `0x006EE540`, next at `room+0x8`
   — the list `player-object.md` already documents) and for every room whose
   type byte `+0x18` matches the attraction's, add its contribution **shifted
   left 12** into fixed point.
3. **Draw down.** Walk the keeper's thing list (`player+0x6`, next at
   `thing+0x10`) and subtract **1.0** for each whose `+0x36F` equals this
   type, but only while the value is still positive.
4. A type is a candidate only while `value > 0`. Past that the arm computes
   against `0xA000` (10.0) and `0xC8000` (200.0), which is unread.

**The gates ahead of the budget**, same loop, in order: an availability bitmap
at `player+0x1109`; creature flag `def+0x686 & 0x8000000` excludes outright;
flag bit 10 against a spawner of room type **3 (Portal)** or `def+0x68a & 8`
against type **25**; `FUN_004DC810` on the player, unread; and then a
**population cap**.

**That cap is Q87's capacity, found in the code.** It counts the keeper's
things carrying `+0x3E6 & 2` and compares against
`low + (portals - 1) × high` read from the two bytes of a loader-filled
global at `0x006F28B4` — the shape of
`CreaturesSupportedByFirstPortal` **15** and
`CreaturesSupportedPerAdditionalPortal` **5**, which the owner confirmed with
a stopwatch on 2026-09-07. The global is uninitialised in the file, so the
values are the variables' and only the formula is read here. **STRONGLY
INFERRED**, and it is a separate gate from attraction: the fifteen that
bounds level 1 is this one, not the budget.

**Built 2026-09-14 as ADR-0147**, once ADR-0146 supplied the furniture the
general contribution needs. `AttractionBudget.Admits` is the fifth portal gate:
first slot only, contributions summed, admitted while the sum exceeds the count
held, and the Lair exempt. Goblins are unchanged, which is why nothing moved.

---

## 5. Where the interval is not

**There is no halving, no multiply-by-count and no floating-point arithmetic
anywhere in the 20 functions of `Entrance.cpp`.** The spawn clock is not in this
translation unit. That is a durable negative result: it rules the interval out of
`Entrance.cpp` and rules out any formula of the `base × creatures ÷ 2` shape
being computed here.

The interval itself was settled afterwards, by stopwatch rather than by
disassembly: it is `n² + base` game seconds, an addition, not a multiplication
(ADR-0086, `portals-interval.md`). The candidate builder in this unit is reached
through a **vtable**, so it has no direct callers, which is why the walk up the
call graph closed here.

---

## How this pass was run, so it can be repeated

Ghidra project `D:\ghidra projects\DK2_RE` (outside the repo — a Ghidra project
of this binary contains the binary). Scripts are throwaway and live in the
session scratchpad; each takes arguments and writes to
`reverse-engineering/`, which is gitignored:

| Script | Does |
|---|---|
| `DumpNearString.py <needle> <out>` | Finds defined strings containing a needle, decompiles every function referencing one. **This is the way in** — assert strings are the only names EA left behind |
| `DumpRange.py <startHex> <endHex> <out>` | Decompiles every function in an address range. A C++ translation unit is emitted contiguously, so one assert plus a range gives the whole unit |
| `Callers.py <addrHex> <out>` | Decompiles everything calling a function. Returns nothing for a vtable-dispatched method, which is itself informative |

Headless, with `JAVA_HOME` pointed at the JDK for the invocation rather than set
on the machine. This pass ran on Ghidra 11.0.1 with Jython. Since 2026-09-25
the setup is 12.1.4 with PyGhidra, projects under `D:\Ghidra\`. The current
command and porting notes are in `docs/RESEARCH-QUEUE.md`:

```text
pyghidraRun.bat -H "D:\Ghidra\ghidra projects claude" DK2_RE -process DKII.EXE -noanalysis \
  -scriptPath <scratchpad> -postScript DumpRange.py 004db000 004de000 <out>
```
