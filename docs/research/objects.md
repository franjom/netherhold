# The OBJECTS record: beds, chickens, gold — and no corpses

`Objects.kwd`, record type **240**: **145 objects of 894 bytes**. Parsed
2026-09-04. This is the record four half-built mechanics were recorded as blocked
on, and parsing it **settled three of them and refuted the fourth**.

Evidence classes per ADR-0046 rule 4: **shipped data** (every number below),
**OpenKeeper** (the field order and the flag names, under ADR-0003's second
amendment), **the binary** (the object kind list, from `Object.cpp`'s debug
dump).

---

## 1. Only the last 82 bytes are gameplay

The first **812** bytes are nine art resources and a light — meshes, GUI icons,
in-hand art. None of it is a rule, so `KwdObjects` does not model an art resource
at all and starts reading at **0x32C**. That is why the parser is thirty lines
rather than three hundred, and why nothing about DK2's art pipeline had to be
understood before a creature could sleep in a bed.

| Offset | Field | Notes |
|---|---|---|
| 0x000 | name, 32 bytes | as in every other record type |
| 0x32C–0x33B | width, height, mass, speed | fixed point, denominator 4096 |
| 0x340 | material | unread |
| 0x344 | **flags**, u32 | the type discriminator — see §3 |
| 0x348 | **hp**, u16 | see §5 for the Dungeon Heart |
| 0x34E | mana value | non-zero on very few |
| 0x350 / 0x352 | tooltip / name string ids | presentation |
| 0x35A | **object id** | what creatures and rooms point at |
| 0x35B | start state | its own small state machine |
| 0x35C | room capacity | |
| 0x35D | pick-up priority | 176 on 122 records, 1 on 17, 0 on 4 |
| 0x35E–0x37D | sound category, 32 bytes | `OBJECT_BED`, `OBJECT_HEART`, … |

## 2. Why the layout is believed

The rule this project uses for a record without a template — the stride must
divide the content, a reading must be plausible for *every* record, and something
must be anchored outside the file. All four held, and three of them are
self-checking:

1. **The arithmetic closes on an independently measured number.** OpenKeeper
   names one unknown field `x34c` after its offset. Laying out the fields it
   lists after that one ends at **0x37E = 894** — the record stride, measured
   from the chunk header months earlier. A field list that lands exactly on an
   independently measured stride cannot contain a wrong width that another wrong
   width cancels.
2. **The id column is a permutation.** `ObjectId` at 0x35A reads **1 to 145,
   each exactly once**. A wrong stride would scramble it; a wrong offset would
   not produce a permutation.
3. **The sound category reads as text** at 0x35E — `OBJECT_BED`, `OBJECT_HEART`
   — which pins the *end* of the record independently of the start.
4. **Four other records point into this one and every pointer lands:**

| Pointer | Where from | Lands on |
|---|---|---|
| `LairObjectId` 22 | Goblin's creature record | **Goblin Creature Bed** |
| `LairObjectId` 26 | Vampire's creature record | **Vampire Creature Bed** |
| `ObjectIds` 31–34 | Graveyard's room record | **Tomb Stone 1–4** |
| `ObjectIds` 35 | Hatchery's room record | **Chicken Coop** |
| `ObjectIds` 14, 29 | Training Room's room record | **Training Dummy**, **Training Target** |

The Graveyard row is the one that closed Q113: its furniture is *tombstones*,
which is also the word `Object.cpp`'s union uses (`TombStone`), and it is not the
vampire's bed.

## 3. The type flags are the discriminator

There is no type *field*; seven flag bits do the job. The union of all flags
across the 145 records is **0x07F7F7FF**, so bits 11 and 19 are never set and
nothing above bit 26 is either — `ObjectFlags` names exactly the bits the data
uses, and a test asserts that so a bit nobody has seen set cannot be quietly
named.

| Type | Count | Which |
|---|---|---|
| **Lair** (a bed) | **12** | eleven evil beds plus one **Hero Creature Bed** |
| **Food** | 2 | **Chicken** (9), and oddly the **Torture Pendulum** (65) |
| **Gold** | 3 | Gold, Gold Bag, Gold Pile 1 |
| Spell book | 1 | what research produces |
| Crate | 18 | a manufactured trap or door waiting to be placed |
| Special | 15 | the level bonus boxes, named in full (Reveal Map, Mana Boost, …) |
| Level gem | 1 | Portal Gem |
| Decays | 5 | Chicken, Rat, Bat, Egg, Jack-In-The-Box |

