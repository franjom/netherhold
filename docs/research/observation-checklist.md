# What can be settled by playing the game

Open questions that a person sitting in front of DK2 can answer by **looking**,
ordered by what they unblock. Each entry is one experiment and what to watch.

This list exists because of a process failure worth not repeating: several
questions were written up as UNKNOWN on the grounds that the KWD data could not
settle them, when the owner could have settled them in a sentence. The original
is this project's behavioural oracle and the owner has played it. See
`HANDOFF.md` §"Research method that has been working", step 0.

**The test for whether a question belongs here: would a player *see* it happen?**
If yes, it is on this list. If it is an internal — tick rate, per-tile
arithmetic, update order, RNG — it is not, and no amount of playing will settle
it. The last section says which those are, so nobody wastes an evening on them.

Answers go in `open-questions.md` marked **CONFIRMED — owner observation**, with
the same weight as a data extract. A "no" is as useful as a "yes"; recording that
something does *not* happen closes a question just as hard.

---

## Answered so far (2026-08-29)

Nineteen questions went onto this list; **fifteen came back in two sittings**.
Kept as a record of what the method is worth, and because several of the answers
are load-bearing enough that a future session should see them here rather than
having to dig them out of the question list.

| Q | What playing settled |
|---|---|
| Q12 | Imps never eat. The Imp's hunger rate of 32767 does mean "never" |
| Q20 | Rock: 2-3 hits, 1-2 s. Gold: 4-5 hits, 1.5-3 s |
| Q21 | A seam pays out **gradually as it is mined**, not in a lump |
| Q24 | Job order: **marked excavation → claim → reinforce → wander** |
| Q25 | Nearest **by walking distance**; marking order is irrelevant |
| Q26/Q43 | Three imps a face, twelve a block — and a fourth **goes to find other work** rather than queueing |
| Q27 | Work is **orthogonal**: twelve a block at three a face is four faces |
| Q28 | Creatures **can cut a diagonal corner** — movement is eight-connected |
| Q30 | Terrain does **not** weight a path; the shortest route wins even through water |
| Q31 | An imp mines **into its own hands** and delivers when full; a floor lump is the fallback for when there is nowhere to store it |
| Q33 | A room tile must **touch land the keeper owns** |
| Q34 | Selling refunds **50% of the cost, per tile** — and the data says 50 too; the "neither variable" note was our own transcription error |
| Q36 | A 1×1 temple really is buildable; minimum sizes are about **function**, not building |
| Q37 | The mana ceiling is a flat **200000** and never moves |
| Q38 | The mana gain cap is **per keeper**, not shared |
| Q40 | On defeat, creatures **leave by the nearest portal** and **rivals claim the rooms and gold** |
| Q42/Q44 | An imp **jumps onto** the tile it claims, and only claims from beside land its keeper owns |
| Q46 | Claiming a tile takes **2-4 jumps, about 2 seconds** |
| — | A keeper has **exactly one dungeon heart**, and losing it is defeat |
| Q7 | What angers a creature: **no lair, hunger, no pay, the company of creatures they hate** |
| Q9 | Unpaid → unhappy → angry → **leaves**; a converted creature **rebels** instead |

Four of those contradicted the implementation (Q24, Q28, Q42/Q44, Q43) and one
contradicted the data (Q37's ceiling). That is the list working. **Q34 looked
like a second such contradiction and was not**: `RoomSellValuePercentageOfCost` is
50, exactly as observed, and had been mis-transcribed here as 100 for four
sessions - our own extracted dump said 50 the whole time. Check the dump before
recording a number as absent.

**One was also corrected by the owner after the fact** — the claim count came
back as ten jumps and was revised to 2–4 the same day. Worth recording as
process, not embarrassment: at ten there was no common factor between the
observed work rates and the whole tile-work model looked wrong; at 2–4 the factor
is about two everywhere and the model is fine but too coarse. A wrong number
recorded confidently would have sent the next session rewriting something that
was not broken. Ranges and "I am not sure" are more useful here than round
numbers, and Q35 is explicitly flagged as belief rather than observation for the
same reason.

---

## Answered on 2026-08-30 — the measurements

Three numbers arrived, and they were worth more than the fifteen yes/no answers
before them, because a number propagates and a yes does not.

| Q | What was measured | What it settled |
|---|---|---|
| **Q47** | **273 gold per swing** at a gold seam | The whole tile-work granularity. 273 × 660 / 3000 = 60 health a swing, so 11 swings a seam, so an application is a **fifth** of the tabled delta and work lands **five times a second**. Every application count in the game changed; no time in seconds did |
| **Q32** | Room costs: Treasury 200, Lair 300, Hatchery 300, Library 600, Training Room 500 — **per tile** | All five match the ROOMS record exactly. `Cost` is per tile and the record's number is the price |
| **Q52** | A keeper with no treasury tops out at **16000 gold** | `MaxGoldPerDungeonHeartTile` is 1000, so 16 heart tiles store gold. A heart is 5×5 = 25, and 25 − 16 = 9 = the 3×3 the heart structure stands on. **Ours stores 25000** — a defect we cannot fix until rooms are instances |

A screenshot settled two more at a glance: **"Mana held: 15081 / 200000"** put
Q37's flat ceiling on screen, and the heart's own geometry — a raised 3×3 with
gold piles ringing it — is what makes Q52's 16 tiles obvious rather than inferred.

**Screenshots are the best format for this.** A still frame with the HUD visible
is worth more than video: the numbers can be read directly, and a counter before
and after is a measurement. Video cannot be read at all.

### What Q47 cost by being asked properly

Worth recording next to the correction below, because it is the same lesson from
the other side. Q47 had a plausible answer — "twice a second, half the delta" —
that fitted the ranges, was self-consistent, and was **wrong**. It was left
unimplemented on the grounds that ranges are not counts. One measured number
later, the answer turned out to be five a second, and **825 is odd**, which alone
rules out the twice-a-second reading. Not having built it made the correction
free; building it would have changed every timing in the game twice.

---


## Answered on 2026-08-30, second sitting — imps

| Q | What was settled |
|---|---|
| — | Every keeper starts with **4 imps**, and 4 is a **floor**: drop them all into the heart and they come back at ~5s intervals. The data names both — `MinimumImpThreshold` 4, `TimeBeforeFreeImpGeneratedSeconds` 5 |
| Q54 | Create Imp costs **1500**, rising by 1500 each time — **and falling back** when imps die, because the free four never count towards the price. That second half is what made the rule provable rather than merely fitted |
| Q55 | The 1500 is DK2's own: it is the mana cost on the Create Imp record in `KeeperSpells.kwd`, at an offset that holds a plausible cost for all thirteen spells |

Both are **implemented** — `ImpSupplySystem`, and the first thing in this project
that spends mana rather than only earning it.

## Answered on 2026-08-30, third sitting — the mana drain

| Q | What was settled |
|---|---|
| **Q53** | **7 mana a second, per imp.** Two screenshots: four imps read −28, five read −35. Two readings rather than one, so the *unit* is confirmed as well as the rate — and it found the field, which a single reading would not have |
| **Q39** | The rates are **per second**, to the unit. Between the two frames mana moved 2684 → 2369 across a 1500 spend, so the keeper gained 1185; at +122/s then +115/s that is 9.7–10.3 seconds, and the HUD clock moved from 38 to 28. Two numbers read for different reasons, agreeing |
| **Q54** | Photographed rather than described: the second frame's tooltip reads **"Create Imp. Spell Grade 1. Mana Cost 3000"** with five imps, which is the implemented `1500 × (5 − 4 + 1)` |
| **Q37b** | **A claimed mana vault is +100 a second.** Already implemented — `TerrainDef.ManaGain` for Claimed Vault is 100 — so nothing changed, but it is the first time the *size* of a mana rate has been checked against the game rather than only its sign |
| **Q59** | **Traps draw mana continuously**, a standing charge per trap built and not only a cost per trigger |
| **Q58** | Possession showed **no** continuous drain. Recorded as a disagreement, not an answer — see below |

**Why the screenshots were worth so much more than a description.** The blocker
was never "what is the rate", it was "where does the game keep it". A measured 7
turned an offset scan from a search needing two unverified numbers to be true
into one needing none — and the scan then said, trustworthily, *not in the
creature record*, which is what pointed at the spell record. The field was found
within minutes of the second screenshot arriving. See `imps-and-mana.md`
§*How this was established*.

---

## Answered on 2026-09-05 — by a recording, and one of them reversed shipped behaviour

The owner filmed the game and a second assistant turned the footage into a list
of numbered questions for the decompiler. That pipeline is worth naming: **it
found a defect three weeks old that no test could have caught**, because the
tests agreed with the wrong rule.

| Q | What was settled |
|---|---|
| **Q56** | **The free-imp clock is a DEADLINE, not a countdown.** Thirty seconds at exactly four imps, one sacrificed, and the replacement arrived in **~1.4 seconds** — then ~5s for each further one. Ours waited a fresh five seconds every time. Fixed, ADR-0057 |
| Q100 | **Regeneration does not care that a creature is working.** A wounded imp healed while digging and while running. Which is what ours already did — by omission, and now by observation |
| Q150 | **A happy bedless creature does not claim a lair.** It went to the Training Room instead, and only looked for a lair when its mood turned Unhappy over not having one. **Ours claims a bed the first tick one is free** |
| — | **Claiming a lair is a journey.** Creatures crossed a lair, walked up to occupied tiles, rejected them and moved on. A 1×5 lair filled 1, 3, 5, 4 |
| — | Imps held in the Hand of Evil are **not** in the imp portrait's count |

### Q148 — does an imp in the Hand of Evil count towards the four? — **ANSWERED the same hour, and the answer was yes**

Asked and answered inside one exchange: hold one of exactly four imps, wait, and
**no fifth imp appears**. So the free supply's count **includes** a held imp,
and the imp portrait's figure — which visibly excludes them — is a different
count kept for a different purpose.

**Why it was worth asking before the Hand of Evil exists.** When somebody builds
picking creatures up, the natural thing to reuse is the count the interface
shows, and that would let you hold an imp indefinitely and farm a fifth one out
of the dungeon heart. The rule is now written at
`ImpSupplyRules.ImpCount`, which is the code that would have broken.

It also agrees with what the binary said independently: the tally the supply
reads is moved only when a thing gains or loses an **owner**, and picking a
creature up does not change whose it is.

### Q150 — what makes a creature go and get a lair? — *added 2026-09-05, and it contradicts shipped behaviour*

The observation is already made and is not in doubt. What would sharpen it:

1. drop a **fresh, happy, bedless** creature into a dungeon with a free lair
   tile. Time how long before it goes for the lair, and watch what it does
   meanwhile;
2. do it again with a creature whose anger is already part-way up;
3. if there is a moment where the mood flips and the creature turns on its heel,
   that timing against `AngerNoLair` is the whole answer.

**Why it matters:** ours claims a bed on the first tick one exists, from
anywhere, so a creature that should be training is asleep instead, and beds are
taken in an order the original would never produce.

### Q87 — is a level's creature pool a standing limit or a draining quota? — *promoted 2026-09-05, and it had been sitting where nobody would look*

**Found by applying the new review clause to this project's own comments** (see
`GOAL_ALIGNMENT.md`, 2026-09-05): a hedge that names the observation which would
settle it is not a defence, it is an experiment nobody has run.
`PortalRules.PoolLimit` says *"on level 1 the two are indistinguishable, because
the pool and the first portal's cap are both 15"* — which is a statement about
**level 1**, and the campaign has 65 other levels.

