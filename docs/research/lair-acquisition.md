# Mechanic: how a creature gets a lair

Source(s):
- The owner's recordings of DK2 v1.70, 2026-09-05
- `DKII.EXE` v1.70, `LairState.cpp`'s translation unit and the player's lair
  finder `FUN_004e0470`, read 2026-09-05
- `DKII.EXE` v1.70, lair and sleep state handlers 79–84, read 2026-09-23
- OpenKeeper `RoomLairControl` and `TaskManager`, for the contrast in §7a only

Original version: DK2 v1.70

Facts in our own words; ADR-0046 governs the boundary.

Confidence: **CONFIRMED** for §3–§6 and §8 from the binary, **STRONGLY
INFERRED** for §9 from complete handler reads, and **CONFIRMED (owner)** for
§1. Built 2026-09-05, ADR-0060 and ADR-0061.

## 1. What was watched

**In a large lair.** A Salamander came in from the upper right, **crossed the
room**, and put its bed down near the far side. A second Salamander entered from
the same direction and chose a spot on the **opposite** side from the first. A
Warlock later put its chair down right beside the first Salamander's bed.

**In a 1×5 lair**, the controlled version of the same thing, four creatures
claimed tiles in this order:

| | creature | tile |
|---|---|---|
| 1 | Goblin | 1 |
| 2 | Warlock | 3 |
| 3 | Goblin | 5 |
| 4 | Salamander | 4 |

The Salamander was **seen to walk up to an occupied tile, reject it, and carry on
to a free one**, and creatures were repeatedly watched visiting taken lair tiles,
turning them down and trying others.

A free lair tile existed while a bedless Bile Demon and a bedless Troll went off
to the **Training Room** instead, and only went looking for a lair later, when
the mood turned **Unhappy** with "no lair" as the reason. Goblin, Salamander,
Warlock and Firefly *always* went for the lair first; the Bile Demon and Troll
were **not consistent** and went lair-first on other runs. §5 and §8 are what
the binary made of that: mood is not the trigger, and the training detour is the
lair attempt *failing*.

## 2. What follows from it

| Claim | Status |
|---|---|
| DK2 does not simply assign the lowest free lair tile | **CONFIRMED** — the 1×5 order was 1, 3, 5, 4 |
| Acquiring a lair is a **journey**, not a reservation | **CONFIRMED** — creatures cross the room to place a bed |
| Occupancy is tested **on arrival**, not when choosing | **CONFIRMED** — a candidate was rejected in front of the camera |
| A rejected candidate is followed by another attempt | **CONFIRMED** |
| Candidate choice contains randomness | **STRONGLY INFERRED** from the watched spread, **corroborated by the binary** — §4 |
| A creature remembers what it has already rejected within one search | **HYPOTHESIS** |
| An idle creature with no lair tries for one **first, whatever its mood** | **CONFIRMED** (§5). The attempt can *fail* — no reachable lair with a free slot — and only then does the creature take a job |
| Being idle without a lair is the trigger; a sleep need every `TimeAwake` is a second one | **CONFIRMED** (§5). The unhappy and angry job choosers also try for a lair first, but the happy idle path does too, so mood gates nothing |

## 3. The link between creature and bed is two-way

The creature record carries the **thing id of its lair**, and the lair object
carries the **thing id of its creature** back. A helper that answers *"is this my
own lair?"* checks all of it: the id is set, the thing still exists, it is of the
object kind, it has an owner creature, and that owner is this creature.

That is exactly the relation ADR-0054 moved this project to, when
`Lair.CreatureId` on the object replaced a tile index on the creature.

## 4. Choosing where to go: a room first, then a tile

