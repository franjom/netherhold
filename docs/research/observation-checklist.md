# What can be settled by playing the game

Open questions a person sitting in front of DK2 v1.70 can answer by **looking**.
Each entry is one experiment: what to do, what to watch, and what each outcome
would mean. Status of every Q-number here is `docs/research/open-questions.md`;
an experiment is removed from this file when its question closes.

**The test for whether a question belongs here: would a player *see* it happen?**
Internals — tick rate, per-tile arithmetic, update order, RNG — will never be
settled by playing. §8 says which those are, so nobody wastes an evening on one.

Answers go in `open-questions.md` marked **CONFIRMED — owner observation**, with
the same weight as a data extract. A "no" closes a question as hard as a "yes".

**Two rules the answered items earned.** Ask for a **quantity by name** ("how
much gold per swing", not "how fast"), and ask for values that **differ from
each other** — two identical readings cannot separate a real column from a
coincidence. A **screenshot with the HUD visible** beats a description, and a
60 fps recording beats a stopwatch where the answer is a count.

**Where the numbers are read.** A rate is read off the **thing's own remaining
value** over *consecutive* decrements, never one — the seam's gold, the tile's
health. That is what settled gold per swing to the last digit four times
(`tile-work.md`). A creature's **Efficiency** and **Health: NN%** are on the
status bar while it is selected.

---

## 1. Run sheet — which level, and in what order

Four sittings. Every experiment is written up in its own section below; this is
only the route through them, and the order inside a sitting matters where it
says so.

**Mode changes answers, so it is part of the method** — Q199 is the proof, the
same rule reading +500 in skirmish and **+1400** in My Pet Dungeon. Mana, gold
and wages in the campaign; geometry in MPD, where a room's capacity has no
reason to care.

### Sitting A — campaign level 1, from a fresh load

One keeper, no combat, and a creature pool of exactly one entry, which is what
makes it the clean instrument. **Level 1 overrides payday to 1200 s** where the
global is 600 (`Level1Variables.kld`), so do not sit waiting for one here.

1. **Q75 first, inside the first ten seconds, before an imp has moved.** Read
   the HUD gold total against what is actually in the treasury and heart. Once
   mining starts the comparison is gone for the session.
2. **Q49** — ten unclaimed tiles, one imp, nothing else marked anywhere on the
   map. Time the chain, never one claim.
3. **Q102** — one fresh arrival, untouched; time its first walk to a hatchery.
   Then repeat with one you pick up and drop halfway.
4. **Q50, Q143, Q145, Q177** share one setup: two blocks of gold marked far
   apart, one imp, kept selected so the status bar's Efficiency is visible.

### Sitting B — My Pet Dungeon, for room geometry

Build to the exact sizes given; the size *is* the experiment.

- **Q179** — a **4x4** prison, against the record's 5x5.
- **Q216** — one creature in a library, let the bar move, then slap it.
- **Q63** — train a *different* creature at the dummy: a goblin against a bile
  demon.
- **Q164** — kill something as far from the graveyard as the map allows.
- **Q35, Q138, Q140** — one 3x3 treasury: fill it, then keep mining.
- **Q202** — slap to unhappy with a free bed available; no setup of its own.

### Sitting C — a skirmish map, for a rival and a fight

**Q23** a rival's dig markers, **Q152** a wounded imp in a fight, in the hand and
possessed, **Q57** mana spent down to nothing on a small dungeon.

### Sitting D — the My Pet Dungeon level that read +1400

**Q199**, and **write down which level it is**: no note here names it.

### Sitting E — anywhere with a fight, for §6

**Q335, Q337, Q325** share one setup and one rule: **count blows to kill, never
seconds**. **Q223** is a single look on Level 13; **Q87** needs two portals.

### Waiting on tooling, not on you

**Q89** needs a level whose pool holds the **Dark Angel** (creature id 23) or the
**Maiden Of The Nest** (45). `dk2tools kwd vars` prints a level's pool *count*
and not its entries, so nobody can currently say which levels those are.

---

## 2. Imps, work and gold

### Q145 — does fetching interrupt a swing already in progress?
Ours finishes the wall it is swinging at, then turns round. A **gem** tile
cannot settle it: it is inexhaustible, so the imp re-chooses after every
completion and interrupt-now looks identical to interrupt-at-the-next-choice.

**Do:** mark a long stretch of ordinary rock, let an imp get well into one tile,
then drop gold beside it — sell a treasury tile, or drop a pile from the hand.
**Watch:** whether it abandons the tile mid-swing or finishes it.
**Means:** either answer takes this mechanic to CONFIRMED.

### Q177 — does the aimed bonus carry from one marked tile to the next?
Dropping an imp onto marked work pays 90% instead of 80% (ADR-0069). The binary
says the bonus is cleared only when a worker on that *kind* of task can find no
more anywhere on the map, so it should last the whole marked seam.

**Do:** mark two blocks of gold far apart. Drop an imp on one. Keep it selected.
**Watch:** Efficiency on the walk to the second block.
**Means:** **90%** and the binary is right. **80%** and DK2 clears the flag
somewhere unfound — which matters, because `DroppedTask` has only two writers in
the whole executable and one of them would have to be wrong.

### Q50 — how many swings before an imp runs off with its load?
Answered by arithmetic: a swing frees 273, an Imp holds 1400, so it fills after
**five** swings (1363), and an eleven-swing seam is three trips — 1363, 1364,
273. Marked HYPOTHESIS because nobody has counted.

**Do:** count the swings between an imp starting on a fresh seam and running off.
**Means:** five confirms the chain from Q47 through `MaxGoldHeld`.

### Q35 — where does a full treasury's overflow go?
The deposit half is read from the binary: the carrier takes the first tile below
capacity along the room's own tile chain. Still open are the **withdrawal** order
and the lump.

**Do:** fill a **3x3 treasury**, then keep mining.
**Watch:** where exactly the lump appears — beside the treasury, or back at the
seam. **Means:** decides which gold survives a treasury partly destroyed.

### Q138 / Q140 — how big does a floor lump grow?
`MaxGoldPileOutsideTreasury` is **2000** and is the candidate ceiling.
`MaxGoldPerTreasuryTile` is 3000. Ours keeps surplus in the imp's hands and
sends it to find other work; the original has a twelve-slot layout for putting a
load down.

**Do:** fill the treasury, keep mining.
**Watch:** how much piles up on the floor before the imp stops, and whether an
imp that cannot deliver puts its load down at all.

### Q143 — does picking gold up off the floor take time?
Ours picks up the instant the imp arrives. Nothing is damaged and the balance
table has no number for it, so charging a duration would be an invention.
**Do:** watch an imp reach a floor pile. **Watch:** whether it pauses at all.

### Q49 — how long is the pause after claiming a tile?
The pause is observed and specific to claiming — an imp whose *wall* has fallen
moves straight on. At roughly 500 ms on top of a 0.8 s claim it is a ~60% tax on
how fast territory spreads. **No balance variable is a candidate:** every
`...Seconds` variable is a whole number and none is below 1.

**Do:** one imp, one straight run of **ten** unclaimed floor tiles it can reach,
nothing else marked anywhere. Start the clock as it lands on the first tile,
stop it as the tenth turns your colour. Time the chain, never one claim.
**Means:** **~12 s** → no pause. **~17 s** → a 500 ms pause. Dividing by ten
reaches a precision one observation never could.

### Q23 — are dig markers per keeper or shared?
**Do:** in a skirmish, look at a rival keeper's walls. **Watch:** whether their
dig markers are visible to you. Ours assumes per player (STRONGLY INFERRED).

---

## 3. Creatures, mood and needs

### Q102 — when does a creature's hunger clock start?
Implemented: at spawn, counting to the record's `HungerRate` (180 s for a
Goblin). It could equally start at the first meal, at level start, or reset when
the creature is picked up.