**The experiment.** Play any level whose pool for some creature type is larger
than the portal capacity that gates it, take the full complement of that type,
then **sack or kill one** and wait.

- **another one arrives** → standing limit, which is what we implement;
- **nothing arrives, ever** → draining quota, and every long level in the
  campaign eventually starves in a way ours never will.

Ours is a standing limit on three arguments (OpenKeeper compares against the
current count, the 1999 manual says a sacked creature *"[is] replaced by a new
creature"*, and the file has nowhere to record depletion). It is a good case. It
is not an observation.

### Q152 — does regeneration continue in combat, in the hand, or under possession? — *added 2026-09-05*

Three separate ten-second looks, and the first is the one that changes a game:
an imp regenerates **67 a second**, which is meaningful inside a fight and
irrelevant outside one.

1. wound an imp, start a fight it will survive, and watch the bar;
2. wound one and hold it in the hand;
3. wound one and possess it.

Ours regenerates in all three.

---

## 1. Still open, and blocking

### Q141 — where does "fetch that gold" sit in an imp's job priority? — **ANSWERED, and the owner's doubt was the valuable part**

The owner reported:

> *"If there is gold on the ground an imp will first take from that gold pile
> and take it to a treasury, until all of it is gone."*

This project read that as fetching **outranking marked excavation** and built it
that way. The owner then read their own report back, said they were not sure, and
**asked for the binary instead of letting it stand.**

**The binary disagrees.** DK2 keeps a keeper-wide task list whose entries carry a
kind, and one function tries the kinds in a fixed order: digging and mining a
marked tile are kinds 1 and 2, the **first two**; floor gold is kind 7, the
**sixth of nine**. Q24's August answer was right all along.
`docs/research/imp-task-priority.md` has the whole chain.

**What is left to watch is Q147 below**, and it may make the priority question
disappear entirely.

### Q147 — is the priority global, or a tie-break within one position? — *added 2026-09-05, and it is now the most valuable thing on this page*

**The chain that orders DK2's task kinds is a callback**, invoked from a scan,
and every handler takes a position and looks around it. So the original's primary
key is **where** and the kind order breaks ties **within** a position. This
project does the opposite — one whole-map search per priority — so the kind is
primary and distance breaks ties.

**That difference explains the Q141 observation with no priority claim at all.**
Imps had just mined a gem tile; gold was lying on every tile around them; so the
nearest position with any task was a gold position, and every imp fetched while
the gem tile stood marked and untouched. Not because gold outranks mining —
because it was underfoot.

**The experiment, and it is two minutes.** Take one imp with nothing else to do:

1. mark a wall **far away**, and drop gold **at the imp's feet** (sell a treasury
   tile under it, or drop a pile from the hand). Which does it go to?
2. now mark a wall **right beside the same imp**, with gold still on the floor a
   few tiles off. Which does it go to?

**If the far wall wins the first**, priority is global and ours is the right
shape. **If the gold wins the first and the near wall wins the second**, DK2
picks the nearest position and only then decides what to do there — and this
project's whole job-selection model is wrong in a way that has nothing to do with
gold.

Worth doing before anything else in the imp code, because it decides whether
Q146 is a real question or an artefact.

### Q145 — does fetching interrupt a swing already in progress? — *added 2026-09-05*

