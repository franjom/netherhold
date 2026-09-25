# What torture tells a keeper: `0x004F2370`

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- **Built:** ADR-0230, `TortureInformationRules`.
- Related: `state-category-hooks.md` §3.3 (the Torture hook), `fog-reveal-sources.md` §3.7.

## Result in one sentence

When an enemy dies on a keeper's torture device, the keeper is given one piece of information.
Rewards 3–10 each unlock a row of the **enemy-information panel**, once per pair of keepers.
Reward 11, always available, **reveals** part of the victim keeper's dungeon.

## 1. When — CONFIRMED

The Torture hook `0x0047B590` calls `0x004F2370(chamber, victim)` for an enemy that is dead after
the act (`state-category-hooks.md` §3.3).

## 2. The choice — CONFIRMED

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

## 3. What 3–10 unlock — CONFIRMED

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

## 4. Reward 11 — CONFIRMED

Over the victim keeper's rooms (`player+0x36`, next `+8`), it skips each whose first tile
(`+0x2D`, `+0x2E`) is explored (tile byte `+9` bit `0x20`). On the first that is not, it
explores every tile of the room (`0x00454B80`), each with a 3 × 3 area of kind 6
(`0x00517480`). With none, it takes the keeper's first creature standing on unexplored ground
and writes `+0x475` = 16 and `+0x476` = the chamber's keeper (`fog-reveal-sources.md` §3.1).

## 5. The speech — CONFIRMED

Speech `0x61` goes to the chamber's keeper. Then `0x60` follows for 3–10, and `0x3B` for 11.

## 6. Ours

- `InformationGiven` on the chamber's keeper holds the flags, bit `(k − 3) × 8 + victim's
  player id`.
- `TortureSystem` emits `information` with the reward.
- **Not built:**
  - the panel;
  - reward 11's reveal and tag, since there is no fog of war;
  - the speech.
