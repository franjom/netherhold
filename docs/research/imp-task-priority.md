# The imp's task list, and the order it tries kinds in

**Source:** `DKII.EXE`, the `ImpUtils.cpp` and `ImpState.cpp` translation units
plus the tile-marking code, read on 2026-09-05. Facts in our own words;
ADR-0046 governs the boundary.

**Why this exists:** the owner reported (Q141) that an imp goes for gold on the
floor before mining, this project believed it and built it, and **the owner then
doubted their own reading and asked for the binary.** The binary disagrees. This
document is what the binary says, and it is worth more than the one answer,
because it also gives DK2's task-kind list and the shape of how an imp chooses.

## 1. DK2 has a keeper-wide task list

The creature debug dump prints an **`ImpJobData`** block — `ScanningJob`,
`LastEvaluate`, `DroppedTask`, `JustDropped`, **`LastTaskType`**, `ImpTaskSlot`,
`ImpTaskSurface`, `ImpTaskPositionX/Y`, `ImpTaskID` — and the player dump prints
a **task list**: `TaskStack`, `TaskListHead`, `NumberInTaskList`,
`NumberOfFreeTasks`. So a keeper accumulates **tasks**, each with a **type**, and
an imp claims one.

Three functions carry the machinery, and they are used the same way everywhere:

| | |
|---|---|
| **claim** | *"is there a task of this kind at/near here that I may take"* |
| **create** | *"raise a task of this kind here"* |
| **dispatch** | a `switch` on the kind, to the handler for it |

The dispatch switch is the enumeration of kinds, and it has **ten arms covering
task types 1, 2, 3, 6, 7, 8, 9, 10, 11, 12, 13, 14 and 16**.

## 2. The order, which is the answer to Q141

One function tries the kinds in sequence and returns on the first that takes —
the plainest possible priority chain, nine calls each guarded by
*"if that worked, stop"*:

| # | Task kind(s) | What it is |
|---|---|---|
| **1** | **1 and 2** | **dig a marked tile / mine a marked gold-bearing tile** |
| 2 | 3 | *unidentified* — tests the **Lava** and **Water** terrain flags on owned ground, so a bridge |
| 3 | 9 and 10 | *unidentified* — room-typed; the same pair `ImpState.cpp` raises for things that are neither creatures nor objects |
| 4 | 13 | *unidentified*, and the largest handler of the lot |
| 5 | 6 and 16 | *unidentified* — tests the **Solid** flag, so walls |
| **6** | **7** | **pick up an object flagged `TypeGold` from the floor** |
| 7 | 8 | *unidentified* — reads the object definition's whole flags word, so other carryable objects |
| 8 | 12 | *unidentified* — tests for **unowned** ground |
| 9 | 11 | *unidentified* — tests the **Solid** flag |

**So marked digging and mining are the first two kinds tried, and gold on the
floor is the sixth of nine.** Fetching does **not** outrank excavation, and
Q24's order — the owner watching, in August — stands exactly as ADR-0018 had it.

### How kinds 1 and 2 were identified

Every other kind is created by the very handler that consumes it: the handler
scans, finds work, and raises the task. **Kinds 1 and 2 are the exception** —
they are raised from outside the imp code entirely, by the routine that runs when
a tile is marked:

- if the terrain is not **Taggable**, no task at all;
- if the terrain's **`GoldValue` is 0** → task kind **1**;
- if its `GoldValue` is not 0 → task kind **2**.

That is dig-a-wall and mine-a-seam, in as many words, and it is the strongest
kind of identification available: the *data field* our own parse calls
`GoldValue` selecting between two task types.

Two tile predicates the kind-1/2 handler uses corroborate it: one accepts
**Solid and not Impenetrable** (a diggable wall), the other **Solid with a
non-zero `GoldValue`** (a seam).

### How kind 7 was identified

Its handler walks the things standing on a tile and takes the first that is an
**object** whose **definition's flags have bit 0x40 — `TypeGold`** — set. That
bit is the one objects.md §7 already confirmed from the original's own object
dump.

## 3. An imp only fetches gold from its keeper's own ground

Before the gold handler looks at anything on the tile, it **refuses a tile that
is neither water nor owned by the worker's own player**.

**This is the answer to Q144, and it is not the answer this project guessed.**
The owner corrected their own earlier report: a gem tile marked while it was not
connected to their dungeon by bridge, with the tiles around it unclaimed, *was*
mined and **none of the gold ever came home**. This project reproduced that
outcome by accident — both of its searches have to walk, so a stranded imp could
not reach a store — and wrote down "reachability or ownership, one bridge
separates them". It is **ownership**, and the bridge never came into it.

The accident is the part worth remembering: **the outcome matched while the rule
was wrong**, which is precisely the shape of defect this project keeps finding in
its own history.

Why **water** is exempt is UNKNOWN. Water is never owned by anybody, so without
the exception gold that fell in would be unrecoverable for good.

## 4. And it also settles who owns a floor pile

The gold handler additionally requires the **object's** own owner to equal the
same global that every gold-creating path uses as the new pile's owner
(`gold-on-the-floor.md` §8). So floor gold carries a fixed level-wide player id
and imps look for exactly that id — the two ends agree, which is what Q136 was
missing. Which id it is is still unread.

## 5. The shape this exposed, which is bigger than the answer — Q147

**The chain above is not called once per imp. It is a callback**, registered in a
table and invoked from a scan, and every handler takes a position and looks
around it.

So the original's structure is: **walk over candidate positions; at each one, try
the task kinds in the order above.** The primary key is *where*, and the kind
order is the tie-break *within* a position.

This project does the opposite: one whole-map search per priority, so the primary
key is the *kind* and distance breaks ties within it (ADR-0018).

**That difference explains the owner's observation without any priority claim at
all.** In a dungeon where imps had just mined a gem tile, gold was lying on every
tile around them — so the nearest position with any task was a gold position, and
every imp fetched, while the gem tile stood marked and untouched. Not because
gold outranks mining. Because it was underfoot.

**Q147**, and it is a change to how *every* job is chosen rather than to this
one, so it is written down rather than made. The cheap test is in the checklist:
mark a wall far away, drop gold at an imp's feet, and see which it does — then
mark a wall right beside the same imp and repeat.

## 6. What this project does now

- **Marked excavation first**, unchanged, CONFIRMED twice over — by the owner in
  August and by the binary now.
- **Gold on the floor second**, above claiming and reinforcing. Only "below
  excavation" is read; the rest is a choice, and it is made this way because
  putting gold last would make the mechanic *dead* — an imp can nearly always
  find a wall to reinforce, so gold would never be collected, which is flatly
  contrary to what the owner watched. **Q146.**
- **An imp will not take gold that is not on its keeper's ground**, which is §3
  and is read.