Ours finishes the wall it is swinging at, then turns round. Q141's observation
cannot settle this: a **gem** tile is inexhaustible, so an imp re-chooses its
job after every completion, and "interrupt now" and "interrupt at the next
choice" look identical from outside.

**Use a finite job.** Mark a long stretch of ordinary rock, let imps get well
into a tile, then drop gold beside them — sell a treasury tile, or drop a pile
from the hand. Do they abandon the tile mid-swing, or finish it first? Visible
in a second, and it is the last thing between this mechanic and CONFIRMED.

### Q101 — the lair — **ANSWERED the same day, by the owner**

> *"A lair is a room with tiles, on each tile only one bed/chair/whatever per
> creature can be present. When a creature enters a dungeon through a portal it
> goes to a lair, finds a free tile and occupies it with its bed. When a
> creature is injured it heals while it sleeps in its own bed."*

**Asleep, and in its own bed.** Both halves of the shipped proxy were wrong, and
the answer settled a third thing nobody had asked — a lair holds one creature
per tile. Built as ADR-0049. The follow-ups below (Q103, Q104, Q105) are what
is left.

<details><summary>The question as it was asked</summary>

### Q101 — the lair: what actually heals a creature?

**The weakest reading in the whole health mechanic**, and the one most worth an
hour of play. Implemented: a creature heals at **100 a second** while standing
on a lair tile its keeper owns. DK2 gives each creature a **personal** lair — a
specific tile with its own bed — and heals it while it *sleeps* there.

**Two questions:**

1. Must it be **asleep**, or is standing in the lair enough?
2. Must it be **its own** lair? Does a wounded goblin heal in any lair its
   keeper owns, or only in the one belonging to it — and does a creature with no
   lair of its own heal at all?

