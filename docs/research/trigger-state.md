# Trigger state: flags, timers, and who owns them

What the trigger system stores between one record firing and another reading it,
and when the tree that reads it runs.

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70, read statically (ADR-0046); **four live memory captures of campaign level 1** (two of 300 s), correlated against that level's trigger graph; the shipped 66-level corpus |
| Confidence | The layout, the mode bytes, the deadline shape and the once-per-turn cadence are **strongly inferred** from instructions; the authored unit, the stored-times-four scaling and the byte-01 corrections are **observed** in the running game. `CONFIRMED` is not used: no *gameplay* observation was made, only memory reads |
| Implementation | `TriggerState` on `WorldState` (`WorldState.Triggers`), `TriggerConditionEvaluator`, `TriggerExecutor` / `TriggerTree`, `TriggerTurnCsvEmitter`; ADR-0092, ADR-0102 |
| Tests | `TriggerStateOnRealDataTests`, `TriggerExecutorTests`, `KwdAdditionalRecordTests` |
| Open questions | Q229 (what flag id 128 addresses), Q231 (whether the two globals are one object), Q232 answered (the level time limit), Q228's residue (what the 24 slots at `+0x4030` are), Q230 (ids above 128) |

Follows on from `trigger-semantics.md` (ADR-0092), which established every
record's parameter offsets and widths; this establishes the state those
parameters address. The walk over the tree is `trigger-tree.md`.

## 1. The banks are level-global, not per-player

Worth settling before writing any code, because the wrong shape puts the wrong
state into the determinism hash and is silent either way — a per-player model
carries seven copies nothing writes, and a global model under per-player truth
lets one keeper's script clobber another's. Four readings agree that the banks
belong to a single level-wide object:

1. **Only two functions index either bank**, and both are trigger dispatchers:
   the world generic dispatcher `0x00518270` and the world action dispatcher
   `0x00518AD0`. The remaining sites are `LEA`s in what look like save, load and
   reset routines.
2. **Every call site of the action dispatcher loads `this` from a global
   pointer** — `MOV ECX, [0x006E5050]` at `0x004A3F19` and `0x004B0D3B`, and
   `MOV ECX, [0x006E5054]` at `0x004C8A45`.
3. **The player-context dispatcher does not pass its player.** `0x004C8620` is
   invoked for one specific player and holds that player as its own `this` — and
   at `0x004C8A45` it deliberately loads the global instead before delegating.
   If flags were per-player this is the one call site that would have shown it.
4. **Neither global is per-player.** `0x006E5050` is written at two sites
   (`0x004B5A38`, `0x004B6026`) and `0x006E5054` at exactly one (`0x004C1EA9`),
   each from a constructor parameter. Singletons, set at construction.

**Method control:** the scan walked every initialised block, `cseg`
(`0x00653000`–`0x0066BFFF`) included, and returned 19 references to the flag bank
and 21 to the timer bank; the positive control at `0x006F2590` returned its
expected 226.

**Bounded uncertainty.** `0x006E5050` and `0x006E5054` are two distinct globals
reaching the same dispatcher — most likely two subsystems caching a pointer to
the same object, which is not proven. It does not affect the conclusion (both are
singletons and neither is a player) and would matter only if DK2 ran two script
engines with separate banks at once. **Q231.**

## 2. Layout

| bank | offset on the owning object | width |
|---|---|---|
| flags | `+0x3F70` | 128 bytes |
| timers | `+0x3FF0` | **16 dwords** |
| 24 trigger slots | `+0x4030` | 24 x 0x208 bytes |
| special target | `+0x72A2` (dword), companion set to 1 at `+0x72A6` | — |

The flag bank's width is **derived from the gap**: the timer bank begins `0x80`
bytes after it. That is also what makes id 128 the first id past the end, and
both dispatchers compare against exactly that value before touching the bank.

**The timer bank is sixteen dwords, not 256.** The write path masks the id with
`0xFF` and bounds it no further, but the id is bounded elsewhere and two
independent lines agree on sixteen: the next field on the object is the 24-slot
trigger array at `+0x4030`, leaving the bank exactly `0x40` bytes; and the
condition path compares the id against `0x10` before indexing it (`CMP DL,0x10`
at `0x005184EB`). The corpus agrees twice over — every timer *started* by action
type 8 has an id in 0..15, and **level 1 alone starts all sixteen, 0 through 15,
and never a seventeenth**.

