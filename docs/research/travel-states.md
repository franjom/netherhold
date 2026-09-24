# Mechanic: travel states — how a caller hands work to a walk, and how the walk hands it back

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-23. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). State names are the executable's own `Text.str` labels
  (creature-state-table.md); `StateBeforeUtilityState` and `UtilityNewState` are its
  creature dumper's names for the state object's `+0x40` and `+0xDF`
  (creature-job-object.md).

Original version: DK2 v1.70 (GOG)

Questions: answers **Q414** (who writes `UtilityNewState`, what fires the resume);
opens **Q415** (states 9 and 88 have no entry site) and **Q416** (unread details in the
two movers). Corrects eating.md §2.1–2.2 (state 92's resume state, state 94's test, and
which routine writes `+0xDF`) and steering-behaviours.md §7.6 (`0x00471A70` takes a
room, and option bit 0 is now read). `combat-approach-and-victory.md` owns state 8 as
combat; `steering-behaviours.md` owns the body the movers integrate;
`creature-state-table.md` owns the table and the transition rule.

**What this settles:** a caller never writes the resume pair itself. It calls one of two
**route helpers** with *(target, resume state, option word)*. The helper requests the
route, enters state **6** (to a position) or **7** (to a room), and **only then** writes
the resume state into `+0xDF`. `SetState` fills `+0x40` on the way in. When the path is
finished, the travel handler's own arrival branch stops the body, calls
`SetState(+0xDF)` and resets `+0xDF` to **1**. No category hook is involved and no
caller polls.

---

## 1. The protocol at a glance — CONFIRMED

| step | who | what |
|---|---|---|
| 1 | caller (a state handler) | `0x00471890(&position, resume, options)` or `0x00471A70(room, resume, options)` |
| 2 | helper | tile check `0x004B35C0`, route request `0x004D0F60(creature, &point, options)` |
| 3 | helper | `SetState(6)` or `SetState(7)`, **result ignored** |
| 4 | `SetState` | current category not 2, target category 2 → `+0x40` = the caller's state id, **no hooks run** (§3) |
| 5 | helper | `+0xDF` = resume; destination stored; combat left by hand if needed (§2.1) |
| 6 | travel handler, each tick | integrate the body; test arrival |
| 7 | travel handler, on arrival | `0x0048ECE0` (stop), `SetState(+0xDF)`, `+0xDF = 1`, return that `SetState`'s result |

A zero return reaches the dispatcher (`0x00476FC0`), which sets state 1 (eating.md §8).

## 2. The route helpers

### 2.1 `0x00471890(position*, resume, options)` — to a position, state 6 — CONFIRMED

`RET 0x0C`, three stack arguments in that order (112 call sites).

1. `0x004B35C0` must accept the point and `0x004D0F60(creature, point, options)` must
   succeed; otherwise go to §2.3.
2. If `creature+0x3DB` is set, clear `creature+0xF2`; then clear `+0x3DB`.
3. `SetState(6, 0)`; the result is not tested. A refused transition leaves the creature
   in its old state with a route loaded and `+0xDF` written, and the helper returns 1.
4. `+0xDF` = resume.
5. If the category of `+0x40` is **22** (combat) and the resume state's is not, call
   `0x0045ADC0`, the routine combat's own leave hook calls (state-category-hooks.md).
   This is needed because §3's entry into category 2 skipped that hook.
6. `+0xEB` = 0; `+0xE3`/`+0xE7` = the point's x/y.
7. If busy (`0x00496D00`), end the current action (`0x00496D20`).
8. `creature+0x47C` (word) = 0; return 1.

### 2.2 `0x00471A70(room, resume, options)` — to a room, state 7 — CONFIRMED

`RET 0x0C`, 22 call sites. steering-behaviours.md called the first argument an
entity. It is a **room**: it is compared with `0x004559B0` of the creature's tile
(tile → room) and handed to `0x004E4C10`, which yields a point inside it.

1. `creature+0x47C` = 0.
2. **Already inside:** if the creature's tile belongs to that room, `SetState(1)` then
   `SetState(resume)`, and return the second result. There is no walk, and `+0xDF` is
   not written.
3. Otherwise take a point from `0x004E4C10(room)`, then the tile check and the route
   request as §2.1; on failure go to §2.3.
