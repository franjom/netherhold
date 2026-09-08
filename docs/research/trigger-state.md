# Trigger state: flags, timers, and who owns them

What the trigger system stores between one record firing and another reading
it. This is a **static reading of DKII.EXE v1.70**, corroborated against the
shipped campaign. It does not say *when* a trigger is evaluated — that is Q228
and nothing here assumes an answer to it.

Follows on from `trigger-semantics.md` (ADR-0092), which established every
record's parameter offsets and widths. This establishes the state those
parameters address.

## 1. The banks are level-global, not per-player

**This was the question worth settling before writing any code**, because
getting it wrong puts the wrong shape of state into the determinism hash and is
silent either way — a per-player model would carry seven copies nothing writes,
and a global model under per-player truth would let one keeper's script clobber
another's.

Four readings agree that the banks belong to a single level-wide object:

1. **Only two functions index either bank**, and both are trigger dispatchers:
   the world generic dispatcher `0x00518270` and the world action dispatcher
   `0x00518AD0`. The remaining sites are `LEA`s in what look like save, load and
   reset routines.
2. **Every call site of the action dispatcher loads `this` from a global
   pointer** — `MOV ECX, [0x006E5050]` at `0x004A3F19` and `0x004B0D3B`, and
   `MOV ECX, [0x006E5054]` at `0x004C8A45`.
3. **The player-context dispatcher does not pass its player.** `0x004C8620` is
   invoked for one specific player and holds that player as its own `this` —
   and at `0x004C8A45` it deliberately loads the global instead before
   delegating. If flags were per-player this is the one call site that would
   have shown it.
4. **Neither global is per-player.** `0x006E5050` is written at two sites
   (`0x004B5A38`, `0x004B6026`) and `0x006E5054` at exactly one
   (`0x004C1EA9`), each from a constructor parameter. Singletons, set at
   construction.

**Method control:** the scan walked every initialised block, `cseg`
(`0x00653000`–`0x0066BFFF`) included, and returned 19 references to the flag
bank and 21 to the timer bank. The run's positive control at `0x006F2590`
returned its expected 226.

**Bounded uncertainty.** `0x006E5050` and `0x006E5054` are two distinct globals
reaching the same dispatcher. They are most likely two subsystems caching a
pointer to the same object, but that is not proven here. It does not affect the
conclusion — both are singletons and neither is a player — and it would only
matter if DK2 ran two script engines with separate banks at once. **Q231.**

## 2. Layout

| bank | offset on the owning object | width |
|---|---|---|
| flags | `+0x3F70` | 128 bytes |
| timers | `+0x3FF0` | **16 dwords** (see below) |
| 24 trigger slots | `+0x4030` | 24 x 0x208 bytes |
| special target | `+0x72A2` (dword), companion set to 1 at `+0x72A6` | — |

The flag bank's width is **derived from the gap**: the timer bank begins
`0x80` bytes after it. That is also what makes id 128 the first id past the
end, and both dispatchers compare against exactly that value before touching
the bank.

**The timer bank is sixteen dwords, not 256**, and the first reading of it here
was wrong. The write path masks the id with `0xFF` and bounds it no further,
which suggested 256 — but the id is bounded elsewhere, and two independent lines
agree on sixteen:

- the next field on the object is the 24-slot trigger array at `+0x4030`,
  leaving the bank exactly `0x40` bytes;
- the condition path compares the id against `0x10` before indexing it
  (`CMP DL,0x10` at `0x005184EB`).

The corpus agrees twice over. Every timer *started* by action type 8 has an id
in 0..15 — and **level 1 alone starts all sixteen of them, 0 through 15, and
never a seventeenth**, which is about as direct as data-side corroboration of a
bank size gets.

## 3. A flag is a byte, and the arithmetic is asymmetric

Action type 7 (1,417 records — the single most common action in the game)
writes a flag. Its byte at body offset `01`, which the external field list calls
`Flag`, is not a flag at all: it is an **operation and addressing mode**.

