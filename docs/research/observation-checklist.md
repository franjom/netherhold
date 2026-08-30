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
| Q34 | Selling refunds **50% of the cost, per tile** — a number in neither candidate variable |
| Q36 | A 1×1 temple really is buildable; minimum sizes are about **function**, not building |
| Q37 | The mana ceiling is a flat **200000** and never moves |
| Q38 | The mana gain cap is **per keeper**, not shared |
| Q40 | On defeat, creatures **leave by the nearest portal** and **rivals claim the rooms and gold** |
| Q42/Q44 | An imp **jumps onto** the tile it claims, and only claims from beside land its keeper owns |
| Q46 | Claiming a tile takes **2-4 jumps, about 2 seconds** |
| — | A keeper has **exactly one dungeon heart**, and losing it is defeat |
| Q7 | What angers a creature: **no lair, hunger, no pay, the company of creatures they hate** |
| Q9 | Unpaid → unhappy → angry → **leaves**; a converted creature **rebels** instead |

Four of those contradicted the implementation (Q24, Q28, Q42/Q44, Q43) and two
contradicted the data (Q34's refund, Q37's ceiling). That is the list working.

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

## 1. Still open, and blocking

### Q59 — how much mana does one trap draw? — **one more trap finishes this**
You measured a **spike trap** and a **sentry trap**: both **+5**. That was enough
to parse `Traps.kwd` for the first time - 16 records of 579 bytes, names readable
- and narrow the field to **two offsets**, but not to pick between them, because
your two traps happen to share a value.

| offset | Guard Post | most traps | Fireburst / Freeze | Lightning | Magic Door | Hero Sentry |
|---|---|---|---|---|---|---|
| **0x0202** | **0** | **5** | **10** | **25** | **30** | **50** |
| 0x0070 | 5 | 5 | 5 | 5 | 5 | 5 |

0x0202 is far the better candidate - it ranks the way the game plays, and 0x0070
being 5-or-6 for nearly everything is the shape of a category byte rather than a
cost. But **0x0202 could also be a charge or shot count**, which fits Spike = 5
just as comfortably, and that is exactly the reading a second identical
measurement cannot rule out.

**Build one Lightning Trap and note the change.** The predictions are as far
apart as they get:

- **+25** -> 0x0202 is the mana upkeep, and the whole column comes with it;
- **+5** -> the charge is flat per trap and both offsets are red herrings;
- anything else -> neither, and it is more interesting than either.

**Two cheap extras that would make it airtight.** A **Freeze** or **Fireburst**
trap should be **+10**. And a **Guard Post** should be **+0** - a negative
prediction, and the strongest kind, because a guard post is a standing spot
rather than a trap and nothing else in the column is zero.

**Note there is no global to fall back on:** the balance table was checked and
all thirteen variables holding 5 are something else entirely.

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

### Q48 — what does a diagonal step cost?
Now that Q28 says creatures cut corners, an eight-connected router needs to know
whether a diagonal is worth the same as a straight step. Time a creature walking
**ten tiles straight** and then **ten tiles diagonally** across open floor. If the
diagonal run takes about 1.4× as long, diagonals cost their true length; if the
two take the same time, they are free.

### Q62 — reinforcing takes longer than the model says — **the new live one**
You timed an imp reinforcing a wall at **14–15 seconds**. Ours predicts **10.0**:
Rock's 165 health up to its maxHealth 660 is 495 at 10 a swing, so 50 swings at
five a second.

Some of the 4.5-second gap is walking up and animating — claiming is 4 swings
(0.8 s) and you observed "about 2 seconds", so the overhead looks like ~1.2 s —
but not all of it. **Two re-readings fit, and they disagree about where an upward
job stops:**

- **(a) it works up to the destination terrain's health**, not the source's
  maxHealth: 165 → Reinforced Wall's 900 is 735, **74 swings = 14.8 s**, which
  fits with no overhead at all;
- **(b) it accumulates the source's maxHealth from zero**: **66 swings = 13.2 s**,
  which fits with the 1.2 s overhead.

**Reinforcing is the only job that can separate them**, which is why this never
showed up: for claiming, Dirt Path's maxHealth and Claimed Path's health are both
660, so both readings give the same four swings.

**The experiment is not to re-time reinforcing.** Time something with no
ambiguity in it and use it to calibrate the overhead: **dig through your own
reinforced wall** (900 health down at 26 a swing = **35 swings = 7.0 s**). If it
comes out near **8.2 s** the overhead is real and reading (b) wins; if it comes
out near **7.0 s** there is no overhead and (a) does.

### Q22 — filling a tile in
Not yet observed — the answer offered against it described claiming instead.
Fill in a claimed floor tile: does it take about as long as digging one out? Does
a partly-filled tile revert if you cancel?

### Q23 — are dig markers per keeper or shared?
In a skirmish, can you **see a rival keeper's dig markers** on their walls?

### Q49 — do the other floor jobs work like claiming?
An imp stands *on* a tile to claim it (Q44). Does it also stand on the tile to
**repair** damaged floor, to **attack** a rival's floor, and to **fill in**? Or
does it stand beside those? And is the ~500 ms an imp lingers after claiming a
real pause, or just the animation finishing?

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

**What is left:** a **non-square** temple (a 3x5 should give 12, a 3x7 should
give 16) and **one more graveyard size** (a 4x4, or a 7x7 - the prediction is 24
if the interior-ring rule holds).

### Q61 — does a small temple really not attract Dark Angels?
You flagged this as needing verifying, and **the data does not support it**.
Creature attraction is a parsed field — `CreatureAttraction(RoomId, RoomSize)`,
two per creature — and it reads sensibly right across the roster: Warlock→Library,
Vampire→Graveyard, Mistress→Torture Chamber, Skeleton→Prison, Goblin→Lair,
Salamander→Training Room, Troll→Workshop, **Dark Angel→Temple**.

`RoomSize` is a **tile count**, and the Bile Demon proves it: it is the only
creature with a non-zero one, wanting a Workshop of any size *plus* a
**Hatchery of 25 tiles or more** — 5x5, and a bile demon demanding a big hatchery
is exactly how the original plays.

**The Dark Angel's temple entry is `size >= 0`.** So either a 3x3 temple does
attract them and the 5x5 belief is folklore, or the requirement lives in some
mechanism other than this field. **Cheapest test:** a dungeon with a 3x3 temple
and nothing else a Dark Angel wants — do any turn up?


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
