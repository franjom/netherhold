# Mechanic: CREATURES(170) record — the creature definition table

Source(s):
- ufdada/dk2-tools `Formats/Level/creatures_struct.bt` (field offsets)
- DiaLight/Flame `src/dk2/entities/entities_type.h` (creature id list, derived
  independently by disassembling DKII.EXE v1.70)
- Direct parsing of the reference install's `Data/editor/Creatures.kwd`

Original version: DK2 v1.70 (GOG)

## Evidence

- 46 records, 250,654 bytes ÷ 46 = **5,449 bytes per record** exactly.
  `KwdCreatures` also accepts a 5,537-byte variant (88 extra trailing bytes) for
  creature data embedded in a globals file; no shipped file exercises it, and DK2
  never reads the tail (§ The binary's reader, Q14).
- Creature ids are 1..46 contiguous, and the id→name mapping **matches Flame's
  independently reverse-engineered list** (Imp=1, BileDemon=3, Warlock=5,
  Vampire=8, Troll=10, Horny=27, …). Two unrelated methods agreeing is the
  strongest confirmation available short of running the game.
- Attraction room ids match Flame's `room_type.h`: Warlock→6 (Library),
  Troll→10 (Workshop). Third independent agreement.
- **Happy-job chance percentages sum to exactly 100 for every creature that has
  a populated table** — a structural invariant that would break immediately if
  the JobPreference offsets were wrong. Asserted in tests.
- Spot values are unmistakably DK2: Vampire melee damage 666; Imp pay 0;
  King Reginald 6000 hp / 930 dmg; Bile Demon speed 0.4 (slowest).

## Layout notes (offsets within a record)

Every offset below is read by `KwdCreatures`; the full per-field dump for the
other record types is `kwd-record-fields.md`.
The base-creature byte follows the shuffle-speed dword (Q528); its binary copy
is in [the loader record](functions/00492320.md#writer-offset-check).

```
0x000  char[32]   name
0x0CFF u16        introductionStringId   0x0D01 fp32  perceptionRange
0x0D37 fp32       shuffleSpeed           0x0D3B u8    baseCreatureId (both typed views)
0x0DAC Attraction[2]  (8 bytes each: u32 present, u16 roomId, u16 roomSize)
0x0DC4 Spell[3]       (24 bytes each; spellId at +22, levelAvailable at +23)
0x0E0C Resistance[4]  (2 bytes each: u8 attackType, u8 value)
0x0E14 JobPreference[3]  happy    (12 bytes: u32 job, s16 mood, s16 mana, u8 chance, 3 unk)
0x0E38 JobPreference[2]  unhappy
0x0E50 JobPreference[3]  angry
0x0E74 JobType[2]        hated
0x0E7C JobAlternative[3] (8 bytes: u32 job, s16 mood, s16 mana)
0x0EA4 fp32 height      0x0EB0 fp32 eyeHeight   0x0EB4 fp32 speed
0x0EB8 fp32 runSpeed    0x0EBC fp32 hungerRate  0x0EC0 s32 timeAwake
0x0EC4 s32 timeSleep    0x0EC8 fp32 distanceCanSee   0x0ECC fp32 distanceCanHear
0x0ED0 fp32 stunDuration 0x0ED4 fp32 guardDuration   0x0ED8 fp32 idleDuration
0x0EDC fp32 slapFearlessDuration        0x0EE8 s16 possessionManaCost
0x0EEA s16 ownLandHealthIncrease        0x0EEC fp32 meleeRange
0x0EF4 fp32 tortureTimeToConvert        0x0EF8 fp32 meleeRecharge
0x0EFC u32 flags        0x0F00 u16 expForNextLevel   0x0F02 u8 jobClass
0x0F03 u8 fightStyle    0x0F04 u16 expPerSecond      0x0F06 u16 expPerSecondTraining
0x0F08 u16 researchPerSecond  0x0F0A u16 manufacturePerSecond
0x0F0C u16 hp           0x0F0E u16 hpFromChicken     0x0F10 u16 fear
0x0F12 u16 threat       0x0F14 u16 meleeDamage       0x0F16 u16 slapDamage
0x0F18 u16 manaGenPrayer 0x0F1C u16 pay              0x0F1E u16 maxGoldHeld
0x0F22 u16 decomposeValue 0x0F24 u16 nameStringId    0x0F26 u16 tooltipStringId
0x0F28..0x0F32 **s16** angerNoLair/NoFood/NoPay/NoWork/Slap/InHand
0x0F34 u16 initialGoldHeld     0x0F4B u8 creatureId
0x0F4E u8 hungerFill    0x0F4F u8 unhappyThreshold   0x0F50 u8 meleeAttackType
0x0F52 u8 lairObjectId  0x107F s16 tortureHpChange   0x1081 s16 tortureMoodChange
0x14F1 ArtResource[84]   uniqueResource (not parsed)  0x1545 u32 flags2
```

`fp32` is DK2's fixed-point: raw int with denominator 4096 (`DK2.Core.Fp32`).

The six anger fields at `0x0F28`–`0x0F32` are **signed** (ADR-0050). Read as
u16 a Goblin's `angerInHand` is 65286 and Jack's `angerSlap` is 60536, which are
−250 and −5000: slapping angers, being picked up calms. See `creature-anger.md`.

## The binary's reader (read 2026-10-05)

The CREATURES chunk reader `0x0056A250` divides the chunk by the record count. A
5449-byte record is read whole into the scratch record at `0x00746EC0`; any other size
is read after zero-filling 5449 bytes there (0x0056A3A0–0x0056A3ED). No 5537 (`0x15A1`)
constant exists in the binary. `CreatureRecord_ReadFromFile` (`0x00568E30`) copies file
fields to staging at `file + 0x3F0` and reads nothing past `0x1548`;
`CreatureDef_BuildFromRecord` (`0x00492320`) reads no staging offset above `0x1935` and,
while `[ECX+0xA3C7]` is zero, checksums exactly `0x1939` staging bytes (0x0049235A).
**The 88-byte tail is never read (Q14).** Ours reads any size, zero-filled to 5449 (ADR-0375).

**The last 72 bytes (Q70).** `0x14F1`–`0x1544` is one 84-byte ArtResource (OpenKeeper's
`UniqueResource`): name to `0x1530`, flags and data `0x1531`–`0x1540`, type `0x1541`,
three bytes after. ReadFromFile copies it whole; the builder (0x00492867–0x00492965)
resolves the name when the type is non-zero and fills **art slot 50** (`def+0x546`)
through `0x00504D80`, with `%sStart`/`%sEnd` handles at `+0x559`/`+0x55D`. Its type-2 arm
uses 13 of the block's 16 bytes; `0x153E`–`0x1540` and `0x1542`–`0x1544` are copied but
unread. `0x1545` is ORed into staging `+0x1935` and lands at `def+0x68A` (0x00493D0A).
Only Unique Goblin (`Unique_GoblinFront`) and Unique Mistress (`Unique_Mistress1`) fill
the resource. Who requests art slot 50 is unread.

**Job preferences (Q11).** ReadFromFile copies job, mood, mana and chance of each entry
(not bytes 9–11). The builder sign-extends mood and mana, divides each by variable 19
(ticks per second) with an unsigned `DIV` — floor-like in the low 16 bits for a negative
— and makes a non-zero value that rounds to 0 into 1. It packs 6-byte entries (`+0` mood,
`+2` mana, `+4` chance, `+5` job): happy at `def+0x628` (count `+0x78B`), unhappy
`+0x63C` (`+0x78C`), angry `+0x64A` (`+0x78D`); the alternatives go to 5-byte entries at
`def+0x65E`. So both fields are per-second rates stored per tick (STRONGLY INFERRED from
the divisor). No reader of mood or mana was found (STRONGLY INFERRED): the list accessors
`0x004944D0`, `0x00494550`, `0x004945D0` and all eight of their callers use only chance
and job, and the def offsets of all eight entries are touched only by the initialiser,
serialiser, loader and copy. Ours reads both as signed (ADR-0375) and uses neither.

## Significant finding — job selection is partly data-driven

Each creature carries **three weighted job tables** (happy / unhappy / angry),
a **hated-jobs** list, and **alternative jobs**, with explicit percentage
chances. Examples:

```
Troll    happy: Manufacture(100%)      unhappy: Sulk(10%), Drink(90%)
                                       angry:   Leave(50%), Rebel(50%)
                                       hates:   Research      attracts: Workshop
Warlock  happy: Research(80%), Train(10%), Praying(10%)
                                       hates:   Manufacture   attracts: Library
Imp      happy: Explore(100%)          (never eats or sleeps — sentinel values)
```

The candidate job set and its weights are data, not code. The surrounding
procedure is Q2, ANSWERED from the binary and built (ADR-0082): it is not a
priority list — see `job-selection.md` for the chooser, `unhappyThreshold`
(per-creature, default 50) for which of the three tables applies.

## Confidence

CONFIRMED — record size, creature ids, and all fields asserted in tests.
Cross-validated against two independent sources.

UNKNOWN — what (if anything) consumes `moodChange`/`manaChange`; the ~45
ArtResource blocks (not parsed, not needed for simulation); `expForNextLevel`
is a uniform 3000 (2800 for uniques), suggesting per-level progression actually
comes from the VARIABLES `CreatureStats` rows instead.

## Implementation

`DK2.Data.Kwd.KwdCreatures`, `DK2.Data.Model.CreatureDef`,
`DK2.Data.Model.CreatureEnums`, `DK2.Core.Fp32`

## Tests

`DK2.Data.Tests.KwdCreaturesTests` (7 test methods, incl. the sum-to-100
invariant and the Flame cross-source id check)

## Open questions

- Q11: a consumer of `moodChange` / `manaChange`, if any.

## Known exceptions

`hungerRate` / `timeAwake` == 32767 is a general "never" sentinel and applies
**per need rather than per creature** (ADR-0048). Eight creatures never get
hungry (Imp, Vampire, Skeleton, Horny, Stone Knight, and the unique Firefly,
Skeleton and Vampire); six never sleep (Imp, Skeleton, **Dwarf**, Horny, Stone
Knight, Unique Skeleton). Neither set contains the other — the Vampire sleeps
and never eats, the Dwarf eats and never sleeps — so no single "undead" flag
produces it. Both sets are asserted by name in `CreatureNeedsOnRealDataTests`,
because both were guessed wrong first. See `creature-needs.md`.
