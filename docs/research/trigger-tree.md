# World trigger-tree traversal in DKII.EXE v1.70

**Scope:** the world tree walker at `0x00518150`, its record initialisers at
`0x00508600` and `0x00508810`, and the shipped `*Triggers.kld` corpus. This
describes traversal and repeat retirement; it does not establish action-type
semantics or add an executor to the reimplementation.

## Confidence

| Claim | Confidence | Basis |
|---|---|---|
| Positive ids select tag 213 conditions; negative signed ids select tag 214 actions; zero ends a chain | **Strongly inferred** | Direct instruction read of `0x00518150` |
| `NextTriggerId` is unconditional sibling continuation | **Strongly inferred** | Every active, inactive, true and false path rejoins the load from record `+0x0A` |
| `ChildTriggerId` is visited only after the current condition or action succeeds | **Strongly inferred** | Calls from `0x005181C6` and `0x00518242`, after each dispatcher succeeds |
| A condition's `RepeatCount` is passed to its child chain; value 1 retires each successful action | **Strongly inferred** | Byte `+0x0F` is the recursive argument; the action path clears its active bit only when that byte equals 1 |
| Across all 9,161 shipped records, every non-zero child and next link resolves within its file | **Known** | Exhaustive read of 66 shipped trigger files |

`Confirmed` is reserved for an observation of the running game. These control-
flow claims come from the binary.

## The walk

**There are twenty-five roots per turn, not one.** The world frame walks the id
at `+0x7310` with the repeat byte at `+0x731A`, and then loops the 24 party slots
(`0x0050A760`: `LEA EDI,[ESI + 0x4030]`, `MOV EBP,0x18`, stride `0x208`),
walking each slot's non-zero trigger id **with repeat argument 1**. An executor
that walks only `+0x7310` can arm a party subtree and never traverse it, so
nothing it contains will ever run. Q228's cadence result is in
`trigger-state.md`: the caller runs once per game turn.

Repeat 1 on those party roots is not incidental — it is the value that retires a
successful action, so every action directly under a party root is one-shot.

Ids are treated as signed 16-bit values. A positive id is looked up in the
condition array at `+0x7308`; a negative id is looked up in the action array at
`+0x730C`. Action ids therefore appear as large unsigned values in the file
model. Zero returns without evaluating a record.

For each resolved record:

1. If its runtime active bit (`+0x10`, bit 0) is clear, skip evaluation.
2. Otherwise run the appropriate world dispatcher.
3. On success, recursively visit `ChildTriggerId` (`+0x0C`). A condition passes
   its own `RepeatCount`; an action passes through the inherited value.
4. Visit `NextTriggerId` (`+0x0A`) whether the record was inactive, failed, or
   succeeded.

The result is a sibling chain whose successful nodes may enter child chains.
A nested condition replaces the inherited repeat policy for its own children;
the condition's next sibling remains under the policy of the current call.

The lookup code uses the first record if a non-zero id is absent. This is a
binary-visible fallback, but it is not exercised by shipped data: every one of
the 9,161 records' non-zero `NextTriggerId` and `ChildTriggerId` values resolve
within the same trigger file.

## Repeat and active state

The condition initialiser sets the active bit exactly when the authored
`RepeatCount` is non-zero. The action initialiser sets actions active regardless
of their own authored repeat byte.

When an action succeeds, inherited repeat value 1 clears that action's active
bit before its child is visited. Other inherited values do not retire the
action in this walker. The action record's own `RepeatCount` is not consulted.
Across the shipped corpus, condition repeat values are:

```text
0: 23   1: 3484   2: 5   10: 4   30: 5   255: 232
```

The condition path contains a call to a helper that decrements the condition's
repeat byte and clears its active bit at zero. That call requires the recursive
walk to return non-zero, but every exit from this walker returns zero. The same
shape occurs in the player, creature and area walkers. Therefore condition
repeat decrement is unreachable here: values other than zero control initial
activation, and value 1 additionally makes successful descendant actions
one-shot.

This dead return-dependent path is preserved as a binary quirk, not treated as
an intended countdown.

## Re-arming: retirement is reversible, and action type 14 is how

`0x00508660` (condition) and `0x00508840` (action) each take a flag and set or
clear bit 0 of `+0x10`. Enabling a **condition** also writes **1** to its repeat
byte; disabling writes 0. Each setter has exactly one caller: `FUN_00519A20`, a
second recursive walk that resolves ids by the same rules as the evaluator above
and applies the flag to every record in a subtree.

That walk is reached from two places, and they pull in opposite directions:

| caller | flag | when |
|---|---|---|
| `0x0050C2AC`, `0x0050C2C5` in the level initialiser `FUN_0050B5B0` | **0** — disable | level load, over the `+0x4030` array |
| `0x0051A19C`, `0x0051A594` in `FUN_00519F90` | **1** — enable | runtime |

`FUN_00519F90`'s only caller is the world action dispatcher, at the arm for
**action type 14** (`0x00519182`, resolved through the dispatcher's two-level
table; 259 records ship, and level 1 uses it).

**The consequence an executor must not miss.** A re-armed condition carries
repeat byte 1 whatever it was authored with, and a condition passes its own
repeat byte to its children, where 1 is exactly the value that retires a
successful action. So re-arming a subtree silently makes its actions one-shot.
**232 shipped conditions carry 255**, so this is not a corner case.

## The 24 slots at `+0x4030` are the level's party table

`FUN_00519F90`, the action type 14 handler, indexes them at `0x0051A18A` with
`party * 0x208`, and walks their members at `0x0051A1C1`
(`LEA ESI,[ECX + EBX*0x1 + 0x4038]`, with `ECX` the member index shifted by 5).
That fixes the record exactly, and it fills without a byte to spare:

| offset | |
|---|---|
| `+0x00` | the party's own trigger id |
| `+0x08 + i * 0x20`, i in 0..15 | sixteen member records of 0x20 bytes |
| member `+0x18` | that member's trigger id |

`8 + 16 * 0x20 = 0x208`. The loader's disable sweep reads exactly these
seventeen ids — the member ids are its `+0x20`-stride reads — and **disables
every one of those subtrees** before the level runs.

**Action type 14 creates a party**, and for once the transcribed names hold. Body
byte `00` is the table index (traced to arg 1 at `0x0051A16C`; `AND EAX,0xff`
with **no bound check**, though shipped ids run 0..16 against 24 slots). Byte
`01` is read at entry (`0x00519F98`) and selects a path; it ships as 0, 1 or 2.
The handler enables the party's subtree, then for each member whose first byte is
non-zero creates a creature — a block of per-creature setters unpacking a
bitfield — and enables that member's own subtree. **Byte `04` is not traced**, so
whether it is the action point the transcription calls it is still UNKNOWN.

`+0x10` is initialised to **2**, not to 0 or 1: both initialisers set bit 1 and
only bit 0 varies. What bit 1 means is UNKNOWN; nothing above reads it.

## Implementation boundary

`TriggerExecutor` walks this tree once per turn and runs action types 7 and 8
only; everything else is counted, not guessed. Type 14 is the one whose absence
is structural rather than incidental — level 1's root fires two of them, and
until they run the party subtrees stay disabled, so most of the level's script
never arms. Its creature-creation half needs its own reads before it can ship.
