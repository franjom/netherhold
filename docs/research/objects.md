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

**A corpse is a creature in a state.** Which means the graveyard is buildable
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
Either a data-entry accident or a broader meaning for the bit. Two members is too
small a set to argue from, and nothing depends on it: the rule that will matter
asks whether a creature can eat the thing, and a pendulum is not on a hatchery
tile.

## 6. What this unblocks

| Mechanic | Was blocked on | Now |
|---|---|---|
| **Eating** | "chickens are objects" | The Chicken is object 9, `TypeFood`, decays outside a room; the Coop is 35 and the Egg is 47. Needs object *instances* in the world and a Hatchery |
| **Gold piles** | "a pile is an object" | Three gold objects, and `MaxGoldPileOutsideTreasury` already exists (Q65) |
| **Beds as things** | "a bed is an object" | Twelve bed objects, and `LairRules` already tracks which tile holds whose |
| **Graveyard / vampires** | "a corpse is an object" | **Refuted** — see §4. It needs a creature-carrying task, not this record |

The next step is the same for the first three: **object instances in the world**.
`WorldState` has tiles and creatures and no third list, and every one of those
mechanics needs one.
