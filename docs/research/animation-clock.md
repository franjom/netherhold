# The action animation clock

2026-10-19. Retail v1.70 through PyGhidra. Built for the dig and mining actions as
ADR-0514 (hash/save 282). It replaces the two provisional end-of-action models,
which reconstructed the phase from work entry ([mining-finish.md](mining-finish.md),
[imp-dig-stop-clock.md](imp-dig-stop-clock.md)).

## The record — CONFIRMED (read)

A creature's animation object sits at creature `+0x44`, so its fields appear at creature
`+0xD8` (phase), `+0xDC` (rate), `+0xF1` (flags) and `+0xF2` (art slot).

- **Phase:** in 1/4096 of the current segment. **Rate:** the advance per turn.
- **Flags:** bit 0 loop, 1 complete, 2 wrapped, 3 stop, 4 in Start, 5 in End.
- **Resource descriptor** (`+0x28`): FPS, Main frames, and Start and End frames. These are
  Creatures.kwd's art slot fields, loaded as `CreatureAnimationTiming`.
- **Durations:** a segment of n frames lasts `D = n*4096/fps`.
- **Rate:** `min(4095, 4096*4096/(D*4))`, from [0x004B41E0](functions/004B41E0.md). The
  divisor 4 is the tick-rate global `0x006F2590`; that it holds 4 is LIKELY, and it
  reproduces both Imp rates.
- **Carry:** `over*4096 / (Dnext*4096/Dprev)`, truncating at each division.
- **Imp art slot 5:** 15 Main frames, 8 End frames, 20 FPS and no Start segment. That gives a
  Main rate of 1365 and an End rate of 2560.

**Install** ([0x004B37D0](functions/004B37D0.md)) zeroes the phase and the rate. It keeps
only flag bits `0xC3`, then sets loop (and "in Start" if the clip has a Start segment), which
clears the wrapped and stop bits.

**Advance** ([0x004B3C10](functions/004B3C10.md)) runs once a turn:

1. A completed play pins the phase at 4096 and zeroes the rate.
2. Otherwise it adds the rate to the phase.
3. When the phase overflows 4096:
   - In End, the play completes.
   - In Start, the overflow carries into Main.
   - In a looping Main without the stop bit, it wraps and sets **wrapped**.
   - In a stopped Main, the overflow carries into End, or completes if the clip has no End.

**Keep or install** ([0x00496700](functions/00496700.md)): an action keeps the running clip
when the art slot matches and it has not finished a non-looping play. Otherwise it installs.

**The runner's test** ([0x00496AA0](functions/00496AA0.md)): a mode-3 action ends when the
handler is done and the animation is (rate 0, or wrapped or complete).

- **The handler is called only until it first returns done.** Creature `+0x3AD` bit 0
  records that, and the runner skips the call while it is set. Only an action's begin
  clears it.
- **The wrapped bit is sticky:** only an install, or the runner's own count-down for a
  counted action, clears it. So after a loop's first wrap, each action ends as soon as its
  handler is done.

This also gives the measured strike cadence:

1. The handler strikes on its first call and is done.
2. The first action then waits for the wrap, so the second strike comes four turns after
   the first.
3. From then on, each action ends on its strike and the next begins.

**Stop:**

- The dig handler ([0x00496E00](functions/00496E00.md)) and the mining handler
  ([0x004971D0](functions/004971D0.md)) test their tile on entry. If it is no longer a
  diggable wall or a workable seam, they set the stop bit and return busy until the
  complete bit appears.
- Otherwise they strike and test again. A tile emptied by their own strike takes the same
  stop path; otherwise they return done.
- A tile taken by another worker after this handler is done sets no stop bit. That action
  ends at the wrap.
- The action clears on the poll after the advance that completes it.
- With no action running, the chooser ([0x0048BE50](functions/0048BE50.md)) puts the
  standing or walking clip on, which clears every action flag.

**Rate sources:** the slot-5 rate comes from art data alone. The speed scalar scales only the
walk and run slots (1 and 2). Freeze skips the advance. Nothing found activates the
alternate rate at `+0xA4` (UNKNOWN).

## What is built

`ActionAnimationClock` keeps the slot, phase, rate and flags on `CreatureState`, hashed and
saved. `ImpTaskSystem` uses it for actions 1 and 2 (dig of all three kinds, and mining), in
states 14 and 26, for any creature whose slot 5 has timing data:

- **Start:** the action starts on the turn after the work state is entered, and the clip
  advances last in each worker's update.