4. The `+0x3DB`/`+0xF2` step, then `SetState(7, 0)`, result ignored; `+0xDF` = resume.
5. The combat exit of §2.1 step 5, preceded here by a debug print through `0x00634DB0`
   (format at `0x00698D28`) carrying both state ids.
6. **A different destination layout:** `+0xEE` = 0; `+0xE3` (word) = the room's id
   (`room+4`); `+0xE5` (byte) = `room+0x18`; `+0xE6`/`+0xEA` (dwords) = the point. The
   dumper line `MoveToRoom (%d, %d)` at `+E6` is this point.
7. If busy, end the action; return 1.

### 2.3 A failed route with option bit 0: tunnelling — CONFIRMED (branches)

Both helpers share the failure path. Without option bit 0 they return 0. With it:

- **A hero worker** (owner `== [0x006F254C]`, the hero player; definition `+0x686`
  bit 0, `IsWorker`) calls `0x00485B80(target, resume)` (position) or
  `0x00485AB0(room, resume)`. Each enters state **186, tunnelling**, stores the target
  tile, and keeps the resume state at `+0xD3`, so the tunnel carries the errand. Success
  returns 1.
- **Anyone else** with `+0x417 != 0xFF`, `+0x46F` bit 1 and `+0x3E1 > 0` walks the
  thing list from `[+0x2A]+6` for a creature with the same `+0x417`, `+0x46F` bit 2 and
  `IsWorker`. It tries to put that worker into tunnelling toward the target, with
  resume **224, waiting**. On the first success the worker's tag goes to
  `creature+0x3DF`, and the caller enters state **223, advancing**, following it.

That `+0x417` is a party id, and that the worker is the Dwarf, are STRONGLY INFERRED
from the shape; the three party bytes are unnamed.

### 2.4 Other writers of `+0xDF` — CONFIRMED (complete list)

A sweep of every store to `+0xDF` (and to `creature+0x32D`, the same slot addressed
from the creature) finds **16**: the two helpers; the resets in states 6, 7, 8 and 88;
state 8's entry `0x00471D10` (199, with a copy in `+0xEB`); the constructor
`0x00476B40`; and three that are not category 2:

| site | owner | what it does |
|---|---|---|
| `0x004652FC` | `0x004651A0`, the Imp's route-or-teleport | more than two tiles away on either axis and reachable (`0x00471630`): `+0xDF` = resume, `SetState(1)`, action 60 at the point, `SetState(51)` teleporting. Otherwise it calls `0x00471890`. State 52's handler (`0x004654A0`) resumes through `+0xDF` |
| `0x004618BF` | state 261, hypnotised (`0x00461860`) | on its re-route timer writes `+0xDF = 261`, routes directly, and between re-routes **calls state 6's handler as a subroutine** |
| `0x004744CC` | `0x00474430` | `SetState(234)`, then `+0xDF = 233`, a category-28 pair using the slot as its own next state |

So `+0xDF` is a general "next state" slot. Category 2 is its main user, not its only
one. **The constructor sets `+0x10` = 1, `+0x40` = 0 and `+0xDF` = 0.** After any
resume the slot holds 1.

## 3. `SetState`'s part — CONFIRMED (`0x00476D30`, read in full)

After the allowed-test of creature-state-table.md §2.2, the routine branches on the
current category (`EBP`) and the target's (`EDI`):

| current → target | what happens |
|---|---|
| not 2 → 2 | **`+0x40` = current id.** Commit. **No leave or enter hook runs** |
| 2 → 2 | first a recursive `SetState(1)`; if refused, the whole call is refused. Then as the row above, so **`+0x40` = 1**, not the earlier saved state |
| 2 → not 2 | hooks judged against **the category of `+0x40`**: equal to the target's → commit with no hooks; else the leave hook of `+0x40`'s category, `+0x10` = 1 and the out-flag set, then the target's enter hook, and a zero return leaves the creature in state 1 |
| not 2 → not 2 | same categories → commit; else leave hook, state 1, enter hook as above |

