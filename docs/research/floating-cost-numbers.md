---
systems: [hud-front-end, rooms, art-assets]
---
# Floating gold and mana numbers

Read 2026-11-04 (claude) from DKII.EXE and the shipped `Effects.kwd`/`EffectElements.kwd`,
after the owner's screenshot of a Lair build showing a gold "4800" above the room. Confidence
labels are per claim.

**In the game — CONFIRMED (owner, 2026-11-04).** After a room is built, the summed price of the
built tiles appears as gold digits in the world. They float upward and, at the top of their
climb, disappear without fading. Selling room tiles shows one number per tile, not a sum.

Screenshots SHOT0001-0014 (1600×1200, a Prison, kept in
`F:/DK2Evidence/captures/visual/cost-numbers-shot0001-0014--2026-11-04--001`):

- 0001-0003: the cyan `$375` and `$9375` beside the cursor are the hover price popup
  ([Interface_FormatCostPopup](functions/0042EA70.md)), not these digits.
- 0004-0009: a drag sale. Every tile shows its own gold `375`. The numbers are staggered by
  diagonal from the corner under the cursor to the far corner, the nearest highest, and the
  oldest are gone first (0009).
- 0010-0012: a 5×5 Prison build shows one `18750` (25 × 750) at the far corner from the cursor,
  high over the back wall. It is gone by 0013.

## 1. The request — STRONGLY INFERRED

Every source calls [World_ShowCostNumber](functions/0050FB70.md) `0x0050FB70` on the world
(`[0x006E5054]`; `0x00499F9E` and `0x004D685D` reach the same object through `[0x006E5050]` and
`[0x006ED484]`, Q231) with `(player, &point, amount, kind)`. The world forwards it to the presentation
object (world `+4` → `+0x10`, attached by [0x00527040](functions/00527040.md)), vtable `0x0066CC94`
slot `+0x64`. That slot is [Presentation_ShowCostNumber](functions/00441060.md) `0x00441060`:

- It does nothing unless `player` is the **local viewer** (slot `+0xBC`) and `amount >= 0`.
  A rival keeper's spending is never shown to you.
- `kind == 0` creates effect **22, Gold Cost**; any other `kind` creates effect **364, Mana
  Cost**. Both go through view slot `+0x94` ([0x0043A8E0](functions/0043A8E0.md)) at `point`,
  heading 0, owned by the viewer.
- If the effect was created, the amount is stored in the new effect at `+0x12F`.

The world's own random stream is not touched: this is view-side (`keeper-spell-tremor.md`
section 3 has the view's gates and allocation).

## 2. Building: one sum per drag — STRONGLY INFERRED

[Player_BuildWave](functions/004C7380.md) `0x004C7380` lays a queued rectangle one diagonal a
turn from the release corner to the press corner ([room-slab-chain.md](room-slab-chain.md)
section 2). Each tile that passes its placement and cost test adds its price to the slot's
word `+0x1D3` (slot base `+0x1C2`, stride `0x19`; the slot's `+0x11`).

When the wave reaches the press corner it pops the sum once:

| | value |
|---|---|
| when | the turn the wave reaches the press corner, the tile where the drag began. A money refusal (`-1`) jumps the wave there too, so the number is what was actually paid |
| shown if | the sum is non-zero (a drag that built nothing shows nothing) |
| amount | the word `+0x1D3`, zero-extended (`AND EAX,0xFFFF` at `0x004C77E5`), so it wraps past 65535; the queue `0x004C6EF0` zeroes it |
| point | press-corner tile centre `(x·4096+2048, y·4096+2048)`, height **0** (`0x004C7813`) |
| kind | 0, gold |

So a single-tile build shows that tile's price, and a drag shows one number for the whole
rectangle, as in SHOT0010 (18750 for 25 Prison tiles).

## 3. Selling: one number per tile — STRONGLY INFERRED

[Player_SellRoomTile](functions/004C5DB0.md) `0x004C5DB0` sells one tile. When its pay flag is
set, it pays the tile's refund (from `0x004EBF20`) in at the tile through `0x004BA420`. Then it
pops that refund at the tile:

| | value |
|---|---|
| amount | the tile's refund (`0x004C5EC7`); unlike the build there is no non-zero test, so a paid sale refunding 0 would show `0` |
| point | the sold tile's centre, height **0x1000** |
| kind | 0, gold |

A drag sale calls it once per tile, so each tile gets its own number, one diagonal a turn
([Player_SellWave](functions/004C7160.md)). The wave always starts at the rectangle's **(max x,
max y)** corner: the Hand's sell release normalises the corners before sending action 0x31, and the
wave starts at the second, the maximum ([0x004C70A0](functions/004C70A0.md)). A build is not
normalised and starts where the drag is released. A single-tile sale (action 0x30,
[0x00512890](functions/00512890.md)) is immediate.

## 4. The digits — STRONGLY INFERRED (binary and data)

Effects 22 and 364 (shipped data): generation type 14, HP 1, flags `0x480`, no art, no motion,
generate id 27 (gold) or 573 (mana). Type 14 is
[ViewEffect_GenerateType14](functions/00439480.md) `0x00439480`:

- It counts the decimal digits `n` of `+0x12F` (0 counts as one digit).
- It makes one element per digit, units first, element id = first id + digit, at the effect's
  point, heading 0.