**Do:** take one fresh arrival, do not touch it, and time its first walk to the
hatchery. Then repeat with one you pick up and drop halfway through.
**Means:** an unchanged interval says the clock is not reset by the hand.

### Q7 — the shape of anger
The four causes are recorded (no lair, hunger, no pay, hated company) and the
ceiling is measured (32767, Q106). What is missing is the shape.

**Do:** with no pay at all, watch one creature from content to gone.
**Watch:** whether anger arrives in steps or builds smoothly; roughly how long
from unhappy to leaving; whether two causes together are faster than one; and
whether it fades once the cause is fixed.

### Q202 — does sleeping cool anger, or only cool *some* anger?
DK2 keeps six anger counters. Four are certain; buckets 0 and 5 are catch-alls
and their names in our code are ours.
**Do:** slap a creature to unhappy with a **free bed available**.
**Watch:** whether sleeping brings it back down.
**Means:** it does → bucket 5 confirmed as the slap's bucket and cooling covers
it. It does not → the slap's anger is in a bucket sleep never touches.

### Q152 — does regeneration continue in combat, in the hand, or under possession?
Ours regenerates in all three. An imp is the subject: 67 a second, one of only
two creatures that regenerate at all (King Reginald, 400, is the other), and 67
a second matters inside a fight and nowhere else.
**Do:** three ten-second looks — wound an imp and (1) start a fight it will
survive, (2) hold it in the hand, (3) possess it. **Watch:** the health bar.

