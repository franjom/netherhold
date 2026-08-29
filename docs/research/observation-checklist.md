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

## 1. Blocking — these stop work from continuing

### Q31 — where does mined gold go?
**The single most valuable observation on this list.** It is the missing half of
M3's economy and everything about the gold loop waits on it.

Watch one imp mine a gold seam, closely, and answer:

1. When gold comes out, does the **imp pick it up and carry it**, or does a
   **pile appear on the floor** for someone to fetch later? (Or both — mines into
   its hands, drops a pile when full?)
2. Does the seam give up gold **gradually as it is mined**, or **all at once when
   the tile collapses**? (That is Q21, and the same session answers it.)
3. What makes the imp **stop mining and walk off to the treasury** — a full load,
   the tile finishing, something else? Roughly how many trips does one seam take?
4. When it arrives at a multi-tile treasury, **which tile does it fill** — the
   nearest, the first, the emptiest? (Q35. Watch where the gold piles appear.)
5. What happens when the treasury is **full** — does the imp stand there, drop
   the gold on the floor, or take it somewhere else?

### Q24 — what does an imp do when nothing is marked?
The other blocker, and the reason no worker behaviour beyond digging exists.
Ours only does work the keeper marked, on purpose.

Leave a dungeon alone with **nothing marked for digging** and watch idle imps:

1. What is the **first** thing an unprompted imp does? Claim floor? Reinforce a
   wall? Fetch gold? Something else?
2. Give it several options at once — unclaimed floor, an unreinforced wall, a
   gold pile on the ground — and see which it picks. That ordering is the answer.
3. Now **mark a wall for digging** while it is mid-job. Does it drop what it is
   doing immediately, or finish first? (Does marked work outrank everything?)
4. Does an imp ever **stop and do nothing at all**, and if so, when?
5. What does an imp do when an enemy creature comes near — flee at once, flee
   only when attacked, keep working?

### Q20 — how long does digging actually take?
The data says one application of `DigRockHealth` destroys a rock tile outright,
which would make digging earth instantaneous. It is not. Something is wrong with
our reading and nothing else in the tile rules is.

With a single imp digging alone:

1. Roughly **how many seconds** to break through one plain earth/rock tile?
2. Does a **gold seam** take noticeably longer than plain rock? A **gem** face?
3. Does an imp visibly **swing repeatedly** at one tile, or does the tile go on
   the first contact?
4. Do **two imps on the same tile** finish it in about half the time?

---

## 2. Cheap, and each settles a contradiction in the data

### Q34 — what does selling a room refund?
Both candidate numbers in the data say **100%**, which would make rooms free to
try. The original is not like that, so one of the two numbers is not what its
name says.

Build a 1-tile treasury (200 gold), note the gold, sell that tile, note it again.
**How much came back?** That is the whole experiment.

### Q32 — is a room's cost per tile or per room?
Ours charges per tile. Note your gold, paint a **two-tile** treasury, note it
again. 200 or 400?

### Q36 — do a room's recommended dimensions constrain what you may build?
The ROOMS record carries `RecommendedSizeX/Y` and a construction style; nothing
here reads them, so a 1×1 temple is buildable.

**Try to build a 1×1 temple, a 1×1 lair, a 1×2 casino.** Does the game refuse, or
allow it and just draw it oddly? Does anything need a minimum size?

### Q33 — must a new room tile touch land you already hold?
On land, ownership already forbids everything such a rule would. The gap shows
only for **bridges**.

Find water with your land on the near shore. **Can you start a bridge tile out in
the middle of the water**, not touching anything you own, or must it grow out
from your shore one tile at a time?

---

## 3. Movement and worker detail — a few minutes each

### Q28 — can creatures cut a diagonal corner?
Ours says no (four-connected), chosen as the conservative guess. Dig an L-bend so
two solid tiles meet at a diagonal pinch, and watch a creature routed through it:
**does it slip diagonally between the two corners, or go around?**

### Q27 — where does an imp stand to work a tile?
Watch an imp digging a wall. Is it always **orthogonally** adjacent (N/S/E/W), or
will it work a tile from a **diagonal** neighbour?

### Q25 — which marked tile does an imp choose?
Mark two walls, one clearly nearer **by walking distance** and one nearer **in a
straight line but around a corner**. Which does the imp go to? (Ours: fewest
steps.) Then mark a far one *first* and a near one *second* — does marking order
matter at all?

### Q26 — do imps spread out or pile on?
Mark **four separate walls** far apart and release **four imps**. Do they take
one each, or do several converge on the same tile? (Ours: no reservation, they
converge.)

### Q30 — does terrain slow a path down, or only the creature?
With a creature that can cross water, offer it a **short route through water** and
a **longer dry route**. Which does it take? And does anything visibly move slower
*on* water rather than simply taking longer to arrive?

### Q29 — is creature speed in tiles per second?
Stopwatch an imp walking a straight **ten-tile corridor**, then a dwarf over the
same ground. We predict the imp is about 1.84 tiles/second and the dwarf slower
in a fixed ratio. Even a rough number confirms or kills the unit.

---

## 4. Free to note whenever you happen to see them

### Q40 — what does defeat actually do?
We know losing your heart is defeat. Watch a **rival keeper** lose theirs:
what happens to their **creatures**, their **rooms**, their **claimed land**, and
their **gold**? Does their territory revert to unclaimed, or stay coloured? Do
their creatures die, flee, or change owner?

### Q23 — are dig markers per keeper or shared?
In a skirmish, **can you see a rival keeper's dig markers** on their walls? (Ours
assumes each keeper has their own.)

### Q9 — payday
Roughly **how often** does payday come round, and is it the same clock for every
creature or per creature? What visibly happens when a keeper **cannot pay**?

### Q22 — filling a tile back in
Fill in a claimed floor tile. Does it take about as long as digging it out, or
noticeably less? Does a partly-filled tile **revert** if you cancel?

### Q12 — do imps ever eat or sleep?
The Imp's hunger rate and wake time are both 32767, which we read as a "never"
sentinel. **Do imps ever go to a hatchery, or to a lair to sleep?** A plain no
confirms it.

### Q38 — is the mana gain cap shared between keepers?
Hard to stage, so only if it falls out naturally: in a skirmish where one keeper
holds an enormous dungeon, does **another keeper's** mana income visibly suffer?
(Ours: per keeper, no interaction.)

---

## What playing **cannot** settle

Do not spend time on these at the keyboard — they need instrumentation,
measurement tooling, or disassembly, and are scheduled accordingly:

- **Q1 — the exact tick rate.** Fixed, but the number is internal. Needs the M5
  oracle probe, not an eye. (A stopwatch on a *long* repeated action gives an
  approximation, which is why some entries above ask for seconds.)
- **Q19 — what the health deltas are *per*.** Tied to Q1, same reason.
- **Q5 — the pathfinding algorithm.** You can see the *route* (Q28, Q30 above),
  never the algorithm.
- **Q3 — which RNG.** Invisible by construction.
- **Q7 — the mood model.** You can see a creature get angry; the formula behind
  it is a disassembly job.
- **Q35's tie-breaks, Q21's exact arithmetic** and anything else that is a
  *number per tile per application* rather than a visible event.
- **Q37a — what `MaxManaGain` and `DungeonHeartMaxManaIncrease` are for.**
  Closed by elimination; they affect nothing a player can see, so there is
  nothing to look at.
