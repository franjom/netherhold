# Mechanic: rebellion

Source: `DKII.EXE` v1.70 (GOG). Job 16's handler `0x00475170`, state 271's
handler `0x004750B0` and the angry chooser's first step `0x004784C0` were
disassembled in full with Ghidra 11.0.1 on 2026-09-23, and the four `Rebel`
globals were scanned for readers over the whole image. Confidence is
**CONFIRMED** where a row cites an instruction and **STRONGLY INFERRED** where it
names a routine from its callers; **UNKNOWN** is marked. Binary evidence, not a
running-game observation (ADR-0046).

`creature-anger.md` owns the ladder and the chooser that *picks* job 16. This
note owns what job 16 then does.

## 1. The shape, in one paragraph

Rebelling is not a roll. A creature that has drawn job 16 walks out through its
own keeper's nearest Portal if it can reach one, and if it cannot it stands
still for **30 ticks** and then **changes owner to the hero player**, carrying a
standing job of `KillPlayer` naming the keeper it just left. There is no second
chance: both branches return success, so the chooser never falls through to
another job.

## 2. Job 16's handler, `0x00475170`

Reached twice from the dispatcher `0x00478800` — through its jump table at
`0x00478936`, and through the Call To Arms shortcut at `0x0047885B` that admits
only jobs 10 and 16 (`job-selection.md` §2).

**Three guards, each returning failure:**

| # | Test | Our name |
|---:|---|---|
| 1 | `0x00473990` returns nonzero | `PrisonRules.IsImprisoned` — state group 14 or state 126 |
| 2 | `+0x3E6` bit 15 set | `CombatPitFighter` |
| 3 | `u16 [+0x3CB]` nonzero | held in the Hand |

Guard 2 is the interesting one: a creature fighting in the pit cannot rebel out
of it, whatever its anger.

**Then it marks itself and looks for the exit.** It clears `+0x3E6` bit 4 and
sets bit 3 — `RebelLeader := 0`, `RebelActive := 1` (§4) — and calls
`0x004C61C0(3, &position, &out, 0)` on the creature's **own player**
(`[creature+0x2A]`): find the nearest room of type **3, the Portal**
(`computer-keeper-handlers.md` §5 for the finder). The search is the keeper's
own room list, so an enemy portal is not a candidate.

**With a portal**, it converts the found room's tile bytes `+0x2D`/`+0x2E` to
world coordinates — `tile << 12 | 0x800`, the tile centre — and calls the
route-and-enter helper `0x00471890(&coords, 194, 0)`. **State 194 is
`leaving Dungeon`**, the same state job 10 `Leave` sets. A failure is retried
once as `0x00471890(&coords, 194, 2)`. On either success it sets `+0x3E6` bits 3
and 4 together — `RebelActive` **and** `RebelLeader` — and returns success.

**With no portal, or with both routes refused**, it takes the other branch:

1. `SetState(271)` through `0x00476D30`;
2. if that took, start creature **action 43** (`0x004969D0`, the action table of
   `creature-action-table.md`; which action 43 is, is **UNKNOWN**);
3. `[controller+0xB7] := 0x00517490() + 30` — the game tick (`trigger-state.md`
   §6 settles the unit as ticks) plus **30**, so **7.5 game seconds** at
   variable 19 = 4;
4. return success.

**Both branches return 1.** Like `Leave` (`creature-anger.md`), job 16 cannot
fail once past its three guards, so the chooser's fall-through never sees it.

## 3. State 271's handler, `0x004750B0` — the countdown and the defection

State 271 is labelled `idle`, group 3, category 7. Category 7 has **no enter,
update or leave hook at all** (`state-categories.md` §6), so this handler is the
whole of the state.

It runs per tick and does nothing until two things hold:

- `0x00496D00` returns zero — **not busy**, the same predicate the fight state
  uses (`combat-fight-state.md` §7: current action `+0x3A9` nonzero and not 21);
- `0x00517490()` is **strictly greater** than `[controller+0xB7]`, the deadline
  set in §2. The test is `JBE → return`, so the tick that equals the deadline
  still waits.

When both hold, in this order:

1. **The owner changes.** It reads `u16 [0x006F254C]` — **the hero player id**,
   ANSWERED as Q266 in ADR-0142 — keeps the creature's current owner from
   `u16 [+0x28]`, and calls the thing's virtual `+0x24` with the hero id.
2. `0x0048AD30(1)` → `SetState(1)`: state 1 is `idle` (§7.2).
3. **It is given a job.** `0x0048BE20(19, oldOwner, 0)` writes the triple
   `{u32 job, u16 player, u32 value}` at creature `+0x409`/`+0x40D`/`+0x40F`.
   That first field is a **job id**: `0x004787F0` loads `[creature+0x409]` and
   passes it straight to the job dispatcher `0x00478800`, and `0x00477850` does
   the same behind two flag tests. **Job 19 is `KillPlayer`**, and the player it
   is to kill is the keeper the creature just left.