- **Handler done:** a strike sets `ActionHandlerDone`. It clears when an action whose handler
  and clip are both done ends and the next begins.
- **Stop request:** a worker whose handler is still being called and whose tile is gone sets
  the stop bit. So does a worker whose own strike this turn emptied the tile.
  - A tile emptied by an earlier worker in this turn's update order is seen this turn.
  - A worker updated before the collapse sees it next turn.
- **Poll:** with no action running, the work state checks its task at once. With the stop bit
  set, it waits for complete. Otherwise it waits for the wrap.
- **Leaving:** leaving the work state clears the clip.

`WorkCadence` still times the strikes, and still times the end of every other kind's action.

## Evidence

The clock reproduces every date the provisional models were fitted to, through the stepping
state rather than a formula:

- three captured dig completions (entry 137, 150 and 158; collapse 165)
- five captured mining holds
- the miner's own last strike

The provisional models matched 1068 mining phase samples and predicted all 102 measured
stop-to-exit intervals in the Level 1–4 seed-1 captures. On an uninterrupted bout the stored
clock computes the same phase, because each wrap subtracts exactly 4096.
Tests: `ImpWorkAnimationTests`.

## Limits

- **Uncaptured paths:** these follow the binary's rules, not a capture, and their test names
  say so:
  - the job removed while the wall still stands
  - a wall taken after this worker struck
  - bouts interrupted and resumed
- **Untagged standing wall:** DK2's handler ignores the job and would strike once more. Ours
  does not strike work it no longer holds.
- **Tile test:** the handlers test terrain flags only (`0x00453D10` for a wall, `0x00453D80`
  for a seam). Ours uses `TryResolveDigTask` and `TileWorkRules.Applies`, which also apply
  owner rules. They could differ on some owned or enemy walls (unchecked).
- **Excluded workers:** held, imprisoned, down and preoccupied workers skip the step. A frozen
  worker keeps its clip unadvanced, as `0x0048BE50` does. Other exclusions change state, and
  the next step clears the clip.
- **Phase carry between states:** a direct dig-to-mine change with no turn between actions
  would keep the phase in DK2 (LIKELY). Ours reinstalls, because any turn outside a work
  state clears the clip.
- **Other actions** still use closed-form `ActionUntilTick` hold times, correct only from a
  fresh install: claim, reinforce, convert, melee, spells, training, eating and the victory
  pause. They can move onto this record.

## Idle actions

Actions 43 and 44 ([0x004950C0](functions/004950C0.md)'s records) have no handler and
play art slots 20 and 31. State 12 ([0x00464240](functions/00464240.md)) checks the art
is available, then begins them counted ([0x004969D0](functions/004969D0.md)): count 2
and the loop bit cleared, so the clip plays once.

The runner ends the action on the poll after the clip's rate drops to 0. That is one
turn after completion, so an action lasts k + 1 turns, where k advances reach 4096.

**Install coin (CONFIRMED, read):** when the art flags hit `0xFC00`,
[0x004B37D0](functions/004B37D0.md) draws `rand(2)` on the simulation stream. On a 1
with flag `0x400` it installs the definition's alternate clip (definition `+0x46E`) and
sets animation flag bit 6. That `+0x46E` is loaded from file `+0x1083` is LIKELY: the
block there is an art block (the Imp's IMP-Idle4), but the loader's copy was not read.

| Imp clip | Frames / fps | Rate | Turns |
|---|---|---|---|
| Slot 20 | 171 / 30 | 179 | 24 |
| Slot 31 | 172 / 30 | 178 | 25 |
| IMP-Idle4, alternate | 129 / 30 | 238 | 19 |

All 14 rests in the Level 4 capture's first 1100 turns have one of these three
lengths, matching the clip installed; 7 were swapped. Our coin comes from our own
stream, so the capture checks the lengths and the rate of swaps, not the draw's
position in DK2's stream.
Imp dig art (slot 5, flags `0x28011`) has `0x8000`, so every fresh dig or mining
install draws one number too, without swapping. Built in ADR-0515; tests:
`IdleActionClockTests`.

Not mirrored, and unused so far: `0x00496700` keeps a clip by resource and the
unresolved art byte, so it would reinstall (and draw again) after a swap, where our
`Begin` keeps it. The runner's count-down mirrors only a count of 2.

Other creatures' idle actions (`CreatureJobSystem`) still use a guessed 8 turns. Only
the Imp's idle slots carry `0x400`. Other actions that install art in DK2 draw their
coin too, but ours do not install a clip yet.
