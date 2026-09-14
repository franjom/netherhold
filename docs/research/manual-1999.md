# Source: the shipped Dungeon Keeper 2 manual (1999)

Source: `original-reference/Dungeon_Keeper_2_Manual.pdf`, 46 pages, added by the
owner 2026-09-04. Bullfrog / Electronic Arts, manual writers James Lenoël and Oli
Ladenburg (credits, p.44). Text extracted locally with `pdftotext`; page numbers
below are PDF pages, and the manual's own printed numbers run 8 pages behind
(PDF p.19 is printed p.34).

> **LEGAL — flagged, and handled.** The PDF is EA's copyrighted work and this
> repository is public. (It was MIT when this was written and is GPL-3.0-or-later
> since 2026-09-04, ADR-0041; that changes nothing here — our licence grants
> nothing over EA's work either way.) It was raised as a question when the file
> first landed in `docs/`; the owner moved it to `original-reference/`, which the
> existing "NEVER COMMIT: original Dungeon Keeper 2 material" rule already
> ignores. So it is a local reference and nothing more. **This document quotes it
> in short excerpts for research purposes and paraphrases everywhere the wording
> does not matter.** Sharing the PDF itself would be the owner's call and a
> lawyer's, not an assistant's.

---

## What this source is worth, and what it is not

**It is a primary design document and a secondary behavioural source.** The
manual was written by EA about EA's game, so when it describes a mechanic it is
describing the intended design. But it shipped with **DK2 1.0 in 1999**, and this
project targets **v1.70**. Where the manual and the shipped v1.70 data disagree,
**the data wins** — it is what the executable reads.

That is not a hypothetical. Section [Where the manual and the data
disagree](#where-the-manual-and-the-data-disagree) lists five disagreements, and
in four of them the manual describes a rule the shipped `Creatures.kwd` simply
does not carry.

**What it is exceptionally good for** is the thing this project has been short of
all along: *what a mechanic is for*. The balance table gives us
`ModifyAngerOfCreatureInLairPerSecond = -550` with no statement of what a lair is;
the manual gives us "creatures' health rises each turn, as does their happiness
rating" with no number. Read together they are a complete rule. **Most of this
document is that pairing.**

**It is not an oracle for anything it does not state.** It gives almost no
numbers, it says "each turn" without ever defining a turn, and it is written in
marketing voice. It does not answer Q1.

---

## The cross-check that establishes its reliability

Before using a source, test it against something already known. The manual's room
pages state a **Cost** and a **Minimum Recommended Size** for every room. Both are
fields we parse out of `Rooms.kwd` (`RoomDef.Cost`, `RoomDef.RecommendedSizeX/Y`).
Neither was consulted when those offsets were found.

| Room | Manual cost | `RoomDef.Cost` | Manual min size | `RecommendedSizeX/Y` |
|---|---|---|---|---|
| Dungeon Heart | N/A | 0 | 5x5 | 5x5 |
| Treasury | 200 | 200 | 1x1 | 1x1 |
| Lair | 300 | 300 | 1x1 | 1x1 |
| Hatchery | 300 | 300 | 3x3 | 3x3 |
| Training Room | 500 | 500 | 3x3 | 3x3 |
| Combat Pit | 750 | 750 | 5x5 | 5x5 |
| Temple | 3000 | 3000 | 5x5 | 5x5 |
| Workshop | 600 | 600 | **5x5** | **3x3** |
| Casino | 750 (per tile) | 750 | 3x3 | 3x3 |
| Graveyard | 2000 | 2000 | 3x3 | 3x3 |
| Portal | N/A | 0 | 3x3 | 3x3 |
| Torture Chamber | 1500 | 1500 | 3x3 | 3x3 |
| Prison | 750 | 750 | 5x5 | 5x5 |
| Library | 600 | 600 | 3x3 | 3x3 |
| Guard Room | 600 | 600 | 1x1 | 1x1 |
| Wooden Bridge | 200 (per tile) | 200 | 1x1 | 1x1 |
| Stone Bridge | 500 | 500 | 1x1 | 1x1 |

**17 of 17 costs match. 16 of 17 sizes match.** The one exception is the
Workshop, and the manual contradicts *itself* there: its Workshop page says 5x5,
while its Bile Demon page says the creature wants "Workshop (3X3 tiles)". The
record says 3x3. Treat the Workshop room page as a typo.

Two consequences:

1. **Our `Rooms.kwd` offsets are right**, cross-validated by a source that could
   not have copied them.
2. **`RecommendedSizeX/Y` means what Q45/ADR-0027 says it means.** The manual is
   explicit about what the number is for: *"At this size the room is most likely
   to function effectively, containing many of the features that make a room
   efficacious for its role"* (p.19) — and, crucially, *"you can create smaller
   rooms but only experienced Keepers will be effective in their attempts"*, i.e.
   **it does not constrain building**. That is exactly `RoomInstance.IsFunctional`
   and exactly the reason Q36 concluded a 1x1 temple is buildable. Q45 was
   STRONGLY INFERRED from two owner observations; it now has a third, independent
   agreement on **sixteen rooms at once**.

The costs also settle a labelling wobble: the manual writes "Cost per Tile" for
the Casino and the Wooden Bridge and a bare "Cost" for the other fifteen, but the
record has one `Cost` field and it is per tile for all of them (ADR-0029). The
manual is inconsistent; the data is not.

---

## What each room does

This is the section the project needed. Manual prose on the left, the shipped
number that quantifies it on the right. **Nothing here is implemented** — it is
the research that has to precede implementing it, and the "what a room does"
question in `RESEARCH_STATUS.md`.

Confidence convention: the *purpose* statements are CONFIRMED by the manual (EA
describing its own game). The *pairings* with variables are STRONGLY INFERRED —
the variable names say what they modify and the manual says what the room does,
and in several cases the two contain the same number, but nobody has watched the
value move.

### Dungeon Heart (p.19)

- Losing it loses the game (except multiplayer). Already Q40.
- *"All of your Mana is stored in the Dungeon Heart and some of your Gold can be
  stored in its outer ring."* — **the outer ring**, not the whole footprint.
  `MaxGoldPerDungeonHeartTile` = 1000. This is the same rim idiom Q60 measured on
  the temple (`2(w+h) − 4`), which makes the rim look like a DK2-wide pattern
  rather than a temple quirk.
- Destroying an enemy heart transfers **all of that keeper's stored mana** to you
  (single-player only).
- Creatures may only be dropped into the **outer causeway**, never the centre —
  except **imps, which can be dropped into the centre and redeemed for half their
  mana cost**. Create Imp costs 1500 (Q54), so a redeemed imp returns 750; the
  escalating price makes "half of what?" a real question.
- Friendly creatures near the heart *"feel less fear in combat"*.
  `DungeonHeartCreatureGatheringDistanceTiles` = 7, `DungeonHeartReportingDistanceTiles` = 4.
- Others: `DungeonHeartObjectHealth` = 100000,
  `DungeonHeartHealthRegenerationPerSecond` = 25,
  `DungeonHeartManaGenerationIncreasePerSecond` = 30 (this is `mana.md`'s flat 30),
  `DungeonHeartMaxManaIncrease` = 32000, `DungeonHeartClaimScanRadiusTiles` = 15.

### Treasury (p.20)

Already implemented (ADR-0015/0018/0019). The manual **independently confirms the
overflow chain we derived from the owner's observation**: *"If the Treasury is
full, the Gold can be stored in the Dungeon Heart though if this, too, is full,
then it is left where it falls and fails to register in your total wealth."* Seam
→ hands → treasury → heart → floor, and floor gold is **not** counted. That is
Q31 and ADR-0018 word for word, from a source written eighteen years before
either.

Selling: *"a room tile that cost you 200 pieces of Gold may only fetch 100 when
sold on"* (p.18) — 50%, matching `RoomSellValuePercentageOfCost` = 50 and the
owner's measurement. Q34 now has three agreeing sources.

`MaxGoldPerTreasuryTile` = 3000, `MaxGoldPileOutsideTreasury` = 2000.

### Lair (p.20) — the manual gives an almost complete model

- A creature arriving in the dungeon *"makes its way to the most appropriate Lair
  and sets up its 'home'"*, and each creature owns **a bed**, not a tile.
  `CreatureDef.LairObjectId` names the object. Capacity is therefore a count of
  beds, and **"capacity = tile count" is not safe** (Q60).
- *"Creatures always return to their Lairs to recuperate whenever their health
  falls below a certain level."* — `CreatureSleepsWhenBelowPercentHealth` = **50**.
- *"Whilst in their Lairs, creatures' health rises each turn, as does their
  happiness rating."* — `ModifyHealthOfCreatureInLairPerSecond` = **+100**,
  `ModifyAngerOfCreatureInLairPerSecond` = **−550**.
- *"they generally wait until they reach full health before venturing out … but
  you can pull them out sooner, provided their health is at a sufficient level"*.
- Hostile roommates *"have an adverse effect on the creatures' happiness"* —
  `ModifyAngerInCompanyOfHatedCreaturesPerSecond` = **+150**. This is the fourth
  of the owner's four anger causes (Q7), the one with no obvious per-creature
  field; it turns out to be a global rate, not a creature stat.
- Bile Demons prefer to lair near a Hatchery, Warlocks near a Library, Dark Angels
  near a Temple, Salamanders near lava — bed **placement** preferences, distinct
  from attraction.
- Re-housing by hand is supported and *"its old bed disappears"*.
- Related: `TimeBetweenReattemptingSleepSeconds` = 4,
  `CannotSleepModifyCreatureHealthPerSecond` = 0.

### Hatchery (p.20)

- Breeds chickens; a hungry creature goes to a hatchery on its own and eating
  *"reduce[s] its hunger, but it also increases its health"*.
- Chickens can be **picked up and dropped on prisoners and torture victims** to
  keep them alive, which is how conversion is made likelier.
- `ChickenGenerationTimePerHatchery` = 16, `MaxFreeRangeChickensPerPlayer` = 12,
  `TimeBetweenReattemptingHungerSeconds` = 4,
  `StateCounterDelayBeforeReattemptEating` = 5, plus `CreatureDef.HungerRate` and
  `HungerFill`.
- **Blocked on objects (M6):** a chicken is an object, so nothing here can be
  built until the OBJECTS record is parsed. Same blocker as Q31's floor pile.

### Training Room (p.20–21)

- *"creatures increase their experience level every turn"* working dummies and
  targets, and *"Each turn, there is a cost in Gold incurred for each creature
  that you are training."*
- **`TrainingRoomMaxExperienceLevel` = 4**, and the manual states independently:
  *"The highest level a creature can attain in the Training Room is level 4."*
  Two sources, same number, neither derived from the other. This is the strongest
  single validation of the whole VARIABLES table's semantics.
- **`ModifyPlayerColdWhileTrainingPerSecond` = −5 is a typo for "Gold".** The
  community variable name says "Cold", which is meaningless; the manual says
  training costs the keeper gold per turn; the value is negative and player-scoped.
  So: **5 gold per second, per creature training.** Recorded because the name
  alone would have left this unusable.
- `MotionlessWhileTrainingSeconds` = 5, `EPLossSecondWhileNotTraining` = 0
  (experience does **not** decay in v1.70), and per-creature
  `ExpPerSecondTraining` / `ExpForNextLevel`.

### Combat Pit (p.21)

- **`CombatPitMaxExperienceLevel` = 8**, and the manual: *"the Combat Pit allows
  creatures to reach experience levels 5, 6, 7 and 8"*, beyond which only real
  combat counts. Second exact agreement.
- Friendly vs enemy is a fight to the death; friendly vs friendly is to
  unconsciousness *"but there is always a chance that one friendly creature may
  accidentally kill the other"* — `PercentageChanceOfKillingCreatureInPit` = **10**.
- A group of friendlies all treat each other as enemies, last one standing.
- Winners gain happiness — `ModifyAngerOfCombatPitVictor` = **−16400** (a one-off,
  not a rate: no `PerSecond` in the name). Spectators:
  `ModifyAngerOfPitSpectatorPerSecond` = −275. A creature **alone** in the pit
  gets angry: `ModifyAngerWhileSolitaryInPitPerSecond` = +275.
- Pit fighting is not full-strength: `PitPercentageDamageTakenOfNormalCombat` = 20,
  `CombatPitMeleeDamageModifierPercentage` = 100.
- Losers are dragged *"to your Prison or Graveyard"*; unconscious friendlies are
  dragged to their **Lair**.

### Temple (p.21)

- Praying generates mana, and *"The amount generated depends on the creature's Job
  Class (Thinkers generate the most, Fighters the least). Converted enemy
  creatures produce twice as much Mana as friendly creatures."*
- **This explains a field we parse and do not use**: `CreatureJobPreference` has a
  `ManaChange` alongside `MoodChange`. Per-creature, per-job mana is exactly what
  "depends on the creature's Job Class" needs. Worth checking whether the pray
  job's `ManaChange` really does rank Thinker > Scout/Artisan > Fighter.
- `MotionlessWhilePrayingSeconds` = 30,
  `ModifyCreatureAngerWhilePrayingPerSecond` = −275,
  `ModifyCreatureHealthWhilePrayingPerSecond` = +33.
- **Sacrificial pool**: throwing creatures and objects in *"you may receive a
  gift from the Gods"*, and a bad sacrifice is punished. The 44 recipes are in
  `sacrifice.md`; none rewards direct mana. Variable 112's inherited name is
  wrong: its shipped zero doubles converted creatures' prayer mana.
- The pool sits inside the temple, which is why the temple's usable capacity is
  its rim (Q60).

### Workshop (p.21–22)

- Manufactures doors, traps and guard posts. Items are built **in the order they
  are placed**, i.e. a FIFO queue per keeper.
- *"The creatures in the Workshop each undertake only a finite amount of work per
  turn until the work is complete; the more workers present, the speedier their
  output will be."* — an accumulating point total, `CreatureDef.ManufacturePerSecond`
  per worker, summed.
- **`ManufacturePointLossWhileNoWorkersPerSecond` = 1** — progress **decays** when
  the workshop is empty. Not something anyone would guess, and not in the manual.
- Placing an item charges the keeper immediately (*"your coffers are lightened
  accordingly"*), then an imp carries the finished item into position.
- Creatures that hate manufacturing refuse; so does a workshop with nothing queued.
- `MotionlessWhileManufacturingSeconds` = 5.

### Casino (p.22)

The manual explains a lever the variables would otherwise be unreadable without.
The payout lever has two settings, **Smiley** and **$**, and every gambling
variable comes in a `Smiles` and a `Money` pair:

| | Smiley | $ |
|---|---|---|
| creature money per second | `GamblingModifyCreatureMoneySmilesPerSecond` = **+1** | `GamblingModifyCreatureMoneyMoneyPerSecond` = **−8** |
| creature anger per second | `GamblingModifyCreatureAngerSmilesPerSecond` = **−2000** | `GamblingModifyCreatureAngerMoneyPerSecond` = **+100** |
| jackpot chance per second | `GamblingCreatureJackpotChanceSmilesPerSecond` = **5000** | `GamblingCreatureJackpotChanceMoneyPerSecond` = **20000** |

Which matches the manual exactly: *"If you set the payout levels high (Smiley)
then your creatures' happiness ratings rise. If you set them low ($), you stand
to make more money, but at a cost to your creatures' happiness ratings."*
Note the jackpot chance is *higher* on the stingy setting — an offsetting
sweetener the manual does not mention.

Also: `CasinoModifyCreatureGoldPerSecond` = −50, `MotionlessInCasinoSeconds` = 13,
`GamblingJackpotBasicAmount` = 1000, `GamblingJackpotPayTimeLimit` = 240,
`GamblingJackpotPayTimeLimitMissedAngerPercentageModifier` = 100,
`CasinoBigWinnerIncreasedHappinessPersentage` = 100,
`IncreasedWorkRatePersentageWhenCasinoBigWinOccurs` = 35,
`CasinoWinSpeedUpDurationSeconds` = 60, `CasinoBigWinKeeperLossPercentage` = 0,
`GamblingDelayBeforeBecomingFearlessSeconds` = 120,
`GamblingFearlessDurationSeconds` = 120 (creatures get drunk-brave),
`DrunkChancePercentagePerDrink` = 5, `DrunkenDurationSeconds` = 15.

Slapping a jackpot winner makes it drop *"a large portion of his winnings"*, at a
happiness cost, and *"If other creatures are witness … you may provoke a mutiny"*.

### Graveyard (p.22)

- Corpses are brought here; each *"grants the Vampire life-force, the amount being
  dependent on how powerful the creature was during its life"*; when enough has
  accumulated **a Vampire rises**.
- `DecomposeValueNeededForVampire` = **1000**, and `RoomDef.CreatedCreatureId` for
  the Graveyard is **8 = Vampire**. That confirms what `CreatedCreatureId` means,
  with `RoomDef` and the manual agreeing.
- `DeadBodyDiesAfterSeconds` = 60 (a corpse left lying rots away).
- **The per-creature decompose value has no field we have named.** Finding it is a
  new question.
- Bodies taken to a graveyard *"cannot be resurrected by a Special or raised by the
  creature spell Raise Dead"*.
- A Vampire that dies **loses a level and re-incarnates in the Graveyard**; with no
  graveyard, or at level 1, it is gone for good.

### Portal (p.22)

- Cannot be built — found and claimed.
- *"The types of creatures that are available to enter … depend on the type and
  size of rooms that you've created"* — this is `CreatureAttraction`, see below.
- `CreaturesSupportedByFirstPortal` = **15**, `CreaturesSupportedPerAdditionalPortal`
  = **5**: the creature cap is per-portal, not per-keeper.
- `EntranceGenerationSpeedSeconds` = 25.
- Sacking = dropping a creature into the portal; it leaves at once and is replaced.
  `DecreaseEntrancePersentageOfLastSackedCreatureType` = 33 (sack one and its type
  gets rarer), `IncreaseEntrancePersentageOfNewCreatureTypes` = 33.
- Multiplayer pools are finite, and **losing a room removes from the pool every
  creature that depended on it**.

### Torture Chamber (p.23)

- Enemies placed here *"are likely to convert or, failing that, release a valuable
  piece of information and die"*; conversion time depends on creature type.
- Prolong with the Heal spell or chickens, which raises the conversion chance.
- `TortureChanceOfDyingWhenConverted` = **0** in v1.70, `TortureSpeedUpDurationSeconds`
  = 40, `IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes` = 25 (meaning
  unclear — flagged below).
- The Mistress is the exception to every unhappiness rule about torture: she enjoys
  it, and she enjoys being slapped.

### Prison (p.23)

- Imps carry **unconscious** enemies to the **nearest** prison.
- *"Whilst in Prison, creatures gradually lose health as they wither away"* —
  `PrisonModifyCreatureHealthPerSecond` = **−3**. `MotionlessInPrisonSeconds` = 8.
- *"Any creatures that perish in Prison will rise as Skeletons under your
  control"* — `RoomDef.CreatedCreatureId` for the Prison is **9 = Skeleton**.
  Second confirmation of that field's meaning.
- The prison door has a **bar** that stops further prisoners being delivered.
- Full prison → imps look for the next nearest with space → none → **imps stop
  collecting prisoners at all**.
- **Jailbreak** on manual overcrowding, on selling part of a prison in use, or on
  an enemy freeing his comrades —
  `OvercrowdingJailBreakPercentagePerExtraCreature` = **50**.
- Losing a prison frees every enemy in it and keeps every friendly.

### Library (p.23)

- Research happens here; **any** creature can contribute *"depending on their love
  of books"* (`CreatureDef.ResearchPerSecond`, and the hate-jobs table).
- Once researched a spell stays researched, but researchers can **upgrade** spells
  afterwards (`UpgradedPossessionCostPercentage` = 70,
  `UpgradedCallToArmsCostPercentage` = 70 are two such upgrades).
- **The surprising one:** *"If an enemy captures a Library, any Spells that have
  been researched there become unavailable until the Library is re-captured or a
  new one is built and the Spells are researched again."* **Research is held by
  the room, not by the keeper.** Nobody would have guessed that, and it changes
  where the state lives.
- `MotionlessWhileResearchingSeconds` = 70 — much the longest of the "motionless
  while …" family, so a researcher settles in.

### Guard Room (p.23)

- Creatures placed here *"are duty-bound to remain there, and seldom leave their
  posts (unless … hungry or in need of paying)"*, and **patrol to nearby Guard
  Posts**.
- `MaxTimeInGuardRoomBeforeWanderingToGuardPostSeconds` = **60**,
  `GuardRoomReportingDistanceTiles` = **7**, `GuardPostReportingDistanceTiles` = 7,
  `StateCounterWhileGuarding` = 10, `ExternalGuardingDurationSeconds` = 15, plus
  `CreatureDef.GuardDuration`.

### Wooden and Stone Bridge (p.23–24)

- Both cross water and lava; the wooden one burns —
  `WoodBridgeLifeOnLavaSeconds` = **30**, which is `BridgeBurnSystem` and Q67.
- *"the only way … that you can take over unconnected shores of the Underworld"*.

---

## Creature needs, moods and pay

The manual's §5 is the clearest statement anywhere of the mood model (Q7), and it
matches the fields on every CREATURES record.

**Three moods** (§5.2): happy, unhappy, angry. Happy creatures *"even undertake
certain jobs off their own backs"*; unhappy ones mostly *"go to their Lairs and
sulk"*; angry ones *"head straight for the nearest Portal, kicking down (or
attempting to kick down) any locked doors or obstacles"*. That is a state machine
with an exit condition, and it matches `CreatureDef.UnhappyThreshold`.

**Eight causes of unhappiness** (§5.3.1), against the fields:

| Manual | Field |
|---|---|
| No lair | `AngerNoLair` |
| No food | `AngerNoFood` |
| No pay | `AngerNoPay` |
| Out of work — *"prevented from carrying out a job that it wants to do"* | `AngerNoWork` |
| Held in the hand too long | `AngerInHand`, `TimeInHandBeforeCreaturesBecomeAngrySeconds` = 1000 |
| Slapping | `AngerSlap` |
| Lairing with a hated creature | `ModifyAngerInCompanyOfHatedCreaturesPerSecond` = 150 |
| Imprisonment or torture | — |

Q7 recorded the owner's four causes and noted that the fourth had no obvious
field. It has one; it is global rather than per-creature. The manual adds three
more causes and the fields for two of them.

**Three comforts** (§5.3.2): the casino, the temple, and **treats** — dropping
gold on a creature. Treats do two things: *"those creatures you have treated
consider themselves paid in advance and do not stop working to collect their
wages on the next Payday"*, and happiness rises. So a treat is a **prepayment**,
not just a mood bump.

**Payday** (§7.6.2.2): every creature collects automatically, unpaid creatures
get unhappy. `PayDayFrequencySeconds` = 600, `TimeBeforeFirstPayDaySeconds` = 600,
`PayDayCutOffTimeSeconds` = 120, `DelayBeforePayReevaluationSeconds` = 4. Q9's
timing half, corroborated.

**Slapping** (§3.7.1.2, §5.1.1): *"makes your creatures faster and more obedient
but does damage their health"* and makes them unhappy.
`IncreasedWorkRatePecentageFromSlapping` = 50,
`IncreasedWorkRateDurationFromSlappingSeconds` = 60,
`SlapWorkFasterDurationSeconds` = 25, `SlapSpeedUpDurationSeconds` = 25,
`ForceAppliedToSlappedCreature` = 1024. (Two different durations for the same
effect; unresolved.)

**Dropping** (§5.1.2.1): a dropped creature *"lies stunned on the floor with stars
circling its head"* and *"will take considerable extra damage"* —
`CreatureStunnedTimeSeconds` = 2, `CreatureStunnedEffectDelaySeconds` = 1,
`StunnedDamageIncreasePercentage` = 100, `StunTimePercentageIncreaseWhenHit` = 25.
Recovery speed varies by creature (§14.4 names Goblins and Mistresses as fast).

**Fear and threat** (§13.3): a creature retreats when it judges it will lose, or
when hurt. *"Groups of creatures represent a greater threat than individuals and
large creatures are more threatening than smaller creatures."* That is
`CreatureDef.Fear` and `CreatureDef.Threat`, and it tells us `Threat` is a value
a creature *contributes* to an assessment, not one it feels.
`CreatureCriticalHealthPercentageOfMax` = 20, `CriticalHealthPercentageForHeroLair`
= 20.

**Combat classes** (§13.5) — Blocker holds ground, Blitzer drives for the enemy
support line, Flanker gets behind, Support stays back and shoots. Matches
`FightStyle { NonFighter, Blitzer, Blocker, Flanker, Support }`.

**Rebellion**: the manual (§5.2.3) says angry creatures leave; the owner added
(Q9) that *converted* creatures rebel instead. `RebelMaxFollowers` = 2,
`RebelLeavingPercentage` = 70, `RebelBecomeGoodPercentage` = 15,
`RebelDefectPercentage` = 15 — a 70/15/15 split, which is the owner's rule with
its numbers.

---

## Imps and mana

- *"The Dungeon Heart always provides enough Mana to sustain your four Imps, but
  any additional ones that you create feed from your Mana reserves."* —
  `MinimumImpThreshold` = **4**. This confirms both the number and the semantics
  behind `ImpSupplySystem` (ADR-0021): four is a floor the heart pays for, not a
  starting count.
- *"if you run out of Mana then your Imps may disappear altogether"* — Q57.
  `InsufficientManaImpEvaluationPeriodSeconds` = 10, `ImpPopCountdownTimeSeconds` = 10.
- Imps *"don't require food or Lairs"* and *"never complain or become unhappy"* —
  which is why the Imp's only job-table entry is `Explore` at 100% (Q2).
- *"Imps gain experience based on the number of jobs they have done."* The variable
  is `ImpExperienceGainPerSecond` = 20, which is **per second, not per job**. Both
  cannot be literally true; flagged below.
- Experienced imps get Haste Self, veterans get Teleport — matching the Imp's two
  parsed spell slots.
- Four mana sources (§10.1.1): claimed territory (*"Room tiles generate the same
  amount of Mana as claimed but undeveloped tiles"* — exactly `mana.md`), temple
  prayer, **mana vaults**, and the Increase Mana special
  (`SpecialIncreaseManaAmount` = 50000).
- Mana **drains**: imps, Horny, spell upkeep, traps, and dying while possessing
  (`DeathWhenPossessingManaReduction` = 100, `ManaCostInFirstPersonPerSecond` = 5).
  All of Q53/Q58/Q59.
- *"The amount of Mana you can store is restricted by the limited capacity of your
  Dungeon Heart."* — `DungeonHeartMaxManaIncrease` = 32000 vs
  `MaximumManaThreshold` = 200000. See the open questions.

---

## Terrain and gold

Section 7 lists the terrain types a player sees, and they map onto the 41 parsed
types without surprise: Unmined Earth, Water, Lava, Reinforced Wall, Impenetrable
Rock, Gold, Gems, Mana Vaults, Hero Gate, Dirt Path, Claimed Path.

Worth recording:

- **Reinforced walls can be dug** *"but require much more time and effort"* —
  `DigOwnWallHealth` = −130 vs `DigEnemyWallHealth` = −8, and `DigRockHealth` =
  −5000 (the value ADR-0023 corrects to −500). The manual confirms the *ordering*
  the corrected value produces.
- **Imps repair the inside face of a wall under attack**, which *"won't halt their
  entry, but it does give you extra time"* — `RepairWallHealth` = 22,
  `RepairTileHealth` = 125, and the reason `TileWorkKind` has a repair.
- *"Any rock you cannot tag with your Pickaxe is impenetrable rock"* — the
  taggable flag is the whole rule, as `TileWork` already has it.
- **Gems are an everlasting source** — `GoldMinedFromGems` = 25 per swing against
  `MineGoldHealth` = −300 for a seam, which is why gems are slow and infinite.
- Gold from **selling, stealing** (Thief and Rogue, from enemy treasuries;
  `AmountOfGoldStolenInFirstPerson` = 100) and **hero bounty**.
- Claimed path colour identifies the owning keeper — the minimap legend on p.16 is
  a complete colour key, useful for the Unity view.

---

## Doors, traps and the one movement exception

Doors (§11.3): *"must be laid down on a claimed tile and between two walls"*.
Enemies cannot open them but can kick them down; friendly creatures pass unless
locked; **locking creatures away from food or rest makes them unhappy and then
angry**. `DoorPickedTimeSeconds` = 5.

Costs, for when `Doors.kwd` is parsed: Wooden 500, Braced 1000, Steel 1500, Magic
6000, Secret 3000, Barricade 400.
Traps: Sentry 1000, Fear 750, Alarm 500, Gas 600, Guard Post 300, Spike 750,
Trigger 300, Boulder (cost not given on the extracted page), Freeze 1500,
Lightning 3000, Fireburst 6000.

**The Barricade is the only stated exception to "creatures walk on floor"**:
*"prevents any land-based creatures (including your own) from passing … Flying
creatures and projectile attacks can pass over the Barricade."* So flight matters
**over a barricade**, not over rock. See the next section.

---

## Three rules the owner made explicit alongside the manual

All three were volunteered rather than asked for, in the two messages that
accompanied the manual. All three are CONFIRMED — the owner has played the game.
They are recorded here rather than scattered because they arrived together, and
because each is the kind of rule that is too obvious to write down until somebody
gets it wrong.

### 1. Creatures cannot pass through walls

> *"I was watching a video gameplay from OpenKeeper and I noticed imps passing
> through walls… creatures CAN NOT pass through walls, they can only move through
> dug out tiles, not through solid rocks, that is an obvious observation, just
> wanted to make it explicit."* — owner, 2026-09-04

**CONFIRMED.** Status in this project: the rule was already implemented and had
never been asserted.

- `WorldPathGrid.IsWalkable` has refused `TerrainFlags.Solid` since the first
  worker, so no route has ever been *planned* through rock.
- Nothing checked that no creature ever *was* in rock. `PathfindingTests` proves
  the search will not cross a wall on a hand-drawn grid; that is a property of the
  search, not of the game.
- And there was a real hole: `CreatureMovementSystem` walked the stored route
  without re-checking it. A route is planned once and the world keeps moving. The
  shipped cause is the **burning bridge**: a wooden bridge over lava runs out
  thirty seconds after it is built and the tile becomes lava again (Q67), so a
  creature already walking a route across it would have strolled over the lava.
  `BridgeBurnSystem`'s own notes had anticipated a creature being caught on
  ground that turned impassable; nothing had stopped one *entering* such ground.

Fixed and pinned (2026-09-04): the mover re-checks each waypoint as it steps onto
it and drops the route if it is blocked, and `WallsBlockMovementTests` asserts
across a 4000-tick dig with four imps that no creature ever stands on a solid tile
and no route in flight ever crosses one, plus a staged case where lava appears
under a walk. The tile a creature is *standing* on is deliberately not re-checked,
so a creature caught on ground that turned impassable can still walk off it — the
same rule `GridPathfinder` already applies to a mover's starting tile.

Two existing movement tests had to be fixed: they hand-fed routes straight across
the fixture's rock, which nobody had noticed because nothing looked.

**Flight is not an exception.** `CreatureFlags.CanFly` buys height over water and
lava, not passage through mass; the manual's only "passes over" claim is about
barricades. Nothing in `WorldPathGrid` needs a flying case for solid terrain.

UNKNOWN and deliberately not invented: what the original's creature does at that
instant — stop dead, or re-route. Only the observable rule is reproduced.

### 2. A dug-out tile can never be closed again

> *"once a tile is dug out, and maybe claimed, there is no possible way to fill
> in that tile to rock again. DK2 doesn't have a game mechanic for filling up dug
> out tiles."* — owner, 2026-09-04

**This one sits against the data rather than on it, which is why it matters.** It
was investigated properly on 2026-09-04 at the owner's request, and the answer
came out cleaner than expected. What follows is the whole chain.

#### The three fields that describe a mechanic

- `FillInHealth` = **5** in the global balance table — a variable that names the
  mechanic;
- `TerrainFlags.FillInable` (bit `0x8000`) on Dirt Path, Claimed Path, both mana
  vaults and Hero Lair — a per-terrain flag saying *this one can be filled in*;
- Claimed Path's `MaxHealthTypeTerrainId` is **Rock** — the destination.

Q18 and Q22 read those three together and produced `TileWorkKind.FillIn`. Three
*coordinated* pieces of data are not an accident: a rate, a per-terrain
eligibility flag, and a transition. The owner, who has played the game, says no
keeper can do it.

#### What OpenKeeper has (asked for by the owner)

**Exactly what we have, used exactly as much: not at all.** OpenKeeper is the most
complete DK2 reimplementation in existence — it has creature AI, tasks, rooms,
triggers, multiplayer — and it carries both fields in its **file-format layer
only**:

| where | what |
|---|---|
| `tools/convert/map/Terrain.java:55` | `FILL_INABLE(0x00008000)` — the same bit we have |
| `tools/convert/map/Variable.java:333` | `FILL_IN_HEALTH(11), // value=5` — the same id and value |

Both are **declared once and referenced nowhere else in the entire source tree**
(verified by grep over `src/`, and over `assets/` for good measure). Their worker
task list — `DigTileTask`, `ClaimTileTask`, `ClaimWallTileTask`,
`RepairWallTileTask`, `ClaimRoomTask`, plus the carry/capture tasks — has **no
fill-in task**, and their player interaction has no fill-in action.

So the two independent projects that have read this data most carefully are in the
same position: the fields are there, nobody has found a use, and the more advanced
of the two never implemented one. That is corroboration, not proof — but it does
rule out "OpenKeeper knows something we don't".

*(Provenance note: OpenKeeper is GPLv3. Those are two facts about what its source
contains, cited with file and line, per ADR-0003 and the owner's standing
instruction. No code was copied.)*

#### The one thing OpenKeeper did point at, and it turned out to matter

Their trigger-action enum (`tools/convert/map/TriggerAction.java:55`) has
**`ALTER_TERRAIN_TYPE(22)`** — a level-script action that writes a terrain id
straight onto a tile — and `MapController.alterTerrain` shows what it does:
`changeTerrain(tile, terrainId)` and optionally set the owner. **No health, no
rate, no fill-in.** A direct write.

That is a candidate for "some tile somewhere becomes rock again", so it was
checked **against the install rather than against anybody's word**. New tool
command `dk2tools kwd alterterrain [-v]` decodes every `ALTER_TERRAIN_TYPE`
action in every shipped map and prints what the target tile *starts* as. The
result, over all **66** trigger files and **5408** action records:

```
160 ALTER_TERRAIN_TYPE actions

terrain written:
    2 Rock                     SOLID     3 times
    3 Dirt Path                open     94 times
    5 Lava                     open     14 times
    8 Claimed Path             open     41 times
    9 Reinforced Wall          SOLID     2 times
   17 Wooden Bridge            open      6 times
```

The layout was a hypothesis taken from OpenKeeper (action type in the
second-to-last payload byte; body `terrainId, playerId, 0, 0, posX:u16,
posY:u16`; coordinates 1-based) and it **self-validates**: every one of the 5408
decoded action types lands inside the enumerated 1..60 range, every terrain id
names a real terrain, and every target coordinate is inside its own map.

**The dominant pattern is the opposite of filling in.** Ninety-four writes are
`Impenetrable Rock → Dirt Path`: scripted levels opening a passage that was sealed
against digging. Level 15b opens a nine-tile corridor that way; levels 7 and 18 do
the same.

**And the three Rock writes in level 1 — the level we render — are also opening,
not closing.** They target (15,18), (16,18), (17,18), and all three tiles start as
**Impenetrable Rock**. The script *softens* three boundary tiles into diggable
Rock. That is the tutorial handing the player a route.

**Exactly two writes in the entire shipped campaign turn an open tile solid**, and
both are in the same secret level:

```
Secret3  (13,20) p3   was 8 Claimed Path  ->  9 Reinforced Wall
Secret3  ( 5,25) p3   was 8 Claimed Path  ->  9 Reinforced Wall
```

#### So what is the answer

**The owner is right about gameplay, and it is now much better established than
"the manual does not mention it".**

1. **No keeper can fill in a tile.** No player action, no imp job, nothing in the
   UI. Confirmed by the owner, and consistent with the manual documenting every
   keeper affordance down to the casino payout lever and never mentioning it.
   (Its one nearby remark — *"once an area has been dug out, it is very difficult
   to fill it back in again"*, p.8 — is flavour text in a paragraph about planning
   ahead. "Very difficult" is not "possible", and it is the only such sentence.)
2. **A level script can close a tile**, through `ALTER_TERRAIN_TYPE`, and the
   shipped campaign does it **twice in 66 maps** — both times writing Reinforced
   Wall, never Rock, both in a secret level. So it is a scripted set-piece, not a
   mechanic.
3. **None of that explains `FillInHealth`.** `ALTER_TERRAIN_TYPE` is a direct
   write and needs no rate. So the *fill-in health mechanic* specifically — a
   worker applying +5 a swing until a floor tile becomes Rock — has no user
   anywhere: not in the keeper's hands, not in the level script, not in
   OpenKeeper. It remains most likely a level-editor facility or a cut feature.
   Q74 keeps that half open; the level editor is where to look next.

#### What this project does about it

**Nothing was deleted** — the transition is in the shipped data, and removing it
would make our terrain graph disagree with the file it was read from. What changed
is that the rule is now *asserted* rather than merely absent:
`DugOutTilesStayDugOutTests` pins that, over a 4000-tick dig, **no tile ever goes
from open back to solid** and **no worker ever queues a fill-in**. `ImpTaskSystem`
only ever chooses excavation, claiming and reinforcing, so this already held; it
now holds on purpose, and adding a fill-in job priority later would fail loudly.

Three consequences worth carrying forward:

- The dungeon's walkable set only ever grows. Anything that caches reachability
  can rely on that — with two exceptions, both narrow: a wooden bridge burning
  over lava, and an `ALTER_TERRAIN_TYPE` trigger, which arrives at M6 and which
  two shipped tiles use to build a wall.
- Q22's "fill-in's 660 swings" is a fact about the terrain table, not about
  anything a player can make happen.
- **When M6 implements triggers, `ALTER_TERRAIN_TYPE` must be able to write solid
  terrain onto an open tile**, and the `DugOutTilesStayDugOutTests` invariant is
  about *workers*, not about the engine. Level 1's three Rock writes are also a
  ready-made first test for the action, on the level we already render.

### 3. Two kinds of tile can never be dug through

> *"there are 2 categories (that I know of) of indestructable tiles, that can't be
> dug through by imps, solid rock and a gem tile. gem tile is an infinite gold
> source and solid rock is the boundary to limit expansion on the map."* — owner,
> 2026-09-04

**The shipped data already says exactly this, by two different mechanisms**, and
the model was already right — but nothing asserted it, so it is asserted now
(`DugOutTilesStayDugOutTests`, and on the real table in
`TileWorkOnRealDataTests`).

| | Impenetrable Rock | Gems |
|---|---|---|
| flags | `Solid, Impenetrable` | `Solid, Impenetrable, Taggable` |
| can a keeper mark it? | **no** — not taggable | **yes** |
| gold value | 0 | 3000 |
| destroyed type | itself | **itself** |
| effect | the map boundary | an infinite seam |

So the two categories are indestructible for different reasons: the boundary is
unmarkable, and the gem seam is markable and workable but destroys to itself, so
every swing yields gold and the tile survives. `TileWork`'s dig rule already
requires `IsSolid && IsTaggable && !IsImpenetrable`, which excludes both without a
special case, and mining requires only `IsSolid && IsTaggable && GoldValue > 0`,
which includes gems.

The manual agrees on both (§7.5: *"Any rock you cannot tag with your Pickaxe is
impenetrable rock"*; §7.7: *"Gem seams are an everlasting source of wealth"*), and
adds the economics: `GoldMinedFromGems` = 25 a swing against `MineGoldHealth` =
−300 for an ordinary seam, so gems are slow *and* endless.

**One caveat on "two categories".** The owner's "(that I know of)" is right to
hedge, but not in a way that changes anything: the shipped table has three more
impenetrable entries — `Edge of Map`, `Fog Of War` and `Claim Scan` — and all
three are engine internals rather than ground a keeper meets. Water and Lava also
carry the `Impenetrable` flag, but they are not solid; for them the flag means
"not diggable", not "not passable". Among terrain a keeper can actually mark,
Gems is the only impenetrable one, and the test asserts that.

---

## Where the manual and the data disagree

All five are in creature attraction, and in four of them the shipped record is
*less* demanding than the manual. `CreatureAttraction` lives at CREATURES 0x0DAC,
**two slots** of 8 bytes each — and that is now structurally certain: the eight
bytes after the second slot decode as two fixed-point values in the 0.1–1.0 range,
not a third slot.

| # | Manual | Shipped v1.70 `Creatures.kwd` |
|---|---|---|
| 1 | Bile Demon is attracted by Hatchery **and Lair (min 9 tiles)** and Workshop | Workshop (any size) and Hatchery (≥25 tiles). **No lair entry — and no room for one.** |
| 2 | Rogue is attracted by **Treasury +** Casino | Casino only |
| 3 | Goblin wants a **Lair of at least 5 tiles** | Lair, size 0 |
| 4 | Dark Angel wants a **Temple of at least 5x5** | Temple, size 0 |
| 5 | Hatchery page says Bile Demon needs **3x3**; Bile Demon page says **5x5** | 25 tiles = 5x5 — the creature page is right |

Reading: **the manual describes the 1.0 design and v1.70's balance table
relaxed it.** Two slots cannot hold the Bile Demon's three rooms, so the third was
not dropped by a patch to the *data* alone — either the record shrank or the
manual describes a design that never shipped. Either way the shipped data is the
oracle.

Row 4 matters because Q61 recorded the owner's belief that only a 5x5 temple
attracts a Dark Angel as *contradicted* by the data. It is now **the owner and the
1999 manual on one side and the v1.70 record on the other**, which is a much more
interesting disagreement and is on the observation checklist.

**And a correction to our own notes.** Q61 says the Bile Demon *"is the only
creature in the table with a non-zero"* `RoomSize`. It is not — four carry one:

| Creature | Slot 0 | Slot 1 |
|---|---|---|
| Bile Demon | Workshop, any | **Hatchery ≥ 25** |
| Giant (hero) | Workshop, any | **Hatchery ≥ 25** |
| Royal Guard (hero) | Torture Chamber, any | **Training Room ≥ 25** |
| Maiden Of The Nest | Library, any | **Temple ≥ 9** |

So a **temple does carry a size requirement** in the shipped data — 9 tiles, for
the Maiden of the Nest. The pattern is that only *second* attractions ever have a
minimum. And heroes have attractions too, mirroring the keeper roster room for
room (Knight→Combat Pit, Wizard→Library, Elven Archer→Guard Room, Thief→Casino,
Monk→Graveyard, Fairy→Temple, Guard→Training Room), which is unexplained.

Two smaller mismatches, both cosmetic:

- The manual calls the Troll's job class **Worker**; the enum from dk2-tools calls
  it **Artisan**. Same slot, different label. `IsWorker` is a separate flag, so
  "Artisan" is probably the better name and the manual the friendlier one.
- The manual gives the Skeleton **Combat Class: Blocker**; the record says
  **Flanker**. Every other creature's combat class matches.

**A data quirk found while checking this**, and worth reproducing rather than
tidying: creature 23's name in `Creatures.kwd` is `"Dark Angel "` — **with a
trailing space**. It is the only such name in the file. Our reader preserves it,
which is correct; anything matching creature names must not assume it is trimmed.
`AssetRef.NormaliseSubject` already trims, so the asset layer is unaffected.

---

## What the manual settles, corroborates, and opens

**Settles** (new, and nothing else had it):

- Research is stored **in the library**, and is lost when the library is.
- `ModifyPlayerColdWhileTrainingPerSecond` is **Gold** — training costs the keeper.
- The casino lever's two positions are the `Smiles`/`Money` variable pairs.
- Creature cap is **per portal**: 15 for the first, 5 for each after.
- Gold treats are **prepaid wages**, not only a mood bump.
- Imps redeem in the heart's centre for **half** their mana cost.
- A lair holds **beds**, one per creature, not tiles.
- The heart stores gold in its **outer ring**.

**Corroborates** (independently, and in several cases exactly):

- `RecommendedSizeX/Y` is the functional minimum (Q45) — 16 rooms.
- Every room cost (17 of 17).
- The gold overflow chain treasury → heart → floor (Q31, ADR-0018/0019).
- 50% sell value (Q34).
- `TrainingRoomMaxExperienceLevel` = 4 and `CombatPitMaxExperienceLevel` = 8.
- `RoomDef.CreatedCreatureId` (Graveyard → Vampire, Prison → Skeleton).
- `MinimumImpThreshold` = 4 (ADR-0021).
- Room tiles generate the same mana as plain claimed tiles (`mana.md`).
- Wooden bridges burn over lava (Q67).
- Payday every 600s (Q9).
- `Fear`/`Threat` semantics, and the four fight styles.
- The 70/15/15 rebel split.

**Opens** — new questions, listed in `open-questions.md`:

- Is there a **player-level starting-gold stockpile** after all? The manual says
  starting gold *"is treated as a separate stockpile, to be used up before the Gold
  in your Treasuries"*. ADR-0015 says DK2 has no player-gold variable and gold
  lives in tiles. Both can be true only if the starting stockpile is a separate
  thing from stored gold — which is what the manual says, and which we do not model.
- **Is the mana ceiling flat or per-heart?** `MaximumManaThreshold` = 200000 vs
  `DungeonHeartMaxManaIncrease` = 32000, against a manual that says the heart's
  capacity is the limit.
- **Where is a creature's decompose value?** Needed for the graveyard.
- **Does the pray job's `ManaChange` rank Thinker > … > Fighter**, and is a
  converted creature's contribution doubled?
- **Imp experience: per job or per second?**
- What is `IncreasedWorkRatePersentageOfPlayersTorturedCreatureTypes` = 25?
- Why two slapping durations (50%/60s vs 25s)?
- Does a small temple attract a Dark Angel in v1.70? (checklist)

---

## Pages worth re-reading later, by milestone

| Milestone | Pages |
|---|---|
| Rooms (now) | 19–24 |
| Creature moods and needs (M8) | 10–12, 14 |
| Mana (done, but §10 for the four sources) | 28 |
| Traps and doors (M6/M7) | 31–33 |
| Creature spells (M7) | 33–34 |
| Combat (M7) | 35–36 |
| Possession, first person (late); p.3's card is superseded — see `controls.md` | 37–38 |
| UI, minimap colours (Unity view) | 13–16 |
| Skirmish/MPD/multiplayer options | 39–44 |
| Game speed slider (25%–400% of default turn rate) | 44 |
