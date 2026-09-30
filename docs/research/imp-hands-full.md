# Imp hands full: why DK2's imps rest where ours fetch gold

**Source:** `DKII.EXE`, read on 2026-09-30 (headless Ghidra 12.1.4 and pyghidra-mcp):
`0x00488930`, `0x00464360`, `0x0051CD90`, `0x0051DC40`, `0x0051AA90`. Also
`GlobalVariables.kwd`, the 66 level `Variables.kld` files, and nine captures. Facts in our
own words; ADR-0046 governs the boundary. The walk-over pick-up itself is
`gold-on-the-floor.md` §12. Q494 is its question, and this doc settles its worker half.

**Status: CONFIRMED; items 1 to 4 below BUILT (ADR-0293), item 5 open.**

The decision checker (`dk2tools decide` on `level2-live2/seed01`) finds 85 Level 2 choices
where DK2's imp rests (state 12) and ours starts a gold fetch. They fall in two groups, and
three facts from the binary explain both. Each fact is **CONFIRMED**: the instructions were
read (headless Ghidra 12.1.4 and pyghidra-mcp), then checked against nine captures.

**Fact 1: the pick-up runs before the state handler, in the same tick.** In `0x00488930`
the order, by address and by control flow, is:

| Address | Call | What |
|---|---|---|
| `0x0048904E`–`0x0048954B` | (inline) | the gold block: worker arm, then creature arm |
| `0x00489584` | `0x00485880` | experience drain |
| `0x00489A68` | `0x00496AA0` | action runner |
| `0x00489AC7` | `0x0045E9F0` | ProcessFight (skipped when possessed) |
| `0x00489ACE` | `0x0045E4F0` | fight follow-up |
| `0x0048A030` | `0x00476FC0` | **state dispatcher**, on the state machine at `+0x24E` (skipped when possessed) |
| `0x0048A037` | `0x0045A8B0` | attackability recompute |

No `RET`, and no jump that skips the gold block, comes before `0x0048A030`. The one jump
over the state call is the possessed test at `0x0048A028`. So an imp that takes a pile in
the worker arm reaches state 10's handler holding it. In the capture, imp 448 (level 4)
deposits in state 29 at tick 2901, is in state 10 empty at 2902, and at 2903 holds 1,585
in state 12: the pile went into its hands, and the chooser that ran afterwards saw them full.

The worker arm's gate, read exactly: it takes only while the purse (`+0x3FB`) is below
MaxGoldHeld(level) less the dword at `0x006F28B6`, compared **unsigned**. It takes the
smaller of the pile's gold (object `+0x14A`) and MaxGoldHeld(level) less the purse. So an
imp holding 1,270 or more at level 4 does not take a pile it stands on.

**Fact 2: variable 74 (`0x006F28B6`) is 340 during every shipped level.** Its only writer
in any block is the Variables stub `0x0051CFE0`, entry 71 (id 74) of the switch in
`0x0051CD90`. That switch jumps through `0x0051D740`, indexed by id less 3, and stores the
record's value at `+0x14`. The Variables dispatcher `0x0051AA90` routes plain variables of
both `GlobalVariables.kwd` and a level's `Variables.kld` there. `GlobalVariables.kwd` has no
id 74. All 66 level files carry it at 340 (`dk2tools kwd varsweep`: the one id that is in
every file with one value; `kwd vars` shows 340 in `level1` and `level2`). The global lies
past `.data`'s initialised bytes, so it is zero only before a level loads.
`balance-variables-in-the-binary.md` lists it among ids the global file does not carry. Its readers are the hands test `0x00464360` and this worker
arm. The name `GameTicks` comes from a community template, and nothing in the binary uses
this variable as a tick count (Q83).

**Fact 3: the MaxGoldHeld level row is 105, 110, 115, 120, then 120.** `[0x006E4154]` is a
pointer (`0x006F2550`), and the row is read at that pointer + `0x13B` + 2 × level. The
CreatureStats handler `0x0051DC40` (kind `0x41`) stores each record's value at
`0x006F261F` + 2 × (9 × stat + level). For stat 6 that is the same cell, so file stat 6 at
level L is exactly what the runtime reads at level L. No level file carries CreatureStats.
For the Imp (1,400, definition `+0x69C`, the same on every level) MaxGoldHeld is:

| Level | 1 | 2 | 3 | 4 | 5–10 |
|---|---|---|---|---|---|
| Percent | (unscaled) | 105 | 110 | 115 | 120 |
| MaxGoldHeld | 1,400 | 1,470 | 1,540 | **1,610** | 1,680 |
| Full (less 340) | 1,060 | 1,130 | 1,200 | **1,270** | 1,340 |

In nine captures (`level1-live1`, the six `level1-live1-seeds`, `level2-live2`,
`level2-live2-seeds`), the largest imp purse at each level is exactly the MaxGoldHeld row
above for levels 1 to 5. `CreatureLevelRules.MaxGoldHeld` computes the same numbers.
Its summary says "120% from level 5", which is true but leaves out 105 to 115 below it. The
1,400 in our runs comes from two callers that bypass it (below).

**The level byte** is `+0x403`, and it is 1-based. The constructor stores 1
(`0x004875C9`), the increment `0x0048B120` stops at 10, and every reader here treats 1 as
unscaled and indexes the row by the level itself. The captures show levels 1 to 5.

**The capture check.** An imp started a gold fetch if it went from state 10 to 6 with kind 7,
or from 10 to 27, and its task id was new and non-zero. A deposit walk keeps its stale task
id, so it is excluded. There were **69** such starts in the nine captures, and **none** had
a purse at or above the threshold above. The closest were 1,260 and 1,210 at level 4
(threshold 1,270) and 1,092 at level 2 (1,130). Group 2's refusals at level 4 hold
1,308–1,600, so a threshold that splits both sets must lie between 1,261 and 1,308. The
binary's 1,270 does. The plain cap of 1,610 does not: it would let every one of those imps
go for gold. The script is local (`fetchcheck.py`, scratchpad) and uses
`reverse-engineering/dk2capture.py`.

**How the two groups follow.**

1. *An empty imp standing on a pile.* DK2's worker arm fills its hands (up to 1,610 at
   level 4) before state 10 runs. The chooser then sees full hands and does not choose gold.
   Ours runs the chooser on empty hands.
2. *Level-4 imps holding 1,308–1,600.* They are at or above DK2's 1,270, so DK2's hands
   test says full. Ours tests `GoldHeld >= 1,610` with no 340 margin, so it says there is
   room and sends them.

**What our engine must change** (facts only here; nothing in `src/` was changed):

1. **Build the worker arm of the walk-over pick-up** in the per-tick creature update,
   **before** the imp's state 10 choice in the same tick. It applies to a keeper's
   unpossessed worker whose purse is below MaxGoldHeld(level) − 340. It takes the first
   neutral gold pile (object 1, 2 or 3) within `0x555` of the imp on both axes, strictly,
   and moves min(pile, MaxGoldHeld(level) − purse) straight into `GoldHeld`, not halved.
   An emptied pile is destroyed.
2. **Put variable 74 into `ImpTaskSystem.HandsFull`**: full when
   `GoldHeld >= MaxGoldHeld(level) − variable 74`. Read the variable from the merged level
   variables (id 74, `GameTicks` in `VariableTypeId`), not as a constant. Its doc comment
   ("ships no value") is wrong.
3. **Scale the two unscaled caps by level.** `TileWorkSystem.HandTo` takes a miner's
   capacity from the raw `CreatureDef.MaxGoldHeld`, and
   `ImpTaskSystem.WouldOverfillItsHands` compares with it. Both should use
   `CreatureLevelRules.MaxGoldHeld`. HandTo's remark that the two "agree below level 5" is
   wrong: they agree only at level 1, and at level 4 the difference is 210.
4. **Fix the wording** in `CreatureLevelRules.MaxGoldHeld`'s summary (105/110/115 at
   levels 2 to 4, 120 from 5). `balance-variables-in-the-binary.md` is already corrected
   (id 74 = 340).
5. **The creature arm (non-workers) stays Q494's open half** (`gold-on-the-floor.md` §12).
   This doc covers only the worker arm and the hands test.