## 3. A flag is a byte, and the arithmetic is asymmetric

Action type 7 (1,417 records — the single most common action in the game) writes
a flag. Its byte at body offset `01`, which the external field list calls `Flag`,
is not a flag at all: it is an **operation and addressing mode**.

| bit | meaning |
|---|---|
| `0x02` | set → the operand is a literal; clear → **indirect**, the operand is `flags[operand]` |
| `0x08` | set → assign; clear → add or subtract |
| `0x10` | with `0x08` clear: set → add, clear → subtract |

The generic read side (types 1 and 2) carries the same idea in a different place:
byte `02`, bit `0x01` set means the comparison operand is a literal, clear means
it is another flag's value. **That closes part of Q226 for these three types**:
`Flag` is a mode byte in both, and the name was misleading rather than merely
unverified.

**The operand is truncated to its low byte** on the flag path, because the bank
is bytes wide. The record carries 32 bits; only the special target receives all
of them.

**Add and subtract are signed; reading is not.** The write path loads the flag as
a *signed* char, does the arithmetic, and stores a byte. Every read
zero-extends. So a flag driven below zero reads back as 128..255, and a condition
comparing it against 0 sees a **large positive** number rather than a negative
one. Reproduced, not corrected.

## 4. Flag id 128 is not a flag

Both dispatchers compare the id against 128 and, on a match, divert to a separate
dword at `+0x72A2`, set a companion at `+0x72A6` to 1, and make a virtual call
through slot `+0x348`. Unlike a flag it takes the operand's **full 32 bits**.

**What it is remains UNKNOWN (Q229).** It is modelled as an opaque value rather
than given a plausible name, because naming a thing from the shape of its
handling is the move that has been wrong repeatedly in this project.

No shipped level writes a flag id **above** 128; in the original such an id would
index past the flag bank and into the timers, and this reimplementation refuses
rather than reproducing an overrun nobody has observed (Q230).

## 5. A timer stores when it started, and the clock counts ticks

Action type 8 stamps a timer with the current clock (`timers[id] = clock()`);
action type 23 sets an explicit stamp. **The comparison is a deadline, not a
subtraction**: the condition computes `stamp + value` and compares the clock
against it (`ADD EBX,EAX` then `CMP EAX,EBX` at `0x00518537`). Equivalent to
comparing elapsed time, except once the clock saturates — and it means nothing is
ever decremented, so a timer costs no per-tick work.

**A stamp of zero means "not started".** The condition path opens with
`TEST EAX,EAX; JZ` on the stored stamp and bails before comparing anything, so
tick 0 is not a usable stamp and an unstarted timer's condition is not merely "a
very long time ago".

There is also an **indirect mode**: at `0x005185C1`, `timers[byte 03]` is
compared to `timers[byte 01]`, with the indirect stamp on the left and no clock
or arithmetic. None of the 303 shipped type-2 records uses this branch.

### The clock is the game's tick counter

`0x00517490` returns `this->[+4]->[+4]` on the game object, and **it is not a
trigger clock at all** — 196 functions call it, at 448 sites. It is the "now"
every gate in the game reads. **Its unit is ticks, read directly**: in the
creature's per-tick update at `0x00488D89`,

`now` is fetched from `0x00517490` and divided, unsigned, by variable 19 (the
dword at `0x006F2590`); the update proceeds only when the remainder is zero.

`now % var19 == 0` is the once-a-game-second gate, and it is only once a game
second if `now` counts **ticks**, four to the second. That fixes a trigger
timer's unit at 250 ms (ADR-0089).

### Authored thresholds are in seconds, and the loader scales them

Three facts hold at three layers: **the `.kld` holds seconds, the loader
multiplies by variable 19 in place, and the comparison consumes ticks.** So the
evaluation path needs no multiply — and indeed **no code in the trigger region
references variable 19's global at all**, checked across every initialised block
including `cseg`, positive control 226 hits.

The conversion is at `0x0050C2DB`, inside the level trigger initialiser
`FUN_0050B5B0`:

1. `0x0050C2DB`: variable 19 is loaded from `0x006F2590`.
2. `0x0050C2E3`: the loop runs over the condition count, the word at
   `+0x7314` of the level's trigger data.
3. `0x0050C2EC`: the condition array is the pointer at `+0x7308`.
4. `0x0050C2F9`–`0x0050C2FC`: each record is found at `index * 5 * 4` — a
   20-byte stride.
