# Dungeon Keeper 2 reimplementation

This branch contains the Phase 0 report for `docs/CODEX_TASKS.md` Brief 8,
"build fog of war". The stop rule fired before implementation.

## Fog of war — Phase 0 stop report

### Outcome

**STRONGLY INFERRED — fog is persisted world state, not presentation-local
state.** Brief 8 therefore requires this branch to stop without implementing
fog, changing `WorldState`, bumping `GameSimulation.HashVersion`, or re-pinning
`DeterminismTests`.

### Binary evidence

The following facts were read from DKII.EXE v1.70 in Ghidra. They are stated in
our own words; decompiler output remains quarantined under
`reverse-engineering/` in accordance with ADR-0046.

- The whole-world save coordinator at `0x0050AA60` invokes the runtime map's
  virtual save operation.
- The runtime-map save operation at `0x0044F3F0` walks every tile and delegates
  each one to the per-tile operation at `0x00452B90`.
- The per-tile operation writes a contiguous ten-byte slice of the live
  sixteen-byte tile record. The slice runs from live-tile offset `+0x02`
  through `+0x0B`, so the live field at `+0x0B` is persisted as saved-tile lane
  9.
- Map and presentation-side routines test bit 6 of the live byte at `+0x0B` as
  the fog gate. At `0x00452DC0`, a tile behind that gate is treated specially
  when its actual terrain carries terrain flag bit 24,
  `RevealThroughFogOfWar`. Other flag-bit-24 reads in the map presentation path
  follow the same relationship: the tile fog bit and the terrain exception bit
  are tested together.

That chain identifies a fog-controlling bit in the live runtime tile and proves
that the same bit is written into a saved game. Under Brief 8's explicit rule,
state persisted by a world save is simulation state. This is enough to decide
Q190 and trigger the stop rule even without proving whether the computer keeper
also consults the bit.

Flame was consulted second, for structure only. It corroborates a sixteen-byte
runtime tile, a runtime-map object embedded in the world, and the virtual target
addresses above. No Flame identifier or implementation was adopted. OpenKeeper
was not consulted because the binary already settled the stop-rule question.

### Other Phase 0 findings

- **STRONGLY INFERRED:** `RevealThroughFogOfWar` is actively tested by the
  original engine; it is not a dead data flag. The clearest compact reader is
  at `0x00452DC0`.
- **STRONGLY INFERRED:** `FogOfWar` is a real 32-bit configuration value. The
  configuration reader at `0x0055D340` loads it into offset `+0x144`, and the
  corresponding writer at `0x0055D620` emits it. This proves a setting exists,
  but not what each numeric value does.
- **STRONGLY INFERRED:** the creature state called exploring uses the handler at
  `0x0045A3E0`. Its local destination search does not expose the fog reveal
  radius; no radius was inferred from it.
- The newest installed saves were not a clean before/after fog experiment: they
  also contain mining, claiming, and room construction changes. Their lane
  differences therefore cannot identify discoveredness independently.

### Explicit UNKNOWNs after the required stop

- Whether a computer keeper consults fog when selecting actions.
- The reveal radius, its exact geometry, and which entities reveal tiles.
- The numeric meanings and selectors for the five setup labels (`Creatures
  Reveal`, `All Owned Reveal`, `Stay Revealed`, `Re-shroud`, and `Re-fog`).
- The complete per-keeper encoding within the persisted tile fields.
- How speculative tags on undiscovered tiles are stored and cleared.
- Whether Sight of Evil and Reveal Map share the ordinary discovery state.

These were not guessed after the architecture question became decisive.
