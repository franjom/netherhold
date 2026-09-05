# How a creature gets a lair, and how ours now does

**Sources:** the owner's recordings of DK2 v1.70 (2026-09-05, analysed by a
second assistant) and `DKII.EXE` read the same day. Facts in our own words;
ADR-0046 governs the boundary.

**Built 2026-09-05 (ADR-0060).** `CreatureSleepSystem` now does what §4, §5
and §7 describe: a bedless idle creature picks the nearest reachable lair with
a free slot (preferring one with no bed of the other side), walks to it, and
finds its tile on arrival — underfoot, the eight neighbours in the original's
order, then a walk of the room from a random start. §1–§2 describe what was
watched and what the old implementation got wrong; they are kept as the record
of why. What is still ours is listed at the end of §7.

## 1. What was watched

**In a large lair.** A Salamander came in from the upper right, **crossed the
room**, and put its bed down near the far side. A second Salamander entered from
the same direction and chose a spot on the **opposite** side from the first. A
Warlock later put its chair down right beside the first Salamander's bed.

**In a 1×5 lair**, which is the controlled version of the same thing, four
creatures claimed tiles in this order:

| | creature | tile |
|---|---|---|
| 1 | Goblin | 1 |
| 2 | Warlock | 3 |
| 3 | Goblin | 5 |
| 4 | Salamander | 4 |

The Salamander was **seen to walk up to an occupied tile, reject it, and carry on
to a free one**. That was not a one-off: creatures were repeatedly watched
visiting taken lair tiles, turning them down and trying others.

**And the trigger looked like it was not availability.** A free lair tile
existed while a bedless Bile Demon and a bedless Troll went off to the **Training
Room** instead, and only went looking for a lair later, when the mood turned
**Unhappy** with "no lair" as the reason. (The first write-up said Goblin; the
owner corrected it to Troll — *Goblins always went for the lair first*, as did
Salamander, Warlock and Firefly, and the Bile Demon and Troll were **not
consistent**: on other runs they went lair-first too.) §5 and §8 are what the
binary made of that.

## 2. What follows, and what it costs us

| Claim | Status |
|---|---|
| DK2 does not simply assign the lowest free lair tile | **CONFIRMED** — the 1×5 order was 1, 3, 5, 4 |
| Acquiring a lair is a **journey**, not a reservation | **CONFIRMED** — creatures cross the room to place a bed |
| Occupancy is tested **on arrival**, not when choosing | **CONFIRMED** — a candidate was rejected in front of the camera |
| A rejected candidate is followed by another attempt | **CONFIRMED** |
| Candidate choice contains randomness, or something that looks like it | **STRONGLY INFERRED** from the watched spread, and **corroborated by the binary** — see §4 |
| A creature remembers what it has already rejected within one search | **HYPOTHESIS** |
| **A happy bedless creature does not claim a free tile** | **WRONG as first stated, and the binary says why.** An idle creature with no lair tries for one *first*, whatever its mood — the attempt can *fail* (no reachable lair with a free slot), and only then does it do a job. The Bile Demon and Troll that trained first were the failure case; the four types that "always" went lair-first were the rule. See §5, §8 |
| **`AngerNoLair` accumulating to unhappiness is what starts the search** | **Half right.** The *unhappy* and *angry* job choosers do try for a lair first, unconditionally — but so does the *happy* idle path. Mood is not the trigger; **being idle without a lair is**, and a sleep need every `TimeAwake` is a second trigger. §5 |

**Contradicted, and it is ours:** `CreatureSleepSystem` claims a bed on the first
tick one is free, from wherever the creature happens to be standing, with no walk
and no mood test. Both halves are wrong. It was built from the owner's earlier
and entirely accurate description — *"when a creature enters a dungeon through a
portal it goes to a lair, finds a free tile and occupies it with its bed"* — of
which this project implemented the second half and skipped the going.

## 3. The link between creature and bed is two-way, and ours already matches

The creature record carries the **thing id of its lair**, and the lair object
carries the **thing id of its creature** back. A helper that answers *"is this my
own lair?"* checks all of it: the id is set, the thing still exists, it is of the
object kind, it has an owner creature, and that owner is this creature.