5. `0x0050C2FF`: the record's type byte at `+0x0E` is read.
6. `0x0050C302`–`0x0050C30A`: only type 2 (a timer condition) or type `0x22`
   (34) is converted.
7. `0x0050C30E`–`0x0050C312`: the dword value at `+0x04` is multiplied by
   variable 19 and written back **in place**.

A second loop immediately after does the same to the **action** array for types
`0x10` (16) and `0x17` (23). Q233.

For a comparable duration authored in seconds elsewhere, the idiom is the same
multiply at the point of use — 30 bytes earlier at `0x00488D1D`,
`LEA EDX,[ECX + ECX*4]` makes five game seconds out of variable 19 before adding
the stamp. Structurally identical to the trigger comparison **except for the
multiply**, which the trigger path does not need.

### What the running game says

Four captures of campaign level 1 with a memory reader, correlated against this
level's trigger graph. The method that discriminates is to predict, for each
type-2 condition, the tick at which **the specific flag that condition's subtree
writes** should change — not merely to look for activity near a predicted tick,
because level 1 changes some flag every ~13 ticks and a tick-only match is worth
nothing.

| run | condition | stamp | authored | if ticks | if seconds | flag | observed |
|----:|-----------|------:|---------:|---------:|-----------:|------|---------:|
| 1 | 10: timer15 >= 8 | 6 | 8 | 14 | 38 | flag 1 | **39** |
| 2 | 10: timer15 >= 8 | 10 | 8 | 18 | 42 | flag 1 | **43** |
| 1 | 50: timer2 >= 5 | 273 | 5 | 278 | 293 | flag 48 | **294** |
| 2 | 50: timer2 >= 5 | 426 | 5 | 431 | 446 | flag 48 | **447** |

The residual's *size* is weak evidence; the way it *scales* is not. Under seconds
it is a constant `+1` tick — the sampler's own lag, polling at roughly the tick
rate. Under ticks it is exactly `3 * threshold + 1`, the arithmetic signature of
one missing multiply by variable 19 and not of noise. Timer 2's stamp moved 153
ticks between the two runs and the seconds model, which has no free parameter,
tracked it.

**Thirteen direct measurements now stand across four runs, every one with a
residual of exactly +1 tick and none for ticks.** Three are worth naming.

**A chain.** Condition 41 (`timer15 >= 150`) starts timer 5; condition 153
(`timer5 >= 40`) then writes flag 40. In run 3, cond 41 was predicted at tick 608
and timer 5 was stamped at 609; cond 153 was then predicted at 768 and flag 40
appeared at 769. The second prediction is only right *because* the first was —
under the ticks reading timer 5 would have been stamped at 158 and flag 40
written at 198.

**An exact stamp, with no sampling lag.** A timer records the tick it was set at,
so unlike a flag it is not observed late. In run 4 timer 15 was stamped at 11, so
cond 41 predicts timer 5's stamp at `11 + 150 * 4 = 611`. The stored value is
**611**. Residual zero; the ticks reading gives 161. Five timers were caught being
stamped across the two long runs, each holding exactly the tick it was set at, so
**a stamp in the bank is a tick count** is observed rather than inferred.

**The models disagree about which flag, not just when.** Condition 10
(`timer15 >= 8`) has two child branches, `flag0 == 0` and `flag1 == 1`, so
whether flag 1 gets written depends on flag 0's value at the moment cond 10
fires, and flag 0 changes during the run:

| run | timer15 stamp | ticks fires at | flag0 then | ticks predicts | seconds fires at | flag0 then | seconds predicts | observed |
|---|---:|---:|---:|---|---:|---:|---|---|
| 2 | 10 | 18 | 0 | writes flag 1 | 42 | 0 | writes flag 1 | written |
| 3 | 8 | 16 | 0 | writes flag 1 | 40 | 1 | no write | **not written** |
| 4 | 11 | 19 | 0 | writes flag 1 | 43 | 1 | no write | **not written** |

Two runs where the ticks reading requires a flag to be written and it was not: a
qualitative refutation, not a timing one.

Run 4 also read the condition array straight out of the running game after the
level was up: **356 conditions and 326 actions**, matching level 1's `.kld`
exactly, and **all 22 timer thresholds stored as the authored value times four**
— id 10 holds 32 for an authored 8, id 41 holds 600 for 150, id 157 holds 840 for
210.