### Q164 — does the corpse clock pause while the corpse is carried?
Ours says no: `DeadBodyDiesAfterSeconds` runs while a body is in an imp's arms,
so a long walk to a distant graveyard can outlast the corpse.
**Do:** kill something as far from the graveyard as the map allows.
**Watch:** whether the imp arrives holding a body or holding nothing.

---

## 4. Rooms

### Q179 — a 3x3 prison works where the record says 5x5
`RoomDef.RecommendedSizeX/Y` was read as *the size below which the room does
nothing* (ADR-0027, Q45), on the Graveyard 3x3 and Temple 5x5 matches and the
1999 manual's own wording. The Prison's record says 5x5 and a 3x3 works.

**Settled for the prison** (owner, 2026-09-11): a 4x4 functions, so the field is
a recommendation there and Q45's rule is wrong for at least this room (Q179).

**Do next, and it decides whether Q45 survives anywhere:** a **2x2 graveyard**,
below its own 3x3 record. Working → the field gates nothing and the two matches
the rule was built on were coincidence. Not working → the prison is a real
exception needing its own reason.

### Q89 — does a room below `RecommendedSize` still attract?
**The Dark Angel's temple entry is `size >= 0`**, against both the owner's
recollection and the 1999 manual, which say 5x5. The mechanism *can* express a
minimum — four creatures carry one, the Maiden of the Nest wanting Temple ≥ 9 —
so the Dark Angel's zero is a statement, not an absence. Q89's row has the rest.

**Do:** a dungeon with a **3x3 temple** and nothing else either creature wants,
on a level whose pool holds the Dark Angel and the Maiden.
**Means:** Dark Angels arrive → the 5x5 belief is 1.0-era folklore and the
shipped record is right. None arrive → the requirement lives in a mechanism
outside this field. **The Maiden is the Q89 half:** she arrives at 3x3 → a room
below `RecommendedSize` advertises while doing nothing, and the two numbers are
independent.

### Q168's leftover — can the prison's bar be set in the interface?

The roll is ANSWERED (once per over-capacity insertion, from the binary). What is
not is the manual's claim that a prison door carries a **bar** stopping further
deliveries, with `SafeCreatureCapacity` held separately from the real capacity in
the room dump.
**Do:** fill a 3x3 prison — it holds **two** (Q166, off the status bar).
**Watch:** whether that limit can be set in the interface, and whether it stops
imps at the number you set rather than at the room's size.

