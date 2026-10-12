# Possession arms and inputs: build plan

**Goal:** build what `docs/research/possession-arms-and-inputs.md` read (Q535, Q536) and fix the
gem divergence (Q587), on branch `possession/arms` (worktree `D:/DungeonKeeperRemake-poss`).

**Constraints:** each part takes the next free HashVersion/FormatVersion and ADR number, checked
against every branch and master at commit time (master took 232–233 and ADR-0441–0442 mid-branch). Each part gets
focused tests, the gate (Release build, no-install suite, kb/docs/coverage checks), the 42
scenarios against master, a reviewer pass, and its own commit. Presentation (camera, HUD, input
keys) stays out: commands carry the original packets' meaning.

## Part A: followers (§4) — built, ADR-0445

- Fields: `FollowLeaderId` (+0x482, word), `FollowRouteTick` (+0x47E), `FollowerCount` (+0x488,
  signed), `PossessedTarget` (+0x4D0, kind and id) and `PossessedTargetTick` (+0x4D2); hash and
  snapshot.
- Slot 3: `0x00515D50`'s scan with predicate `0x00491AE0`, the cap (variable 223), then
  `0x00461980`; count only on success; no stamp.
- State 246 (category 29, group 3): end test, re-route through travel resuming 246 with walk or run
  by distance, `+0x47E` retry; dispatch for keeper creatures and imps.
- The melee arm writes +0x4D0/+0x4D2; Creature_Update clears +0x4D0 after 3 s while possessed;
  `SelectAVictim` replaces its pick with the leader's +0x4D0 creature.
- Ends: possess and unpossess zero the count; dismiss (action 38) as a command; death decrements
  the leader; fear in spFighting clears and decrements; the follower-busy readers.

## Part B: sneak, hypnosis, sniper (§5) — built, ADR-0446

- Commands for 0x1D (tile x, y), 0x1B (pose and aim), 39 (the value).
- Sneak: the watcher test, the combat-scan skip, the hand refusal, the unmask on melee and spells.
- Hypnosis: the pick, owner change with the old owner kept, follow, flag; the force-target per
  possessed melee hit and the commanded-attack counter; release on the second press, on action 21
  and on death.
- Sniper: the free-aim range term and the shot's doubled contact damage.

## Part C: objects, shots and bodies (§1–2)

- Chicken (eat or kill), Training Dummy (no simulation effect), Arch Pillar to the heart, shot and
  body health taken flat, Book Case nothing; action 16 at an Arch Pillar.

## Part D: dig and claim (§3)

- The pose-tile claim/convert and the facing-tile mine/dig, through `TileWorkSystem` orders with
  the possessed ×MRT factor; claim's repeat strike; action 6's kind with no XP and the trap and
  door health zeroing; the non-Imp dig with the `0x0075AE93` gate taken as 0 (campaign).

## Part E: gems (Q587)

- A gem face takes no damage and pays GoldMinedFromGems every strike, for every miner. Measured on
  a gem level against master before and after.
