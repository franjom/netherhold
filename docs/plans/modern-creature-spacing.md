# Optional rigid creature spacing

Status: future research and implementation; requested 2026-10-17. Outside the
classic-look Step5 scope. No implementation or original-game parity claim.

## Proposed rule

In an explicitly enabled modern presentation mode, visible creature footprints
must not overlap: their horizontal centre distance must be at least the sum of
their presentation radii. Determine suitable radii from the rendered bodies,
including large creatures, rather than imposing one distance on every species.
Apply continuous displacement; do not snap creatures apart between frames.

This is a deliberate departure from DK2's soft separation, whose evidence is in
[creature-neighbour-query.md](../research/creature-neighbour-query.md). Keep the
classic mode and deterministic simulation unchanged. The option must not affect
pathfinding, attacks, damage, jobs, triggers, replay hashes or save compatibility.

## Research before implementation

- Establish whether bounded presentation offsets alone can enforce the rule.
  Define a maximum offset and its return rate; do not accumulate visual drift.
- Constrain every adjusted footprint to valid terrain and obstacle clearance,
  including bridges, banks, furniture and the walkable rim of a living Heart.
- Specify an explicit fallback where strict spacing and bounded offsets cannot
  both hold, such as packed doorways. Never conceal the conflict by moving a
  creature through a wall. If gameplay changes are needed, propose a separate
  simulation mode and decision rather than changing the renderer's contract.
- Keep picking, hover markers, health bars and attached effects aligned with the
  rendered body. Evaluate weapon contact, target facing and attack reach against
  authoritative positions before allowing offsets during combat or work.
- Preserve authored motion: offset correction must not cause foot sliding,
  turning during swings, oscillation or apparent overtaking in a narrow queue.
- Budget the neighbour solve and allocations on crowded campaign scenes; use a
  stable ordering so the same inputs do not produce frame-dependent jitter.

## Acceptance gates

Compare the Level8 entrance procession, opposing traffic through a one-tile
doorway/bridge, mixed creature sizes, crowded combat, working and sleeping
creatures, and hand drops. Measure minimum footprint clearance and maximum
visual offset across frames. Verify terrain clearance and selection by eye.
Repeat with the option disabled and compare simulation hashes in both modes.
Resolve the narrow-passage fallback and combat alignment before implementation
is accepted; rigid spacing is a proposal until those constraints are feasible.
