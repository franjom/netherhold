# A portal's arrival interval

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 at `0x004DBBE0` and `FUN_004DBF90` (read 2026-09-07 and again 2026-10-01, ADR-0046), `0x004DC9A0`, `0x004DC8F0`, the player action arm `0x004C892D`, the death bookkeeping `0x00487C70` (2026-10-04); three hand-timed runs on the original (owner, 2026-09-07); three seeded Level 2 captures (`level2-live2-seeds`, 2026-10-01); shipped per-level `VARIABLES` and Level 2's triggers |
| Observed behaviour | Arrivals from one portal get further apart as it produces, and one step closer again each time a creature it produced dies. A newly claimed portal runs at full speed however worn out its neighbour is. A due roll that produces nothing still restarts the wait |
| Confidence | **CONFIRMED** — formula, per-portal clock, per-portal tally, the reset, the restamp at every due roll and the `PortalsOpen` gate. The one-off residual in run 1 is unexplained |
| Implementation | `PortalRules.SpawnIntervalTicks`, `PortalRules.OnOwnerChanged`, `PortalRules.PortalsOpen`, `PlayerState.PortalsClosed`, `TileState.EntranceCreaturesGenerated`, `TileState.EntranceIdleTicks`; ADR-0086, ADR-0314 |
| Tests | `PortalTests`, `LevelVariablesOnRealDataTests` |
| Open questions | none on the clock; Q86, Q97, Q505 and Q508 (the decrement on death) are closed; whether leaving the dungeon also decrements is unread. Q90's weighting is in `portals.md`; Q507 (the boost's decay step) |

The mechanic and the other four gates: `portals.md`.

---

## The formula

The portal's per-tick routine begins at `0x004DBBE0`. The arithmetic that
decides when the next creature is due is six instructions:

```
n        = room[0x4D]                            -- a per-ROOM counter
base     = EntranceGenerationSpeedSeconds        -- 25 on 61 levels, 18/20/22/30/10000 on five
dueTicks = (n * n + base) * variable19 + room[0x49]
if (now >= dueTicks) <generate>
```

**The interval is `(n² + base)` game seconds** — an *addition* of n² seconds,
not a multiple of the base. `room[0x4D]` is incremented once per creature
generated (`FUN_004DBF90`, `INC dword ptr [EDI + 0x4D]` at `0x004DC5E3`);
`room[0x49]` holds the tick of the last **due roll**, whether or not that roll
produced anything (corrected 2026-10-01; this line used to say the stamp moves
only at a generation — see *A due roll that creates nothing*).

| n | level 1, base 18 | base 25 |
|---|---|---|
| 0 | 18 s | 25 s |
| 1 | 19 s | 26 s |
| 2 | 22 s | 29 s |
| 3 | 27 s | 34 s |
| 4 | 34 s | 41 s |
| 5 | 43 s | 50 s |
| 10 | 118 s | 125 s |
| 15 | 243 s | 250 s |

`n * n` is formed **in seconds** before the conversion to ticks, as the original
does; squaring a tick count would be wrong by a factor of `variable19`.

## Three timed runs, and what each one settles

Runs 1 and 2 are consecutive arrivals from one claimed portal on a base-25
level (level 1 is the only shipped level that overrides the base, so the first
gap of 25 s gives the level away). Run 3 is a **second** portal, claimed after
the first had produced five creatures. Owner's caveat: *"not completely precise,
but could maybe be 99% close enough"*.

| gap | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|
| run 1 observed | 25 | 26 | 30 | 40 | 41 | 51 | 59 |
| run 2 observed | 25 | 26 | 32 | 33 | 42 | | |
| `n² + 25` | 25 | 26 | 29 | 34 | 41 | 50 | 61 |
| `max(25, 25n/2)`, refuted | 25 | 25 | 25 | 37 | 50 | 62 | 75 |

Mean absolute error per gap: **1.4 s** (run 1) and **1.0 s** (run 2) for the
binary formula, against 6.4 s and 4.0 s for the refuted one, which also
*diverges* — `base × n / 2` grows by 12.5 s per creature where `n²` grows by
`2n + 1`. Two gaps discriminate on their own: at n = 1 and n = 2 the old formula
predicts **no change at all**, both floored at the base, and the owner measured
26 then 30.

| run 3 (second portal) | 1 | 2 | 3 | 4 | MAE |
|---|---|---|---|---|---|
| observed | 27 | 30 | 31 | 33 | |
| per-room tally | 25 | 26 | 29 | 34 | **2.3 s** |
| keeper-wide tally | 50 | 61 | 74 | 89 | 38.3 s |
| refuted formula | 62 | 75 | 87 | 100 | 50.8 s |