| bit | meaning |
|---|---|
| `0x02` | set → the operand is a literal; clear → **indirect**, the operand is `flags[operand]` |
| `0x08` | set → assign; clear → add or subtract |
| `0x10` | with `0x08` clear: set → add, clear → subtract |

The generic read side (types 1 and 2) carries the same idea in a different
place: byte `02`, bit `0x01` set means the comparison operand is a literal, and
clear means it is another flag's value.

**That closes part of Q226 for these three types.** `Flag` is a mode byte in
both, and the name was misleading rather than merely unverified.

**The operand is truncated to its low byte** on the flag path, because the bank
is bytes wide. The record carries 32 bits; only the special target receives all
of them.

**Add and subtract are signed; reading is not.** The write path loads the flag
as a *signed* char, does the arithmetic, and stores a byte. Every read
zero-extends. So a flag driven below zero reads back as 128..255, and a
condition comparing it against 0 sees a **large positive** number rather than a
negative one. Reproduced, not corrected.

## 4. Flag id 128 is not a flag

Both dispatchers compare the id against 128 and, on a match, divert to a
separate dword at `+0x72A2`, set a companion at `+0x72A6` to 1, and make a
virtual call through slot `+0x348`. Unlike a flag it takes the operand's **full
32 bits**.

**What it is remains UNKNOWN (Q229).** It is modelled as an opaque value rather
than given a plausible name, because naming it from the shape of its handling
is the move that has been wrong repeatedly in this project.

No shipped level writes a flag id **above** 128; in the original such an id
would index past the flag bank and into the timers, and this reimplementation
refuses rather than reproducing an overrun nobody has observed (Q230).

## 5. A timer stores when it started, and the clock counts ticks

Action type 8 stamps a timer with the current clock (`timers[id] = clock()`).
Action type 23 sets an explicit stamp. **The comparison is a deadline, not a
subtraction**: the condition computes `stamp + value` and compares the clock
against it (`ADD EBX,EAX` then `CMP EAX,EBX` at `0x00518537`). Equivalent to
comparing elapsed time, but it means nothing is ever decremented — a timer
costs no per-tick work.

**A stamp of zero means "not started".** The condition path opens with
`TEST EAX,EAX; JZ` on the stored stamp and bails before comparing anything, so
tick 0 is not a usable stamp and an unstarted timer's condition is not merely
"a very long time ago".

There is also an **indirect mode**: at `0x005185C1` the id in byte `03` selects
a *second* timer and the comparison runs against that timer's stored stamp
rather than against a literal.

### The clock is the game's tick counter

`0x00517490` returns `this->[+4]->[+4]` on the game object, and **it is not a
trigger clock at all** — 196 functions call it, at 448 sites. It is the "now"
every gate in the game reads, which this project had already established while
settling Q1.

**Its unit is ticks, read directly rather than inferred.** In the creature's
per-tick update at `0x00488D89`:

```
CALL 0x00517490            ; now
XOR  EDX,EDX
DIV  dword ptr [0x006F2590] ; / variable 19
TEST EDX,EDX
JNZ  ...                    ; proceed only when now % var19 == 0
```

`now % var19 == 0` is the once-a-game-second gate, and it is only once a game
second if `now` counts **ticks**, four to the second. That fixes a trigger
timer's unit at 250 ms per unit (ADR-0089).

### Authored timer thresholds are in seconds

**This section said "in ticks, not seconds" and was wrong. It is corrected
below, and the disassembly it rested on is kept, because that part holds.**

The same function shows the game's idiom for a duration authored in seconds, 30
bytes earlier at `0x00488D1D`:

```
CALL 0x00517490            ; now
MOV  ECX,[0x006F2590]      ; variable 19
MOV  EDI,[ESI + 0x4B3]     ; a stored stamp
LEA  EDX,[ECX + ECX*4]     ; var19 * 5  -> five game seconds, in ticks
ADD  EDX,EDI               ; stamp + five seconds
CMP  EAX,EDX               ; now vs deadline
```

Structurally identical to the trigger timer comparison **except for the
multiply**. The trigger path has none, and **no code in the trigger region
references variable 19's global at all** — checked with a scan of every
initialised block, `cseg` included, whose positive control returned its expected
226 hits.

