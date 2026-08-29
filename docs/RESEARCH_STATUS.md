# Research status

Snapshot of what is known vs unknown. Authoritative long-form:
`DK2_REIMPLEMENTATION_PLAN.md` §3 (full system-by-system table).
Live question list: `docs/research/open-questions.md`.
What is *built* rather than known, against the long-term goals:
`docs/GOAL_ALIGNMENT.md`.

## Solved (usable today)
- Terrain (41 types), rooms (26 types) and the level tile grid: parsed and
  cross-validated; the dig/claim terrain transition graph is data. See
  docs/research/kwd-terrain-rooms.md.
- Creature definitions: all 46 records parsed (stats, flags, job tables,
  attractions, spells, resistances) — cross-validated against Flame. See
  docs/research/kwd-creatures.md and creature-table-dump.md.
- File formats: WAD, KWD/KLD (~90%), KMF, STR, textures, video — via
  OpenKeeper + dk2-tools; record header verified locally (kwd-container.md).
- Game database: DK2 balance data is KWD `VARIABLES`/`CREATURES`/... records,
  shipped standalone in `Data/editor/*.kwd` on the GOG release.
- Structures: Flame's v1.70 map — 868 structs, 10,548 globals, 7,282 fields.
- Level scripting: all 51 trigger action types enumerated.
- Where gold is: DK2 has no player-gold variable, only four per-place limits.
  Gold lives in tiles. See docs/research/rooms-and-gold.md and ADR-0015.
- Where a room may go: the ROOMS record's own placeable-on flags decide, and
  they partition the shipped rooms exactly as the game plays.

## Unsolved (research required before implementing)
- Pathfinding (biggest risk), creature moods/anger, combat resolution,
  job-selection priority order, computer-player AI, audio event semantics.
- Where mined gold goes, and what a room does beyond holding gold or not.

## Per-mechanic notes
One file per mechanic in `docs/research/`, tagged
CONFIRMED / STRONGLY INFERRED / HYPOTHESIS / UNKNOWN per AGENTS.md.