Commit writes `+0x10`, stamps `+0x18` from the turn, and zeroes `+0x1C` and `+0x44`.
**`SetState` never writes `+0xDF`**; its only use of the slot is the substitution in
the allowed-test. The hook tables are `0x0069968C` (enter), `0x0069969C` (update)
and `0x006996AC` (leave), each `+cat*0x40`, as creature-state-table.md §6 decodes.

The effect: **a walk is invisible to the category hooks.** A creature leaving a room
job to fetch something and coming back to the same category never runs that
category's leave or enter hook.

## 4. The resume — CONFIRMED

It is the travel handler's own arrival branch. The sites:

| state | test | resume site |
|---|---|---|
| 6 | `0x004D5B20(body) == 0x10000`, path finished (`0x00470DE1`) | `0x00470DF5`–`0x00470E07` |
| 7 | path finished **and** the room test of §5 (`0x00471573`) | `0x004715D8`–`0x00471606` |
| 8 | its own reach test, or 6 finishing | four sites, combat-approach-and-victory.md §4 |
| 88 | `now - StartedAt >= +0xE3` | `0x00480F78`–`0x00480F88` |

Each calls `0x0048ECE0` first, except 88, which has nothing to stop. `0x0048ECE0`
clears `creature+0x3E6` bits 17–18, the route's door bits (steering-behaviours.md
§7.6), and requests the stop `0x004D1070`. Bit 19 is left alone. Then
`SetState(+0xDF, 0)`, then `+0xDF = 1` **whether or not the transition was allowed**,
then that result is returned.

