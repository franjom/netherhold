# Eating action timing

Source: retail v1.70 DKII.EXE, whole handlers and action initializer read through
Ghidra, 2026-10-11. CONFIRMED unless stated otherwise. Meal quantities and the
search chain remain in eating.md §7 and prison.md §7.1.

## Hatchery action 26

Before setting state 94, `0x00463760` resolves art 13 through `0x00494860`
and starts action 26 at the creature's position if the returned resource is
present. It captures `HungerNeed != 0` into its state scratch at +0xAB.
`0x00463720` polls the current action through `0x00496D00`, then branches on
that captured decision, rather than hunger accumulated during the wait.

`0x004950C0` writes action 26's null handler, art 13, mode 0 and zero duration.
`0x00496980` delegates to `0x00496700`, which clears the cycle-count byte.
The mode-0 runner `0x00496AA0` therefore ends the action on a zero animation
rate or its first cycle flag. Fresh installation and same-turn advancement
use `FirstCycleHoldTicks`; its integer clock is keeper-spell-reaper.md.

Built under ADR-0432: state 94 persists until the action deadline, then uses
the saved decision to search again or idle. Missing art starts no action; the
next handler poll decides. Presentation resolves art 13 or its fallback 8 and
restarts from the state-entry stamp. Our next-tick render clock is one greater
than the executing tick, so the playback origin is `(stamp + 1)/4` seconds.

## Prison wait

`0x00473750` starts the same action only when the meal clears hunger, then
sets 119. `0x00473730` polls `0x00496D00`, sets **117** (`0x75`) when there is
no current action, and always returns 1. The previous return-to-115 reading
confused the literal. Built under ADR-0438: a filled prisoner waits for the same
first-cycle action boundary as the Hatchery, then searches again in 117.
Missing art leaves no action, so the next poll returns to 117.