**The experiment.** Wound a creature (drop it into a fight, or leave it on a
rival's land), then drop it on a lair tile that belongs to a *different*
creature and watch whether its health bar climbs while it stands there awake.

Healing is the main way anything recovers in DK2, so this sets the pace of every
fight and every siege. See `docs/research/creature-needs.md` §7.

</details>

### Q103 — is the sleep cycle 240 seconds up and 9 down? — *added 2026-09-04*

A Goblin's record says `TimeAwake` 240 and `TimeSleep` 9, and both are read as
seconds. Four minutes awake and **nine seconds asleep** looks wrong until you
multiply: nine seconds at 100 health a second is **900**, well over half a
Goblin's 1500. A short sleep is a big heal.

**The experiment.** Watch a healthy goblin in a dungeon that has a lair. Does it
go to bed roughly every four minutes, and is it up again inside ten seconds?

### Q106 — what is anger measured in? — *added 2026-09-04, and it blocks the whole mood mechanic*

**Anger is not built, and this is the only reason.** Every rate is known — a
Goblin accrues `AngerNoLair` **100 a second**, a lair calms at **−550**, a slap
is **+3000** — and `UnhappyThreshold` is **50 on all 46 creatures**, which makes
it a percentage. Of what? **There is no ceiling in the 214 balance variables,
none in the creature record, and none in OpenKeeper** (its mood system is an
empty stub).

| Ceiling | Lairless Goblin unhappy | …leaves | One slap |
|---|---|---|---|
| 65535 | ~5.5 min | ~11 min | 4.6% |
| 10000 | ~50 s | ~100 s | 30% |

Both are self-consistent, and a wrong ceiling propagates into job selection and
desertion rather than staying in one rule. This is **Q47's lesson applied**: a
plausible-but-wrong answer left unbuilt cost nothing to correct.

**The experiment — two of them, neither needing precision:**

1. Give a creature **nowhere to sleep** (no lair, or a full one) and time how
   long until it looks unhappy, and how long until it walks out. Two minutes
   means a ceiling near 10000; ten minutes means 65535.
2. **Count the slaps.** How many does it take to turn a content creature angry?
   A slap is +3000, so three or four means ~10000, twenty means ~65535.

Either answer unblocks anger, desertion, sulking and the three job tables.

### Q105 — a creature with no bed — **ANSWERED the same day, by the owner**

> *"If a creature can't get a lair it becomes unhappy, it can't go to sleep,
> then angry, then it leaves the dungeon."*

So it **cannot sleep at all** — not on the floor — which is what
`CreatureSleepSystem` already does. And the consequence is the anger ladder,
confirmed now from two separate causes (Q9 gave the same sequence for unpaid
creatures). Blocked on Q106 above.

### Q99 — is zero health death, or a window before it? — *added 2026-09-04*

Implemented: the creature is removed at zero. But
`CreatureDyingStateDurationSeconds` is **60**, which is a long time to specify
for nothing, and OpenKeeper models an *unconscious* state that becomes death
after that timer and can be interrupted by rescue.

**The experiment.** Let one of your own creatures lose a fight and watch the
floor. Does it vanish, or lie there? Can it be picked up? Does it recover if
dropped in a lair? Does it matter what killed it?

### Q100 — does regeneration care what the creature is doing? — *added 2026-09-04*

Implemented: own-land regeneration and lair healing **add**, and both apply
regardless of activity. **An imp is the easy subject**, being one of only two
creatures in the game that regenerate at all (67 a second; the other is King
Reginald at 400).

**The experiment.** Wound an imp, then watch whether it heals *while digging* on
its keeper's land, or only when idle. 67 a second matters during a fight and is
irrelevant outside one.

### Q97 — the portal: **why** arrivals slow down — *added 2026-09-04, and it displaces Q86 at the head of this list*

**The owner has confirmed that they do**, unprompted: *"goblin interval, seems
to be prolonging after each goblin arrives."* That closes the question this list
called unfalsifiable. It opens a better one, because **two different mechanisms
predict the same thing** and the binary points at the one that is *not*
implemented:

| | Mechanism | Prediction |
|---|---|---|
| (a) | The **interval stretches** with your creature count — `max(base, base × creatures ÷ 2)`, from OpenKeeper, and what the code does today | Lair size is irrelevant to timing. A 3x3 lair and a 6x6 lair pace identically |
| (b) | The interval never changes; **eligibility drains** — each creature type is weighted by its attracting rooms, minus one per creature of that type held, and drops out at zero | Lair size is the whole story. A big lair keeps goblins arriving at full speed until it fills |

(b) is what `Entrance.cpp` appears to do, and there is **no interval arithmetic
anywhere in that translation unit** — but the reading contradicts itself (taken
literally it caps level 1 at one goblin) so it is a hypothesis, not a finding.
See `docs/research/binary-entrance.md` §4.

**The experiment, and it needs no stopwatch precision.** Play level 1 twice.
Once with a **small lair** (one or two tiles), once with a **large** one (say
5x5 or bigger), and in each case note roughly how long the third, fourth and
fifth goblins take. **If the big-lair run keeps arrivals fast and then slows
abruptly, it is (b).** If both runs slow at the same steady rate regardless of
lair size, it is (a) and the code is right.

Worth doing before the slope below, because if it is (b) the slope does not
exist.

### Q86 — the portal: **the slope, now that the direction is confirmed** — *updated 2026-09-04*

The portal is built (ADR-0042, `docs/research/portals.md`). Three of this entry's
four original questions were answered from OpenKeeper's implementation, now that
the licence permits reusing it: **the cap really is per portal** and rises the
moment a second is held; **attraction is re-checked every tick**, so selling your
only lair stops the next arrival immediately; and **each portal keeps its own
clock**, with two ripe portals producing on the same tick.

**What is left is the one thing no file expresses, and it changes how a whole
level feels.**

**The interval grows with your dungeon.** The implemented rule is
`max(base, base x creatures / 2)`, so on level 1 — whose own
`EntranceGenerationSpeedSeconds` is **18**, not the game-wide 25 — the first
goblin takes 18 seconds, the fifth 36, the tenth 90, the fifteenth 126. Without
the scaling, all fifteen arrive in four and a half minutes; with it, in about
fifteen. **It comes entirely from OpenKeeper. Nothing in the balance table says
it and nothing in the shipped data can confirm or refute it.**

**The experiment, and it is quick.** Start level 1, claim the portal, build a
lair, and time the gap between arrivals **twice**: once early (one or two
creatures in the dungeon) and once later (six or more).

- If both gaps are about **18 seconds**, there is no scaling and it should come
  out.
- If the later gap is roughly **half your creature count times 18**, the rule is
  right.
- If it grows but not like that, the shape is different and worth writing down
  exactly — how many creatures you had, and how many seconds each gap took.

A second, cheaper reading of the same run: **does the very first goblin arrive
before you have a lair?** It should not — the Goblin's only attraction is a Lair,
and that is what makes level 1 teach lairs first. If one arrives anyway, the
attraction gate is wrong.

**Still open from the original list, and it needs sacking:** whether a sacked
creature's replacement arrives at once or after the normal interval. Nothing can
pick up a creature yet, so it is Q90's problem rather than this one's.

**And the warning about attraction sizes still stands.** Q61/Q80 is a live
three-way disagreement: you *and* the 1999 manual say a Dark Angel needs a 5x5
temple; the shipped v1.70 record says `size >= 0`. Four creatures do carry a real
minimum (Bile Demon and Giant want a Hatchery of 25+ tiles, Royal Guard a
Training Room of 25+, Maiden Of The Nest a Temple of 9+), so the mechanism can
express one — it just does not for the Dark Angel. The 3x3-temple experiment
under Q61 below settles it, and **Q89 is now a second reason to run it**: if a
3x3 temple attracts anything at all, a room below its `RecommendedSize` still
advertises while doing nothing.

### Q92 — how do you claim a portal? — **ANSWERED, and it exposed a four-month defect**

Answered by the owner within an hour of being asked: *"once an imp claims a
portal then the entire portal gets claimed, all tiles that are assigned to that
portal are immediately claimed … that is a game mechanic that works on claiming
enemy/neutral rooms too, rooms are instantly claimed by imps, not tile by
tile."* Implemented as ADR-0044.

**Worth recording what that one sentence was worth.** It was asked as a portal
question and it was really a defect report: `Claim` already applied to room
terrain, so every room in the project had been changing hands one tile at a time
at the wrong rate since rooms existed, and nothing had checked because nobody had
asked. Then implementing it produced a scenario that dug toward level 1's portal
and did nothing for 130 seconds — which turned out to be a *second*, unrelated
four-month defect that had **walled a level-1 keeper in permanently** (ADR-0045).
One sentence, two defects, neither of them about portals.

**Three things about it are still ours rather than yours, in descending order of
how much they would change:**

**1. Does a long bridge go whole?** (Q94) A wooden bridge is a room in every way
the data can tell, so taking one tile of a rival's ten-tile bridge hands over all
ten. That is a big consequence to draw from a sentence about portals. **Claim one
end of a long bridge and see whether the far end changes colour.**

**2. Can a dungeon heart be claimed?** (Q93) Excluded here, on the grounds that a
heart is beaten down rather than taken and that Q40's "rivals claim the rooms and
gold" happens *after* it falls. **Try to claim a rival's heart and see whether an
imp even walks over.**

**3. How long does it take?** Only the propagation is confirmed. The rate here is
`ConvertRoomHealth` = −4000 a second against a room tile's 10000 health, so
thirteen swings, about **2.6 seconds** for one imp. **Time an imp taking a
portal**, and if it is much faster than that, the rate is not this variable.

And a fourth that is cheap while you are there: **does a portal claimed mid-game
produce its first creature straight away, or after the normal interval?** Ours
waits a full interval — the clock starts at the claim and an idle portal banks
nothing. OpenKeeper does the opposite, by accident rather than design.

### Q59 — how much mana does one trap draw? — **ANSWERED and pinned**
You measured five: **Spike 5, Sentry 5, Fireburst 10, Freeze 10, Lightning 25**.
That is `Traps.kwd` offset **0x0202**, and the whole column comes with it:

```
  Gas 5     Fireburst 10    Guard Post 0      Hero Sentry 50
  Boulder 5 Lightning 25    Magic Door 30     Hero Alarm 5
  Sentry 5  Freeze 10       Spare Door 1 250  Jack In The Box 5
  Alarm 5   Fear 5          Trigger 5         Spike 5
```

**Guard Post reading zero is the column's own argument.** It is the only zero of
the sixteen, and it is the one entry in TRAPS that is not a trap — it is a spot
for a creature to stand on.

**Worth recording how this went, because the detour was the useful part.** With
only your first two traps measured, both read 5 — and *two* offsets fitted, one
of which (0x0070) reads 5-or-6 for nearly everything and cannot be a cost. A
second identical value cannot separate a real column from a coincidence. It was
the **three-valued spread** that settled it, and briefly the fifth value looked
like it refuted the whole column, which sent four further searches through
`Shots.kwd`, `Objects.kwd` and the balance table before it turned out to fit
after all. **Ask for values that differ from each other**, not more of the same.

Pinned by `TrapsOnRealDataTests`. There is deliberately no `KwdTraps` parser:
one understood field out of 579 bytes is not a parser, and traps are objects that
arrive at M6 regardless.

**One prediction of mine was then refuted, and it sharpened the answer.** I said
a Magic Door should be +30. You built two and the drain did not move at all.

**The door rows in TRAPS are not live data.** Doors have their own record -
`Doors.kwd`, eight of them, Magic Door among them - so the two door rows here are
vestigial, and "Spare Door 1" reading **250** corroborates it: nothing in this
game costs 250 mana a second. Every value that has been measured is a trap, and
every trap value matches. **Nothing left worth testing**: the three distinct
values among buildable traps (5, 10, 25) are all confirmed, and Guard Post
predicts 0 under any reading, so it can no longer discriminate.

### Q58 — does possession really cost nothing per second?
You reported no continuous drain while possessing, and **the data disagrees with
itself as well as with you**: the Create Imp scan turned up Possession = 25 a
second on the spell record, while `ManaCostInFirstPersonPerSecond` in the balance
table says 5.

The likely explanation is that the mana orb is not on screen in first person, so
what you saw was the absence of the HUD rather than the absence of a drain — but
that is a guess and it is written down as one. **To settle it:** note your mana
total, possess a creature, stay in for a measured ~30 seconds, leave, and note it
again. Compare against what your green rate alone would have earned. Nothing
depends on this yet; possession arrives at M7.

### Q57 — what happens when a keeper runs out of mana?
New, and it exists because of the upkeep. Ours floors mana at zero and carries
on, which is certainly not what DK2 does:
`InsufficientManaImpEvaluationPeriodSeconds` (10) says the original re-evaluates
imps on a ten-second clock when mana is short. **Do imps start dying? Do they
stop working? Does the game just refuse to spend?** Buy a lot of imps on a small
dungeon, spend down to nothing, and watch.

### Q35 — which treasury tile does an imp fill?
The owner's belief is *nearest tile, spillover to another empty treasury tile,
and when the whole treasury is full a lump on an empty tile beside it* — but
flagged as uncertain, and it decides which gold survives a treasury being partly
destroyed. Worth a deliberate look before it is implemented.

Watch imps deliver into a **3×3 treasury** and answer: does the first load land
in the tile nearest the door, or somewhere fixed? When one tile fills, where does
the next load go? And when the whole treasury is full, where exactly does the
lump appear — beside the treasury, or back where the imp was mining?

### Q50 — how many swings before an imp runs off with its load? — **answered by arithmetic**
Was on this list as the cheapest useful experiment. Q47's measurement answered it
without anyone playing: a swing frees 273, an Imp holds 1400, so it fills after
**five** swings (1363) and a seam of eleven swings is **three trips** — 1363,
1364, 273. That is what an imp shuttling to and from a rock face looks like.

Still worth thirty seconds of confirmation if you are in front of the game
anyway: **count the swings between an imp starting on a fresh seam and running
off.** Five is the prediction.

### Q31 leftover — how big is a floor lump?
When there is nowhere to store gold, a lump grows in front of the mining imp
"up to a designated sum". `MaxGoldPileOutsideTreasury` is 2000 and is the
candidate. Fill the treasury, keep mining, and see how much piles up before the
imp stops.

### Q47 — the tile-work rates — **ANSWERED, and the answer was 273**
Left here as the record of what one number was worth. Every rate used to be too
fast against observation and the best available reading was "twice a second, half
the delta" — plausible, self-consistent, and wrong. The measurement of **273 gold
a swing** put an application at a *fifth* of the tabled delta, five a second, and
two things nobody had looked for agreed: eight of the ten tabled deltas divide by
five exactly, and claiming comes out at four swings, which is Q46's independently
observed "2-3-4 jumps MAX".

Implemented (ADR-0020). `DigRockHealth` still fits nothing and is now the only
number in the tile mechanics that does not — which makes it much more likely to
be a variable we have misidentified than a balance value.


## 2. Still open, and cheap

### Q72 — does a wall deform the same way twice? — **new, and it is a look-and-see**
**This one is purely cosmetic, and that is why it is worth asking now.** The
Unity view is being built, and the answer decides whether a renderer can
reproduce DK2's wall shapes from nothing but a tile coordinate, or has to
remember them.

**The background.** DK2's wall meshes are authored straight — somebody extracted
them and looked. In the game they are visibly bent, and the bending is
sometimes dramatic rather than a subtle wobble. Whoever wrote that up thinks the
engine displaces the vertices at runtime; he says out loud that it is his
inference and that the alternative is several hand-made variants assembled by
code.

**What to do.** Find a wall with a distinctive silhouette — a bulge, a lean, a
notch. Take a screenshot. Have an imp dig it out. Then **fill the tile back in**
(the fill-in order, so the wall returns), let it finish, and screenshot the same
wall again from the same camera angle.

**What the answer means:**
- **Identical both times** → the shape is a function of where the tile is. A
  renderer computes it from the coordinate and stores nothing. This is the
  convenient answer and the likely one.
- **Different** → the shape is decided when the wall appears, so it is view
  state a renderer has to keep.

**Either way, nothing in the simulation changes.** Where a wall's vertices sit
cannot affect a rule, so this must never become a field on a tile — the question
is only about which half of the renderer owns it.

**If you would rather not bother:** say so and it stays open. It costs a
renderer some memory and nothing else, and the answer is cheap to get later
because it is not blocking anything.


### Q20 — how many swings to dig one plain wall? — **ANSWERED and now IMPLEMENTED**
**Which tile: the plain, unreinforced wall.** Terrain id 2, "Rock" in the data —
the ordinary one an imp digs out to open a corridor, not the reinforced kind and
not the impenetrable map edge. A reinforced wall is a different terrain with its
own two rates (`DigOwnWallHealth` −130 for your own, `DigEnemyWallHealth` −8 for
a rival's), and **neither of those is anomalous**, so they are not what is
wanted here.

`DigRockHealth` is −5000 a second against 165 health, so under the confirmed
five-swings-a-second model one swing would end it. You measured 2–3 hits, which
puts the true rate at 55–82 a swing — the field is **12× to 18× too big**, and
it is the only number in the tile table that fits nothing.

**ANSWERED (2026-08-30): two swings.** You drew the distinction that made it
usable - two swings do the damage, and the third animation plays over the block
already crumbling. That bounds the per-swing damage at [83, 164] and the
per-second field at **415-820** against a shipped **5000**, so it is 6x to 12x
too big, and **500 is the only round number in the window and is exactly
5000/10**. No other variable is a candidate; the table was scanned.

**You approved the override, so it is built** (ADR-0023): -500 lives in
`ObservedCorrections`, the only place in the project allowed to hold a number the
data does not, guarded so it fires on the exact shipped -5000 and nothing else.
A plain wall now takes two swings here, as it does in the original.

**Worth having at the same time, and cheap once you are counting:** the swings to
dig through **your own reinforced wall** (prediction: **35**) and through **a
rival's** (prediction: **563**, which is about two minutes). Those two come from
the same model that 273 gold a swing fixed, and neither has been checked against
the game. If they land, the model is confirmed on numbers three orders of
magnitude apart and `DigRockHealth` is definitively alone.

### Q48 — what does a diagonal step cost? — **ANSWERED, and you confirmed it**
You looked (2026-09-03): **a glide, not a staircase.** That is the whole of part
(a), and it agrees with what the physics forced. Nothing further is needed here;
the reasoning below is kept because it is the second case where deriving beat
asking, and part (b) is still open.

**The half that blocks Q28 is forced by physics, not by observation.** A
creature's speed is in **tiles per second** (Q29 — and three variables in the
balance table name that unit in their own words), so a creature moves
continuously through world space at a constant rate. A step to a diagonal
neighbour covers **√2 tile-widths**. At a constant speed that takes **1.41× as
long**, and there is no mechanism by which it could be free short of the creature
teleporting or speeding up on the bias.

So the mover's cost is **octile — 14 against 10** — and it is *uniform* cost that
would be the invention here, not octile. That is enough to implement Q28.

**What is still open is different and much harder to see.** What the *pathfinder*
charges for a diagonal when choosing between routes is not the same question. In
open uniform terrain both cost models produce paths of the same shape; they only
diverge where a route with fewer steps is longer in true distance. If DK2 charged
10 for a diagonal, its creatures would sometimes take visibly slower routes —
and nobody has ever reported that.

**If you want to confirm the first half anyway, it needs no stopwatch.** Watch a
creature cross a large open claimed room **corner to corner**:

- a **smooth diagonal glide** means each step really is √2 long, and octile is
  right;
- a **staircase** of alternating orthogonal moves would mean the "diagonal" is
  actually two steps and costs 2, not 1.41.

Your Q28 answer — that creatures cut corners between two solid tiles — already
points at the first.

**You raised the real difficulty: you cannot order a creature to walk anywhere,
because creatures pick their own routes.** You can, though, arrange a dungeon in
which the route they pick is the one you want to see. Use an **imp**, which is
the one creature whose destination you *can* choose:

1. A large claimed room, at least 8x8, with nothing else in it.
2. Put the imp in one corner and mark **one** tile at the far corner. It is the
   only job on the map, so it goes there, and marked excavation outranks
   everything (Q24).
3. Watch the shape of the walk, not the clock. A **glide along the diagonal** is
   the answer; a **staircase** of alternating north/east steps is the other one.

If you do want a number, 7 tiles diagonally is **3.8 s** if a diagonal is free
and **5.4 s** if it costs √2, and the gap is wide enough for a phone stopwatch.
But the shape alone settles it, and the shape needs no timing at all.

### Q22 — how long does filling a tile in take? — **WITHDRAWN: you cannot**
This used to ask you to fill in a claimed floor tile and time it, on the strength
of a prediction (660 swings, about 132 seconds) derived from `FillInHealth` = 5
and Claimed Path's route back to Rock.

**You answered it by telling us the experiment does not exist** (2026-09-04):
*"once a tile is dug out, and maybe claimed, there is no possible way to fill in
that tile to rock again. DK2 doesn't have a game mechanic for filling up dug out
tiles."*

That is worth more than the timing would have been, because it caught a whole
mechanic we had derived from data and would eventually have given imps a job for.
Nothing needs deleting — `FillInHealth`, the `FillInable` flag and the terrain
transition are all in the shipped file, three coordinated pieces of data that are
not an accident — so the reading recorded is that the engine carries the concept
and the game exposes no way to reach it (Q74). What changed is that "no tile ever
closes" is now an asserted invariant rather than an accident of what imps happen
to choose.

**The level-script half is now answered, and it was not fill-in.** DK2 has an
`ALTER_TERRAIN_TYPE` trigger action that writes a terrain id straight onto a tile,
with no health and no rate. Sweeping all 66 shipped maps: 160 of them, and the
dominant use is the *opposite* of filling in — 94 are `Impenetrable Rock -> Dirt
Path`, scripts opening sealed passages. Level 1 has three that soften Impenetrable
Rock into diggable Rock, which is the tutorial handing you a route. **Exactly two
in the whole campaign turn an open tile solid**, both in `Secret3`, both Claimed
Path to Reinforced Wall. So a script can wall a tile up as a set-piece, and
nothing anywhere uses the fill-in *health* mechanic.

**One thing you might still see, if you ever have the level editor open:** is
there a fill-in tool in it? That is all that is left of Q74, and it is curiosity
rather than a blocker.

### Q23 — are dig markers per keeper or shared?
In a skirmish, can you **see a rival keeper's dig markers** on their walls?

### Q49 — do the other floor jobs work like claiming? — **withdrawn, mostly**
This used to ask you to watch four jobs. Three of them are answered without
playing, and asking would have been the wrong move — the same mistake as Q48.

The rule turns out to be forced by two physical clauses rather than being four
separate facts: **a worker works from the tile itself when it can stand there
both while it works and when the job finishes.** The first clause rules out every
wall job (those tiles are solid). The second rules out **filling in** — a filled
tile becomes rock, so a worker standing on it would be sealed inside its own
work. Repairing and attacking floor are then the same case as claiming, because
they are the same geometry. See ADR-0028.

**The pause is real, and now it needs a number.** You looked (2026-09-03): an
imp that has claimed a tile **hesitates** before starting the next one. That is
worth more than it sounds, because you separately reported that an imp whose
*wall* has just fallen moves straight on - so this is specific to claiming, not a
generic pause after every job. At roughly 500 ms on top of a 0.8 s claim it is a
~60% tax on how fast territory spreads, so it is not cosmetic.

**The balance table has no field for it.** Every `...Seconds` variable in it is a
whole number and none is below 1, so a sub-second delay is in nothing we read.
Before it goes into the simulation it needs a measurement, and 500 ms is below
anybody's stopwatch reaction time - so **do not time one claim, time a chain of
them**:

1. One imp, one straight run of **10 unclaimed floor tiles** it can reach, and
   nothing else marked anywhere.
2. Start the clock when it lands on the first tile, stop it when the tenth turns
   your colour.
3. **We predict about 12 s with no pause** (10 claims at 0.8 s of work, plus the
   walking). **A 500 ms pause makes it about 17 s.** Five seconds apart is far
   wider than your reaction time, and dividing by ten gets the per-tile pause to
   a precision a single observation could never reach.

This is the same trick that made reinforcing measurable: you timed 66 swings, not
one.

**That loose end is closed, and it was not nothing.** I had written this off as
"they were probably mid-job". You answered it directly: **only one claiming imp
per unclaimed tile is allowed.** So the reason the others did not join in was a
rule, not a coincidence — and our cap was wrong, because ADR-0028 had reused the
three-a-face wall number for a job done standing on the tile.

It also refutes the *reason* ADR-0026 gave for that cap. "Three is how many
bodies fit on a tile" cannot be right when three share a tile to reach a wall and
only one may stand on floor. **The cap is per job, and both numbers are observed
rather than derived** (ADR-0030). Worth remembering the shape of this: the tidier
single-number story was the one that turned out to be wrong, and it had also made
a test pass for the wrong reason.

### Q63 — what does training cost? — **20 at a dummy, 0 at the target**
You measured a melee creature swinging at a training dummy paying **20 gold** an
action, and then corrected the ranged half: a creature hitting the moving target
pays **0**.

**The asymmetry is worth more than the 20.** Ranged creatures appear to train for
free, which is a gameplay fact in its own right and not something anyone would
have guessed from the data.

**The source is still not identified.** The balance table has no training cost in
it at all — its four training-named variables are a motionless duration, a max
level, an experience-loss rate and a colour tweak — and `Rooms.kwd` carries no 20
for the Training Room.

There is one candidate, and your correction turned it from *contradicted* into
*merely unconvincing*. `Objects.kwd` offset **0x0208** reads Training Dummy **20**
and Training Target **0**, which now matches you exactly. But it is the only
offset in the whole record where the dummy holds 20, and **the rest of the column
does not look like money**:

```
  Gold Pile 1  4096      every creature bed  60      Chicken   45
  Book Case    4096      Chicken Coop        77      Egg       16
  Prison Door    10      Hero Gate Door      24      Call To Arms 60
```

4096 is this project's fixed-point denominator, and every creature bed reading
the same 60 is the signature of a size or a duration rather than a price. So it
is recorded and not believed.

**The level half came back flat.** It is 20 a hit at every level, and **level 4
is the ceiling** the training room can reach - which `TrainingRoomMaxExperienceLevel`
(4) says in its own words. That is the fourth time one of your observations and a
variable name have met outright.

**Flat across levels helps 0x0208**, because a cost that does not vary with the
creature is a property of the *thing being hit*, and that is exactly what an
OBJECTS field is. It is now the better of two poor options rather than a
curiosity - but two anchors, one of them zero, is not enough.

**The one anchor still missing is a different creature**: a goblin against a bile
demon. Flat there too would put the cost firmly on the dummy and make 0x0208 hard
to argue against.

Nothing is blocked: training is M4 and no creature here gains experience. It is
worth having because it is a **gold sink**, and the economy so far has sinks only
for building rooms and buying imps.

### Q9 — payday — **ANSWERED, and the data agreed with you**
Your "every 10 minutes" is `PayDayFrequencySeconds` = **600**, a variable that
names itself. Three more came with it, all unread by anything:
`TimeBeforeFirstPayDaySeconds` 600 (the first payday is at ten minutes, not at
zero), `PayDayCutOffTimeSeconds` 120, and `DelayBeforePayReevaluationSeconds` 4.

**The one part still open** is what that 120-second cut-off is: the guess is how
long a creature has to reach the treasury before it counts as unpaid. Watch a
creature that is slow to arrive at payday and see whether it gets angry.

---

## 3. Bigger, and worth it whenever you fancy

### Q45 — which rooms need a minimum size to work?
**Largely answered, and it turned out to be sitting in data we already parse.**
Your graveyard note - "needs a minimum 3x3 so one grave appears" - matches
`RoomDef.RecommendedSizeX/Y`, which says 3x3 for the Graveyard and 5x5 for the
Temple, the size you had flagged earlier. Two matches from two sittings, neither
made with the field in view. It is a badly named field: it is the size below
which a room does nothing (Q45).

**What would confirm it:** one more room checked against the table. It says
Portal 3x3, Prison 5x5, Combat Pit 5x5, Crypt 5x5, and Treasury/Lair/Guard Room
1x1 - so a **1x1 lair should work** and a **4x4 prison should not**.

**And the open half is now Q60: how the yield scales above the minimum.** Two
rooms measured, and **they do not follow the same rule**, which is the finding.

| room | size | yield |
|---|---|---|
| Temple | 3x3 | capacity **8** |
| Temple | 4x4 | capacity **12** |
| Temple | 5x5 | capacity **16** |
| Graveyard | 3x3 | **1** grave |
| Graveyard | 5x5 | **8** graves |

The temple series is exactly the count of **edge tiles**, `2(w+h) − 4`: 8, 12,
16 for 3x3, 4x4, 5x5. That fits what the room looks like — it is a
pool, and creatures stand on the rim. The graveyard's 8 is **not** the 5x5 edge
count of 16; it is the ring of eight tiles around the centre of the interior 3x3,
which is exactly what your screenshot shows: a coffin with eight headstones round
it.

**The temple half is now CONFIRMED.** You pinned the three sizes down as 3x3,
4x4 and 5x5, which is what turned it from a fit into a confirmation - 12 could
have come from a 3x5, and three consecutive squares landing on 4n-4 leaves
nothing else standing.

**You asked what these two are actually for. Here is exactly what each would
settle, and neither is about confirming a number I already believe.**

**The non-square temple: 3x5.** All three temple measurements were *squares*, so
width and height were never independent - and **any rule that agrees along w = h
is indistinguishable from the rim count on the evidence we have.** A 3x5 splits
them three ways:

| what the rule might be | 3x5 predicts |
|---|---|
| **edge tiles**, `2(w+h) - 4` - what is implemented | **12** |
| `4 x (smaller side - 1)` | 8 |
| `4 x (larger side - 1)` | 16 |

So a 3x5 temple is not a fourth data point on a line - it is the only
measurement that can tell a rim from a rule keyed on one dimension. If it says 8
or 16, `RoomInstance.EdgeTileCount` is wrong and so is the story about creatures
standing on the pool rim.

**The graveyard: the cheapest test is not a new room at all.** The whole
ambiguity is one tile. A 5x5 graveyard has **9 interior tiles**, and you counted
**8** graves - "a coffin with eight headstones round it". Two readings fit:

- **graves = interior tiles** (9), and the coffin at the centre is the ninth
  burial spot, drawn differently and so counted separately by eye;
- **graves = interior tiles minus one** (8), the centre being a fixture rather
  than a grave.

And 3x3 does not separate them: its interior is a single tile, and you saw one
grave, which both readings allow.

1. **If the graveyard has a capacity in its tooltip the way the temple did, just
   read it off a 5x5.** 8 or 9 ends the question in one look, on a room you may
   already have built.
2. **Otherwise build a 4x4.** Its interior is a 2x2 block with **no centre tile
   at all**, so there is no coffin-versus-headstone ambiguity to muddy the count:
   the first reading predicts **4**, the second predicts **3**.

That is why 4x4 rather than a bigger one - a 7x7 would give 25 against 24 and
leave you counting headstones in a crowd.

### Q61 — does a small temple really not attract Dark Angels?
You flagged this as needing verifying, and **the data does not support it**.
Creature attraction is a parsed field — `CreatureAttraction(RoomId, RoomSize)`,
two per creature — and it reads sensibly right across the roster: Warlock→Library,
Vampire→Graveyard, Mistress→Torture Chamber, Skeleton→Prison, Goblin→Lair,
Salamander→Training Room, Troll→Workshop, **Dark Angel→Temple**.

`RoomSize` is a **tile count**, and the Bile Demon shows it: it wants a Workshop
of any size *plus* a **Hatchery of 25 tiles or more** — 5x5, and a bile demon
demanding a big hatchery is exactly how the original plays.

**The Dark Angel's temple entry is `size >= 0`.** So either a 3x3 temple does
attract them and the 5x5 belief is folklore, or the requirement lives in some
mechanism other than this field. **Cheapest test:** a dungeon with a 3x3 temple
and nothing else a Dark Angel wants — do any turn up?

**Update 2026-09-04 — you have a witness, and the odds have shifted.** The 1999
manual states your belief outright: *"9.1.14 DARK ANGEL — Attracted By: Temple
(Minimum 5x5 tiles)"*. So this is no longer your recollection against the file; it
is **you and EA's own manual** against the v1.70 record. The likeliest reading is
that the manual describes 1.0 and the shipped balance relaxed the minimum — the
manual gets four other attraction details wrong the same way (Q80) — but that is a
guess, and the experiment above settles it either way.

Two related things fell out of re-checking the field, and they make the question
sharper rather than duller:

- **The Bile Demon is not the only creature with a size minimum.** Four have one,
  and only ever on a *second* attraction: Bile Demon and Giant want Hatchery ≥ 25,
  Royal Guard wants Training Room ≥ 25, and **Maiden Of The Nest wants Temple ≥ 9**.
  So temples *do* carry a size requirement in the shipped data — just not the Dark
  Angel's. "The mechanism cannot express it" is therefore ruled out.
- The manual gives the Bile Demon a **third** attraction (a Lair of 9+ tiles) that
  the record has no room for: the eight bytes after the second slot decode as two
  fixed-point values, not a third entry. Whatever changed between 1.0 and 1.70
  changed the record, not just the numbers in it.


### Q7 — the mood model, beyond the four causes
The four causes are recorded. What is still missing is the shape: does anger
build gradually or arrive in steps? Roughly how long from "unhappy" to "leaves"
with no pay at all? Do the four stack, or does the worst one dominate? Does anger
fade once the cause is fixed?

---

## What playing **cannot** settle

Do not spend time on these at the keyboard — they need instrumentation,
measurement tooling, or disassembly, and are scheduled accordingly:

- **Q1 — the exact tick rate.** Fixed, but internal. Needs the M5 oracle probe.
  (Stopwatch counts on long repeated actions give an approximation, which is why
  several entries above ask for seconds.) **Note what Q47 did NOT settle**: it
  fixed the ratio between a swing and a per-second delta, not the tick rate. The
  swing count a tile takes is now measured; the wall-clock time it takes is not.
- ~~**Q19 - what the health deltas are per.**~~ **ANSWERED (2026-08-30)** and not
  by playing directly: the 273-gold measurement fixed it. They are per second, and
  an application is a fifth of one. What is left is only how long a second is,
  which is Q1.
- **Q5 - the pathfinding algorithm.** You can see the *route* - Q28 and Q30 both
  came from watching one - never the algorithm.
- **Q3 — which RNG.** Invisible by construction.
- **Q11, Q13, Q14, Q15, Q16, Q17** — record layouts and field meanings.
- **Q37a — what `MaxManaGain` and `DungeonHeartMaxManaIncrease` are for.** Closed
  by elimination; they affect nothing a player can see.

### Q133 — does a hatchery make eggs or chickens? — **ANSWERED: eggs, and they hatch**

> *"It produces eggs that hatch, that entire animation of eggs appearing and
> hatching lasts about 2 seconds, and after that a chicken appears and roams
> around the hatchery. No it is not capped at 12, a 5x5 hatchery produces more
> than 20 chickens, seems like it is continuously producing eggs and chicken,
> however they also seem to be disappearing if no creature eats them, so the
> total population remains stable."* — 2026-09-05

Three answers in one, and the second overturned something:

- **the egg-to-chicken step is real**, and takes about two seconds. Built as
  `HatcheryRules.Hatch`, with the two seconds recorded as the animation length
  it is;
- **the cap of 12 is not a cap on the flock.** Twelve was the only limit this
  project had found, and a 5x5 holding more than twenty rules it out. The
  variable is real — one byte, one reader in the whole binary, checked before
  laying at a coop — so it caps something narrower. Loose chickens, presumably,
  which is what "free range" means and what the Chicken's own `FreeRange` field
  distinguishes;
- **the population is stable**, which fits the Chicken carrying
  `DieOverTimeIfNotInRoom` and not `DieOverTime`: a bird that wanders off the
  hatchery's tiles is no longer in a room, and rots.

**The follow-up was asked and answered the same day (Q135): a 3x3 settles at
exactly nine.** So it is **one chicken per room tile**, the Chicken record's
`RoomCapacity` of 1 read as a per-tile limit — a field that had been parsed and
read by nothing. Twelve is ruled out as the room's limit. `HatcheryRules` takes
the number from the definition rather than hardcoding nine, and a test runs a 3x3
a hundred times to prove it stops.

### Q133 (original) — does a hatchery make eggs or chickens?

**Build a hatchery and look at what appears on the coops.**

The binary says the room lays **object 47, the Egg** — not object 9, the
Chicken. Two different objects, and only the Chicken carries the "this is food"
flag; only the Chicken has a per-creature `HpFromChicken` value against it (375
for a Goblin). So something turns one into the other, and we have not found what.

- **Eggs appear and visibly hatch** → there is a timed transition to build, and
  the egg's own state machine is where it lives.
- **Chickens appear directly** → then object 47 is a brief laying animation
  rather than a thing that sits there, and eating can be built without it.

Worth noticing too: **do the chickens wander?** The binary gives a chicken four
pieces of state — free-range or not, a pecking timer, a "stay there" flag and a
scatter countdown — so it should peck about and bolt when disturbed. And there
is a cap of **12 free-range chickens per player**, which is the sort of number
that shows: if you build an enormous hatchery, does the loose flock stop growing?

### Q131 — does a fresh lair tile look bare, or already furnished? — **ANSWERED: bare**

> *"Yes a lair is bare, green lair texture on the floor tile, only when a
> creature enters a lair and claims a tile by dropping its bed does that tile get
> furniture on top of it."* — 2026-09-05

Which is exactly what ADR-0054 assumed and the only thing it had left open. The
bed object is **created on claim**, of the claiming creature's own kind, and
`LairRules.TryClaimBed` already did that — so the confidence moves from STRONGLY
INFERRED to CONFIRMED and nothing had to change.

The wording adds one thing worth keeping: the creature **drops** its bed, so
placing it is an act the creature performs on arrival rather than something the
room does. That is presentation as far as the simulation is concerned, and it is
also why `FlagDestroyWhenDropped` sits beside `CreatureId` in the object's union.

### Q131 (original) — does a fresh lair tile look bare, or already furnished?

**Build a lair somewhere with no creatures near it, and look at it.**

That is the whole experiment. It settles a thing this project now assumes in
code: that a **bed is created when a creature claims a tile**, rather than
placed when the room is built.

Everything in the data points that way. There are **twelve bed objects** for 46
creatures, and each creature's record names which of the twelve is its — a
Goblin's is object 22, a Vampire's is 26, a coffin. So the object standing on a
lair tile cannot be chosen before you know who is going to sleep in it. The bed
object also carries the id of the creature that owns it, and a flag about being
destroyed when dropped, both of which read like a thing that belongs to somebody
rather than to the room.

**But nobody has looked.** The alternative — a lair is furnished the moment it is
built and an arriving creature takes a bed over — would look identical in every
test written so far, and would mean the code should be *finding* a bed rather
than making one.

- **Bare tiles, and a bed appears when somebody moves in** → what is implemented
  is right.
- **Furnished the moment it is built** → tell me, and claiming becomes a lookup.

Worth noticing while you are there: whether the bed that appears **matches the
creature** (a coffin for a vampire, and not a goblin's bed), which is the same
question from the other side.

### Q66 — does "adjacent to claimed ground" include diagonals? — **new, and cheap**

You said imps claim tiles **adjacent to claimed tiles**, which confirms the rule
as built. What it does not settle is what "adjacent" means at a **corner**.

This was a non-question until last week. While movement was four-connected an imp
could not get to a corner-touching tile anyway; now that movement is
eight-connected (Q28, your corner-cutting answer) it can, so the two readings give
visibly different dungeons — a diagonal reading lets territory spread through a
corner and creep around a pillar, an orthogonal one walls it off.

**Orthogonal is what is implemented**, which at least keeps it consistent with the
way work spreads (Q27, three imps a side) and the way we decide which tiles make
up one room.

**The experiment:** claim a tile, then dig out **only** the tile diagonally beyond
one of its corners — no orthogonal route of owned floor to it. Then leave an imp
with nothing else to do and see whether it claims the diagonal tile or ignores it.
The same question applies to reinforcing, and one answer probably covers both.

### Q68 — does a rival's imp take a tile in one step or two? — **ANSWERED**

**One step.** You answered immediately: it does not go neutral first, it is
claimed directly. That refuted the two-step model this project had and, once
`Claim` was fixed to take ground that is merely *not yours*, **hostile expansion
fell out of rules already present** - see ADR-0033. The experiment below is kept
only because it is the shape of a good question: one tile, one look, no timing.

What is open now is **Q69**: `AttackTileHealth` (-140) has no user left, and the
two mana-vault claim rates (4 neutral, 20 enemy) are read by nothing while saying
DK2 does differentiate somewhere.

---

#### The original question, for the record

You told me (2026-09-03) that a bridge is only ever taken by a **rival keeper's
imps jumping on it and claiming it**, adjacent to their own territory. Both halves
of that were already rules here, arrived at from other evidence — which is the
strongest confirmation either has had, and there is nothing to ask about them.

**What is still open is the step count**, and it applies to all of a keeper's
ground rather than to bridges:

- **one step** — the rival imp claims your tile directly, and it flips from your
  colour to theirs;
- **two steps** — it damages the tile to neutral first (`AttackTileHealth` = −140),
  and then claims the neutral tile (`ClaimTileHealth` = 825).

Ours is the two-step reading, and the data is genuinely ambiguous: there is one
claim rate and one attack rate and **no** "claim enemy tile" rate — but a **mana
vault** gets dedicated neutral *and* enemy claim rates (4 and 20), so DK2 clearly
does direct enemy-claiming for at least one thing.

**The experiment:** let a rival imp take one tile of your claimed floor and watch
that single tile. **Does it go grey or neutral in between, or flip straight from
your colour to theirs?** One tile, one look, no timing.

Nothing waits on the answer — hostile expansion is unbuilt and unscheduled — so
this is for whenever you happen to be in a skirmish.
