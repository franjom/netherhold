# The research tree

Milestone M6. What a keeper's banked research points buy. The accrual that banks them is
[library-and-research.md](library-and-research.md).

**Implementation:** `DK2.Simulation.ResearchTreeRules`, advanced from the research
credit in `Systems.CreatureJobSystem` and chosen each turn by `Systems.ResearchSystem`;
Create Imp's level in `Systems.ImpSupplySystem` (ADR-0247).
**Tests:** `ResearchTreeTests` (`DK2.Simulation.Tests`) and
`ResearchTreeOnRealDataTests` (`DK2.Behavior.Tests`).
**Questions:** Q447.

Read from DKII.EXE on **2026-09-28**, after the owner reported from play that
spells must be researched before they can be cast, that research then upgrades
them, and that an upgraded Create Imp makes level-4 imps. The binary agrees on
all three points and adds the parts play cannot show. Every claim in this document
is **STRONGLY INFERRED** from the instructions unless it says otherwise.

## 1. The list: Variables id 17

The Variables dispatcher (`0x0051AA90` fills a `{id, handler}` table at
`0x006A5CD0`) sends id 1 to the creature pool, id 2 to availability and **id 17
to `0x0051DB00`**. That handler appends the record's three payload words
`{kind, typeId, cost}` to a per-level list at world `+0x72EC`. Id 17 was one of
the unnamed ids in [kwd-variables.md](kwd-variables.md). The list is sized at
level start (`0x00506D60`) to one slot per room, door, trap and spell type, and
its count starts at zero. Entries are addressed **1-based**, and index 0 means no
target.

The shipped data has **29 entries, all in `GlobalVariables.kwd`, and every one
costs 1000**. None of the 66 level Variables files carries an id-17 record
(counted 2026-09-28), so every level researches the same list in the same order:

| Order | Kind | What | Type ids, in list order |
|---|---|---|---|
| 1–12 | 4 | keeper spells | 4, 6, 2, 10, 1, 3, 8, 7, 5, 13, 9, 11 |
| 13–18 | 2 | doors | 1, 3, 6, 2, 4, 5 |
| 19–29 | 3 | traps | 3, 1, 11, 8, 7, 4, 10, 2, 9, 6, 5 |

Spell 4 is Create Imp, 6 Thunderbolt, 2 Possession and 10 Heal. No shipped entry
has kind 1 (room), so **rooms are never researched**. A level unlocks them
directly. Spell 12 has no record and 14 (OneShotReaper) is not on the list.

**Research kinds are not availability kinds.** The list uses 1 room, 2 door, 3
trap, 4 spell, which are the four player tables (`RoomInfo +0x56`, `DoorInfo
+0x5E`, `TrapInfo +0x66`, spells `+0x6E`). The Variables availability records
use 1 room, 3 trap, 4 door, 5 spell (kwd-variables.md).