**The tick rate over five minutes.** Steady state is **3.93 to 3.95 ticks/s (253
to 254 ms)** in both 300-second runs. Run 4's first 60 seconds ran at 3.687 and
every later window at ~3.93, so the opening of a fresh level is slower and the
rest is flat; the worst single interval in 300 s was 335 ms. Nothing is a stall,
and the few ms above 250 track the host rather than the design, so the simulation
keeps 250 ms exactly.

### The record layout, and two things it names

The stride is 20 bytes and the type byte sits at `+0xE`. The `.kld` payload for
tags 213 and 214 is **16 bytes** with `Type` at `+14` and the scaled dword at
`+4`, so **the in-memory record is the file record verbatim** with four bytes
appended; those four bytes are runtime-only and still UNKNOWN. The two agree
offset for offset, which is itself the check that the arrays are what they look
like.

**There are two record arrays, not one.** `+0x730C` is the **tag 214 (action)**
array with its count at `+0x7318`; the **tag 213 (condition)** array is at
`+0x7308` with its count at `+0x7314`.

**`+0x731A` is the root trigger's repeat count.** After both loops, if the root
trigger id at `+0x7310` is non-zero the initialiser looks the record up through
`0x00518090` and copies byte `+0xF` of it — `RepeatCount` in the file layout —
into `+0x731A`. `TriggerTree.RootRepeat` reproduces it, keeping the binary's
first-record fallback.

### 5a. A flag condition names its flag in byte 01

The handler at `0x0051845B`:

1. `0x0051845F`: byte 01 of the record is taken.
2. `0x00518471`: the subject is the flag it indexes — the byte at `+0x3F70 +
   byte 01` of the flag table.
3. `0x0051847F`: … unless byte 01 is the special id `0x80`.
4. `0x005184AB`: the low bit of byte 02 is tested.
5. `0x005184B0`: set — the operand is the immediate dword at `+4` of the record.
6. `0x005184B7`: clear — the operand is the flag byte at `+0x3F70 + byte 03`.

So byte 01 is the flag id, byte 02's low bit selects the operand, and byte 03 is
the **indirect** flag — the same shape as the timer condition. The reader had
this on byte 03 and every test still passed, **because byte 03 is zero in 1374 of
1375 shipped records**. Corrected in `KwdAdditionalRecords` and pinned by a test
written to fail under the old reading: byte 01 yields **91 distinct flag ids**
across the campaign against byte 03's two.

**Indirect flag mode is live, with a population of one.** Exactly one shipped
record has byte 02's low bit clear — **level 3, trigger 18: `flag 3 > flag 4`,
repeating** — and it is the same record that has a non-zero byte 03. If the
decode were wrong those two facts would have no reason to coincide. A condition
evaluator that only compares against an immediate cannot run level 3.

### The timer index is byte 01, not byte 03

`trigger-semantics.md` had the name on the wrong byte, and the corpus made it
obvious: the field it called `TimerId` (byte `03`) is **0 in all 303 shipped
records**, while the field it called `TargetId` (byte `01`) varies across 0..16 —
exactly the range of the bank plus its sentinel. The binary settles it: byte `01`
is compared against `0x10` at `0x005184EB` and then used as the index at
`0x0051851A`; byte `03` is the **indirect operand's** timer, read only when the
mode bit is clear (`SHR ECX,0x18` at `0x005185C1`), which no shipped level does.

### Timer id 16 is not a timer

The same shape as flag id 128, one past its own bank. When the condition's id
byte equals 16 the handler reads a value through a **virtual call at slot
`+0x34C`** instead of indexing the bank, and there is only **one** indexed store
to the timer bank in the whole executable — action type 8's, which stamps the
clock — so a write naming 16 does not reach the bank either.

**It is the level time limit (Q232, answered 2026-09-26, ADR-0235).** Slot `+0x34C`
is `0x00509660`, which returns `world+0x72AA`, DK2's own `LevelTimeLimit`. All 86
action type 23 records set it, and the five type 2 conditions compare it raw with
their `Value`. The world update counts it down once a turn; `level-time-limit.md`
has the whole mechanism.

## 6. Where evaluation happens — once per game turn

Trigger evaluation sits inside `FUN_0050A450`, a method on the same object that
owns the banks and the two record arrays. It is **virtual**: zero direct call
sites and exactly one absolute reference in any initialised block, a vtable slot
at `0x0066E404`. Inside it, in order:

1. A **per-player loop** over the player table at `0x006EE540`
   (`MOV EBP,[EDX*0x4 + 0x6EE540]`, looping back to `0x0050A6A2`), which is what
   makes this the object's own update pass rather than a trigger-specific
   routine.