- Each element gets a horizontal screen offset (`+0x7D`) and a vertical one (`+0x7F` = 0). The
  units digit gets `(n/2)·w` (integer `n/2`, `0x00439518`), and each following digit `w` less,
  where `w` is the first element's MaxScale (definition `+0x2F`). With the 0.3-tile sprite width
  that makes the digits abut. That a positive offset is to the right, so the units digit is
  rightmost, is HYPOTHESIS: `0x0059C360` scales `+0x57` but its sign convention is unread.

The ten elements per kind (shipped data):

| field | gold 27–36 `InGameDigit0..9` | mana 573–582 `ManaInGameDigit0..9` |
|---|---|---|
| art | `InGameDigit0..9`, type 2 (alpha sprite), art flags 0 | `gInGameDigit0..9`, type 2, art flags `0x1` |
| sprite size × scale | 4096 × 1228/4096: 0.3 tile | same |
| HP | 48/48 | same |
| speed XY / Z | 0 / 8192 up | same |
| mass / air friction / elasticity | 0 / 327 / 0 | same |
| element flags | 0 | 0 |
| RGB / fade percentage | 0,0,0 / 100 | same |

**Motion.** Element movement ([room-construction-effects.md](room-construction-effects.md)
section 3) subtracts mass (0), multiplies velocity by `(4096−327)/4096` with truncation, then
adds `velocity >> 4` to the position, once per 50 ms update. So each digit rises fast and
slows, and lives 48 updates (2.4 s). Rise from its birth height, by that rule:

| update | 1 | 5 | 10 | 15 | 20 | 30 | 40 | 48 |
|---|---|---|---|---|---|---|---|---|
| height (tiles) | 0.115 | 0.490 | 0.813 | 1.025 | 1.165 | 1.317 | 1.382 | 1.405 |

The digits climb about 1.2 tiles in the first second and drift 0.2 tile in the next 1.4 s,
then vanish when HP reaches zero. The table assumes one 50 ms quantum per update: the update
subtracts the elapsed quanta from HP but moves once ([0x00436430](functions/00436430.md)), so a
slow frame ends the climb lower.

**No fade.** [0x00436430](functions/00436430.md) dims colour and alpha only for element flag 8,
and the alpha threshold it would use is `HP − 100·HP/100 = 0` (initialiser `0x00435F20`). The
digits have flags 0, so they stay opaque and are removed whole when their HP runs out, near the
top of the climb. Binary STRONGLY INFERRED; the owner confirms no fade (2026-11-04).

## 5. Every source of the numbers — STRONGLY INFERRED (call sites)

`0x0050FB70` has 14 callers. The 4th argument read at each site is 0 (gold) except one:

| caller | what is shown |
|---|---|
| `0x004C7380` Player_BuildWave | the room drag's summed price (section 2) |
| `0x004C5DB0` Player_SellRoomTile | each sold tile's refund (section 3) |
| `0x004C3519` door, `0x004C3918` trap blueprint placement | the price paid (call sites; amounts not read) |
| `0x004C3765` door sale, `0x004C3AAE` trap sale | the refund (call sites) |
| `0x0047B2D0` Training update hook | `4 × training cost`, at the trainee, every fourth charge ([training.md](training.md) section 3) |
| `0x0047B970` Temple update hook | **mana**: kind 1, each prayer's mana at the praying creature ([temple-states.md](temple-states.md)) |
| `0x0047281E` payday, `0x00457644`, `0x004D685D` Casino, `0x004C0DDA` Hand lift, `0x00499F9E`, `0x00514D6F` possessed pick-up | gold amounts; not read beyond the call |

## 6. Measured in the game — CONFIRMED (capture and video, 2026-11-04)

Level `Q750CostNumbersBank` (tools/levelgen), played by the owner with `dk2record.py` running and
the screen recorded: `F:/DK2Evidence/studies/q750-cost-numbers--2026-11-04--003/payload`
(`run.dk2cap`, ticks 1-343; `video-2026-11-04-18-42-50.mp4`, 94 s, 30 fps). Video time is capture
wall time minus about 8.2 s, fixed by the waves below.

| | capture | video |
|---|---|---|
| 5×5 Lair, pressed NW (17,2), released SE (21,6) | one diagonal per tick from (21,6), ticks 71-79, −300 a tile | **7500** appears at the NW corner with the last diagonal (20.6 s), shoots up in about 0.3 s, holds, and is gone 2.2-2.3 s later, no fade |
| 5×5 Lair, pressed SE, released NW | one diagonal per tick from (17,2), ticks 162-170; tiles already built are skipped and cost nothing | |
| drag sales of the Lair (twice) and a 3×3 Temple, each pressed SE | one diagonal per tick from the SE corner, which is (max x, max y) and here also the press corner (owner: "opposite to a build"), refund half the price (150, 1500) | a number per tile, born diagonal by diagonal (29-32 s, 1:22-1:28) |
| single-tile builds and sales | one tile, ±300 / +150 | |

So the build sum is shown at the press corner when the wave closes, and both waves move one diagonal
per game turn. Mana digits did not appear in this run: the Goblins were dropped on the Temple and the
prayer counter (player `+0x10EE`, credited by `0x0047B970`) never changed. The owner reports blue
digits only while creatures pray at the Temple's rim, not when they are dropped in (2026-11-04).

## 7. What ours does

Not built. ADR-0597 deferred numeric display ("ours has no cost-number request caller"), and
ADR-0534 and ADR-0313 list the store-lift amount and the training −20 as not modelled. A build
would put the request where DK2 puts it: a view request from the build wave's completion and
from each paid tile sale, carrying viewer, point, amount and kind, with the digits drawn by
the presentation layer from section 4.