**So the tally is per room, not per keeper.** Half a minute against a minute and
a half: the split is not subtle.

**The clock is per portal too, and observed rather than inferred.** In run 1 the
second portal produced 8 seconds after portal 1 did, at a point where the
required interval was ~60 s. One shared clock cannot emit twice in 8 seconds
while it is waiting 60, whatever formula sits on top of it.

**The fit also pins the unit: one game second is one real second** at normal
speed. `variable19` and `OriginalTicksPerGameSecond` had only ever been assumed
to compose into that, and an error of 0.8 or 1.25 would have grown visibly
across a series running out to 59 s (ADR-0089).

### The one residual, and it has a shape

Run 1's fourth gap is 40 s where the formula says 34, and **every gap after it
is correct again** (41/41, 51/50, 59/61). The cumulative residual steps once,
from +1 s to +7 s, then stays flat. Noise would wander. This is the signature of
a **one-off shift of about six seconds** that moved every later arrival without
corrupting a single later gap. This section used to explain it as a banked
wait: the portal came due, nothing was eligible, and the stamp moved only at the
eventual generation. **That explanation is refuted** (2026-10-01): a roll that
creates nothing restamps at once, so a blocked roll costs a whole interval
(26 s at n = 1 here), not six seconds. The residual is unexplained; one
mistimed reading, which the owner's caveat allows, is what is left. Named in
the test data rather than absorbed by a wider tolerance, because a tolerance
wide enough to admit it would also admit the refuted formula on three of six
gaps.

## A due roll that creates nothing (Q505)

Read 2026-10-01 from the binary (pyghidra-mcp, ADR-0046) and checked against
three seeded Level 2 captures. Confidence is per claim.

**Every due roll restamps the clock. CONFIRMED (binary).** When
`now >= room[0x49] + (n² + base) × variable19`, `0x004DBBE0` calls the pick
`0x004DBF90` with 1 and then writes `room[0x49] = now` at `0x004DBC7C`. Nothing
tests what the pick returned. So the clock restarts at the due tick whether a
creature came or not, and a portal held up by a missing room, a full dungeon or
a closed gate waits for its next roll. It does **not** produce the moment the
obstacle goes. The wait is never banked. Before the due test the routine checks
only that the room's owner is not `DAT_006F2548` and that the owner's
`+0x132` (`KilledByPlayerId`) is 0.

**The special-arrival branch is not the campaign's. CONFIRMED (binary).** First
the due path calls `0x004DCF50`. When it returns non-zero for a room of type 3,
the routine restamps and returns without the pick. `0x004DCF50` returns 0 at
once when the mode byte `0x0075AD4B` is 1 (single player, `win-and-defeat.md`).
In other modes it offers the first type in 0x20..0x2C whose player flag
`+0xFCE` is clear and which `0x004DD130` accepts, creates it at the Portal,
sets the flag, and returns 1 whether or not the create succeeded. Not built:
nothing here runs a non-campaign mode.

**The pick can draw and still create nothing. CONFIRMED (binary).** After the
draw, `0x004DBF90` calls the creator `0x004DC9A0` and increments `room[0x4D]`
(`0x004DC5E3`) only when it returns non-zero. It then decays every boost and
block whenever any type had weight, however the create went. The creator's
first test (`0x004DC9AB`–`0x004DC9B8`) is the owner's `+0x76` bit 10. When the
bit is clear it returns 0 before anything else. The player dumper prints that
bit as **`PortalsOpen`** (`0x004BDCEA`).

**Only the script writes `PortalsOpen`. CONFIRMED (binary).** Player action
type 37 maps to arm `0x004C892D` (index byte 9 at `0x004C8AB3`, pointer 9 of the
table at `0x004C8A60`; type 28 maps to `0x004C8851` in the same tables, which
`portal-creature-action.md` had read). A zero body byte 0 clears bit 10 and any
other value sets it. The other writer is the action-28 wrapper `0x004DC8F0`. It
sets the bit for its own call, so a scripted creature comes through a closed
portal, then clears it again if it was clear (`0x004DC983`–`0x004DC989`).
`0x004DC9A0` has exactly those two callers.

**Level 2 closes its portal for the scripted Warlock. CONFIRMED (shipped
triggers).** Condition 3 (*a Portal*) with child condition 4 (*a Library*) runs
action 37 with `Available=0` and starts timer 0. At timer 0 ≥ 10 s it runs action
28 (Warlock, level 1) and action 31 (path 60 to action point 4). At timer 0 ≥ 30 s
it runs action 37 with `Available=1`. The shipped corpus holds 22 type-37
records, 10 closing and 12 opening.

