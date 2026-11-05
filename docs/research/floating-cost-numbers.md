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
  heading 0, owned by the viewer. That slot calls slot `+0x90`, the gated creator
  ([0x00440420](functions/00440420.md)): the camera test unless the definition has flag `0x8000`
  (effects 22 and 364 have flags `0x480`), and the explored-tile or sight test. So a number off the
  camera or on an unexplored tile is never created.
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
  that makes the digits abut. A positive offset is to the viewer's right, so the units digit is
  rightmost: SHOT0010 reads "18750" left to right (and the owner's "4800"); the sign convention of
  `0x0059C360`, which scales `+0x57`, is itself unread.
- Each element first passes the allocator's gate `0x004352E0` (element flag `0x200` clear: camera,
  `0x00440EE0`, solidity). A failure makes `0x00439480` return 0 and the effect be removed.

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

**Colour — STRONGLY INFERRED that the digits show only light; HYPOTHESIS which lights.** The element RGB is 0,0,0, set by [0x00435F20](functions/00435F20.md): art
type 1 is white, runtime art flag 2 takes the owner colour (minimum `0x30`), otherwise the definition's
RGB (`+0x4A..+0x4C`) goes to element `+0x6E`. [0x00504D80](functions/00504D80.md) sets `+0xB` bit 0 for
any non-zero art type, so both digit sets pass the RenderInfo gate. `0x0057F3D0` adds a light term to
that base from a list whose filler is unread (Q609), so all the digits show is light: the owner sees them
darken in unlit places (2026-11-05). A DK2 Prison sale screenshot
(`F:/DK2Evidence/captures/visual/cost-numbers-prison-sale-lighting--2026-11-05--001`) shows the digits
under wall torches bright gold, never white, and those mid-room dimmer but readable. The art itself is gold: the `InGameDigit5` texture averages
RGB 171,159,92 over its opaque pixels, `gInGameDigit5` (mana) 91,171,133 (decoded 2026-11-05 with
`DK2.Art`'s EngineTextureArchive).

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

## 7. What ours does (ADR-0663)

`CostNumberRules.Show` asks for effect 22 (gold) or 364 (mana) as a gated `ViewEffectRequest` with
an `Amount`, and nothing for an amount below 0. Two callers:

- `RoomBuildSystem` shows the wave's summed cost, when non-zero, at the press corner at height 0, on the
  close and on both money-refusal exits (`0x004C775F`).
- `RoomSellSystem.SellOne` shows each paid tile's refund at the tile at height `0x1000`, after the
  deposit or overflow and before sound `0xF5` (`0x004C5ED0`), for wave and immediate sales alike.

The presentation engine drops an `Amount` request whose owner is not its `viewer`. Generation type 14
makes one element per digit with `ConstructionParticle.ScreenOffsetXRaw` (section 4), which Unity adds
along the camera's right axis. The digits keep RGB 0,0,0 and take light (`TakesLight`): Unity adds the
radial term of each light in the digit's tile table (`SpriteLighting`), as ADR-0493 did before its
amendment, which other effect sprites keep. Which lights DK2 lists is still unread (Q609).

Not mirrored: the sale's opening permission test `0x004C5DD0`, which can refuse before the pop; the
per-element allocator gate `0x004352E0`; the other twelve callers of section 5 and the computer
keeper's direct build. ADR-0534 and ADR-0313 still list the store-lift amount and the training -20 as
not modelled. Native visual acceptance is pending.