The reading taken from that was that the authored threshold is consumed raw as
ticks, so `Value=600` on a timer condition would be 150 seconds. The section
flagged the round numbers as a reason to doubt it and Q228 recorded it as
wanting an observation. **The observation was taken, and it went the other
way.**

### What the running game says

Two captures of campaign level 1 with a memory reader, correlated against this
level's trigger graph. The method that discriminates is to predict, for each
type-2 condition, the tick at which **the specific flag that condition's
subtree writes** should change — not merely to look for activity near a
predicted tick. Level 1 changes some flag every ~13 ticks on average, so a
tick-only match is worth nothing.

| run | condition | stamp | authored | if ticks | if seconds | flag | observed |
|----:|-----------|------:|---------:|---------:|-----------:|------|---------:|
| 1 | 10: timer15 >= 8 | 6 | 8 | 14 | 38 | flag 1 | **39** |
| 2 | 10: timer15 >= 8 | 10 | 8 | 18 | 42 | flag 1 | **43** |
| 1 | 50: timer2 >= 5 | 273 | 5 | 278 | 293 | flag 48 | **294** |
| 2 | 50: timer2 >= 5 | 426 | 5 | 431 | 446 | flag 48 | **447** |

The residual's *size* is weak evidence; the way it *scales* is not. Under
seconds the residual is a constant `+1` tick in all four — the sampler's own
lag, since it polls at roughly the tick rate and so sees a change up to one tick
late. Under ticks it is exactly `3 * threshold + 1` in all four, which is the
arithmetic signature of one missing multiply by variable 19 and not of noise.
Timer 2's stamp moved 153 ticks between the two runs; the seconds model has no
free parameter and tracked it.

**A claim that stood here is withdrawn.** It said twelve further predictions
agreed by silence, and that "every gate between those conditions and those
writes is a `flag0 == 0` test, and flag0 read 0 at both ticks". That was read
off **byte 03 of the flag condition, which is the indirect operand and is zero
in 1374 of 1375 shipped records** — so every gate looked like flag 0 and none
of them was checked at all. The correct byte is 01 (see §5a). The conclusion
happened to be right; the argument for it was not, and two 300-second captures
have since replaced it with direct measurements that need no gate analysis.

### Runs 3 and 4: a chain, an exact stamp, and a disagreement about *which* flag

Two 300-second captures put far more of the cascade inside the window.
Thirteen direct measurements now stand across four runs, **every one with a
residual of exactly +1 tick, and none for ticks**. Three are worth naming.

**A chain.** Condition 41 (`timer15 >= 150`) starts timer 5; condition 153
(`timer5 >= 40`) then writes flag 40. In run 3 cond 41 was predicted at tick
608 and timer 5 was stamped at 609; cond 153 was then predicted at 768 and flag
40 appeared at 769. The second prediction is only right *because* the first
was — under the ticks reading timer 5 would have been stamped at 158 and flag
40 written at 198.

**An exact stamp, with no sampling lag at all.** A timer records the tick it was
set at, so unlike a flag it is not observed late. In run 4, timer 15 was stamped
at 11, so cond 41 predicts timer 5's stamp at `11 + 150 * 4 = 611`. The stored
value is **611**. Residual zero. The ticks reading gives 161.

**And the models disagree about which flag, not just when.** Condition 10
(`timer15 >= 8`) has two child branches — with byte 01 read correctly, they are
`flag0 == 0` and `flag1 == 1` — so whether flag 1 gets written depends on flag
0's value at the moment cond 10 fires, and flag 0 changes during the run:

| run | timer15 stamp | ticks fires at | flag0 then | ticks predicts | seconds fires at | flag0 then | seconds predicts | observed |
|---|---:|---:|---:|---|---:|---:|---|---|
| 2 | 10 | 18 | 0 | writes flag 1 | 42 | 0 | writes flag 1 | written |
| 3 | 8 | 16 | 0 | writes flag 1 | 40 | 1 | no write | **not written** |
| 4 | 11 | 19 | 0 | writes flag 1 | 43 | 1 | no write | **not written** |