**The captures show exactly that. CONFIRMED (observation).** Player 3's `+0x76`
reads `0x…8500` from tick 4: bit 10 is set, the portals start open.

| seed | Library | claim (stamp) | bit 10 cleared | tally 0→1 (scripted) | bit 10 set | roll, nothing | first arrival |
|---|---|---|---|---|---|---|---|
| 04 | 688 | 883 | 884 | 924 | 1004 | 967 | 1051 |
| 03 | 688 | 967 | 968 | 1008 | 1088 | 1051 | 1135 |
| 02 | none | 686 | never | — | — | — | 766 |

A capture frame's tick is one later than the turn that wrote it, which puts the
close on the claim turn itself. The Warlock comes 40 ticks (10 s) later, and
the portal reopens 120 ticks (30 s) after the close. Each skipped roll lands
inside its seed's closed window, at stamp + (1² + 20) × 4 = 84 ticks, because
the scripted Warlock already raised `n` to 1. The next roll, 84 ticks later,
finds the portal open and produces (tally 1→2). Seed 02 never builds a Library,
so the chain never fires, the bit never clears, and the portal produces at its
first roll (n = 0, 80 ticks) and at every roll after.

**The closed roll still drew. STRONGLY INFERRED (capture).** The Warlock's
entrance row first shows a boost at the closed roll (967 in seed 04), and that
boost falls at each later roll (8.0, 6.0, 4.0 at 967, 1051, 1147). The decay
loop runs only when the pick found a weighted type, so at 967 the pick had a
candidate and the creator refused it. It was not an empty pick. The step is
2.0 per roll, where `PortalChoiceRules` decays 1.0. That is Q507, and it was
left alone here.

**Ours, before ADR-0314.** Action 37 was reported partial and did nothing, so
our portals never closed. Our clock also counted up from the last *arrival*, so
a roll that found nothing banked its wait. Either one alone produces the early
Warlock. Built now as `PlayerState.PortalsClosed` (DK2's bit, inverted so a new
player starts open), action 37, the restamp at every roll, and the creator's
refusal after the draw.

## The tally falls when one of the portal's creatures dies (Q508)

The portal's own unit only ever raises `room[0x4D]`: two increments on the
generate path (`0x004DC5E3`, `0x004DC94F`) and two stores of zero (construction
`0x004DBBA1`, ownership change `0x004DBBD1`). The decrement is somewhere else, in
the death bookkeeping `0x00487C70` (binary, 2026-10-04). An earlier scan here
looked only for a `DEC` or `SUB` on memory, so it missed it: the routine loads
the dword into a register, decrements the register and stores it back.

- **The arrival is stamped with its portal.** `Portal_CreateArrival`
  (`0x004DC9A0`) writes the portal room's tag (`room+0x04`) into the creature's
  word `+0x4EB` on both arms: `0x004DCC40` for a keeper portal, `0x004DCC99` for a
  hero portal. `Creature_Construct` zeroes it (`0x004878EC`). CONFIRMED.
- **A death gives the slot back.** At `0x00488150`–`0x00488170`,
  `Creature_DeathBookkeeping` reads `+0x4EB`. When it is non-zero and that
  thing's dword `+0x4D` is above zero, it decrements the dword. The tally never
  goes below zero. Nothing checks that the thing is still a portal. CONFIRMED.
- **A converted creature keeps its stamp.** The text scans for writers of
  `+0x4EB` found only the three writes above; the other hits are on the computer
  player's struct. A creature that changes owner would therefore still lower its
  old portal's tally when it dies. STRONGLY INFERRED: a struct copy or a save
  load would not show up in those scans.

So `n` counts the portal's arrivals that are still alive, plus any that left by a
route other than death. That fits the Level 3 seed 01 capture: the tally went
from 6 to 5 on the tick the portal-born Firefly reached health 0, and the next
roll came 188 ticks later instead of 232. A portal does wear out while its
creatures live, and recovers one step for each of them that dies. Whether
leaving the dungeon, or being dropped back into the portal, goes through the
same bookkeeping is unread. Built as ADR-0358 (`PortalRules.ArrivalDied`).

## The reset is an ownership change, and nothing else

> **Owner, 2026-09-07:** *"if an enemy keeper takes one of the portals from me,
> and if they have the room to attract more creatures, then yes, it resets to
> zero for that portal and that keeper."*

Both zero-stores are handler-table slots for room type 3 — `FUN_004DBB90` at
slot `+0x01` (the room constructor) and `FUN_004DBBC0` at slot `+0x0d` (the room
ownership change), and both also stamp `room[0x49] = now` (the constructor
additionally sets `room[0xA3] = 1`; re-read 2026-09-30, as ADR-0086 amendment 1
says). The table:
`portals-room-handler-table.md`.