4. `RebelActive := 1`, `RebelLeader := 0` again.
5. `0x0045A8B0` recomputes the targetable bit `+0x3EA` bit 6
   (`combat-acquisition.md` §4) — the creature has changed sides and is now a
   target for its old friends.
6. `0x0045A6D0(0)`, the may-I-look-for-a-fight gate, with **its result
   discarded**. Called for its effect, not its answer; what that effect is, is
   **UNKNOWN**.
7. `+0x3E6` bit 14 `Leaving` is cleared. It is not leaving any more.
8. **The old keeper is told.** `0x004C4600` on `world+0x2E54` with type **19**,
   player = the old owner and variant = the creature's `u16 [+0x4]`. That is the
   notification resolver of `mentor-speech-and-notifications.md` §3, whose types
   index the 100-record table at `0x006A0B00`; type 19's row is not read here.

The handler returns 1 either way, so waiting and defecting look the same to the
caller.

## 4. `RebelActive` and `RebelLeader` are DK2's own names

`+0x3E6` is the creature flag dword whose 31 bit names DK2's debug dumper prints
(`creature-object.md`). Bit 3 is **`RebelActive`** and bit 4 is
**`RebelLeader`**. That settles two things that were open:

- **`state-category-hooks.md` recorded `0x004752A0`** — category 26
  `ReportingDanger`'s only hook, its leave — as *"Clears creature `+0x3E6` bit 3.
  The bit's meaning is UNKNOWN."* The bit is `RebelActive`. **Why leaving the
  reporting-danger category should cancel a rebellion is UNKNOWN** (Q411); the
  function sits between job 16's handler and its neighbour in the same
  translation unit, which is evidence about where it was written, not about what
  it means.
- **`RebelLeader` is written and never read.** Two writes, both in §2's portal
  branch. A sweep of every instruction touching `+0x3E6` — 452 sites, 118 direct
  mask tests and 42 byte loads, each byte load's following window disassembled —
  finds **no test of bit 4 anywhere**. The rebel who reaches the portal is
  marked as a leader that nothing ever asks about.

`RebelActive` is cleared by state 89's handler `0x004778E0` (`0x0047792A`, right after the Call
to Arms clear, skipped for a dying creature), by category 26's leave hook and by the
constructor; a sweep of all 498 listing sites on `+0x3E3`–`+0x3E6` and a raw byte scan of every
block found no other writer (2026-10-02). It has two readers; the first is built (ADR-0318):

| Site | What it does with the bit |
|---|---|
| `0x0048A36C`, inside the per-tick creature update `0x00488930` | the door contact: a rebel fights its own keeper's shut door (`door-blocking.md` §1). **STRONGLY INFERRED**, read whole 2026-10-02 |
| `0x00491B18`, in the predicate `0x00491AE0` | one of six clauses that must all hold for it to return 1; a rebelling creature fails it. What the predicate gates is **UNKNOWN**: its only callers are `0x00461980` and `0x00515D50` |

## 5. The three outcome percentages are dead, and so is the follower cap

`RebelMaxFollowers` (47, ships 2), `RebelLeavingPercentage` (48, 70),
`RebelBecomeGoodPercentage` (49, 15) and `RebelDefectPercentage` (50, 15) are
**written by their loader stubs and read by nothing in the executable**.

Ghidra's instruction sweep finds only the four stores at `0x0051CF1C`–`0x0051CF43`.
That sweep sees only what is disassembled, so it was checked again against the
raw file: the four-byte little-endian encoding of each global's address occurs
**exactly once** in all 2,987,568 bytes of `DKII.EXE`, at the store.

**Two positive controls, both from `balance-variables-in-the-binary.md`, and
both matched exactly.** `PrisonModifyCreatureHealthPerSecond` (`0x006F2621`) is
recorded there as having **two** readers and occurs three times. Variable 18
(`0x006F258C`) is that note's own stated control for any re-scan — *"must return
three readers"* — and occurs four times. The method separates a live rate from a
dead one on this exact block.

So the shipped game has **one** rebellion outcome, not a weighted choice of
three, and the leader/follower mechanic the names describe was never wired.
`RebelLeavingPercentage` and `RebelBecomeGoodPercentage` do survive as the two
*code paths* of §2 — portal, and otherwise turn Good — but the branch between
them is "is a portal reachable", not a roll.

## 6. What this settles about Q116

