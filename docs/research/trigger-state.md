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

The corpus agrees: every timer *started* by action type 8 has an id in 0..15.

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

### Authored timer thresholds are in ticks, not seconds

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

So a trigger's authored threshold is consumed raw, in ticks. `Value=600` on a
timer condition is **150 seconds, not ten minutes**.

**This deserves an observation before anything is built on it.** The shipped
thresholds are suspiciously round *as seconds* — 60, 120, 180, 240, 300, 600 —
which is what a designer typing seconds would produce, and a 4x error here would
mis-time every timed objective in the campaign. Against that: the corpus also
contains 1, 3, 5, 15 and 25, which are not multiples of four, so the editor is
not storing `seconds x 4`. The binary reading stands and the round numbers are
most likely round tick counts, but Q228 records what to watch in the running
game to be sure.

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

**Still UNKNOWN:** the vtable slot's index, hence who invokes it and at what
rate; and what the 24 slots are. Q228 keeps both, with the method.

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
| The clock counts ticks, 250 ms each | **Strongly inferred** | `now % var19 == 0` is the once-a-second gate (ADR-0089) |
| Authored thresholds are ticks, not seconds | **Strongly inferred**, wants an observation | no multiply in the trigger path, and no var19 reference in the region |
| No shipped level exceeds either bank | **Known** (from data) | corpus sweep, all 66 levels |
| What id 128's target *is* | **UNKNOWN** | Q229 |
| How often `FUN_0050A450` is invoked, and what the 24 slots are | **UNKNOWN** | Q228 |
| What timer id 16 addresses | **UNKNOWN** | Q232 |
| Whether `0x006E5050` and `0x006E5054` are one object | **UNKNOWN** | Q231 |

`Confirmed` is deliberately not used: no original-game observation was made.
