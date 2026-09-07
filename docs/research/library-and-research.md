# The Library: research accrual

Milestone M4/M6. What a creature standing in a Library earns for its keeper.

Implemented by `DK2.Simulation.ResearchRules` and the `JobType.Research` branch
of `CreatureJobSystem`. Read from the binary on **2026-09-07**; every number
below is from the reference install or from DKII.EXE, and none is written into
the code as a literal.

**Scope: the accrual, not the tree.** DK2 keeps two fields on the keeper — the
accumulated points and a *current target* — and this reproduces only the first.
Nothing here decides what is being researched, what it costs, or what happens
when it completes. That is less of a hole than it sounds: **the original credits
the points whether or not a target is selected**, because the field is written
before the target is even looked at. So the accrual is a complete mechanism.

---

## Summary of confidence

| Claim | Status |
|---|---|
| The per-tick hook is state category **4**, `sub_0047b000` at `+0x14` of the record at `0x00699788` | **STRONGLY INFERRED** — read from the category table |
| It reads `ResearchPerSecond` at runtime `def+0x6BB` | **STRONGLY INFERRED** — the loader pair `src=0x12F8 → dst=0x6BB`, and file `0xF08 + 0x3F0 = 0x12F8` exactly |
| It scales by the level table's row at `+0x201`, which is stat **17** | **STRONGLY INFERRED**, and now pinned from both sides — see below |
| Level 1 bypasses the table; the result is floored at 0 and capped at `0xFFFE` | **STRONGLY INFERRED** — read from the instructions; the cap is unreachable with shipped data |
| The credit goes to the **keeper**, `player+0x20F` | **STRONGLY INFERRED** — the hook ends `MOV ECX,[ESI + 0x2A]`, the creature's player pointer |
| Credited **every tick**; the three bonuses only once a game second | **STRONGLY INFERRED** — the base add sits outside the `tick % variable19 == 0` guard |
| Nothing charges a keeper for research | **STRONGLY INFERRED** — the hook has no cost path, unlike training's |
| A slap makes a creature research faster for **60 game seconds** | **STRONGLY INFERRED** — and it corrects a documented claim, see §4 |
| The slap bonus is **+200%**, not the 50% its variable is named for | **STRONGLY INFERRED** — the extra `variable19` is in the instructions |
| What sets the casino flag at `player+0x20D` | **UNKNOWN** (Q217) |
| What passes the adder a negative amount | **UNKNOWN** (Q218) |
| Whether a player can see the slap tripling their research | **UNKNOWN** (Q216) — needs the tree |

---

## 1. The hook, in full

Eleven lines, and the shortest category hook read so far.

```
sub_0047b000(state):
    creature = state[+0x08]
    if creature[+0x403] == 1:                       # level
        amount = creatureDef[+0x6BB]                # ResearchPerSecond, per TICK
    else:
        amount = levelTable[+0x201 + level*2] * creatureDef[+0x6BB] / 100
        if amount <= 0:      amount = 0
        if amount >= 0xFFFE: amount = 0xFFFE
    FUN_004c2dc0(amount, creature)                  # ECX = creature[+0x2A], the keeper
    return 1
```

**The field is identified exactly, not by neighbourhood.** `CopyPairs.py` gives
the loader's source→destination pair `src=0x12F8 dst=0x6BB`, and
`ResearchPerSecond` is at file offset `0xF08`; the creature loader's constant
delta is `+0x3F0`, and `0xF08 + 0x3F0 = 0x12F8`. The same run re-confirms
`ManaGeneratedByPrayerPerSecond` at `+0x6A2` from the previous sitting.

### The level row was predicted before it was read

`CreatureStatId.ResearchPerSecond = 17` was pinned by an argument from *both
ends*: the battle hook reads `ExpPerSecond` against the column at `+0x1DD`, the
four columns `0x1DD / 0x1EF / 0x201 / 0x213` are stats 15–18 on an 18-byte
stride, and the shipped table shows them as two matched pairs (15 and 16 flat at
100%, 17 and 18 carrying an identical curve).