### Q216 — does slapping a researcher visibly triple their research?
The adder computes the slap bonus as `variable19 × 50 × variable19 × perTick /
100` — **eight times the per-tick amount**, added once a game second on top of a
base of four, so 12 a second against 4 where the variable is named 50%. The extra
`variable19` is read from the instructions, not inferred.
**Do:** put one creature in a library, let the research bar move, then slap it.
**Watch:** whether the bar's rate roughly triples or rises by about half.

---

## 5. Mana and the economy

### Q57 — what does DK2 do when a keeper runs out of mana?
The gold half is settled: an imp takes no wage and no gold upkeep. The mana half
is not. Ours floors mana at zero and carries on;
`InsufficientManaImpEvaluationPeriodSeconds` (10) says the original re-evaluates
imps on a ten-second clock when mana is short.
**Do:** buy a lot of imps on a small dungeon and spend down to nothing.
**Watch:** do imps start dying, do they stop working, or does the game simply
refuse to spend?

### Q58 — does possession really cost nothing per second?
The data disagrees with itself as well as with the owner's report of no drain:
the spell record says Possession = 25 a second, `ManaCostInFirstPersonPerSecond`
in the balance table says 5. The likely explanation is that the mana orb is off
screen in first person, which is a guess.
**Do:** note the mana total, possess a creature for a measured ~30 s, leave, note
it again, and compare against what the green rate alone would have earned.
Nothing depends on this; possession arrives at M7.

### Q199 — is the mana income cap different in My Pet Dungeon?
The skirmish clamp is CONFIRMED: more than 500 claimable tiles and the rate would
not pass 500, *"a hard limit"*. On a pet-dungeon level the owner read **+1400**.
The owner asked for this to be flagged for later rather than settled now.
**Do:** on that level, note **which level**, count your claimed tiles roughly,
then sell a room or lose some floor.
**Means:** +N moves one per tile → territory income with a different or absent
cap in that mode. It does not move → the displayed rate is not what `ManaRules`
computes. `mana.md` §*The cap*.

### Q9's leftover — what is `PayDayCutOffTimeSeconds` = 120?
`PayDayFrequencySeconds` is 600 and names itself; so do
`TimeBeforeFirstPayDaySeconds` (600) and `DelayBeforePayReevaluationSeconds` (4).
The 120 is unread, and the guess is how long a creature has to reach the treasury
before it counts as unpaid.
**Do:** at payday, follow a creature that is slow to arrive.
**Watch:** whether it gets angry, and roughly how late it was when it did.

### Q63 — what does training cost, and where is the number?
Measured: **20 gold** an action at a training dummy, **0** at the moving target,
flat across levels, with level 4 the ceiling (`TrainingRoomMaxExperienceLevel`).
The source is unidentified: the balance table has no training cost and
`Rooms.kwd` carries no 20. `Objects.kwd` offset **0x0208** reads Training Dummy
20 and Training Target 0 — matching exactly — but the rest of that column does
not look like money (Gold Pile 4096, every creature bed 60, Chicken 45), so it is
recorded and not believed.
**Do:** train a **different** creature at the dummy — a goblin against a bile
demon.
**Means:** 20 again → the cost is a property of the thing being hit, which is
what an OBJECTS field is, and 0x0208 becomes hard to argue against. A different
number → the cost is per creature and 0x0208 is out.

### Q75 — is there a player-level starting-gold stockpile?
The 1999 manual, p.20: at level start your total wealth may differ from the
amount in your treasuries. ADR-0015 says DK2 has no player-gold variable and
gold lives in tiles; the two reconcile only if the stockpile is a different
thing from stored gold.
**Do:** at the very start of a campaign level, read the gold total on the HUD and
count what is actually in the treasury and heart.
**Means:** they differ → there is a stockpile and ADR-0015 needs an amendment.

---

## 6. Combat

