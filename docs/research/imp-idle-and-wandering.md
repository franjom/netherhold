# Research: what an imp with nothing to do does

Source: `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-24. Addresses
and control-flow descriptions are facts about the binary in our own words; no
decompiler output enters this repository (ADR-0046). Binary-only findings are
**STRONGLY INFERRED**. Checked afterwards against Flame and OpenKeeper (§6).

Questions: **Q41** (the body behind the owner's observation), **Q415** (whether
the owner's "waiting" needs state 88). State ids, labels and categories come from
`creature-state-table.md`; `imp-state-exits.md` has the one-line summaries of
states 10 and 12 that this note expands.

**Built subset:** `ImpTaskSystem.Wander.cs` (ADR-0224) runs rest and wandering,
with its documented action-duration and destination approximations. The stored
idle flag at `+0x3D1` is not represented yet; its removal reader is audited in
`imp-mana-removal.md`.

---

## 1. The owner's observation

On a level with only imps and a Dungeon Heart, imps with nothing to do *"run
around and then stop, then they do something that can be possibly interpreted as
waiting, then they run around again and repeat"* (owner, 2026-09-24). This is the
loop in §2–§4. The binary explains it without state 88 (§5).

## 2. State 10 hands an idle imp to state 12 with a deadline

State 10 (`0x00463DF0`) runs the task selection (`imp-task-priority.md`). It
leaves for state 12 on three arms, and two of them set state 12's deadline at
state `+0x7E`:

| arm | condition | deadline set |
|---|---|---|
| nothing found | the whole selection took no task | now + 240 + rand(240): **240–479 ticks, 60–120 s** |
| throttled | the owner's per-player counter at `player+0x7A` is already above 3 (and a time test on state `+0x9E` passes); otherwise the counter is incremented and selection runs | now + 20 + rand(80): **20–99 ticks, 5–25 s** |
| special owner | creature `+0x28` equals the global at `0x006F2548` | none written |

Four ticks a second (Q1). Where the per-player counter is reset is not read here.
Neither handler reads variable 30, `ImpIdleDelayBeforeReevaluationSeconds`. That
is a statement about these two functions only, not evidence that the variable is
unused.

## 3. State 12, one tick

`0x00464240`, label *idle* (string 2599), category 3, group 8.

1. Set creature `+0x3D1` to 1 before checking the action or deadline. The
   mana-removal selector reads it (`imp-mana-removal.md`). `0x00463DF0` clears it
   only after its special-owner and throttle exits; state 11 (`0x00463DB0`) clears
   it before requesting state 10, even on refusal. It persists through wandering.
2. If an action is running (`0x00496D00`: creature `+0x3A9` non-zero and not 21),
   do nothing more this tick.
3. At or after deadline `+0x7E`, request state 10; clear `+0x3D1` only if accepted.
4. If state `+0xA7` is non-zero, roll rand(100):
   - **0–24**: wander (step 5);
   - **25–74**: play creature action **43** if the creature can (below), clear
     `+0xA7`, done;
   - **75–99**: play creature action **44** likewise.

   If the chosen action is unavailable, wander instead.
5. Wander: `SetState(87)`. If it is accepted, store now + 32 at `+0xA7`. If it is
   refused, stay in 12.

"If the creature can" is `0x004966C0`. It reads the action record's `+20` byte
(`creature-action-table.md` §1) and resolves it through `0x00494860` to a
27-byte entry on the creature, whose `+0xB` bit 0 must be set. So actions 43 and
44 play only on creatures that have what they need, most plausibly an animation.
In the action table, **43 has `+20` = 20 and 44 has `+20` = 31**, and neither has
a handler of its own. They are not OpenKeeper's animation-order indices, because
under that order dig and mine (actions 1 and 2, both `+20` = 5) would be *death
pose*. Which animations they are is **UNKNOWN**.

Consequence, from steps 4 and 5: after an idle action ends, `+0xA7` is zero, so
the next free tick always wanders. `+0xA7` is only ever tested for non-zero here.
The now + 32 stored into it acts as a flag, and nothing in this state reads it as
a time.

## 4. State 87, the wander walk

`0x004808E0`, label *wandering* (string 2628). Ghidra had no function at this
address (no frame-pointer prologue); forced for the read.

1. If an action is running, do nothing.
2. Pick a destination with `0x00480A00(destination, r)`, trying r = 4 + rand(4)
   (4–7) and then each smaller r down to 1, stopping at the first success. If
   every r fails, the destination is the creature's own position.
3. Randomise the sub-tile point from the nine-entry table at `0x00698EC8`.
4. Walk there: state 6 through `0x00471890`, move options `0x80`, resume **1**.
   A creature whose definition has flag `def+0x686 & 0x4000` resumes in 238
   instead; that bit is the Horned Reaper's (`combat.md`).

**The destination picker `0x00480A00`**:

- **1 in 8** (rand(8) = 0): walk the owner's room list and take a random point in
  a room of one of these types: Treasury, Lair, Hatchery, Library, Training Room,
  Guard Room, Workshop, Prison, Torture Chamber, Temple, Graveyard, Casino, Combat
  Pit, Crypt (room type ids 1, 2, 4, 6, 7, 9–16, 26). The **Dungeon Heart and the
  Portal are not on the list**, and neither is a bridge. The walk's arithmetic
  settles on the last eligible room in list order; the retry path is not fully
  traced.
- **Otherwise** (or when no room qualifies): scan the square ring at Chebyshev
  distance r around the imp's tile, all 8r tiles, starting at a random one. Take
  the first tile that is in bounds, not solid, **not part of a Dungeon Heart** (a
  room tile whose room `+0x18` is type 5 is rejected), **owned by the imp's
  keeper** (creature flag `+0x3E6 & 0x4000` lifts this), and passes `0x00453600`.
  The destination is the tile's centre. If the scan finds nothing, a fallback
  tests one more ring tile on the same conditions, less the heart test.

On the owner's level there are no eligible rooms, so every trip is a ring walk to
one of the keeper's own claimed tiles 4–7 tiles away, falling back to nearer
rings.

## 5. The loop, and why the owner's "waiting" is not state 88

State 1 (`0x004773D0`), where the wander resumes, sends a creature whose
definition has `def+0x686` bit 0 (the worker flag) straight to state 10. So:

```
10 (no task) -> 12 -> 87 -> 6 (walk) -> 1 -> 10 (select again) -> 12 -> ...
                  |
                  +-> action 43 or 44 in place, then 87 on the next free tick
