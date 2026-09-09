# Efficiency — the work rate is a creature stat, and DK2 shows it

| | |
|---|---|
| Source(s) | Owner observations 2026-09-05 and 2026-09-06 (status bar and gold counter); `DKII.EXE` v1.70 — `FUN_004974c0` (imp claim handler), the reinforce handler, `FUN_00488930` (the bonus builder), `FUN_0048b120` (level-up), the slap handler; `GlobalVariables.kwd` |
| Confidence | **CONFIRMED**: 80% base, the three measured swing values, the two ten-point bonuses, the slap's 25 seconds, no level effect. **UNKNOWN**: what moved the base rate mid-session; what the third bonus term counts |
| Implementation | `EfficiencyRules` (`BasePercent` 80, `BonusPercent` 10, `SlapBonusSeconds` 25, `Percent`, `Scale`); `CreatureState.WorkFasterTicks`, `CreatureState.DroppedTask`; consumed by `TileWorkRules.Apply` |
| Tests | `TileWorkTests` (the 80 / 90 / 100 percent swing values); `HandTests` and `ResearchTests` exercise `EfficiencyRules` from the drop and research sides |
| Open questions | Q159 closed; Q6 answered (ADR-0062). The mid-session base-rate shift below has no question row |

Split out of `tile-work.md`, which keeps the swing arithmetic that consumes it.

**CONFIRMED by the owner, 2026-09-05, off the game's own status bar:**
*"Imp. Name: Snipe. Job: idle. **Efficiency: 80%.** Mood: Happy."*

It is `creature[0x3af] + creature[0x3b3]` in 4096 fixed point, read out of the
imp's claim handler `FUN_004974c0` and, in the identical shape, its reinforce
handler: `work = <tabled delta> × (creature[0x3af] + creature[0x3b3])`, with
`FUN_004363f0` = `(a * b) >> 12` and `FUN_0043d750` = `a + b`. Both fields are
pinned at creation (Q6, ADR-0062):

| field | at spawn | meaning |
|---|---|---|
| `0x3af` | `0xccc` = 0.7998 | the **base work rate**, printed as **80%** |
| `0x3b3` | **0** (the zero register, in the block that also zeroes `0x3b7`) | a **temporary bonus** |

The displayed percentages are exact where the fixed-point constants are
approximations, so `EfficiencyRules` keeps whole percents and reproduces the
numbers rather than the arithmetic (ADR-0002). **The per-application health is
rounded to a whole number, halves away from zero**, and that rounding is itself
the measurement:

| Efficiency | health a swing | gold a swing | observed |
|---|---|---|---|
| **80%** | 300 × 0.8 / 4 = 60 exact | 272.7 | **272–273** |
| **90%** | 67.5 → **68** | 309.1 | **309–310** |
| **100%** | 75 exact | 340.9 | **341** |

**The name is DK2's own.** The balance table carries four percentage modifiers
of a thing it calls the work rate — `IncreasedWorkRatePercentageFromSlapping`
(50), `IncreasedWorkRateDurationFromSlappingSeconds` (60),
`IncreasedWorkRatePercentageWhenCasinoBigWinOccurs` (35),
`IncreasedWorkRatePercentageOfPlayersTorturedCreatureTypes` (25). `0x3b3` is
where the first three would land. **The torture one does not** (Q81, binary
2026-09-07): its cached global has one live reader, the keeper-side routine that
credits research. Once a game second that path walks the keeper's
tortured-creature-type list and, on finding the researcher's type, adds the bonus
straight to research progress — and adds variable 19 to the nominal 25 before
taking the percentage, giving a **29% net research increase** before truncation,
not 25%. Shared naming had grouped unlike mechanisms.

### What moves it

Two bonuses of **ten points** each, stacking to exactly 100%. DK2 adds
`0x199` = 409 = 0.0999 per bonus to `0x3b3` from two independent flags
(`FUN_00488930`), plus an unimplemented third term of `205` = **0.05** per unit
of a byte counter on another object the creature points at, most likely the
torture tally. Also unimplemented: DK2's non-worker branch, which reads
`DroppedToDoJobId` for a creature without `IsWorker`.

- **A slap: +10, and it does not stack** — repeated slapping leaves it at 90%.
  **It lasts 25 game seconds, CONFIRMED (owner, 2026-09-06):** *"timeout for
  efficiency to return back to 80% after slap is 25s, not 60s."* The slap
  handler writes a hardcoded **100** into a field its own debug dump calls
  `WorkFasterTimer` and decrements it once a tick, which at variable 19's four
  ticks a game second is exactly the table's `SlapWorkFasterDurationSeconds` of
  25 — a third independent confirmation that variable 19 is ticks per game
  second, and it closes Q159 against a hand-timed *"after a minute"*.
  Not `IncreasedWorkRateDurationFromSlappingSeconds` (60), which is read by the
  keeper's **research** adder and only there (ADR-0090,
  `library-and-research.md` section 4): one slap does two unrelated things,
  Efficiency +10 for 25 s and research +200% for 60 s, both stamped from the
  same handler's `LastSlapTick`. The *"after a minute"* was a real minute-long
  effect, just not this one.