**Only 52 of 145 carry any type flag.** The rest are pillars, arches,
candlesticks, front-end banners and per-room furniture — which is a reason for
rules to ask a flag rather than name an id wherever they can.

**Twelve beds for 46 creatures**, so beds are shared. Every creature with a
non-zero `LairObjectId` points at one of the twelve, and a test checks that link
for all of them rather than for the two we happened to know.

## 4. THE CORRECTION: a corpse is not an object

This project has written **"a corpse is an OBJECT (M6)"** as the named cause of
three gaps — no graveyard, no vampire, and a death that owes a body. It appears
in ADR-0051, in `creature-death.md`, and in several code comments.

**It is wrong.** All 145 objects are named and **none of them is a corpse.** The
nearest things are decoration — **Skeleton** (48), **Skull** (49) and **Bone**
(50) — and none carries a type flag at all; they are graveyard and
torture-chamber props.

So corpses were **never blocked on this record**, and everything else already
pointed that way:

- both timers are **creature** variables — `CreatureDyingStateDurationSeconds`
  and `DeadBodyDiesAfterSeconds`;
- `DecomposeValue` is a **creature** field;
- the binary's per-creature debug dump carries **`HasBeenKnockedOut`** and
  **`DieInstantly`**, and `Creature.cpp` owns the dying state.

**A corpse is not an object.** (Nor, it turned out on 2026-09-25, a creature in a
state: DK2 destroys the creature and makes a `CDeadBody` thing, `dying-states.md`. Ours keeps it as a creature with no state id.) Which means the graveyard is buildable
without any new record: it needs an imp task that carries a *creature* whose
dying window has run out, and a room that consumes it for `DecomposeValue`. That
is a much smaller job than "parse OBJECTS first" implied, and it was hiding
behind a claim nobody had checked — the fourth time that shape has cost this
project something.

## 5. Two things recorded rather than explained

**The Dungeon Heart's health reads 34464** (Q129), and 34464 is exactly
100000 − 65536: the signature of a five-digit number typed into a 16-bit field.
The same arithmetic appears in `KeeperSpells`, where Summon Horny's 100000 comes
back as 34464 read as a u16 — but **there the truncation was ours** and reading
32 bits fixed it. Here it is not: the four bytes at 0x348 read `0x000086A0`
either way, so the *file* carries the truncated value. A shipped data quirk, not
a parser bug, and ADR-0023 already deliberately disbelieves one shipped number —
but that correction was proved from behaviour first, and nobody has done that
here.

**`TypeFood` falls on the Torture Pendulum** (65) as well as the Chicken (Q128).
Either a data-entry accident or a broader meaning for the bit.

**UPDATED 2026-09-06: something does depend on it, and this paragraph used to say
nothing did.** The sentence here was *"the rule that will matter asks whether a
creature can eat the thing, and a pendulum is not on a hatchery tile"*. Half
right. The original has **two** food searches and they do not use the same
predicate (`eating.md` §6):

- the **hatchery** search matches the object **id 9**, so a pendulum in a
  hatchery is indeed not lunch — the prediction holds for the case it was
  thought about;
- the **prison** search matches `flags & 0x80`, which is this bit, so a pendulum
  standing in a prison *is* food to a prisoner.

Whether one can ever get there is a different question and the answer is
probably no: the pendulum's home room is the Torture Chamber. The lesson is not
about pendulums. **A prediction that "nothing depends on this" is a claim about
code nobody has read yet**, and the honest form of it names the search it was
checked against.

## 6. What this unblocks

| Mechanic | Was blocked on | Now |
|---|---|---|
| **Eating** | "chickens are objects" | **DONE** — the Chicken is object 9, `TypeFood`, decays outside a room; the Coop is 35 and the Egg is 47. `HatcheryRules`/`HatcherySystem` lay the eggs and `EatingRules`/`CreatureEatSystem` consume them (`eating.md`, `hatchery.md`) |
| **Gold piles** | "a pile is an object" | **DONE** (2026-09-05) — object 1 on the floor, object 3 in a treasury, `FloorGold`. And `MaxGoldPileOutsideTreasury` turned out not to be a limit at all (Q65) |
| **Beds as things** | "a bed is an object" | Twelve bed objects, and `LairRules` already tracks which tile holds whose |
| **Graveyard / vampires** | "a corpse is an object" | **Refuted** — see §4. A corpse is `CreatureActivity.Dead`; an imp carries it to a Graveyard (ADR-0063), whose per-room tally consumes the creature's `DecomposeValue` (Q165, ADR-0151) |