That is exactly the relation ADR-0054 moved this project to on 2026-09-04, when
`Lair.CreatureId` on the object replaced a tile index on the creature. Nothing to
change.

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
full for a grace period. **A lair's slot count is its tile count** (Q156, read
2026-09-05): the room's capacity routine is a switch on room type, and for a
lair it copies the room's tile counter — a word that is incremented when a tile
joins the room and summed when two rooms merge — straight into the capacity
field. The free-slot test is `used + 1 ≤ tiles`. One bed per tile, exactly.
**The finder tests nothing else** — not the room's size against
`RecommendedSize`, not whether it is "functional"; ownership is implicit in
walking the keeper's own room list.

**The tile is chosen on arrival, and starts underfoot.** The lair-seeking
state lives in EA's `LairState.cpp`. When the creature reaches the room, the
state's entry test requires the tile underfoot to be **the keeper's own lair
tile** and the record's `TimeSleep` to be non-zero; then, if the creature's
existing bed is already in this room, it goes straight to sleeping; otherwise a
room routine takes the creature's position and tries, in order (all read
2026-09-05):

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
second**, which is precisely the behaviour that was filmed: a creature that walks
into a lair looks where it is standing, then next door, and only wanders if both
fail.

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
creature is idle again. **The earlier reading of this paragraph had the jitter
on the bed's position; it is on the walk target, and the bed sits centred.**

**Two more things the state does.** If the creature already owned a bed in a
*different* room when the search found a tile, the old bed is destroyed there
and then — a creature moves house. And after making its bed the creature goes
to sleep **only if its health is below `CreatureSleepsWhenBelowPercentHealth`**
(balance variable 88, shipped 50); otherwise the state simply ends. The sleep
need is not consulted here — it has its own way back in (§5).

The spread between the two Salamanders is therefore explained by step 1: they
entered from the same side but were standing in different places by the time the
search ran.

## 5. What starts the search — READ 2026-09-05 (Q150)

**There is no mood gate. An idle creature with no lair tries to get one before
it does anything else, and the same attempt is also the first thing the unhappy
and angry job choosers make.** The apparent "goes to train first" is the attempt
*failing*.

The creature's decision routine (`FUN_00476fc0`, one call per think) runs, in
order:

1. the current state's own update;
2. **needs** (`FUN_00477c20`): hunger, then sleep — see below;
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
0); and the finder of §4 returns a room. **The `TimeSleep` gate is the half our
`LairRules.NeedsBed` lacks**, and it answers Q104: the Dwarf and the Stone Knight
carry `LairObjectId` 6 but `TimeSleep` 0 (with `TimeAwake` 32767), so the
original never sends them for a bed at all — the two fields together, not the
one, decide who sleeps. The gate excludes exactly: Imp, Skeleton, Unique
Skeleton, Horny, Dwarf, Stone Knight (by `TimeSleep`), and the seven
`LairObjectId`-0 heroes (the princes, King Reginald, Lord of the Land, Jack). It then enters the
lair-seeking state (`0x50`), which is the journey and the on-arrival tile test
§4 describes. **It fails when no reachable lair has a free slot**, and the
creature falls through to a job — to try again at its next idle decision.

**The second trigger is the sleep need.** The needs check keeps three fields
EA's own debug dump names `LastSleptTime`, `SleepNeeded` and
`LastAttemptedSleep`. Every `TimeAwake` (record field, file `0xEC0`) since the
creature last slept, `SleepNeeded` is set to `TimeSleep` (`0xEC4`); while it is
non-zero, and the current state may be interrupted for sleep, the creature
re-attempts the Sleep job every 4 ticks — and the Sleep job on a creature with
no lair is *the same attempt*: find a lair, go and claim a bed. Hunger runs the
same way one block earlier (`HungerNeed` grows by the level-scaled
`HungerFill` every `HungerRate`).