- **Still doing the job it was dropped onto: +10.** CONFIRMED (owner): *"after I
  mark a tile for excavation and drop an imp adjacent to it its efficiency jumps
  to 90%. But if I drop it on a random tile where there is nothing marked by me
  its efficiency remains at 80%."* It is the **job**, not the drop
  (`hand-of-evil.md` section 6, ADR-0069): the field read is
  **`ImpJobData.DroppedTask`**, the *kind* of work the drop found and zero when
  it found none — `JustDropped` is a different field four bytes earlier and a
  one-shot ticket the next evaluation spends; **the look is three tiles, not
  one** (Q162, counted on screen: three boost, the fourth does not); and the
  bonus survives the walk, the digging and finishing the tile, cleared in
  exactly one place in the binary — a later evaluation where the worker is still
  on that kind and no more of that kind exists anywhere on the map. So marking
  work and placing an imp on it is worth **12.5% more work** than letting the
  imp find it, for as long as that kind of work lasts.
- **Not the creature's level** — below.

**Read it off the status bar**: select the creature and the percentage prints
with its name, job and mood (`observation-checklist.md`). Gold decrements are
the cross-check, and four consecutive ones off a seam pinned both the
health-to-gold proportion and the truncation. **Where the per-swing gold number
appears on screen is still unrecorded**, and every claim here rests on that
readout.

### The slap confirms the whole model, and quantises the bonus

A slapped imp mines **341** gold a swing where an unslapped one mines **273**
(owner, 2026-09-05, fresh game, no levelling confound). 341/273 is **1.25
exactly** = 1.0/0.8: the rate goes from the read 0.8 to exactly 1.0, and
300 × 1.0 / 4 = 75 health = 340.9 gold. The fixed-point multiplier read out of
the handlers is observable in the game to three significant figures. Slapped
reinforce times (11.9 s in one run, 11.5–11.6 s in another, against 13.3–13.4
unslapped) bracket the 11.75 s that +0.1 predicts; a 13-second job timed by hand
resolves ten times worse than the gold counter and settles nothing alone.

**The base rate moved mid-session and what moved it is UNKNOWN.** Four readings
in order: level-1 imp, fresh game, 273 baseline / 309–310 slapped; level-2 imp
same game, 273 / 341; level-3 imp later, **309–310** baseline; and a **freshly
created level-1 imp at that same moment**, also 309–310. A brand-new imp cannot
have levelled, so the base is not a property of the imp: something player-wide
moved during that session, by a factor of 1.1333, and every level comparison
taken across the gap is confounded. The slap is most likely +0.1 throughout.

Ruled out: the imp's level; the bonus routine's own census term (the counter at
`+0x1094` is decremented for creature type `0x10` — id 16, the **Wizard** — and
that dungeon had none); and a partial-swing display artefact, because the owner
read four consecutive decrements off the seam — **3000, 2690, 2381, 2072,
1763** — which is exactly 68 health a swing (68 × 3000/660 = 309.0909, truncated
cumulatively), matching to the last digit four times.

**The experiment that would name it**: from a fresh game read the seam
immediately (expect 3000 → 2727, 60 health), then change exactly one thing and
re-read the same seam — create an imp, claim or reinforce some tiles, let an imp
level, or slap and let it wear off. Whichever moves 2727 to 2690 is the cause.

### The work rate does NOT change with level

**CONFIRMED (owner, 2026-09-05): a level-3 imp and a freshly created level-1 imp
reinforce the same wall in 13.1 s and 13.3–13.4 s.** No level effect, and a
13-second job could not have hidden one. The level-up routine `FUN_0048b120`
adds a level-scaled stat to a field at `0x3af`, which predicted 7.5 to 11
seconds at the stat table's level-3 percentages; so that reading is wrong
somewhere — not the imp's path, or the stat is zero for imps, or the field is not
the one the work handlers read. **The measurement is the fact and the decompile
is the hypothesis.** An earlier pair of claim readings (level 1 at 1.30–1.40 s,
level 2 at 1.16–1.20 s) is not support: 0.17 s of hand timing on a job whose
work phase is 1.0 s. Recorded so nobody re-derives it from the same two numbers.

---

