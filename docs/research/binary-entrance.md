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

| Room | Contributes |
|---|---|
| Most rooms | a 16-bit field on the room object, most plausibly its tile count |
| Prison, Combat Pit | a count of something *inside* it — occupants, presumably |
| Temple | 2 if it holds a particular object, otherwise 0 |

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

### The contradiction, stated rather than hidden

Read literally, a Lair attraction contributes a **flat 1.0** and each goblin
held subtracts 1.0. That would make the second goblin ineligible and cap level 1
at **one** creature — which is flatly false; level 1 gives fifteen. **So the
reading is wrong somewhere**, and I did not find where before stopping.

Nothing has been implemented from this section. It is here for what it would
mean if the shape is right:

> **It is a second, independent explanation for the owner's observation that
> arrivals slow down** — not an interval that stretches, but eligibility
> draining as the dungeon fills, room *capacity* on one side and creatures held
> on the other. That would also match a well-known DK2 behaviour: a keeper needs
> more lair space to attract more creatures.

Which makes Q86 sharper rather than answered. The owner has confirmed the
*symptom*; there are now **two candidate mechanisms** for it, one implemented
and one not. See Q97.

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
on the machine:

```text
analyzeHeadless.bat "D:\ghidra projects" DK2_RE -process DKII.EXE -noanalysis \
  -scriptPath <scratchpad> -postScript DumpRange.py 004db000 004de000 <out>
```