Two runs where the ticks reading requires a flag to be written and it was not.
That is a qualitative refutation, not a timing one.

### The load-time multiply, watched rather than argued

Run 4 read the condition array straight out of the running game after the level
was up: **356 conditions and 326 actions**, matching level 1's `.kld` exactly,
and **all 22 timer thresholds stored as the authored value times four** — id 10
holds 32 for an authored 8, id 41 holds 600 for 150, id 157 holds 840 for 210.
Q233 is confirmed by observation, not only by the disassembly below.

### What the tick rate does over five minutes

Steady state is **3.93 to 3.95 ticks/s (253 to 254 ms)** in both 300-second
runs. Run 4's first 60 seconds ran at 3.687 and every later window at ~3.93, so
the opening of a fresh level is slower and the rest is flat; the worst single
interval in 300 s was 335 ms. Nothing here is a stall, and the few ms above 250
track the host rather than the design, so the simulation keeps 250 ms exactly.

### The two readings are compatible, at different layers

The disassembly above is not retracted: there is no multiply in the trigger
comparison and no reference to variable 19's global anywhere in `0x517400` to
`0x519A00`. What was wrong was the inference from that to the *authored* unit.
Both facts hold together if the conversion happens **before evaluation ever
runs** — most likely once at level load, when the trigger records are built,
leaving the in-memory `Value` in ticks while the `.kld` on disk holds seconds.

**And it is not a hypothesis any more.** The conversion is at `0x0050C2DB`,
inside the level trigger initialiser `FUN_0050B5B0`, and it was found by
redoing the failed scan with no range filter at all — 225 sites in 82
functions, the positive control intact:

```
0050c2db  MOV  EDI,dword ptr [0x006f2590]   ; variable 19
0050c2e3  CMP  word ptr [ESI + 0x7314],DX   ; the condition count
0050c2ec  MOV  ECX,dword ptr [ESI + 0x7308] ; the condition array
0050c2f9  LEA  EAX,[EAX + EAX*0x4]          ; index * 5
0050c2fc  LEA  EAX,[ECX + EAX*0x4]          ; ... * 4 -> a 20-byte stride
0050c2ff  MOV  CL,byte ptr [EAX + 0xe]      ; the record's type byte
0050c302  CMP  CL,0x2                       ; a timer condition
0050c30a  CMP  CL,0x22                      ; or type 34
0050c30e  IMUL ECX,dword ptr [EAX + 0x4]    ; var19 * the value
0050c312  MOV  dword ptr [EAX + 0x4],ECX    ; written back IN PLACE
```

A second loop immediately after it does the same to the **action** array for
types `0x10` (16) and `0x17` (23). So the seconds-to-ticks multiply happens
**once, at level load, in place** — which is exactly why the evaluation path
has none: by the time anything is evaluated the field is already in ticks.

**All three readings are true at once, at three different layers.** The `.kld`
holds seconds; the loader scales; the comparison consumes ticks. The mistake
was never a misread instruction, it was assuming a single unit for a value
that passes through a transform.

### The record layout, and two things this names

The stride is 20 bytes and the type byte sits at `+0xE`. The `.kld` payload for
tags 213 and 214 is **16 bytes** with `Type` at `+14` and the scaled dword at
`+4`, so **the in-memory record is the file record verbatim** with four bytes
appended, and those four bytes are runtime-only and still UNKNOWN. Nothing had
to be inferred about an in-memory layout; the two agree offset for offset,
which is itself the check that the arrays are what they look like.

**There are two record arrays, not one.** `+0x730C` was recorded here as "the
level's trigger array"; it is specifically the **tag 214 (action)** array, with
its count at `+0x7318`. The **tag 213 (condition)** array is at `+0x7308` with
its count at `+0x7314`. Level 1 ships 356 conditions and 326 actions, which is
what those two counts should read.