`FUN_004E5650`, the ownership change, in order: decrement a counter on the old
owner; re-link the room from one player's room list to the other's; write the
new owner into `room[0x1d]`; increment the same counter on the new owner;
re-skin it; walk its tiles updating the map; eject every object in it; **fire
the type's `+0x0d` handler** — the reset; credit the new owner with a room count
(excluding three room types) and with a value proportional to tiles; send **two
different notifications**, one to the old owner and one to the new, choosing
between two message ids by comparing against what is almost certainly the local
player.

So a rival taking a worn portal gets it at the base interval, and so does the
first keeper to claim a neutral one. `PortalRules.OnOwnerChanged` zeroes the
tally **and** the clock; without the clock a rival could inherit a portal
already most of the way to its next creature.

**Method trap.** This was recorded for a day as *"reached only through a vtable"*
on the grounds that neither zero-store had a direct caller or an absolute
reference in `.text`. The premise was true and the conclusion did not follow:
the scan covered `.text` alone, and a handler table lives in `.data`. A scan
over every initialised block finds both addresses at once. The silence of a scan
that could not have found the thing was read as evidence about the thing.

## Portal buildup (built, ADR-0467)

CONFIRMED by whole-function reads, refreshed 2026-10-23: `FUN_004DBBE0`
tests equality with the next due turn minus three game seconds (12 ticks).
It calls `FUN_004DBF90` with argument zero and, when its candidate count
and the room definition's runtime ushort `+0x425` are nonzero, requests
that effect at the portal centre, Z zero, heading zero, owned by the keeper.
The native request also requires its graphics level to exceed two; the modern
adapter uses its existing effect rendering path rather than that legacy gate.

The zero-argument preview skips noticing, random selection, modifier decay
and creation. Admission and positive base weight determine its count; a sack
block reducing final probability to zero does not exclude that candidate.
It does not check PortalsOpen. An eligible closed portal can therefore show
buildup and later refuse creation; the preview reserves neither type nor slot.

There is no separate periodic mist timer or once-per-cycle latch. With an
unchanged tally, it occurs once per ordinary interval (see [the formula](#the-formula)),
three seconds before the roll: initially at 22 seconds on a base-25 level.
Changing the tally can move or skip that exact equality. Neutral portals and
defeated owners return before this path; capacity and admission gates also apply.

**Mist does not promise an arrival.** `FUN_004DBF90` rebuilds its candidates
at the roll. A full sacking block can leave preview candidates but zero total
selection weight even without a change during the three seconds. Admission,
pool or capacity can also change between preview and roll. `FUN_004DC9A0`
then refuses a closed portal or a failed creature allocation. Every due roll
restarts the wait even if it creates nothing; see
[Q505](#a-due-roll-that-creates-nothing-q505). There is no separate random
chance to cancel a positive-weight, successfully admitted arrival.

`FUN_004EACC0` copies file ushort `0x386`, via intermediate `0x45A`, to
runtime `0x425`. Shipped Portal sets it to effect 94, "Portal Build up Effect":
generation type 4, flags `0x280`, HP 30, six elements per turn, ids 234/235,
origin range 0–64 and height 50–50. The shared effect consumer emits twelve
sprites per update for thirty updates, retaining authored motion and resources.
`PortalSystem` requests it without changing the ordinary roll or gameplay RNG.

### Mist appearance and duration (2026-10-23)

The supplied red-cloud sequence is STRONGLY INFERRED to show effect94.
The original isolated probe checked particles but missed the weak/occluded
rendering reported by the owner. The corrected blend and depth ordering and
focused GPU regression are in [sprite rendering](room-construction-effects.md#sprite-blend-and-shared-terrain-ordering-2026-10-23).

CONFIRMED shipped definitions and `FUN_00435F20`'s colour selection:

| Element | Resource | RGB | File art flags | Lifetime updates |
|---|---|---|---|---|
| 234 | mist | 220,70,40 | 0x4 | 32–48 |
| 235 | sparkle-02g | 255,10,10 | 0 | 16–24 |

Neither selects owner-palette bit0x2. The parent light is RGB200,100,100
with flags0, also without owner-colour selection. These definitions provide
fixed red/orange colours, not a keeper-colour transition. After rechecking DK2,
the owner withdrew the keeper-colour report; Q619 is closed without a recolour.

At the nominal 50ms effect step, thirty emission updates take1.5s; the last
mist can live another2.4s (about3.9s total). Both sprites dim their RGB; mist
grows while sparkles shrink. The diagnostic samples particles at3001ms and
none at4001ms. Native frame pacing is not measured by that probe.