**What this predicts, for the owner to break:** with a **large, empty,
reachable lair** built before the portal delivers anything, *every* creature —
Bile Demon and Troll included — goes for a bed before its first job. A Troll
that trains first has found no lair with a free slot at that moment: full,
unreachable, or (Q156) counted full by a rule we have not read.

**Why the earlier reading was wrong, kept for the method.** The mood transition
and the lair search coincided in the recording because the *unhappy* chooser
tries the lair first — a real rule — and because the creature's earlier attempt
had failed for a reason the camera could not see. A coincidence between a visible
state change and a behaviour is not the trigger until the behaviour is shown to
be *absent* without it, and the owner's four other creature types showed the
opposite.

## 6. A variable that governs nothing

**`TimeBetweenReattemptingSleepSeconds` (id 22, value 4) is read by zero
instructions in the whole executable.** Its dispatch stub stores it into a
dedicated global and nothing ever loads that global back.

This is a real answer to "what uses it": **nothing does.** It is the third
balance variable this project has caught not meaning what its name implies, after
`MaxFreeRangeChickensPerPlayer` and `MaxGoldPileOutsideTreasury`, and the
cheapest of the three to check. Do not build a retry interval on it.

## 7. What this means for the rework

**Built 2026-09-05 — ADR-0060.** The shape below is what was built; the list
is kept as the specification it was. Marked as ours in the code: the idle
retry cadence (12 ticks), the arrival tile (room centre, Q157), the order of
the room walk (ascending tile index; the random start is kept), and no
sub-tile jitter on the walk target. The shape it had to take:

- a creature with no lair does **not** claim one until something makes it want to;
- wanting one puts it into a state that **picks a room by distance** and walks
  there;
- on arrival it tests **where it is standing**, then its immediate neighbours,
  then wanders the room;
- occupancy is decided **at the moment of the test**, so two creatures heading for
  the same room is normal and one of them losing is normal.

**Every part is now read** (§3, §4, §5, §6, §8), so the rework is buildable
without a guess: an idle bedless creature attempts a lair first; the room is the
nearest reachable lair with a free slot, preferring one with no beds of the
other alignment; the tile is found on arrival; failure falls through to a job
and is retried at the next idle decision; a sleep need every `TimeAwake` is the
second way in. What is still unread and must be marked as ours when built: the
exact idle re-decision cadence (the idle state's own fallback is a 12-tick
wander, after which it decides again), and **which tile of the chosen room the
travel-to-room step aims at** — the arrival test only requires *some* tile of
the keeper's lair underfoot (Q157). Q156 (one bed per tile) and the
neighbour-offset table are now read (§4), and so is the gate: `TimeSleep ≠ 0` as
well as `LairObjectId ≠ 0`, which our `NeedsBed` must gain (Q104).

**Checked against a third party's plan, 2026-09-05.** An implementation plan
drawn from this note by another tool matched the binary on every point it made
from the note, and added one clause the binary does not have: it would also have
required the lair to be *"valid/functional according to the applicable lair
rules"*. The finder tests room type, reachability, a free slot, distance and the
alignment of existing beds — nothing about size or function. The rework must
not add that test.

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

The job ids match the record: **1 Sleep** (the lair attempt when bedless, bed
when not), 2 Eat, 3 Research, 4 Train, 5 Manufacture, 6 Guard, 7 Torture,
8 Pray, 9 Drink, 10 Leave, 11 Destroy enemy rooms, 12 Destroy walls, 13 Steal
gold — dispatched by `FUN_00478800(job)`.

**Hero and neutral creatures skip all of this** — the decision routine sends
them to their own routines by owner id before needs are looked at.

**Built 2026-09-05 — ADR-0061.** The angry chooser's half of this is now in
`CreatureAngerSystem`: the five-second gate, the lair attempt before the roll
(through the shared `LairSeeker`, which is this document's §4 finder), the
cumulative ranges and the fall-through. Two arithmetic quirks that only show
under a second reading — the ranges are inclusive at both ends, and the "sort"
cannot reorder anything — are written out in `creature-anger.md`, "The chooser,
read from the binary". The happy and unhappy choosers have no jobs to run yet.

