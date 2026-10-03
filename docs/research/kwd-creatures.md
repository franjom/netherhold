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
  creature data embedded in a globals file; no shipped file exercises it.
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
The previous clone-id offset is disputed (Q526); the binary speed-width check is
in [the loader record](functions/00492320.md#writer-offset-check).

```
0x000  char[32]   name
0x0CFF u16        introductionStringId   0x0D01 fp32  perceptionRange
0x0D37 fp32       shuffleSpeed
0x0DAC Attraction[2]  (8 bytes each: u32 present, u16 roomId, u16 roomSize)
0x0DC4 Spell[3]       (24 bytes each; spellId at +22, levelAvailable at +23)
0x0E0C Resistance[4]  (2 bytes each: u8 attackType, u8 value)
0x0E14 JobPreference[3]  happy    (12 bytes: u32 job, u16 mood, u16 mana, u8 chance, 3 unk)
0x0E38 JobPreference[2]  unhappy
0x0E50 JobPreference[3]  angry
0x0E74 JobType[2]        hated
0x0E7C JobAlternative[3] (8 bytes: u32 job, u16 mood, u16 mana)
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
```

`fp32` is DK2's fixed-point: raw int with denominator 4096 (`DK2.Core.Fp32`).

The six anger fields at `0x0F28`–`0x0F32` are **signed** (ADR-0050). Read as
u16 a Goblin's `angerInHand` is 65286 and Jack's `angerSlap` is 60536, which are
−250 and −5000: slapping angers, being picked up calms. See `creature-anger.md`.

Nothing past `0x1081` is read; the record's last 72 bytes (`0x1501`–`0x1548`)
are described by no published source — Q70, `kwd-container.md`.

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

UNKNOWN — semantics of `moodChange`/`manaChange` on job preferences; the ~45
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

- `moodChange` / `manaChange` units on JobPreference records.
- The 88 extra bytes in the embedded 5537-byte record variant, unobserved in
  the shipped corpus.

## Known exceptions

`hungerRate` / `timeAwake` == 32767 is a general "never" sentinel and applies
**per need rather than per creature** (ADR-0048). Eight creatures never get
hungry (Imp, Vampire, Skeleton, Horny, Stone Knight, and the unique Firefly,
Skeleton and Vampire); six never sleep (Imp, Skeleton, **Dwarf**, Horny, Stone
Knight, Unique Skeleton). Neither set contains the other — the Vampire sleeps
and never eats, the Dwarf eats and never sleeps — so no single "undead" flag
produces it. Both sets are asserted by name in `CreatureNeedsOnRealDataTests`,
because both were guessed wrong first. See `creature-needs.md`.