| **Putting things away** | nothing — it was never named as blocked, only as *"nothing to act on"* | **DONE** (2026-09-05, ADR-0065). DK2's imp task **kind 8** carries a loose object to the room that lists it, and the list it reads is `RoomDef.ObjectIds` — parsed since Q120 and read by nothing until now. Second in the Tier A table, above marked excavation and above claiming |

`WorldState` holds object instances (`WorldState.Objects`, `ObjectState`), the
Hatchery makes them — it knows how many coops a room gets, where they stand, and
since ADR-0066 it has its clock — and a creature eats them. What a graveyard
*does* with a corpse is the remaining gap: it keeps a per-room tally and the
thing counted is `DecomposeValue` (Q165, ADR-0151).

## 7. The instance: one type, an 18-byte union, and the id that picks it

Everything above is the *definition* — the 145 records in `Objects.kwd`. This
section is about the **instance**: the thing that sits on a tile. It comes from
`DKII.EXE` rather than from any file, read on 2026-09-04 to settle Q130, and it
is the reason that question had an answer instead of a coin flip.

The evidence is the game's own debug dump for an object, in `Object.cpp`. A dump
routine is unusually good evidence: it names every field, in order, and reads
each one at a fixed offset, so the struct falls out of it without any guessing
about types or padding.

### It is a union, and DK2 says so in as many words

The dump prints a literal header — **`Union Data:`** — and then one of
**twenty-three** blocks. Every one of those blocks reads from **offset 0x14A**,
and the field after the union sits at **0x15C**, so the union is exactly
**18 bytes** of per-kind state reinterpreted by whichever kind the object is.
The widest member is the Boulder, which fills 0x14A to 0x15B and therefore sets
that 18.

That answers the shape question directly: **DK2's object is one type with a
tagged union, not a family of types with a list each.** Whether ours *stores* it
as a union is a separate matter — internal layout is explicitly not a fidelity
requirement — but the *semantics* are settled: one list, one id space, one
object that may be any of these kinds, with at most one kind's state live.

### The tag is the object's own definition id

The discriminator is a **single byte at 0x12C**, and it is the object's
`ObjectId` — the same 1..145 column §2 validated. The dump branches on it with
**hardcoded literals**, which is worth stating plainly because §3 recommended
the opposite: it said rules should "ask a flag rather than name an id". The
original does both, and for the kinds below it names the id.

**Twenty-one of those literals were checked against our own parse of
`Objects.kwd`, and all twenty-one agree:**

| Branch | Id | The name our parse gives that id |
|---|---|---|
| `ResearchBook` | 4 | Spell Book |
| `TortureWheel` | 8 | Torture Wheel |
| `Chicken` | 9 | Chicken |
| `CallToArms` | 10 | Call To Arms |
| `Rat` | 11 | Rat |
| `TrainingTarget` | 29 | Training Target |
| `TombStone` | 31, 32, 33, 34 | Tomb Stone 1–4 |
| `Coop` | 35 | Chicken Coop |
| `BookCase` | 64 | Book Case |
| `GuardPost` | 70 | Guard Post |
| `Boulder` | 74 | Boulder |
| `Destructor` | 98 | Destructor |
| `ManaVault` | 101 | Mana Vault |
| `ThreatBurn` | 103 | Threat Burn |
| `ClaimedManaVault` | 108 | Claimed Mana Vault |
| `FECandleStick` | 110 | 3D Front End Candle Stick |
| `TempleCandleStick` | 111 | Temple Candlestick |
| `HeroGate2By2` | 114 | Hero Gate [ 2 By 2 ] |

**This is the strongest check the OBJECTS layout has had**, and it comes from a
direction none of §2's four checks could reach. Those were all internal — a
field list that sums to the stride, an id column that is a permutation, a string
that lands, pointers from other records. Every one of them says "these bytes are
self-consistent". This one says the bytes mean what we think they mean: the
*code* has eighteen names for twenty-one numbers, and the *file* gives those
twenty-one numbers the same names, and the two were compiled and shipped by
people who never had to agree about it afterwards.

### Five type flags confirmed from the other side

The rest of the dispatch does *not* use the id — it tests bits of a flags word
on the loaded definition, and those bits are the ones §3 took on OpenKeeper's
authority. The binary picks:

| Branch | Bit tested | `ObjectFlags` name |
|---|---|---|
| `Gold` | 0x40 | `TypeGold` |
| `Special` | 0x04 | `TypeSpecial` |
| `Lair` | 0x20 | `TypeLair` |
| `ManufactureCrate` | 0x10 | `TypeCrate` |
| `ResearchBook`'s id-4 object | 0x08 | `TypeSpellBook` |

So five of the seven type bits are now confirmed **by the code that acts on
them**, not just by a name in another project and a plausible-looking column.
`TypeFood` (0x80) and `TypeLevelGem` (0x1000) are not tested in this routine and
remain on the earlier evidence.

Note the precedence, because it is behaviour: **flags beat ids.** `Gold` is
tested before any id, and `Lair` before the remaining ids — so a *bed* is a bed
because of its flag, not because of which of the twelve it is. That is exactly
what `ObjectDef.IsLair` does, and it is why a rule may ask `IsLair` and be right
about all twelve.

### The fields around the union

Offsets are from the start of the object, and everything below 0x124 belongs to
a base class this read did not need.

| Offset | Field | What it says |
|---|---|---|
| 0x124 | `RoomNode` (x, y) | the object knows its room's node |
| 0x12C | `TypeId` | **the tag** — the definition id |
| ~0x12D | pointer to the loaded definition | its flags word is at +0xF4 |
| 0x133 | `AutoPlaced` | placed and owned by a room's layout rather than by play |
| 0x138 | `RoomId` | the room it belongs to |
| 0x13A | `StateId`, `StateAge` | its own small state machine, and how long in it |
| 0x142 | `WhoGetsThisFromADrop` | recipient creature of a keeper's drop |
| 0x146 | bit 0 `EnableDeathEffect`, 1 `BeingDragged`, 2 `BeingCarried`, 3 `Possessed`, 4 `InHand`, 5 `InUse` | six flags in one word |
| 0x14A | **the union** | 18 bytes, per kind |
| 0x15C | `UtilityNewState` | |
| 0x160 | `MoveTo`: `EndX`, `EndY`, `dX`, `dY`, `GameTicksToDestination`, `Turning`, `Interruptable` | **an object moves**, on the same kind of interpolated walk a creature does |

Two of those are worth pulling out. **`BeingCarried` and `InHand` are on the
object**, which is how an imp hauling a chicken and a keeper holding one in the
hand are the same object in two states rather than two mechanics. And **the
`MoveTo` block means an object walks** — a chicken, a rat, a boulder — with
`GameTicksToDestination` counted in the same ticks a creature uses.

### `Lair.CreatureId` — and the clash it settles

The `Lair` member of the union is two fields:

- **`CreatureId`**, a u16 at 0x14A — *whose bed this is*;
- `FlagDestroyWhenDropped`, at 0x14C.

**This settles the ownership question Q130 opened.** `LairRules` had put the
relation on the creature ("which bed is mine"); DK2 puts it on the object
("whose bed am I"). Same relation, opposite end, and two copies of one fact is
the shape that has already cost this project three multi-month defects. **The
object's end won**: `ObjectState.CreatureId` is the only copy, `LairRules`
derives the rest, and there is no `CreatureState.LairTileIndex`. Three reasons
beyond fidelity:

1. **A bed is a thing that can be destroyed.** `FlagDestroyWhenDropped` sitting
   right next to `CreatureId` says so. A creature outliving its bed is real —
   sell the lair — and representable either way; a *bed* outliving its creature
   is a leak the creature-side model cannot even express, because there is
   nothing to leak.
2. **The bed is the creature's own.** Twelve bed objects serve 46 creatures and
   each creature's `LairObjectId` names which of the twelve is its. So the thing
   on the tile is not "a bed", it is "a Goblin's bed belonging to Goblin 7" —
   which is one object carrying one creature id, and not a property of the tile.
3. **The presentation layer needs the object anyway** to draw the right bed.

**CONFIRMED by the owner** (2026-09-05): the bed object *is* created when a
creature claims a tile, rather than existing with the room. *"Yes a lair is bare,
green lair texture on the floor tile, only when a creature enters a lair and
claims a tile by dropping its bed does that tile get furniture on top of it."*
The three facts above had all pointed that way — a per-creature bed type cannot
be placed before the creature is known — and this is the observation that settles
it. Q131, closed.

One extra detail worth keeping from the wording: the creature **drops** its bed,
so the placement is an act the creature performs on arrival. That is presentation
rather than simulation as far as anything here is concerned, but it is also why
`FlagDestroyWhenDropped` sits next to `CreatureId` in the object's union.
