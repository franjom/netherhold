# level20-idle-001 — a stored stamp, compared to a stored stamp

**Status:** run 2026-09-10 (owner). Capture `reverse-engineering/dk2runs/dk2run7.csv`,
1198 rows, ticks 36–1218 over 300.10 s. Result recorded below the predictions.

## Why this run exists

`level1-idle-001` produced the project's first readable two-sided comparison,
and left one residual: both flags landed **one tick later** in DK2 than in our
run, identically, 568 ticks apart (Q273). Two explanations survived.

1. We are one tick fast — the gate's arithmetic fires a tick early.
2. The probe reads one tick late — an artefact of sampling a live process.

Nothing in the level-1 capture separates them, because **every event there was
sampled**, and the one unsampled quantity — the timer stamp — was the anchor,
so it agreed by construction rather than by measurement.

Level 20 was chosen because its idle run stamps **two** timers, both early, and
then does nothing for a long time. A stamp is a *stored* value: the game writes
the tick into the timer bank and the probe reads that dword back whenever it
likes. Nothing about when the probe looked can change what it says. So Level 20
offers what level 1 could not — an absolute tick comparison with no sampling
anywhere in the path.

Reaching it at all required `docs/research/campaign-progress-registry.md`.

## Protocol

1. Close DK2. Start it and stop at the front end; load nothing.
2. Alt-tab out and start the probe, which waits for a level:
   ```
   python reverse-engineering/dk2watch.py --seconds 300 --csv D:\dk2run7.csv
   ```
3. Alt-tab back in, load **Level 20**, and touch nothing for five minutes.

Deviation from plan: `--delay 0` was intended and not passed, so the 8 s
countdown ran and sampling began at tick 36 rather than ~4. **It cost nothing
here** — the stamps were already stored by then, and no event was due for
another 1500 ticks. It would have mattered only if an event fell inside the
first 32 ticks.

## Predictions, recorded before the comparison was run

1. The capture shows **no changes at all**. Timer 1's threshold is 430 s = 1720
   ticks and timer 0's is 600 s = 2400; stamped at load, neither can fire
   inside a 1182-tick window. A run that shows changes falsifies our reading of
   the level.
2. Both timers are stamped at the **same** tick, because one root walk starts
   them.
3. Our run stamps them at the **same absolute tick** the game does. This is the
   one that pays for the experiment.
4. Nothing arrives through the portal. Every creature in Level 20's pool needs
   a room, and the owner built none.

## What happened

All four held.

| | ours | capture |
|---|---|---|
| timer 0 stamped | tick 4 | tick 4 |
| timer 1 stamped | tick 4 | tick 4 |
| flag 7 | set by tick 4 | set by tick 36 (censored: on the first row) |
| anything else, ticks 36–1218 | nothing | nothing |

**All 150 columns are identical at every tick both sides cover** — the first
fully identical window this project has had. `dk2tools capture-diff` reports
`event set matched`, and the identification is not circular: ids 12/16/31/56/
60/64/69 with authored 430/600/600/3/3/300/400 appear in the live array scaled
×4, which is Level20Triggers.kld and no other level.

**Q273 is now half-answered.** Absolute tick numbering agrees exactly, with no
sampling in the path, so the two clocks share an origin and a rate. Whatever
produces the level-1 residual is *not* a systematic offset in our numbering.

It is worth being precise about what this does not show: no event fired inside
the window, so the run says nothing about *when* a gate opens. It rules out one
explanation of the residual and leaves the other two untested.

## The run that would finish Q273

The same level, idle, for **≈11 minutes** (`--seconds 660 --delay 0`). Two
events fall inside that window and they are of different kinds:

- **t1 restamps at 1724** (ours). A stored value — no sampling.
- **f6 sets at 2404** (ours). A sampled value.

One run, two measurements on one clock, and the pair discriminates:

| t1 | f6 | conclusion |
|---|---|---|
| 1724 | 2405 | the residual is the probe's sampling; our gate is right |
| 1725 | 2405 | the residual is real and in the gate; we fire a tick early |
| 1724 | 2404 | no residual on this level — level 1's needs its own cause |

Both turns are pinned in
`Level20Idle_PredictsARestampAt1724AndAFlagAt2404_NotYetCaptured` so they
cannot move silently before the capture is taken.

## Side results

The owner reported four figures from the same session. Three check out against
data we had never tested on this level:

| Observed | Ours | Note |
|---|---|---|
| Starting gold 10000, constant | `Level20Players.kld` record 3, `Keeper 1`, offset 0x00 = 10000 | confirms `KwdPlayers` |
| 5 starting imps | five `KEEPER_CREAT` creature 1, player 3, at (38–42, 79) | confirms `KwdThings` |
| Mana drain −35 | 7/s per imp × 5 (Q53, ADR-0022) | third independent confirmation, and the first at five imps |
| Mana income +223/s | not checked | **confounded**: idle imps still claim and reinforce, so the claimed-tile count drifts and 223 is a snapshot at an unknown time |

The portal result is written up in `docs/research/portals.md` §"A portal with
no rooms".
