---
systems: [creatures, rooms, keeper-spells, campaign-triggers]
---

# Healing a creature on a torture device

DKII.EXE 1.70, read with PyGhidra 12.1.4 on 2026-11-18. The behavior below is
STRONGLY INFERRED from the binary; no native experiment was performed here.

## Heal preserves custody

Shot process 3, `0x004ADB30`, cuts the positive health change to the deficit.
Before changing health it requests state 1 only when the target is not frozen
(`+0x447`), has no keeper binding (`+0x3CB`), is not a pit fighter (body bit
15), and is not attacking abnormal targets (body bit 2). The binding is shared
by Prison, Torture and Combat Pit; it is not the hand flag. The health call
`0x004B2CE0` still runs when the state request is skipped.

The remake had substituted `InHand` for the keeper-binding test. Consequently
Heal entered state 1, detached the device through the category leave hook,
and let the hero resume its existing objective. Neither the state wrapper
`0x0048AD30` nor `SetState` (`0x00476D30`) supplies a captivity exemption:
the callers must honor their own binding guards.

Heal's forced collision in `0x004ABD60` raises **shot** process event 4,
not creature event 6. The positive-health arm of `0x004B2CE0` does not fire
the attacked event. The collision's threat helper `0x004B32A0` distributes
fear; it does not invoke the attacked filter. Creature event 6 is fired by
the fight starter `0x0045A550`. A Heal is therefore not itself the attacked
event in this path.

## Level 6a and action 15

Placed creature 48, a full-health Guard initially at (31,15), moved onto the
wheel at (37,15), already has objective 19 (KillPlayer), target player 3,
and no trigger root. Attacked roots 33, 36 and 40
belong to different heroes, at (27,44), (28,44) and (28,43); their action
children 65496, 65495 and 65494 set objective 19. None explains creature 48's
resumed job after Heal. The injured Guard at (37,14) is placed creature 50
and has root 24. Source: shipped Level6aThings/Triggers.kld, decoded
with `dk2tools kwd things` and `kwd triggers`.

The action-15 arm in `0x0049AC80` tests Good ownership, then the **same binding
word**, then dying bit 21 (`0x0049ACE7..0x0049ACFF`). A bound hero is left
completely alone and the action succeeds. Otherwise `0x0048BE20` writes the
objective triple and `0x0048AD30(1)` requests idle. The remake had substituted
`CombatPitFighter`, allowing objectives to release prison/torture captives.

## Need anger and mood selection

`0x00478D00` returns at `0x00478D0C` on state-record flag bit 1, before all
three charges: no lair, no food and no pay. It does not erase existing anger
or gate the separate torture mood change and prison anger. Its other early
exits include following, Call to Arms, keeper binding and RebelActive.

The dispatcher `0x00476FC0`, at `0x00477169`, skips the angry/unhappy prelude
on that bit. An angry chooser's failure otherwise falls into unhappy. The
happy chooser belongs to idle state 89, outside this prelude; the flag is
not a blanket prohibition on that handler.

All 281 flag bytes were reconstructed from the initializer stores
`0x0047BBE3..0x004805E4`, including wider stores spanning flag bytes: none
unresolved. Exactly 43 carry bit 1: 72–74, 114–126, 130–133, 136, 193,
199–203, 210, 251–257, 259–260, 263–265 and 278–280. This agrees with the
existing generated table. Torture entry 130's `0x00484110` also writes the
binding after its tortured event; the flag independently covers voluntary
sessions, which the remake represents without a captive binding.

## Implementation and checks

ADR-0814 applies the binding at Heal/action 15 and reads the state flag in
need anger and both dispatcher mood paths. Tests cover a healed hero staying
attached and converting, objective refusal, all 43 states' three charges,
all modeled flagged states' angry selection, and content/unhappy/angry
Mistresses on their own wheel. Unimplemented states are checked through the
pure anger rule; their handlers cannot be exercised by the simulation.

## Campaign measurement

Historical runs from 2026-11-18 used the unchanged committed bot from
`dfb5209e`, before and after the reviewed fix recorded in `d72b4fc7`.
Campaign player 3, seeds 1–3, maximum 57600 ticks, jobs 3. The uncommitted
nursing change was not imported. Baseline hash 525 and final hash 527 both
used the shipped Level6a start, with no injected commands. Every run won and
reached the gem pickup. These runs were not repeated on integrated master,
whose bot differs from the measured revision.

| Seed | Non-Mistress conversions before / after | Rebellions before / after | Win tick before / after |
|---|---|---|---|
| 1 | 0 / 2 | 0 / 0 | 21104 / 15837 |
| 2 | 0 / 2 | 1 / 1 | 21857 / 10857 |
| 3 | 0 / 5 | 0 / 1 | 21085 / 17172 |

Counts scan `[Torture] converted:` excluding `type=Mistress`, and
`creature_rebelling:`. Win ticks come from `player-run.json` (`result_tick`),
with `finale=ReachedPickup`. The remaining after-run rebels were a converted
Guard and a Knight away from the device, not a voluntarily strapped Mistress.
All three after runs converted creature 48 at tick 1214. Outcomes differ after
the fix; they do not establish complete campaign parity with DK2.

To reproduce these historical measurements, use a separate checkout of
`dfb5209e` for the baseline and `d72b4fc7` for the reviewed fix, retaining
each revision's bot. From each checkout root, use a fresh output directory:

```text
dotnet run --project src/DK2.Harness -c Release -- watch scenarios/campaign-ladder/level6a.yaml --out <fresh-dir> --campaign-player 3 --seeds 1-3 --jobs 3 --player-max-ticks 57600 --player-output-mib 1024
```

Original local regenerable artifacts under `.claude/worktrees/torture-captive`:

- `scratchpad/torture-before/watch-20261118-104233-843`
- `scratchpad/torture-after527/watch-20261118-110114-528`

These are remake measurements, not native evidence.
