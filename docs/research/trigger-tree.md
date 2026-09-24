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
| The arming walk enters a condition's child chain but never an action's | **Strongly inferred** | `0x00519A6A` reads `+0x0C`; the action arm at `0x00519AE1` reads only `+0x0A` |
| Action type 14's member gate is the creature id at member `+0x1C` | **Strongly inferred** | `TEST AL,AL` at `0x0051A1CF` on the copied member record |
| Every shipped party member carrying a trigger id also carries a creature id | **Known** | 66 THINGS files, 220 parties, 947 occupied member slots (`dk2tools kwd parties`) |

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

**That walk does not descend an action's children.** Its condition arm reads
`+0x0C` and then `+0x0A` (`0x00519A6A`, `0x00519A80`); its action arm reads only
`+0x0A` (`0x00519AE1`). So a subtree hanging under an action is reached by
neither the load-time disable nor the runtime arm, and keeps whatever state its
own initialiser gave it — active for an action, and for a condition only if its
authored `RepeatCount` is non-zero. Anything an author hangs below an action
inside a party subtree therefore does **not** come alive with the rest of it.

The two setters differ in one more way. Enabling a condition writes **1** to its
repeat byte and disabling writes 0 (`0x00508660`); the action setter
(`0x00508840`) writes **only** bit 0 of `+0x10` and never touches `+0x0F`, in
either direction. No walk reads an action's repeat byte, so this is invisible in
play and visible in a state hash.

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
every one of those subtrees** before the level runs (`0x0050C289` to
`0x0050C2D9`: sixteen reads from `+0x20` at stride `0x20`, then the party's own
at `+0x00`, times 24 slots). Unlike the runtime arm it **gates on nothing** but
the id being non-zero: an unoccupied member slot is disabled here and, because
the arm skips it, never re-enabled.

**UNKNOWN — the sweep has an off switch.** The whole loop is skipped when bit
`0x40` of the world byte at `+0x7239` is set (`0x0050C280`). Nothing traced sets
it; a restored save is a guess and no more than that.

**Which slot a shipped party lands in is not decided by the data.** Action type
14 selects by id, so a reimplementation should too — but across 66 THINGS files
and 220 parties, no party's `PartyId` differs from its position in its file and
none repeats (`dk2tools kwd parties`). Ids reach 19. The two readings are
indistinguishable in the shipped campaign, and only the consumer's arithmetic
picks between them.

**Action type 14 creates a party**, and all three of its named bytes now hold.
The call site at `0x00519182` pushes them in a fixed order — byte `00`, byte
`04`, byte `01` — which makes them arguments 1, 2 and 3:

| body byte | argument | what the handler does with it |
|---|---|---|
| `00` `PartyId` | 1 | `AND EAX,0xff`, `* 0x208`, index `+0x4030` (`0x0051A16C`). **No bound check**; shipped ids run 0..19 against 24 slots |
| `04` `ActionPointId` | 2 | matched against `[obj+0x37]` (`0x0051A035`), then the object's `+0x38`..`+0x3E` bounds become a centre in `Fp32` (`SHL 0xc`, `ADD 0x800`) — the spawn point |
| `01` `PartyType` | 3 | tested at entry (`0x00519F98`); zero skips a preliminary scan entirely. Ships as 0 (244 records), 2 (14) or 1 (1) |

The handler enables the party's subtree (`0x0051A197`), then loops the sixteen
member slots: **a slot is skipped when the byte at member `+0x1C` is zero**
(`0x0051A1CF`) — the creature id, since the runtime member record is the
THINGS-file member copied verbatim and `+0x18`/`+0x1C` are its trigger and
creature ids. Otherwise it creates a creature — a block of per-creature setters
unpacking a bitfield — and enables that member's own subtree (`0x0051A594`).

**The guarded early return** is read in full in `hero-parties-and-effect-generators.md`:
with `PartyType` non-zero the handler walks the hero player's creatures for a live,
unimprisoned member of this party and returns 1 having armed nothing when it finds
one (`0x00519FE4` to `0x0051A747`), so a party is not raised twice. The rest of the
handler — types, level-ups, gold, health, job, flags, leader and followers — is there too.

`+0x10` is initialised to **2**, not to 0 or 1: both initialisers set bit 1 and
only bit 0 varies. What bit 1 means is UNKNOWN; nothing above reads it.

## Implementation boundary

`TriggerExecutor` walks all twenty-five roots once per turn, starts every party
subtree disabled, and runs action types 7, 8 and 14. Type 14 runs **in part**:
it arms the party's and its occupied members' subtrees and creates no creatures.
That half is counted in `TriggerRunResult.Partial` rather than reported as
success, because a level whose script waits on a hero existing — being killed,
reaching a point, being fought — cannot proceed, and a divergence there is ours.
Everything else is blocked and counted, not guessed.

**What this walker is not.** It is one of four, and campaign level 1 keeps its
script in another one — see `trigger-walkers.md` for which code drives each tree
and how often. So an empty column in this executor's output is not evidence about
a rule: most often it means nothing here ever looked. `dk2tools trigger-run`
prints the count of roots it did not walk for that reason.