2. At `0x0050A745`, the **level's root trigger tree** — id from `+0x7310`, with a
   byte argument from `+0x731A` — evaluated by `0x00518150`.
3. At `0x0050A760`–`0x0050A784`, a loop over **24 slots of stride `0x208` (520
   bytes) starting at `+0x4030`**, evaluating each slot's trigger id, when
   non-zero, with argument `1`. **What those slots are is still UNKNOWN** (Q228's
   residue).

**The class.** The vtable slot is **`0x18`** — index 6. Its base is `0x0066E3EC`,
found by walking back from `0x0066E404` to the first dword that is not a code
pointer; that dword is `0x0067DD40`, an RTTI complete object locator whose
`offset` field is **0**, so this is the class's *primary* vtable, the one at
`[this]`. **DKII.EXE ships MSVC RTTI**, so the class behind any vtable can be
recovered: the locator's type descriptor is at `0x006A49D0`, and `Rtti.py` does
it for any vtable — which is how a future session answers *"is this the same
object?"*. The name itself is deliberately not recorded: it is the original's
internal identifier, every tool in `reverse-engineering/` is driven by addresses,
and the three addresses carry the whole finding. Not measured: the vtable's
extent — code elsewhere calls slots `+0x340`, `+0x34C` and `+0x370` on this same
object, so it is certainly larger than the truncated dump suggested.

**The object is a global static at `0x006F2A08`.** Its constructor
(`FUN_00508A10`, which installs the vtable at `0x0066E3EC`) is called once from
the CRT's static-initialiser list at `0x005089D5`, with the destructor registered
through `atexit`. So it is not allocated and not owned by a manager, and its
address is a constant — a better handle than the two globals that merely cache a
pointer to it.

**The cadence: the world frame, once per game turn** (Q228 answered). Slot
`0x18` has exactly one caller, `0x00526AA7` in `FUN_00526590`, which loads the
callee with `MOV ECX,[ESI+0x14]`. `ESI` is a container that owns the world as a
*component*, which is why every scan for a caller that names the world failed.
Its own caller is `FUN_00525370`, which attaches the world at `0x00525537` and
detaches it at `0x00525A90`. The container has four component slots — two others
at `+0x0C` and `+0x10`, the world at `+0x14`, the turn driver at `+0x18`
(`0x006FCEC0`, or `0x006FD120` when `0x0075AD4B` is 3); attaching stores
`world+4` = container and calls the component's own vtable slot `+4`.

The world frame runs once per main-loop iteration, ungated; the accumulator
beside it at `+0x288` gates world slot `0x28`, the multiplayer resync. The loop
advances only when the driver's slot `0x14` (`FUN_00522760`) reports a turn due,
which `FUN_00521BA0` decides as `lastTurnMs + 1000 / container[+8] < nowMs`.
`container[+8]` is **4**, an immediate at `0x00526264`; `container[+4]`, the game
turn, is set to **1** at `0x00526254` and incremented once per iteration.
`FUN_00517490` returns `world->container->turn` — the `%d` in the binary's own
`**** GameTurn %d ****`. A turn is 250 ms, and the trigger tree is evaluated
exactly once per turn.

**Two fours that never meet.** The loop rate is that immediate; variable 19 is
per-level data. No code puts them in one place, so a level shipping 5 would scale
authored seconds by 5 against a clock still running at 4. All 66 ship 4.

**Built.** `GameSimulation.Tick` runs `TriggerExecutor.Run(dk2Turn)` before the
systems, once per tick, with `dk2Turn = CurrentTick + 1` because DK2's first game
turn is 1 where our next-tick clock starts at 0. Only flag (action type 7) and
timer (type 8) actions execute; an unsupported record blocks its child chain and
is censused in `TriggerRunResult.Summary`.

## 7. Scope in the shipped campaign

Of 9,161 trigger records, **3,472 (38%) name a flag or a timer** — generic type 1
(1,375) and type 2 (303) read them; action type 7 (1,417), type 8 (291) and type
23 (86) write them. It is the trigger system's dominant mechanism by a wide
margin, which is why it was built first.

Every comparison code in the corpus is one of the six the routine at
`0x00519980` implements — signed `<`, `<=`, `==`, `>`, `>=`, `!=` for codes 1 to
6, reproduced in `TriggerState.Compare` — and every flag id is within the bank.