**`+0x731A` is the root trigger's repeat count.** After both loops, if the root
trigger id at `+0x7310` is non-zero the initialiser looks the record up through
`0x00518090` and copies byte `+0xF` of it — `RepeatCount` in the file layout —
into `+0x731A`. That byte was carried as an unexplained argument.

The earlier failure is worth naming, because it is the same one twice. The scan
that found "no var19 in the trigger path" was correct about every block it
covered and was filtered to a 0x2600-byte window; the creature-context
dispatcher `FUN_0049AC80` was already on a list of functions that reference
var19 *and* call the clock, and sits outside that window. A scan proves nothing
outside its range.

### 5a. A flag condition names its flag in byte 01

The handler at `0x0051845B`:

```
0051845f  MOV  DL,CH                              ; byte 01
00518471  MOV  BL,byte ptr [EBP + ESI + 0x3f70]   ; flags[byte 01] -- the subject
0051847f  CMP  EBP,0x80                           ; ... unless it is the special id
005184ab  TEST DL,0x1                             ; byte 02, low bit
005184b0  MOV  EAX,dword ptr [EAX + 0x4]          ;   set   -> the immediate at +4
005184b7  MOV  AL,byte ptr [ECX + ESI + 0x3f70]   ;   clear -> flags[byte 03]
```

So byte 01 is the flag id, byte 02's low bit selects the operand, and byte 03 is
the **indirect** flag — the same shape as the timer condition, whose byte 01 was
corrected earlier. **The reader had this on byte 03 and every test still
passed**, because byte 03 is zero in 1374 of 1375 shipped records. Corrected in
`KwdAdditionalRecords`, and pinned by a test written to fail under the old
reading: byte 01 yields **91 distinct flag ids** across the campaign against
byte 03's two.

**Indirect flag mode is live, with a population of one.** Exactly one shipped
record has byte 02's low bit clear — **level 3, trigger 18: `flag 3 > flag 4`,
repeating** — and it is the same record that has a non-zero byte 03. If the
decode were wrong those two facts would have no reason to coincide. A condition
evaluator that only compares against an immediate cannot run level 3.

### Timer id 16 is not a timer

The same shape as flag id 128, one past its own bank. When the condition's id
byte equals 16 the handler reads a value through a **virtual call at slot
`+0x34C`** instead of indexing the bank, and there is only **one** indexed store
to the timer bank in the whole executable — action type 8's, which stamps the
clock — so a write naming 16 does not reach the bank either.

**It is heavily used and completely unidentified.** All 86 action type 23
records target it, and five type 2 conditions test it. **Q232.** `TriggerState`
refuses the id rather than modelling a stored value it has not seen written:
the read side is a virtual call and the write side is somewhere this reading has
not reached, and they need not be the same field.

### The timer index is byte 01, not byte 03

`trigger-semantics.md` had the name on the wrong byte, and the corpus is what
made it obvious: the field it called `TimerId` (byte `03`) is **0 in all 303
shipped records**, while the field it called `TargetId` (byte `01`, marked as an
unverified name) varies across 0..16 — exactly the range of the bank plus its
sentinel.

The binary settles it. Byte `01` is the one compared against `0x10` at
`0x005184EB` and then used as the index at `0x0051851A`. Byte `03` is the
**indirect operand's** timer, read only when the mode bit is clear
(`SHR ECX,0x18` at `0x005185C1`) — which no shipped level does, every one of the
303 carrying mode `1`. Both are now named for what they are.

## 6. Where evaluation happens

**Q228's cadence half, as far as it is read.** Trigger evaluation sits inside
`FUN_0050A450`, which is a method on the same object that owns the banks and the
trigger array at `+0x730C`.

It is **virtual**: `FUN_0050A450` has **zero direct call sites** and exactly one
absolute reference in any initialised block — a vtable slot at `0x0066E404`. So
it is invoked polymorphically, and *how often* depends on who calls that slot.

Inside it, in order:

1. A **per-player loop** over the player table at `0x006EE540`
   (`MOV EBP,[EDX*0x4 + 0x6EE540]`, looping back to `0x0050A6A2`), which is what
   makes this look like the object's own update pass rather than a
   trigger-specific routine.