**While walking, the dispatcher** (`0x00476FC0`) runs the update hook of the category
of `+0xDF` **only when it equals the category of `+0x40`**; a zero return is
`SetState(1)`. A walk inside one activity (eating's 91 → 92, both category 8) keeps
that activity's hook; a walk between categories runs none.

## 5. What distinguishes 6, 7, 8 and 9

| | 6 `0x00470C80` | 7 `0x00471460` | 8 `0x00470E30` | 9 `0x00471430` |
|---|---|---|---|---|
| entered by | `0x00471890` only | `0x00471A70` only | `0x00471D10` only | **nothing found** (§7) |
| destination | point at `+0xE3/+0xE7` | room id `+0xE3`, byte `+0xE5`, point `+0xE6/+0xEA` | target tag, range, refresh countdown | as 6 |
| arrival | path finished | path finished, then the tile's room must match `+0xE3` (word) and `+0xE5` (`room+0x18`) | reach + sight, or 6 | 6's |
| on a room mismatch | — | return 0 → idle; `+0xDF` **not** reset | — | — |
| extra per tick | creature type 46 (Jack) loses 1 health (`0x004B2CE0(-1, owner, 0)`); holds, returning 1, while the current action's record `+20` byte is 4 | zeroes the body's velocity at arrival (`0x004D59A0(x, y, 0, 0)`) | combat, rerouting every 10 | `+0xEF` countdown; at zero stop and return 0 |

**6 and 7 are not interchangeable:** 7 validates the room at arrival, so a room sold,
retyped or captured during the walk sends the creature idle. The stale `+0xDF` then
makes the transition rule judge the drop to 1 by where it was going.

**8 and 9 use 6 as a subroutine.** State 9 tail-jumps into `0x00470C80`, so 6's arrival
resumes it. State 9 is 6 with a timeout.

**Both movers share a near-destination flag.** Inside `0x200` squared (≈0.35 tile) they
clear `creature+0xF1` bit 0 (6 also sets `+0x3DB = 1`); outside they set it. State 7
computes this from dwords at `+0xE3`/`+0xE7`, which under its own layout hold the room
id, `+0xE5` and bytes of the point, so its flag is computed from a meaningless value.
The arithmetic is CONFIRMED; what the flag drives is UNKNOWN (Q416).

**Who may interrupt a walk** (dispatcher `0x00476FC0`, needs routine `0x00477C20`):
every category decision looks **through** the walk, and every group decision does
not.

- The job and mood chooser is skipped when the category of `+0x32D` (the resume state)
  is 22, so a walk toward a fight is never re-decided. The "group 7, a need" exemption
  reads the travel state's own group, **9**, so a walk toward a need can be.
- The needs routine returns early for group 7, again the travel state's own group, so
  it runs mid-walk. Its hunger branch then tests the category mask and `!= 8` on the
  `+0xDF`-resolved category, so a creature walking to a hatchery is **not** re-asked.
  The sleep and pay branches were not read here.

## 6. State 88, "waiting" — CONFIRMED (body); entry UNKNOWN

`0x00480F50`: if busy, return 1. Otherwise, if `now - StartedAt < +0xE3`, start action
**43** (`0x004969D0(43, 1, 0, 0, 0)`) and return 1. Once the time is up,
`SetState(+0xDF)`, `+0xDF = 1`, and return the result.

It is **not a travel state**: no route, no body, no stop. It is a **timed park with a
resume slot**. Category 2 buys it three things: the entry save of `+0x40`, invisibility
to the category hooks, and being judged by its destination in the transition rule.
Action 43 is also what job 16 starts on a creature that cannot reach a portal (Q413).

## 7. Nothing enters states 9 or 88 — STRONGLY INFERRED (Q415)

- **Direct `SetState`:** 329 sites; a raw E8/E9 scan of `.text` and `cseg` agrees on the
  count, and no absolute occurrence exists. Literal 6, 7 and 8 appear once each, inside
  their helpers. **9 and `0x58` never appear.** The 16 register sites resolve to `+0xDF`
  resumes, the current state (`0x00481610`), 1, or room-table values.
- **The wrappers** `0x0048AD30(state)` (145 sites) and `0x00491830(state)` (4): no 9 or
  88. Their register arguments trace to 1, `0xD6`/`0xEB`, or room `+0x19`. The
  tunnelling entries of §2.3 enter 186 only.
- **`+0xDF` stores** (§2.4): none holds 9 or 88. The helpers' literal resume arguments
  include neither.
- **The room handler table** `+0x19`/`+0x1D`, which reaches `SetState` through
  `0x0046EAB0` and `0x004E8DA0`: no 9 or 88.

Not covered: code reached only through an unscanned computed call, and a state id
restored from a save.

## 8. Worked example: a hungry creature eats — CONFIRMED

1. The needs routine's hunger branch calls `0x004633D0`, which enters **91**
   (eating.md §3).
2. **State 91** (`0x004634B0`): `0x004DED00` finds a hatchery for the creature's owner,
   then `0x00471A70(room, 92, 0x80)`. It returns the helper's result, so no hatchery or
   no route means idle.
3. **In the helper:** already on a tile of that room means `SetState(1)`,
   `SetState(92)`, done. Otherwise a point in the room, route word `0x80`, then
   `SetState(7)`: 91 is category 8, so `+0x40` = 91 and no hooks run. Then `+0xDF` = 92
   and the room layout of §2.2.
4. **State 7**, every tick until the path is finished and the tile is still that
   room: stop, `SetState(92)`, `+0xDF = 1`. The hooks compare category(`+0x40` = 91) =
   8 with 92's 8, so none run. During the walk category 8's update hook would run, but
   category 8 has none.
5. **State 92** (`0x00463500`): on its first tick (`Age == 1`) it stops the body. The
   tile must be a type-4 room of the creature's owner (`0x004539B0(4, owner, 0)`). It
   picks the nearest chicken (eating.md §6), then `0x00471890(&chicken, 93, 0xE8)`:
   `SetState(6)` saves `+0x40` = 92, and `+0xDF` = 93. It then writes the eat byte
   `+0xAB` = 0 and the chicken's tag to `+0xAF`, calls `0x0049CE70(1)` and
   `0x0049EF60(8)` on the chicken, and sets the chicken's `+0x48/+0x4C` to `0x1000`
   and its `+0x58/+0x5C` to 0. With no chicken it calls `SetState(91)`, and the
   hatchery is looked up again next tick.
6. **State 6** until the path is finished: stop, `SetState(93)`, `+0xDF = 1`.
7. **State 93** eats (eating.md §7) and stores `HungerNeed != 0` in `+0xAB`. **State
   94** (`0x00463720`) returns 1 while busy; then, if `+0xAB` is set, `SetState(92)`
   for another bird, else it returns 0.

**What a build copies:** the caller's whole contribution is one call with *(target,
resume, options)*. The helper owns the transition, the slot and the destination; the
mover owns the resume.

## 9. What the thirteen travelling activities need