**The spell records' own research times are not what research costs.**
KeeperSpells.kwd carries a research time at `0xD8` (500 for every spell but
OneShotReaper's 1000) and a bonus research time at `0x111` (1500). The loader
`0x004F7CE0` converts both to ticks at runtime `+0x5E` and `+0x60`. No reader
was found for either in a spell context. Research completes against the list's
1000 instead (§3). A scan proves nothing outside its range: this one covered
the functions that fetch a spell through the world's `+0x110` getter.

## 2. Choosing the target: `0x004C3000`

Two passes over the list, in list order:

1. The first entry whose item is **RESEARCHABLE (2)** in the keeper's table for
   its kind.
2. Failing that, the first **spell** entry that is AVAILABLE (3), **not yet
   upgraded**, and whose spell record has a **bonus shot** (runtime `+0x4B`, file
   `0x113`, non-zero). This is the upgrade phase. Doors and traps are never
   upgraded by research.

If neither pass finds one, the target becomes 0, and the keeper is sent message
**0x18** when a target had been set before. The chooser runs:

- from the per-turn player update (`0x004BB2A0`, at `0x004BB62C`) **whenever the
  target is 0**, so an item that becomes researchable later (a lost book, a
  trigger) is picked up on the next update;
- from the adder, when the current target stops qualifying (below);
- from the adder, after a completion.

Summon Horny (5) and OneShotReaper (14) have no bonus shot, so they are never
upgraded. That matches the owner's report that Summon Horny cannot be upgraded.
Every other shipped spell can be.

## 3. Paying for it: the adder `0x004C2DC0`, after the credit

The credit to `player+0x20F` (§1) always comes first. Then:

1. If the target is 0, stop. Points keep banking; the per-turn update will choose.
2. If the target no longer qualifies (`0x004C30F0`: its item is not state 2), keep
   it only when it is a spell that is AVAILABLE, not upgraded and has a bonus shot
   (an upgrade target). Otherwise choose again.
3. If the points reach the entry's cost (`0x00507040`: `cost <= points`), pick a
   Library (§4). If one takes the result, add 1 to `player+0x17B`, **reset the
   points to 0** and choose the next target. If none takes it, nothing is spent,
   and the next credit tries again.

Progress for the interface is `points × 100 / cost`, capped at 100 (`0x00507060`).

## 4. Delivering it: a book on a Library shelf

`0x004C6CF0` counts the keeper's rooms of type **6 (Library)** that accept the
book and are not locked (`room+0x0A` zero). It then takes a **random one**
(`0x005558D0` draws `random(count)`) and hands it the result (`0x004E0870`,
this = the room):

- **No storage position** (`0x004E1AC0`, §7) means refused. After a timer, and
  only if the keeper has no other room that could take it, message **0x19** is
  sent.
- **An upgrade** (the item is a spell already AVAILABLE and not upgraded) sets
  the spell's `Upgraded` flag (`0x004BB070`) and sends message **0x17**. **No
  book is made.** The room, door and trap grants (`0x004E0D70`, `0x004E0BE0`,
  `0x004E0A50`) have the same upgrade branch for their own tables, but the chooser
  never makes a door or trap an upgrade target and no room is on the list.
- **Otherwise a book is made**: object type **4** (the Spell Book, objects.md),
  created for the neutral player at the storage position, with its union set to
  `+0x14A` = the research kind and `+0x14E` = the type id. The room then takes it
  (`0x004E12A0`): the book becomes the room owner's, and the keeper's table sets
  that item **AVAILABLE (3)**. A spell also sends message **0x16**.

**The book on the shelf is the availability.** When a book leaves the keeper's
possession (`0x004E1590`), the game looks for another book of the same kind and
type among the keeper's objects. If there is none, the item goes back to
**RESEARCHABLE (2)**, and the chooser will research it again. This is the
mechanism behind the manual's *"researched spells are lost with the Library"*.
`Upgraded` is not touched when a book is lost, so a re-researched spell comes
back already upgraded. That consequence is read from the code, not observed.

## 5. What an upgrade changes

The keeper's spell table is **17 bytes** per entry, not 9: `TypeId@0`,
`Availability@1`, `TimeLastCast@5`, a second cast time `@9`, and **`Upgraded@0xD`**.
The room, door and trap tables are 9 bytes. The cast stamps `@5` and `@9` with the
current tick. [player-object.md](player-object.md) reached the same layout
independently the same day, from the lookup, the cooldown reader and the cast
writer. This reading adds the flag's own reader `0x004BB0D0` and writer
`0x004BB070`.

Every cast path (`0x004BBD30`, `0x004BBF90`, `0x004BC290`) reads `Upgraded` and,
when it is set, fires the **bonus shot with the bonus data** instead of the normal
ones:

| Runtime | File | Field |
|---|---|---|
| `+0x42` / `+0x43` / `+0x47` | `0xF0` / `0xD0` / `0xD4` | shot type, data 1, data 2 |
| `+0x4B` / `+0x4C` / `+0x50` | `0x113` / `0x114` / `0x118` | bonus shot type, data 1, data 2 |
| `+0x58` | `0x11C` | mana cost |
| `+0x5C` | `0xE2` | mana upkeep per second |
| `+0x75` | `0xEE` | keeper spell id |

The runtime record is 122 bytes (`0x7A`), stored at world `+0x1604` and filled by
`0x004F7CE0` from the file record (the source sits at file offset + 0x78).

**Create Imp** fires shot 2 with data `(1, 1)`, or upgraded shot 60 with data
`(1, 4)`. Data 1 is the creature type (1, the Imp) and data 2 its level, so the
**upgraded imp is level 4**, as the owner saw. The level comes from the record's
bonus data, not from a constant in the code. Data 1 is the one that the cost
formula reads as the creature type. The shot process itself was not read, so
"data 2 is the level" rests on the data and the owner's observation.

**Free imps are upgraded too.** The per-turn free imp ([imps-and-mana.md](imps-and-mana.md)) calls
the same `0x004BBD30` with spell 4 and a free argument, which skips the mana
charge and the cast check. The upgrade branch comes after both, so once Create
Imp is upgraded, free imps arrive at level 4. This consequence is not observed.

Other upgrade effects live in code, spell by spell. Mana upkeep for Possession
(2) and Call To Arms (3) is scaled by `UpgradedPossessionCostPercentage` and
`UpgradedCallToArmsCostPercentage` (70) when upgraded (`0x004C10E0`,
`0x004C17A0`). The interface shows an upgraded icon (`0x00412FC0`, +0x1B) and a
grade in the tooltip (`0x0042EA70`).

## 6. What the cast check does not check

The paid-cast check `0x004BB8A0` tests mana, the recharge window (`TimeLastCast`
against the record's recharge at `+0x3E`), and two special cases:

- **Summon Horny (5) is refused while the keeper owns a creature with flag
  `0x4000`**, that is, while a Horny is already out.
- **OneShotReaper (14)** is refused once `player+0x76` bit `0x1000` is set, which
  its cast sets.

It then asks two position validators (`0x005176B0`, `0x00517730`), which were
not read. **It does not test availability.** Casting an unresearched spell is
prevented by the interface, which shows only available spells, and by the
computer keeper, which checks for state 3 itself. How many times Horny may be
summoned per campaign is **Q447**.