2. At `0x0050A745`, the **level's root trigger tree** — id from `+0x7310`, with
   a byte argument from `+0x731A` — evaluated by `0x00518150`.
3. At `0x0050A760`–`0x0050A784`, a loop over **24 slots of stride `0x208` (520
   bytes) starting at `+0x4030`**, evaluating each slot's trigger id, when
   non-zero, with argument `1`.

### The slot, and the class

The vtable slot is **`0x18`** — index 6. Its base is `0x0066E3EC`, found by
walking back from `0x0066E404` to the first dword that is not a code pointer;
that dword is `0x0067DD40`, an RTTI complete object locator whose `offset` field
is **0**, so this is the class's *primary* vtable, the one at `[this]`.

**DKII.EXE ships MSVC RTTI**, so the class behind any vtable can be recovered:
the locator's type descriptor is at `0x006A49D0`. `Rtti.py` does it for any
vtable, which is how a future session answers *"is this the same object?"*.

**The name itself is deliberately not recorded.** It is the original's internal
identifier, and every tool in `reverse-engineering/` is driven by addresses,
not names; the three addresses above carry the whole finding.

*Not* measured: the vtable's extent. The forward walk was truncated by the dump
window, so the "69 entries" it suggested is an artefact. The vtable is certainly
larger — code elsewhere calls slots `+0x340`, `+0x34C` and `+0x370` on this same
object. The **base** is what the slot index depends on, and that is solid.

### The object is a static, and the update is driven through an interface

**The object is a global static at `0x006F2A08`.** Its constructor
(`FUN_00508A10`, which installs the vtable at `0x0066E3EC`) is called once from
the CRT's static-initialiser list at `0x005089D5`, with the destructor
registered through `atexit`. So it is not allocated and not owned by a manager,
and its address is a constant — a far better handle than the two globals that
merely cache a pointer to it.

**ANSWERED: the world frame, once per game turn.** Slot `0x18` has exactly one
caller — `0x00526AA7` in `FUN_00526590`, which loads the callee with
`MOV ECX,[ESI+0x14]`. `ESI` is a container that owns the world as a *component*,
which is why every scan for a caller that names the world failed. Its own caller
is `FUN_00525370`, which attaches the world at `0x00525537` and detaches it at
`0x00525A90`. The container has four component slots — two others at `+0x0C` and
`+0x10`, the world at `+0x14`, the turn driver at `+0x18` (`0x006FCEC0`, or
`0x006FD120` when `0x0075AD4B` is 3). Attaching stores `world+4` = container and
calls the component's own vtable slot `+4`.

**The rate.** The world frame runs once per main-loop iteration, ungated; the
accumulator beside it at `+0x288` gates world slot `0x28`, the multiplayer
resync. The loop advances only when the driver's slot `0x14` (`FUN_00522760`)
reports a turn due, which `FUN_00521BA0` decides as
`lastTurnMs + 1000 / container[+8] < nowMs`. `container[+8]` is **4**, an
immediate at `0x00526264`; `container[+4]`, the game turn, is set to **1** at
`0x00526254` and incremented once per iteration. `FUN_00517490` returns
`world->container->turn` — the `%d` in the binary's own `**** GameTurn %d ****`.
A turn is 250 ms; the trigger tree is evaluated exactly once per turn.

**Two fours that never meet.** The loop rate is that immediate; variable 19 is
per-level data. No code puts them in one place, so a level shipping 5 would
scale authored seconds by 5 against a clock still running at 4. All 66 ship 4.

Also still UNKNOWN: what the 24 slots at `+0x4030` are. Q228 keeps that.

**A method note that cost time here.** `0x00518150` appeared to have exactly one
caller — itself — and a byte scan of every block for its address returned **0
hits**. Neither meant what it looked like. The scan finds *absolute* references
only, and a direct `CALL` encodes a relative displacement; and the two real
entry call sites sit in code Ghidra had not defined as a function, so they were
missing from the xref list. Enumerating call sites directly found all four.

## 7. Scope in the shipped campaign

