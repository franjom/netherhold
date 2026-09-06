# Dungeon Keeper 2 reimplementation — fog-of-war branch

This branch implements `docs/CODEX_TASKS.md` Brief 8 after an explicit owner
override of its Phase 0 stop rule. Phase 0 proved that DK2 persists fog in the
world save, so the implementation correctly changes `WorldState`, the snapshot
format and the determinism hash.

## Evidence and confidence

The facts below were read from DKII.EXE v1.70 in Ghidra. They are stated in our
own words; all decompiler output remains quarantined under
`reverse-engineering/` as ADR-0046 requires.

- **STRONGLY INFERRED — fog is simulation state.** The world-save coordinator
  invokes the runtime map's save operation. That operation walks every tile,
  and the per-tile operation writes ten contiguous bytes of the live 16-byte
  tile record, including the byte whose bit 6 gates fog rendering. Thus the fog
  gate is persisted in a SAV, not reconstructed by the renderer.
- **STRONGLY INFERRED — creature reveal uses a strict circle.** The creature
  update path reads a fixed-point range from the creature definition, visits
  candidate tile centres, and changes fog only where squared distance is
  strictly less than squared range. Equality is outside the footprint.
- **CONFIRMED from shipped data — the range is six tiles.** CREATURES(170)
  offset `0x0D01`, already parsed as `CreatureDef.PerceptionRange`, is `0x6000`
  for every one of the 46 shipped creatures. With DK2's 4096-unit fixed point,
  that is exactly 6 tiles. This independently confirms the owner's earlier
  visual estimate.
- **STRONGLY INFERRED — the terrain exception is active.** Map presentation
  routines test terrain flag bit 24, `RevealThroughFogOfWar`, in the same
  decision as the tile fog gate. It is not a dead table field.
- **STRONGLY INFERRED — fog has an on/off game setting.** The configuration
  reader and writer both handle a 32-bit `FogOfWar` value; all observed callers
  pass a single boolean bit. This branch therefore makes fog an explicit world
  and scenario setting.
- **CONFIRMED by the owner — campaign discovery stays revealed**, and a mark
  placed speculatively on fog disappears on reveal when the actual terrain is
  impenetrable or is a room. The implementation is monotonic and revalidates
  that keeper's mark against the revealed terrain's `Taggable` flag.

Flame was consulted second for structure only: it corroborated the live tile's
16-byte width, the embedded runtime map, and the relevant virtual targets. No
Flame identifier or implementation was adopted. OpenKeeper was consulted last
and exposed no working fog implementation; it only pointed back to the
CREATURES field that this repository already parsed from a separately credited
format description. The value and its use were then checked against shipped
data and DKII.EXE respectively, so no OpenKeeper field name was adopted.

## Implemented behavior

- Per-keeper discovery is hashed and snapshotted as part of `WorldState`.
- Each on-map, non-dying creature reveals its data-defined circular footprint
  for its owner after movement; discovery is monotonic.
- `WorldView` is keeper-specific. Hidden tiles are substituted with the shipped
  `Fog Of War` terrain and do not leak real ownership, room furniture, health,
  gold, creatures or objects.
- A keeper sees only their own speculative tag. Terrain carrying
  `RevealThroughFogOfWar` bypasses substitution via the flag, never an id list.
- `fog_of_war` and `viewing_player` are available to scenarios. The fog scenario
  produces a partly explored `world.txt` with the six-tile circular boundary.

The Phase 0 stop commit is retained in this branch's history. The later
simulation/hash work exists only because the owner explicitly authorized
continuation after reviewing that result.

## Explicit UNKNOWNs and boundaries

- Whether computer-keeper decision code consults fog. Persistence proves the
  state belongs to the simulation, but no AI reader was established.
- The selector and exact transition rules behind the setup labels
  `All Owned Reveal`, `Re-shroud`, and `Re-fog`. They are not treated as one enum
  because the binary's `FogOfWar` configuration value is boolean.
- Whether a creature in every uncommon state reveals. The binary excludes the
  dying state; this branch also excludes creatures held in the Hand. Other
  state-specific exceptions remain UNKNOWN.
- How multiplayer DK2 packs each keeper's discovery into its saved/runtime
  structures. This branch uses one bit per tile per keeper as its own clear,
  deterministic representation.
- Sight of Evil and Reveal Map are deliberately not implemented; their later
  systems can write through the same discovery seam once researched.
