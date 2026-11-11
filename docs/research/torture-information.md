---
systems: [rooms]
---
# What torture tells a keeper: `0x004F2370`

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- **Built:** ADR-0230, ADR-0740, `TortureInformationRules`.
- Related: `state-category-hooks.md` §3.3 (the Torture hook), `fog-reveal-sources.md` §3.7.

## Result in one sentence

When an enemy dies on a keeper's torture device, the keeper is given one piece of information.
Rewards 3–10 each unlock a row of the **enemy-information panel**, once per pair of keepers.
Reward 11, always available, **reveals** part of the victim keeper's dungeon.

## 1. When — STRONGLY INFERRED

The Torture hook `0x0047B590` calls `0x004F2370(chamber, victim)` for an enemy that is dead after
the act (`state-category-hooks.md` §3.3).

## 2. The choice — STRONGLY INFERRED

`victim` is the creature and `+0x28` its keeper; the chamber's keeper is room `+0x1D`.
1. A victim of the hero player (`0x006F254C`) skips to step 4.
2. By the definition's **`JobClass`** (`+0x67D`, file `0xF02`, loaded at `0x00493464`):

   | JobClass | candidates |
   |---|---|
   | Artisan (0) | 3, then 7 |
   | Thinker (1) | 4, then 9 |
   | Fighter (2) | 5, then 8 |
   | Scout (3) | none |

   Each is added only while `0x004C2BC0(k, victim's keeper, chamber's keeper)` is 0.
3. Then 10, on the same test.
4. Then **11**, always.
5. `rand(n)` picks one. A reward other than 11 is recorded by `0x004C2B60`.

**The table.** `0x004C2BC0` answers 1 when the two keepers are one player (`player+0x10FC`, the
index). Otherwise it reads the flag at `+0x14 + k × 0xC4 + chamber × 0x1C + victim × 4`: twelve
rows of seven by seven. `0x004C2B60` writes 1 to the same place.

## 3. What 3–10 unlock — STRONGLY INFERRED

The table's only other reader is `0x00423A80`, called eleven times by the panel `0x00423450`,
once for each row `k` = 1–11 at y = `0x46` + 40 × (k − 1). Given, it formats `0x004C2C30(k,
player)`; otherwise a default. The values:

| k | value |
|---|---|
| 1 | `player+0x146 + player+0x14A` |
| 2 | `+0x173` |
| 3 | `+0x177 + +0x7E` |
| 4 | `+0x10EA` |
| 5 | `+0x16` less `0x004BA7D0(1)` |
| 6 | `0x004BA7D0(1)` |
| 7 | `+0x22` |
| 8 | the average level (`+0x403`) of the keeper's creatures |
| 9 | `+0x17B` |
| 10 | `100 − average(0x004871E0) × 100 / 0xFFFE`, a happiness percentage |

So 3–10 are **presentation only**. Nothing in the simulation reads the table. The rows' labels
are the panel's text, which is not read.

## 4. Reward 11 — STRONGLY INFERRED

Over the victim keeper's rooms, newest first (`player+0x36`, next `+8`), it skips each whose first
slab (`+0x2D`, `+0x2E`) is explored (tile byte `+9` bit `0x20`). On the first that is not, it
explores every tile of the room's chain (`0x00454B80`), posting bridge command 6 after each, a
3 × 3 redraw at x − 1, y − 1 through `0x00517480` (presentation only). With none, it takes the
keeper's first creature (`player+6`, next `+0x10`) standing on unexplored ground and writes `+0x475`
= 16 and `+0x476` = the chamber's keeper. `0x0048CC90` spends the passes (`fog-reveal-sources.md`
§3.1, §3.7).

## 5. The messages — STRONGLY INFERRED

Every reward posts message 97 (`0x61`) to the chamber's keeper first, then 96 (`0x60`) for 3–10 or
59 (`0x3B`) for 11. All three are row `0x201E`: 30 seconds, deduplicated (`message-table.md`).
Reward 6 maps to no message, and is never a candidate.

## 6. Ours

- `InformationGiven` on the chamber's keeper holds the flags, bit `(k − 3) × 8 + victim's
  player id`.
- `TortureSystem` emits `information` with the reward, and `reveal=` the reveal's result for 11.
- `TortureInformationRules.Award` posts 97, then 96 or 59 (`InformationLearned`,
  `InformationUnlocked`, `InformationReveal`). `RevealTo` explores a room or tags a creature;
  `FogOfWarSystem.TortureSightPass` is the tag arm of `0x0048CC90`, spending a pass each time a
  tagged creature moves onto another tile (ADR-0740, hash 467).
- **Deviations.** DK2 has one explored layer, the local machine's, and tests and writes it
  whoever tortured, spending a tag only for the local player; ours uses the chamber keeper's
  layer for every keeper. With fog of war off, ours reveals and tags nothing. Command 6 is not
  posted: the view rebuilds from the explored layer. A pass is spent only on a tile change, not at
  DK2's other callers of `0x0048CC90`.
- **Not built:** the enemy-information panel.