**This hook indexes `+0x201`.** That is the column the argument predicted, read
from the other side a week later, so stat 17 is now confirmed rather than
inferred. The curve is the steepest production curve in the game:

| level | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| **%** | 100 | 125 | 175 | 250 | 300 | 350 | 400 | 450 | 550 | **700** |

So a Warlock's *level* is the biggest lever a keeper has over research speed —
seven times the rate at level 10.

---

## 2. The rate is per tick, and the loader's floor inflates three creatures

`ResearchPerSecond` is one of the ten record fields DK2's creature loader
divides by variable 19 as it loads them, flooring a non-zero result at 1. The
hook credits the **divided** figure every tick. `dk2tools kwd research` dumps
the whole column:

| id | creature | tabled | per tick | per second | delta |
|---|---|---|---|---|---|
| 5 | Warlock | 5 | 1 | **4** | −1 |
| 8 | Vampire | 5 | 1 | 4 | −1 |
| **9** | **Skeleton** | **1** | **1** | **4** | **+3** |
| 16 | **Wizard** | 10 | 2 | **8** | −2 |
| **19** | **Monk** | **3** | **1** | **4** | **+1** |
| 20 | Fairy | 5 | 1 | 4 | −1 |
| **23** | **Dark Angel** | **3** | **1** | **4** | **+1** |
| 34 | Unique Warlock | 18 | 4 | 16 | −2 |
| 42 | Unique Vampire | 15 | 3 | 12 | −3 |
| 44 | Unique Dark Angel | 15 | 3 | 12 | −3 |
| 45 | Maiden Of The Nest | 4 | 1 | 4 | 0 |

**Eleven creatures can research, and the Warlock is not the best of them.** The
Wizard — a *hero* — is twice as fast, which is worth knowing because every
description of the game presents the Library as the Warlock's room.

**The loader's floor runs both ways, and the second direction had not been
found.** `SimulationTiming.LoaderRatePerTick` documented the floor as reachable
in the sense that an `angerNo*` of 1 keeps its 1. It is also reachable *upward*:
the **Skeleton's tabled 1 becomes 4 a game second, four times its record**, and
the Monk's and Dark Angel's 3 become 4.

> **A claim that nothing checked was already false.** That doc block said the
> floor's sign-flip case was unreachable because *"every one of the ten is
> either zero or at least four"*. Three creatures carry 1 or 3 in this column.
> The **conclusion** survives — no shipped field is negative in the −3..−1 range,
> so the sign flip still cannot occur — but the reason given for it was wrong,
> and it had stood unchecked for a month. `ResearchOnRealDataTests` checks it now.

---

## 3. The keeper's adder, and three bonuses that are not what they say

`FUN_004c2dc0(this = keeper, amount, creature)`:

```
if amount < 0:
    this[+0x20F] = max(0, this[+0x20F] - |amount|)      # clamp-subtract
else:
    bonus = 0
    if now % variable19 == 0:                            # ONCE A GAME SECOND
        if this[+0x20D] != 0:                            # casino big win?
            bonus += variable19 * var143 * variable19 * amount / 100
        if FUN_004f2300(creature[+0x28]):                # tortured type?
            bonus += (variable19 + var142) * variable19 * amount / 100
        if creature[+0x4BB] != 0 and
           now - creature[+0x4BB] < variable19 * var165: # slapped recently?
            bonus += variable19 * var164 * variable19 * amount / 100
    this[+0x20F] += bonus + amount                       # base EVERY tick
```

The `/100` is the compiler's `MOV EAX,0x51EB851F ; MUL ; SHR EDX,5` reciprocal,
confirmed in the disassembly rather than trusted from the decompile — which
mattered, because the decompile of this function also produced one pure artefact
(a `uVar7 = uVar8 / 0x32000` that is register noise feeding a two-register
argument).

