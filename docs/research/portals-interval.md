# A portal's arrival interval

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 at `0x004DBBE0` and `FUN_004DBF90` (read 2026-09-07, ADR-0046); three hand-timed runs on the original (owner, 2026-09-07); shipped per-level `VARIABLES` |
| Observed behaviour | Arrivals from one portal get further apart, permanently. A newly claimed portal runs at full speed however worn out its neighbour is. Nothing a keeper can do speeds a worn portal up except losing it and taking it back |
| Confidence | **CONFIRMED** — formula, per-portal clock, per-portal tally and the reset. The one-off residual in run 1 is HYPOTHESIS |
| Implementation | `PortalRules.SpawnIntervalTicks`, `PortalRules.OnOwnerChanged`, `TileState.EntranceCreaturesGenerated`, `TileState.EntranceIdleTicks`; ADR-0086 |
| Tests | `PortalTests`, `LevelVariablesOnRealDataTests` |
| Open questions | none; Q86 and Q97 are closed. Q90's weighting is in `portals.md` |

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
`room[0x49]` holds the tick of the last generation, stamped at the *actual*
generation rather than when the portal came due.

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
a **one-off block of about six seconds** — the portal came due, no creature was
eligible, it waited, and `room[0x49]` was stamped at the actual generation, so
the delay shifted every later arrival without corrupting a single later gap.
That is exactly what `portals.md` §4's banked wait predicts. **HYPOTHESIS**; the
alternative is one mistimed reading, which the owner's caveat allows. Named in
the test data rather than absorbed by a wider tolerance, because a tolerance
wide enough to admit it would also admit the refuted formula on three of six
gaps.

## The tally never falls

**Nothing in the executable decrements `room[0x4D]`.** There are **129**
references to a `+0x4D` field across every structure in the binary and **not one
is a `DEC` or a `SUB`**. The portal's own unit holds exactly two increments
(both on the generate path), two stores of literal zero, and two reads. So the
counter only climbs or is zeroed wholesale, and three things follow:

1. **A portal does not speed up when the population falls.** Killing a creature
   and throwing one back into the portal are exactly equivalent: both remove a
   creature, neither can lower `n`.
2. **A portal wears out.** On a base-25 level the fifteenth creature takes 250 s
   where the refuted formula said 187.
3. The refuted formula counted *living* non-workers, so it accelerated on every
   death where the original does not.

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

## One smaller thing, read on the way past

Three game seconds before the creature is due, the same routine computes a
second threshold — `EDI -= variable19 * 3` at `0x004DBC9E`. The shape of a
pre-warning: the animation or sound that plays before a creature appears.
Unbuilt.