The damage formula is **built** (`MeleeDamageRules`, ADR-0153) and four branches
are guesses. **Count blows to kill, never seconds**: possession skips the
recharge gate, so anything timed measures cadence rather than damage.

### Q335 — does a creature resist the attack type its record says it resists?
Nothing in the executable reads the shipped `Resistances` table (`combat.md`
§6.1.1). **Do:** hit a creature with an attack type its row resists and one it
does not. **Means:** equal counts → the column is vestigial; different → it lives
somewhere unfound.

### Q337 — does possession change damage per blow?
**Owner, 2026-09-16: it "feels" stronger — which the gate skip already explains**,
so it does not settle this. The scalar has no writer, so the bonus is either +12%
or nothing. **Do:** same creature and target, blows to kill, possessed and not.
**Means:** 8 against 9 on 1500 HP → +12%; equal → the Hypothesis comes out.

### Q325 — is there really a direction bonus?
The swing adds up to **+51%** off the compass bearing between the two things,
reading neither one's facing (arithmetic CONFIRMED). **Do:** attack along an
axis, then diagonally. **Means:** fewer blows one way → the reading holds; no
difference → a gate was missed.

### Q87 — is a level's `CreaturePool` a quota or a standing limit?
Capacity is CONFIRMED (15, +5 a portal); whether the pool *drains* is not.
**Do:** count arrivals past 15 on a level with two portals claimed.
**Means:** more than the pool's own number ever arrives → a standing limit.

---

## 7. Presentation only — no rule may depend on the answer

### Q72 — does a wall deform the same way twice?
DK2's wall meshes are authored straight and are visibly bent in game, sometimes
dramatically. Whoever extracted them infers runtime vertex displacement and says
so; the alternative is several hand-made variants assembled by code. The answer
decides whether a renderer computes a wall's shape from a tile coordinate or has
to remember it.

**Do:** find a wall with a distinctive silhouette — a bulge, a lean, a notch —
and screenshot it from a fixed camera. Quit to the menu, load the same level
again, and screenshot the same wall from the same angle.
**Means:** **identical** → the shape is a function of where the tile is, a hash,
and a renderer stores nothing. **Different** → it is decided when the wall
appears and is view state a renderer has to keep. Either way **the simulation
must not gain a field for it**. Not blocking anything; say so and it stays open.

### Q282 — does the idle taunt fire at 2:40 and the camera wander at 3:00?
Idleness is cursor movement alone, timed in real milliseconds, so pause does not
stop it; thresholds, alternate line and suppressing mode global are in
`mentor-speech-and-notifications.md` §5, untimed. **Do:** park the pointer clear
of a Library, don't touch the mouse, time both. **Means:** taunt ~160 s then
wander ~20 s later → the read holds; later, or nothing → a gate was missed.
Report the **exact line**: over a Library with >4 creatures it should differ.

---

## 8. What playing cannot settle

Instrumentation, measurement tooling or disassembly, and scheduled accordingly.
`docs/RESEARCH-QUEUE.md` is where they are worked.

| Q | Why not |
|---|---|
| Q3 | Which RNG — invisible by construction |
| Q5 | The pathfinding algorithm. You can see the *route* (Q28 and Q30 both came from watching one), never the algorithm |
| Q39 | Whether mana accrues smoothly or in whole seconds. The unit is CONFIRMED per second; granularity is invisible in any total and nothing depends on it |
| Q48 (second half) | What the *pathfinder* charges for a diagonal when choosing between routes. The mover's cost is forced — a diagonal step covers √2 tile-widths at a constant speed, so octile 14:10 — and the owner confirmed the glide. Two cost models produce the same paths except where a route with fewer steps is longer in true distance |
| Q4, Q11, Q14, Q15, Q70, Q85, Q183 | Record and runtime-structure layouts, field meanings |
| Q103 | The sleep cycle. A stopwatch corroborated 240 s up and could not close it, because the walk to the bed is inside the measurement; what resets the last-slept stamp is only in the binary |