**All four `IncreasedWorkRate*` variables are read here, and only here.** Each
pair is loaded as one dword and split — `DAT_006F2914` is variables 142 and 143,
`DAT_006F2938` is 164 and 165. That folded-neighbour load is why a naive scan
for either high-half variable finds nothing.

### The effective percentages are not the tabled ones

Per game second the base is four per-tick amounts, and a bonus is one lump. So:

| bonus | variable | tabled | in the code | effective |
|---|---|---|---|---|
| casino big win | 143 | 35% | `4 × 35 × 4 / 100` = 5.6× | **+140%** |
| tortured type | 142 | 25% | `(4 + 25) × 4 / 100` = 1.16× | **+29%** |
| slapped | 164 | 50% | `4 × 50 × 4 / 100` = 8.0× | **+200%** |

**Two different slips in three adjacent lines.** The casino and slap lines
multiply by `variable19` **twice** (`IMUL ECX,EAX ; IMUL ECX,EBX`), which is one
factor more than "a percentage of the per-second rate" needs. The tortured-type
line instead **adds** where the others multiply — `ADD ECX,EAX` in the same
position — so its 25 behaves as 29. That two of three siblings are wrong in
unrelated ways is the strongest argument that none of it is deliberate, and it
is why all three are reproduced rather than corrected (ADR-0002).

The tortured-type figure is Q81's, arrived at independently; this reading
reproduces it and puts the other two beside it.

---

## 4. One slap, two effects, and a variable that was not dead after all

This project had `IncreasedWorkRateDurationFromSlappingSeconds` (60) written up
as **a variable nothing reads** — in `tile-work.md` and in the reasoning behind
ADR-0062/Q159. It is read, here.

The reason it looked dead is that it was being looked for in the wrong
mechanism. **A slap does two unrelated things:**

| effect | duration | where the duration comes from |
|---|---|---|
| Efficiency **+10 points** (work rate) | **25** game seconds | a **hardcoded 100** in the slap handler; `SlapWorkFasterDurationSeconds` agrees |
| **Research +200%** | **60** game seconds | `IncreasedWorkRateDurationFromSlappingSeconds` |

Both are stamped by the same handler (`FUN_0048b8a0`, whose step 5 is
`LastSlapTick = now` at `creature+0x4BB`), and the research bonus is gated on
that stamp.

> **This partly rescues an owner measurement that had been written off.** Q159
> closed *against* the owner's own hand-timed *"after a minute"*, in favour of
> the binary's 25 seconds, and the 60 in the table was called a red herring.
> **There really is a minute-long slap effect** — it just is not the work rate.
> Q159's conclusion is unchanged and correct; the dismissal of the 60 was not.

---

## 5. What is deliberately not built

- **The research tree.** No target (`player+0x213`), no costs, no completion,
  no spell or room unlocks. Points bank indefinitely.
- **The casino bonus.** Needs a Casino, and **what sets `player+0x20D` is
  UNKNOWN** (Q217) — it is a 16-bit field beside the accumulator, tested for
  non-zero, and the variable's *name* is the only evidence for reading it as a
  big-win flag. A name is not a reader.
- **The tortured-type bonus.** Needs a Torture Chamber and a per-keeper list of
  the types tortured there.
- **The negative path.** The adder clamp-subtracts a negative credit, and
  nothing has been found that passes it one (Q218).

## 6. What a player could check

- **Q216 — does slapping a Warlock visibly triple research?** Needs the tree, or
  a research bar to watch. At 12 points a second against 4 it should be
  unmissable, and it would confirm the doubled `variable19` from the outside.
- **Does a Skeleton in a Library really research as fast as a Warlock?** The
  loader's floor says yes — 4 a second each, despite records of 1 and 5. That is
  a strange enough consequence to be worth one look.
