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

## 1. Still open, and blocking

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

### Q48 — what does a diagonal step cost?
Now that Q28 says creatures cut corners, an eight-connected router needs to know
whether a diagonal is worth the same as a straight step. Time a creature walking
**ten tiles straight** and then **ten tiles diagonally** across open floor. If the
diagonal run takes about 1.4× as long, diagonals cost their true length; if the
two take the same time, they are free.

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

### Q51 — what is the red number under the mana orb?
**New.** A screenshot of the original shows two rates beneath the mana counter: a
green one (~53) and a **red one (~28)**. Our model has no mana expenditure at all
— every spender is a spell, which is M7 — so if the red figure is a continuous
drain, there is a mechanic missing.

Two screenshots a few seconds apart with the counter visible would settle it: does
the counter climb by the green figure, or by green minus red?

### Q9 — payday
Roughly **how often** does payday come round, and is it one clock for the whole
dungeon or per creature?

---

## 3. Bigger, and worth it whenever you fancy

### Q45 — which rooms need a minimum size to work?
You flagged temple ≈ 5×5. This is the shape of the answer for *every* room's
behaviour, so even a partial list helps — pictures welcome.

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