**The room is chosen before the creature moves**, and it is chosen by distance.
The finder (`FUN_004e0470`, on the player) walks the keeper's rooms, keeps only
lairs that are **reachable** and have **a free slot** (`FUN_004e53b0(1)`: used
+ 1 ≤ the room's slot count), and measures each from the creature to the
**room's centre**. It keeps two answers at once: the nearest such lair of any
kind, and the nearest that also holds **no bed whose owner is of the other
alignment** — for each bed object in the room (`TypeLair` flag), the owner
creature is tested with `FUN_0045c640`: *if I am good, is it evil? if I am evil,
is it good?* The second answer wins when it exists; the first is the fallback.
**That is Q151, answered:** a creature prefers not to bunk with the other side.
The two flags it reads are `IsEvil` (bit 7) and bit 8, which across all 46
creatures is the exact complement of `IsEvil` and is now named **`IsGood`**.

**When the finder fails** — no reachable lair with a free slot — it returns 0 and
the creature does something else. If the keeper can build lairs it also raises
an interface message: *no lair* when the keeper has none, *lair unreachable*
when every lair fails the reach test, and *lair full* once a full lair has been
full for a grace period. **A lair's slot count is its tile count** (Q156): the
room's capacity routine is a switch on room type, and for a lair it copies the
room's tile counter — a word incremented when a tile joins the room and summed
when two rooms merge — straight into the capacity field. The free-slot test is
`used + 1 ≤ tiles`. One bed per tile, exactly. **The finder tests nothing
else** — not the room's size against `RecommendedSize`, not whether it is
"functional"; ownership is implicit in walking the keeper's own room list.

**The tile is chosen on arrival, and starts underfoot.** The lair-seeking state
lives in EA's `LairState.cpp`. When the creature reaches the room, the state's
entry test requires the tile underfoot to be **the keeper's own lair tile** and
the record's `TimeSleep` to be non-zero; then, if the creature's existing bed is
already in this room, it goes straight to sleeping; otherwise a room routine
takes the creature's position and tries, in order:

1. **the tile the creature is standing on**;
2. its **eight neighbours in a fixed order**: (−1,−1), (−1,0), (−1,+1), (0,+1),
   (+1,+1), (+1,0), (+1,−1), (0,−1) — a ring from the north-west corner down the
   west side, along the south, up the east, ending due north;
3. failing both, a **chained walk through the room** starting at a **random tile**
   — the room's random draws `rand(tileCount)` and steps that far along the
   room's own tile chain from its bounding-box origin — following the chain until
   a candidate passes or the walk arrives back where it began, in which case the
   creature gets nothing this time.

Each candidate is put through one predicate: *the keeper's lair tile, with no bed
object of any kind on it*. So the **search is local first and exhaustive
second**, which is precisely what was filmed: a creature that walks into a lair
looks where it is standing, then next door, and only wanders if both fail.

**The random offset is on the walk, not the bed.** Having found a tile, the
creature walks to that tile's centre **displaced by about a third of a tile in a
random direction** (the engine's seeded random through its sine and cosine
tables), still in the lair-seeking state — which is what makes two creatures
entering from the same door stop in visibly different places. On arrival the
tile underfoot is tested *again*: if it still passes, the bed is created **at the
exact centre of the tile** with a **random facing** (0–2047 of a full turn) and
registered in the room, so the room's used-slot count rises by one; if somebody
took it during the walk, the search runs again from where the creature now
stands; if the search finds nothing, the whole attempt restarts through the
finder — possibly at another lair — and if that fails too the state ends and the
creature is idle again. Not the other way round: the jitter is on the walk
target and the bed sits centred.

**Two more things the state does.** If the creature already owned a bed in a
*different* room when the search found a tile, the old bed is destroyed there
and then — a creature moves house. And after making its bed the creature goes
to sleep **only if its health is below `CreatureSleepsWhenBelowPercentHealth`**
(balance variable 88, shipped 50); otherwise the state simply ends. The sleep
need is not consulted here — it has its own way back in (§5).

The spread between the two Salamanders is therefore explained by step 1: they
entered from the same side but were standing in different places by the time the
search ran.

## 5. What starts the search (Q150)

**There is no mood gate. An idle creature with no lair tries to get one before
it does anything else, and the same attempt is also the first thing the unhappy
and angry job choosers make.**

The creature's decision routine (`FUN_00476fc0`, one call per think) runs, in
order:

1. the current state's own update;
2. **needs** (`FUN_00477c20`): hunger, then sleep;
3. if nothing fired and the creature is **unhappy or angry**, that mood's job
   chooser (§8), whose first line is *no lair → go and get one*.

The **happy** path is the idle state's own update (the block at `004779f9`),
and it reads, in order: *no lair → try to get one* (`FUN_0046f390`); a pending
assigned job; a remembered job; the happy-job roll (§8); fallbacks (train if a
training room is available, then casino, combat pit, wander to the heart);
otherwise wander for 12 ticks.

**The attempt (`FUN_0046f390`) succeeds when:** the creature is not held and not
dying; its record's **`TimeSleep` is non-zero**; it is not already in the
lair-seeking state; it has no bed yet; its record names a bed (`LairObjectId` ≠
0); and the finder of §4 returns a room. It then enters the lair-seeking state
(`0x50`), which is the journey and the on-arrival tile test §4 describes. **It
fails when no reachable lair has a free slot**, and the creature falls through to
a job — to try again at its next idle decision.

**The two-field gate answers Q104.** The Dwarf and the Stone Knight carry
`LairObjectId` 6 but `TimeSleep` 0 (with `TimeAwake` 32767), so the original
never sends them for a bed at all: the two fields together, not the one, decide
who sleeps. The gate excludes exactly Imp, Skeleton, Unique Skeleton, Horny,
Dwarf and Stone Knight by `TimeSleep`, and the seven `LairObjectId`-0 heroes (the
princes, King Reginald, Lord of the Land, Jack) by the id.

**The second trigger is the sleep need.** The needs check keeps three fields
EA's own debug dump names `LastSleptTime`, `SleepNeeded` and
`LastAttemptedSleep`. Every `TimeAwake` (record field, file `0xEC0`) since the
creature last slept, `SleepNeeded` is set to `TimeSleep` (`0xEC4`); while it is
non-zero, and the current state may be interrupted for sleep, the creature
re-attempts the Sleep job every 4 ticks — and the Sleep job on a creature with
no lair is *the same attempt*: find a lair, go and claim a bed. Hunger runs the
same way one block earlier (`HungerNeed` grows by the level-scaled `HungerFill`
every `HungerRate`; `eating.md` §3).

**What this predicts, for the owner to break:** with a **large, empty,
reachable lair** built before the portal delivers anything, *every* creature —
Bile Demon and Troll included — goes for a bed before its first job. A Troll
that trains first has found no lair with a free slot at that moment: full,
unreachable, or counted full by a rule we have not read.

**The method note worth keeping:** a coincidence between a visible state change
and a behaviour is not the trigger until the behaviour is shown to be *absent*
without it. The mood transition and the lair search coincided on camera because
the *unhappy* chooser tries the lair first, and because an earlier attempt had
failed for a reason the camera could not see.

## 6. A variable that governs nothing

**`TimeBetweenReattemptingSleepSeconds` (id 22, value 4) is read by zero
instructions in the whole executable.** Its dispatch stub stores it into a
dedicated global and nothing ever loads that global back.

This is a real answer to "what uses it": **nothing does.** It is the third
balance variable this project has caught not meaning what its name implies,
after `MaxFreeRangeChickensPerPlayer` and `MaxGoldPileOutsideTreasury`, and the
cheapest of the three to check. Do not build a retry interval on it.

## 7. Implementation

**ADR-0060.** `CreatureSleepSystem` and the shared `LairSeeker` (this document's
§4 finder) do what §4, §5 and §7a-adjacent readings describe: a bedless idle
creature picks the nearest reachable lair with a free slot, preferring one with
no bed of the other side, walks to it, and finds its tile on arrival —
underfoot, the eight neighbours in the original's order, then a walk of the room
from a random start.

`LairRules` carries the rest: `NeedsBed` (`LairObjectId != 0 && TimeSleep != 0`,
the §5 gate, both halves), `Capacity` (the room's tile count), `HasFreeSlot`,
`IsFreeLairTile`, `HoldsBedOfOtherAlignment`, `DistanceToCentreSquared`,
`ArrivalTile`, `TryFindFreeTile`, `TryPlaceBedInAnyLair`, `PlaceBed`,
`ReleaseBed`, `BedStillValid`, `AsleepInOwnBed`, `WantsToSleep`,
`HasSleptEnough`, and `IdleRedecisionTicks` (12).

**ADR-0328.** The idle decision itself makes the attempt first, as state 89's step 5
does (`idle-fallback.md` §1): `CreatureJobSystem.Choose` tries for a lair before the
dropped job, the remembered job and either chooser, outside the chooser's gate. Until
then only `CreatureSleepSystem`'s 12-tick retry did, and a creature that turned idle
between the two systems rolled a job first; Level 3's Goblins trained bedless from
arrival, reached `AngerNoLair`'s limit and left through the portal, where DK2's
(capture slot 431: idle at 1756, state 89, then the lair walk at 1757) went for a bed
before their first job.

**Ours, and marked as such in the code:** the idle retry cadence (12 ticks); the
arrival tile — room centre, where the original's travel-to-room step
(`FUN_00471A70`, read in travel-states.md §2.2) walks to a random tile of the room,
then the next walkable one (**Q157**); the order of the room walk (ascending
tile index, with the random start kept); and no sub-tile jitter on the walk
target.

**Not added deliberately:** an implementation plan drawn from this note by
another tool required the lair to be *"valid/functional according to the
applicable lair rules"*. The finder tests room type, reachability, a free slot,
distance and the alignment of existing beds — **nothing about size or
function**, so that test must not exist here either.

## 7a. How OpenKeeper does it, for the record

OpenKeeper's `RoomLairControl` holds one bed per tile (`getObjectsPerTile` = 1)
— the same capacity rule, arrived at by guess. Everything else differs from the
binary: its `TaskManager` picks the room *and the tile* centrally before the
creature moves and reserves it as a task; there is no on-arrival search, no
neighbour ring, no random start to a room walk, no alignment preference, no
moving house, and no health threshold deciding whether to sleep afterwards. Its
outcome — creature walks in, bed appears — is right; its mechanism is not
evidence for anything above.

## 8. The job choosers, read on the way

Three functions roll a creature's job for its mood — happy (`FUN_004780e0`),
unhappy (`FUN_004782c0`), angry (`FUN_004784c0`) — and they are the runtime of
the `HappyJobs` / `UnhappyJobs` / `AngryJobs` lists our CREATURES parse already
carries. Each builds cumulative ranges from the entries' chances, sorts them,
rolls `rand(100)`, tries the job whose range holds the roll, and on failure
tries every job in list order. A re-roll is allowed only after **five game
seconds** (5 × variable 19). **The unhappy and angry choosers try for a lair
before rolling; the happy one does not, because the idle state already did.**
`job-selection.md` carries the choosers and the dispatcher in full.

The job ids match the record: **1 Sleep** (the lair attempt when bedless, bed
when not), 2 Eat, 3 Research, 4 Train, 5 Manufacture, 6 Guard, 7 Torture,
8 Pray, 9 Drink, 10 Leave, 11 Destroy enemy rooms, 12 Destroy walls, 13 Steal
gold — dispatched by `FUN_00478800(job)`.

**Hero and neutral creatures skip all of this** — the decision routine sends
them to their own routines by owner id before needs are looked at.

**Built 2026-09-05 — ADR-0061.** The angry chooser's half is in
`CreatureAngerSystem`: the five-second gate, the lair attempt before the roll
(through `LairSeeker`), the cumulative ranges and the fall-through. Two
arithmetic quirks that only show under a second reading — the ranges are
inclusive at both ends, and the "sort" cannot reorder anything — are in
`creature-anger.md`, "The chooser, read from the binary".

## 9. The six lair and sleep handlers

The six contiguous handlers answer what states 79–84 do. Claims below are
**STRONGLY INFERRED** from complete reads of each handler; helper names not
already established elsewhere remain **UNKNOWN** rather than being guessed.

| state | reads | important calls and writes | exit |
|---|---|---|---|
| 79, looking for Lair | tile underfoot, room owner/type, `TimeSleep`, current bed and its room | accepts an existing bed in this room; otherwise finds a free tile, destroys a bed in another room, and gives the walk a seeded random offset | existing bed → 81; candidate → travel to 80; no candidate → message and idle |
| 80, making a home | first-tick marker, current tile, room, candidate validity | stops the old action on entry; validates and registers the bed at the tile, or searches and walks to another candidate | placed bed → 81; candidate → 80 again; exhausted room → restart the global lair finder or end |
| 81, going to bed | bed id, kind, validity, position and facing | derives a point from the bed facing, ends an active action, starts movement | travel to the point in state 82; invalid bed ends the state |
| 82, sleeping | bed id, position and facing; creature position | turns toward the bed; when close, snaps ordinary-bed users to it and copies its facing | close and aligned → 83; too far → 81; no bed ends the state |
| 83, sleeping | bed validity/type, `SleepNeeded`, level-scaled maximum and current health | zeros velocity; starts the bed animation; maintains bed/creature presentation flags; writes a 48-tick effect counter | stays while sleep need is non-zero or health is below maximum; ready in the Mistress bed → 84; ready in an ordinary bed → clean up and end |
| 84, sleeping | bed kind and presentation flags, first-tick marker | performs the Mistress-bed exit animation and placement; its field writes are presentation state, not another rest counter | entry tick remains in 84; a ready Mistress-bed exit → 87, wandering; invalid/non-object bed returns success without a transition |

This separates three activities that share labels in the table. State 79 is an
on-arrival room/tile selector, while state 80 owns the repeated walk, placement
and retry loop. The normal bedless-lair finder enters **80**, not 79. One entry to
79 is the carry: state 45 (`0x00468420`) puts a casualty down on a Lair tile and kicks it to 1,
then 79 (ADR-0417). State 81 is the journey to an existing
bed, state 82 is final alignment, and **83 is the main sleeping state**. State
84 is only the special wake/get-out path for object 23, the Mistress Creature
Bed. Consequently `CreatureActivity.Sleeping` maps to state 83 for migration;
82 and 84 are not interchangeable sleep states.

The state-category hook supplies the rest operation that state 83 itself does
not: it subtracts one from `SleepNeeded` every game tick, flooring at zero, and
runs the lair effect once per game second. The handler leaves only after
`SleepNeeded == 0` **and** current health has reached the creature's
level-scaled maximum. This is a real mismatch with `CreatureSleepSystem`, which
woke after `TimeSleep` regardless of health until ADR-0173 corrected it (Q403,
owner 2026-09-23): `LairRules.HasSleptEnough` now takes the database and requires
both.

States 82 and 84 have table flag byte `+0x18 = 5`, while 83 has 4. The only
difference is bit 0. None of these handlers reads that byte, and the executable
reader survey found no test of bit 0. There is therefore no evidence that it
gates rest depletion, healing, interruption or animation; its meaning remains
**UNKNOWN** (Q185). The observed differences come from the explicit state id,
the handler body and the bed kind.

Finally, none of the six handlers writes a source read by the lair effect's
virtual health predicate. State 83 reads health only for its own exit test.
That does not identify why the once-per-second effect sometimes heals, so the
health half of Q123 remains **UNKNOWN** and must not be widened by inference.

### 9.1 How the chain moves, read for ADR-0178 — CONFIRMED

Three exits, each read instruction by instruction on 2026-09-24:

- **80 after placing the bed** calls `0x00488840`, a percentage test of current
  health against the level-scaled maximum. True sets **81** (`0x0046F793`);
  false returns 0, which the dispatcher turns into idle. That is our
  `ShouldSleepFromWounds` branch. Just before it, `0x00486F80(2)` **zeroes the
  lair-anger bucket**: a creature that gets a bed forgets that grievance at once
  (`payday.md` §4.4 reads the routine). **Built** (ADR-0179). A candidate walk is `0x00471890` with 80 as
  its own resume (`0x0046F7D3`); an exhausted room calls the lair attempt
  `0x0046F390` again, from 80.
- **81** walks to the bed point in state 6, resuming in **82** (`0x0046F8EC`).
- **82** moves to 83 when aligned and within `0x1000` squared (one tile), and to
  81 when farther. **Each move is guarded the same way**: `SetState(target)`;
  if refused, `SetState(1)`, then `SetState(target)` again (`0x0046F9F8`,
  `0x0046FA28`). The rule refuses 82→81, since the two share group 7 but not
  the category, so "too far" reaches 81 through idle. 82→83 is allowed and the
  fallback never runs.

Every other move of the chain is legal under the rule: 1→7, 80→81, 81→6, each
handover, and 80, 81, 82 and 83 down to 1.

## Tests

`LairAcquisitionTests` and `CreatureAngerTests`
(`tests/DK2.Simulation.Tests/`, both citing §4, §5 and §8);
`TheDwarfAndTheStoneKnightCarryABedIdAndAreNeverSentForOne` and
`TwelveCreaturesNeverGoForABedAndTheyAreTheOnesWithNowhereToLiveOrNoSleepToSleep`
(`tests/DK2.Behavior.Tests/CreatureNeedsOnRealDataTests.cs`).

## Known exceptions

- `TimeBetweenReattemptingSleepSeconds` parsed and never applied (§6), because
  the original never reads it.

## Open questions

- **Q157**: which tile of the chosen room the travel-to-room step aims at.
  UNKNOWN; ours arrives at the room centre.
- Whether a creature remembers what it has already rejected within one search
  (§2). **HYPOTHESIS.**
- The exact idle re-decision cadence. The idle state's own fallback is a 12-tick
  wander, after which it decides again; ours uses the same 12.