Of 9,161 trigger records, **3,472 (38%) name a flag or a timer** — generic
type 1 (1,375) and type 2 (303) read them; action type 7 (1,417), type 8 (291)
and type 23 (86) write them. It is the trigger system's dominant mechanism by
a wide margin, which is why it was built first.

Every comparison code in the corpus is one of the six the routine at
`0x00519980` implements, and every flag id is within the bank.

## Summary of confidence

| claim | confidence | basis |
|---|---|---|
| The banks are level-global, not per-player | **Strongly inferred** | four independent readings, §1 |
| Flags are 128 bytes at `+0x3F70`; timers **16** dwords at `+0x3FF0` | **Strongly inferred** | instruction reads; both widths from the gap to the next field, corroborated by an explicit bound compare |
| A flag is a byte and the operand is truncated | **Strongly inferred** | byte-width loads and stores on the write path |
| Add/subtract signed, reads zero-extended | **Strongly inferred** | `char` load in arithmetic, `AND ECX,0xFF` on the read |
| Byte `01` (action 7) / `02` (generic 1,2) is a mode, not a flag | **Strongly inferred** | the three bit tests, §3 |
| Id 128 is a separate 32-bit target | **Strongly inferred** | explicit `CMP ...,0x80` in both dispatchers |
| Timer id 16 is a separate target, not a slot | **Strongly inferred** | `CMP DL,0x10`, then a virtual call at `+0x34C` |
| The condition's timer index is byte 01 | **Strongly inferred** | the byte that is bound-checked and then indexes the bank |
| Evaluation is a virtual method, `FUN_0050A450` | **Strongly inferred** | zero direct call sites; one vtable reference at `0x0066E404` |
| A timer stores a start stamp; comparison is a deadline | **Strongly inferred** | write stamps the clock; read compares `now` to `stamp + value` |
| A stamp of 0 means "not started" | **Strongly inferred** | `TEST EAX,EAX; JZ` before any comparison |
| The clock counts ticks, 250 ms each | **Known** (observed) | `now % var19 == 0` is the once-a-second gate (ADR-0089), and the counter was read live at 3.94 ticks/s with var19 = 4 |
| A stamp in the timer bank is a tick count | **Known** (observed) | five timers caught being stamped across two runs, each holding exactly the tick it was set at |
| Authored thresholds are in **seconds** | **Known** (observed) | four live captures; 13 direct measurements, residual exactly +1 every time and none for ticks; two runs where the ticks reading requires a flag that was never written |
| The stored threshold is the authored value x variable 19 | **Known** (observed) | all 22 of level 1's type-2 thresholds read out of the running game as authored x4 |
| A flag condition's flag is byte 01; byte 03 is the indirect operand | **Known** | handler at `0x0051845B`; 91 distinct ids on byte 01 against 2 on byte 03 |
| Indirect flag mode is used once in the campaign | **Known** | level 3 trigger 18, `flag 3 > flag 4`, the only record with byte 02 bit 0 clear |
| The in-memory threshold is in ticks, converted at load | **Known** | `0x0050C2DB` multiplies `record+4` by variable 19 in place, for condition types 2 and 34 and action types 16 and 23 (Q233) |
| `+0x7308` is the condition array and `+0x730C` the action array | **Known** | both walked at `0x0050C2DB` with a 20-byte stride and counts at `+0x7314` / `+0x7318` |
| `+0x731A` is the root trigger's repeat count | **Strongly inferred** | copied from byte `+0xF` of the root record, which is `RepeatCount` in the file layout |
| No shipped level exceeds either bank | **Known** (from data) | corpus sweep, all 66 levels |
| What id 128's target *is* | **UNKNOWN** | Q229 |
| How often `FUN_0050A450` is invoked, and what the 24 slots are | **UNKNOWN** | Q228 |
| What timer id 16 addresses | **UNKNOWN** | Q232 |
| Whether `0x006E5050` and `0x006E5054` are one object | **UNKNOWN** | Q231 |

`Confirmed` is deliberately not used: no original-game observation was made.