Q116 held two irreconcilable accounts: the wiki's, that rebellion is the
fallback when the exit is unreachable, and the record's, that `AngryJobs` is a
weighted table. **Both are right, one level apart.**

The *table* decides whether the creature takes job 10 or job 16 — that part is
the record's, rolled afresh every five game seconds (ADR-0061). *Within* job 16,
the portal is tried first and turning Good is the fallback — that part is an
engine rule, and it is exactly the wiki's sentence. The wiki was describing the
inside of the Rebel handler, not the chooser.

The practical difference between jobs 10 and 16 is therefore smaller than their
names suggest. Both end in state 194 `leaving Dungeon` when a portal is
reachable; job 10 sets no flags and never checks, while job 16 marks itself,
checks, and has somewhere else to go.

## 7. Two answers found on the way

### 7.1 The angry chooser's first step, and where an angry Horny goes

`creature-anger.md` recorded `0x004784C0`'s opening as *"a per-creature flag
aborts it before anything else"* with that flag unidentified. **It is `+0x3E6`
bit 0, `InHand`** — a creature in the keeper's hand does not choose. This
project already had that rule from the other side (`CreatureAngerSystem` gates
`Decide` on `HandRules.IsHeld`), so it is corroboration, not a change.

The definition flag beside it, `0x4000`, was identified as **`IsHorny`** in
ADR-0145 (Q204). What the diversion *does* was not read then, and is now: unless
the creature is already in state **242**, the chooser puts it in state **238**
and returns success. Both are **category 28**, whose update hook applies
**−10 health a tick** unless the owner's `Status` is Won
(`state-category-hooks.md`). **An angry Horned Reaper kills itself**, and the
chooser is where that starts. Unbuilt, and not part of ADR-0172.

### 7.2 `0x0048AD30` is `SetState` on the controller

`0x0048AD30(x)` is four instructions: add `0x24E` to `this` and tail into
`0x00476D30(x, 0)`. `creature+0x24E` is the controller object the state handlers
run on — the same `this` whose `+0x8` is the creature body. So it is the
body-to-controller adapter for `SetState`, and nothing more.

That answers a sub-item of **Q401**, which listed *"`0x0048AD30(1)` before a
heal (unread, likely display)"*: it is `SetState(1)`, **idle**. The heal
interrupts what the creature was doing; it is not a display call. `shots.md` §3
carries the same guess and is corrected with it.

## Implementation

`RebellionRules` + `CreatureAngerSystem.Rebel`, and
`CreatureState.RebelCountdownTicks` — the deadline of §2 kept as a countdown,
which is the same clock read the other way, as the category hooks keep `HookCountdown`.
`Leave` now reports whether it found a route, because §2's branch is exactly
that question. The defection pass runs **every tick**, before the once-a-second
ladder, because 30 ticks is 7.5 game seconds and a once-a-second system cannot
express it.

`DefectDelayTicks` is 30, `DefectsTo` is `PlayerIds.Good`, and the defected
creature is given `JobType.KillPlayer` with `RebelAgainstPlayerId` remembering
the keeper it left.

## Tests

`RebellionTests` (`tests/DK2.Simulation.Tests/`). Scenario
`a_goblin_with_no_portal_turns_good.yaml` (`scenarios/creatures/`).

## Known exceptions

- **The pit-fighter guard is inert.** Nothing in this simulation sets
  `CombatPitFighter`, so guard 2 of §2 can never refuse. Recorded at the site so
  it lands when the Combat Pit does.
- **`KillPlayer` is remembered, not performed.** DK2 gives the defector a
  standing job naming its old keeper; job 19 has no handler here, and the
  creature fights as any hero creature does.
- **`RebelLeader` is not modelled**: nothing reads it. `RebelActive` is
  `CreatureState.RebelActive`, set by job 16 and the defection and cleared by state 89 and
  category 26's leave (ADR-0318).
- **Action 43 is not started** — which action it is, is UNKNOWN (§2).
- **Notification type 19 is not raised**; notifications are not built.

## Open questions

- **Q410**: whether a creature counting down in state 271 keeps choosing. The
  chooser's only state gate is category 22, and 271 is category 7, so it looks
  as though it does — and job 16 reloads the deadline every time, so a 50/50
  table against a five-second gate and a 7.5-second countdown may never defect.
  Not built; ours commits to the countdown once it starts.
- **Q411**: why category 26 `ReportingDanger`'s leave hook clears `RebelActive`
  (§4).
- **Q412**: what `0x0045A6D0(0)`'s discarded call does (§3 step 6), and what
  `0x00491AE0` gates (§4).
- **Q413**: notification type 19's row in the table at `0x006A0B00`, and
  creature action 43.