They can be retired onto the pair, and nothing about the protocol is left unread.
Each of `MovingToJob`, `GoingToSleep`, `SeekingLair`, `SeekingFood`,
`FetchingChicken`, `SeekingWages`, `GoingToJob`, `FetchingGold`, `FetchingBody`,
`FetchingObject`, `DeliveringGold`, `CarryingBody` and `CarryingObject` becomes:
*state 6 (a point) or state 7 (a room), `+0xDF` = the state the original's caller
passes, `+0x40` = the caller's state*. Per activity, the resume id is the helper
argument at the original's call site. For instance, the lair states call the room
helper with 80 and 81 (`0x0046F422`, `0x0046F4B0`) and the position helper with 80
and 82. All 134 helper sites, and the 45 sites of the Imp's route-or-teleport wrapper
`0x004651A0` through which every imp walk goes, are tabulated in
`travel-state-call-sites.md`, with the resume state for each activity (§4 there).

For the transition gate on master (Q414's note on ADR-0174): in DK2 the slot is
**never unset while a creature is in a travel state**. It is 0 before the first walk
and 1 after every resume. **Outside a travel state it can be stale** (§10): only an
arrival resets it, so a walk that ends any other way leaves its resume behind. That is
harmless, because the substitution applies only in category 2, which is also where
`CreatureStateTransitions.Classify` applies it.

## 10. Checked against a live game — OBSERVED (2026-09-24)

Two world-log samples (`debug-dumpers.md`, "Running one"): belgianguy's, 15 creatures
at GT 231, and the owner's, **four dumps of the skirmish map Hopping** (GT 1234, 1772,
2522, 3177; 76 to 91 creatures, 336 creature records), kept as
`samples/worldlog-owner-4dumps.log.gz`. `python tools/worldlog.py <file>` prints
every table below. All 23 states seen, and every resume and saved state, are in the
table.

**The two helpers hold on every record.** All 92 creatures in state 6 carry
`MoveTo (x, y)` and all 23 in state 7 carry `RoomId N`, so §2.1's point and §2.2's room
are written together with the state.

**Every walk seen is a row of `travel-state-call-sites.md`**, with the saved state
matching the caller:

| errand | saved `+0x40` | walk | resume `+0xDF` | later seen (saved) |
|---|---|---|---|---|
| eating | 91 | 7 | 92 | 94 (92) |
| to bed | 56, 108, 112, 1 (the interrupted job) | 7 | 81 | 83 (81) |
| training | 89 / 108 | 7 / 6 | 108 / 111 | 112 (108) |
| research | 89 / 53 | 7 / 6 | 53 / 54 | 56, 62 (53) |
| wages | 187 | 6 | 188 | 89 (187) |
| exploring, job 24 | 89 | 6 | 225 | |
| imp dig, mine, claim, reinforce | 10 | 6 | 13, 25, 15, 21 | 14, 26, 16, 22 (10) |
| imp gold to the Treasury | 26 | 6 | 28 | 28 (26) |
| wandering (heroes) | 87 | 6 | 1 | |

**The slot outside a travel state:**

- **0** only on hero and neutral creatures, and no creature goes from 1 back to 0: 0
  means "never walked", as §2.4's constructor predicts.
- **Stale after an interrupted walk.** Three imps in 12 idle still hold 13 or 21, each
  entered 12 within 20 ticks of the dump; three creatures in 257 stunned hold 188 or 81.
  §2.4's writer list has no reset outside an arrival, so this is what the protocol
  implies. Only a comparison of a dump with our state needs it: compare the slot
  only in category 2, or model the staleness.
- **Captives hold 116 in state 116** (4 prisoners in all 4 dumps, saved 115,
  re-entering 116 between every pair of dumps). **Explained** (Q422): 115 starts the walk
  (`0x00473331`) and then sets 116 itself, so the walk's resume stays written and the
  creature walks on in 116, with a target and the walk animation in the dumps. Hopping
  places the four (creature 8, flags `0x49`): player 2, held by player 1, health 3125
  in every dump.

The dumps are 500–750 ticks apart, longer than any walk, so they show where creatures
are, not the order they move through states. A tighter series would show that.

## Implementation and tests

Nothing built; research only. The retirement is its own commit with its own hash bump,
after the transition gate.