```

This is the owner's run, stop, "wait", run again. It also means an idle imp
re-runs task selection **after every wander trip**. The 60–120 s deadline of §2
is rarely the thing that ends state 12; the trip usually does.

Whether `+0xA7` still holds state 12's stamp when 12 is re-entered through 6, 1
and 10 depends on what those states write at that offset. That is **UNREAD**, so
the share of stops that play an action, as opposed to leaving at once, is not
pinned. With the flag set, it is 75 %, less any action the imp lacks.

**State 88** ("waiting", `0x00480F50`) plays the same action 43 (`travel-states.md`
§6). The owner's sighting therefore does not need state 88. It is state 12's
25–74 arm, whose activity label is *idle*, not *waiting*. Q415 stays open.

## 6. Flame and OpenKeeper

- **Flame** names `0x00464240` `ImpState_idle_12`, `0x004808E0`
  `wandering_87_StateUtils` and `0x00480750` `wandering_86_StateUtils`. It leaves
  `0x00480F50` (state 88) unnamed. Its `vtbl_0066D404` "membership" for these
  handlers is its label for all 346 state functions (`CState`), not a second
  dispatch path into 88. Cited as Flame's; not adopted.
- **OpenKeeper**'s `IDLE` state walks to a random point when it finds no work,
  and its `WANDER` state is commented out. It has no idle roll, no idle
  actions and no ring search. It lists `IDLE_3`/`IDLE_4` animations as
  imp-specific; whether those are actions 43/44 is not established.

## 7. State 86, for completeness

`0x00480750`, the other *wandering* state. It steps to a random one of the four
orthogonal neighbours (table `0x006BC350`) that is within a Chebyshev radius
`+0xAF` of an anchor `+0xA7`/`+0xAB` and not solid, at a random sub-tile point,
resuming in 86 until the deadline `+0xB3`. It is a local mill-about round an
anchor, not the idle imp's walk. Its callers are not traced here.

## Built (2026-09-25, ADR-0224)

States 10 → 12 → 87 → 1 with the deadline, the roll, the picker's ring and room branches and the
own-spot fallback. Read while building:
- **The ring scan** starts at rand(8r) and runs to the end, not wrapping.
- **The fallback** runs only when the last tile tried was (0, 0).
- **Room branch order:** after the last eligible room, every later room of any type is tried.
- **The picker's rolls** come from a second random stream (`+0x568`).
- **The room point** is `0x004E8430`: a random one of the room's tiles passing `0x00454110` and `0x00454230`.
- **`0x00453600`** rejects a tile holding an object flagged Obstacle (bit 14).

Guessed: the idle actions' length. Not built: the throttle.

## What building it needs

- State 12's roll and deadline, state 87's picker, and the resume through 1 → 10.
- Actions 43 and 44 as timed "busy" periods. Their length comes from the
  animation, which is UNKNOWN (above); a placeholder duration must be marked as a
  guess.
- The per-player selection throttle of §2, which also bounds how many imps search
  in one tick.
