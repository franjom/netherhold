# Gold under the Hand: hover texture, red pass and the store sparkle

Read 2026-10-23 (claude) from DKII.EXE and the shipped data after the owner's
screenshots SHOT0220-SHOT0230 (1600×1200, a Heart-side Gold Chest beside Level 1's
Heart). The owner reported three things about gold chests, piles and urns: a shine
that moves, a pink-red tint and "1000 Gold" when pointed at, and that they can be
picked up and dropped. Confidence labels are per claim. The creature hover tint is
[status-flowers.md](status-flowers.md) §1; this page adds what objects and gold do.

## 1. The hover texture — CONFIRMED

[Presentation_Update](functions/0043B220.md), in its hover-tint arm (0x0043BE40), reads
the hovered thing's installed art resource (thing `+0x6C`, RenderInfo `+0x28`). When
its flags dword (`+0xB`) has bit **0x10000**, it saves the texture phase (0x0043C240)
and writes **0xFFF** to RenderInfo `+0x30` and `+0x34` before the tint; the restore
after the world draw (0x0043C14F..0x0043C1EB) puts it back. CONFIRMED (disassembly
0x0043BE76..0x0043BEA3).

A KMF material holding several textures draws the one its phase selects
([heart-health-visuals.md](heart-health-visuals.md) §2, `DungeonHeartVisual.TextureIndex`);
0xFFF selects the last. Shipped data:

- Art flag 0x10000 is set on exactly the gold meshes: object 1 Gold (`Gold Pile`),
  2 Gold Bag (`Gold Bag1`) and 3 Gold Pile 1 (`Gold Chest`, `Gold Urn1`, `Gold Urn2`,
  `Gold Chest2`), all with flags 0x10205. No other object art has it. CONFIRMED.
- Their spill cards are two-texture materials: `Gold_Spill_Small` +
  `Gold_Spill_Small_HIGHLIGHT` (Gold Pile, Urn1) and `Gold_Spill` + `Gold_Spill_HIGHLIGHT`
  (Urn2, Chest), file flags 0x81, brightness 0.6. The `_HIGHLIGHT` textures are the same
  coins recoloured orange-red, with identical alpha. CONFIRMED (EngineTextures decode).
- The chest's inner gold (`Gold_In_ChestGold`, texture `Gold`) and the wood are
  single-texture materials, so the phase changes nothing there. CONFIRMED.

Unhovered, the spill shows its first texture. The art's flag 4 gives these meshes a
texture phase that advances 0x1000 a turn over a frame count of 1 (art `+0x48` = 1;
[0x004B37D0](functions/004B37D0.md), [0x004B3AD0](functions/004B3AD0.md)); that it wraps to 0
each turn rests on the wrap bound `+0x3C`, whose writer is unread. STRONGLY INFERRED from
SHOT0220-0227, where the cards are gold.

## 2. Who is tinted, and how — CONFIRMED

**The hover id.** The tint arm draws only the Hand's hover id (interface `+0x10BC`, which
0x0040ACD0 copies to bridge `+0x34`). In Hand mode [Hand_UpdateHoverTarget](functions/0040C290.md)
sets it from the pick, per class, for the viewing player (CONFIRMED, decompile 2026-10-23):

| pick kind | published when |
|---|---|
| 2, 3 creature | 0x0048E670: the definition's byte `+0x686` bit 4 and pickup permission for the viewer |
| 4 object | 0x0049CBB0: definition `+0xF4` & 0x300 (CanBePickedUp or CanBeSlapped), and the object's owner is the viewer, or it is type 1 or 2 (loose gold) on a tile the viewer owns |
| 5 door | always |
| 6 trap | 0x004B1870: the tile's owner is the viewer or the neutral player (word 0x006F2548) |
| 7 shot | always |

So rival, neutral and hero creatures, which the viewer may not lift, are never tinted;
the store's Gold Pile 1 is the keeper's own object, and loose gold counts by its tile.
The owner, from play (2026-10-23): the red goes to the keeper's own creatures, doors and
traps, never a rival's or a neutral one, and to gold chests, piles and urns. For doors
this is narrower than 0x0040C290's case 5; whether the picker 0x005992B0 ever hands a
rival door to it is unread. Objects other than gold (an own chicken, a crate, a spell
book) pass 0x0049CBB0 too and are tinted: binary and owner agree (2026-10-23).

Every class passes the arm's vtable `+0x34` test: [0x00628BC0](functions/00628BC0.md)
(returns 1) sits there in the object vtable 0x0066D654 (dword 0x0066D688), the trap
vtable 0x0066D754 and the door's. CONFIRMED.

**The cursor.** Over own creatures and gold the Hand shows the pickup hand; over doors
and traps it does not (owner, 2026-10-23). This follows 0x0040C1E0's pickup permission
([hand-cursor-rendering.md](hand-cursor-rendering.md)).

**Blended materials.** The red `AA_RedTexture` (255,64,65) is an extra pass with draw flag
0x200. Both mesh draws test each material's state after drawing it: with bit 0x1 or 0x20
(additive or alpha-blended) and no 0x400 in the draw flags they jump past the rest of that
material's passes, the 0x200 overlay and the later 0x800 pass alike (static draw
0x00581583..0x00581597 to 0x00581920; animated draw 0x00584FC9..0x00584FE3). The flash override
[0x0059D4B0](functions/0059D4B0.md) gives 0x600 (with 0x400) only in mode 1, which the hover
arm chooses for a door or trap whose type `+0xF6` is 8 (0x0043BEF8..0x0043BF1A): the Fear
Trap and the Hero Portcullis. Everything else hovered, creatures and gold included, has its
additive and alpha-blended materials left untinted: the gold spill cards, a creature's
translucent parts. CONFIRMED by the binary and by the owner (2026-10-23, from a screenshot of
ours that showed the cards as a red box).

The draws test the material's state after the draw flags have adjusted it. In the static
draw, flags 0x2001 force alpha blending (0x00581174: clear 0x1 and 0x80, OR 0x220) and flag
0x2 forces additive (0x005811A8..0x005811C4: clear 0x20 and 0x40, OR 0x201), so an object
drawn faded may count as blended there. Which of our fades sets those flags is unread; ours
tests the KMF material's own blend.

Correction, 2026-10-23: the owner's earlier answers that a creature's translucent parts and
a chest's coins are tinted were withdrawn the same day. The SHOT0225/SHOT0228 coins
((197,166,81) → (214,113,71)) are the `_HIGHLIGHT` texture with no red; the lid's
(+126, +69, +70), red clipped at 255, is the flat add.

**The text.** The status line in SHOT0228 reads "1000 Gold" and the popup in SHOT0230
"Gold": object type 3's status string 2543 with its gold at `%73`
([world-hover-text.md](world-hover-text.md)). The owner confirms the amount is that
chest's own tile, not the room's (2026-10-23). That the chest is on a Heart tile, whose
cap is 1000 ([object-drawing.md](object-drawing.md) §2, 0x004F4440), is STRONGLY
INFERRED from the screenshot: it stands beside the Heart, and 1000 is a full Heart tile.

## 3. The shine is the store sparkle — mechanism CONFIRMED, match STRONGLY INFERRED

The moving glint on the chest in SHOT0222-0224 is not in the material: no gold material
has the environment-map bit (file 0x40), and no gold object has an authored light. It is
effect **34, Gold Sparkle**, made by [0x004F3A00](functions/004F3A00.md):

- The Treasury's tick [0x004F39A0](functions/004F39A0.md) calls it first, every turn; the
  Heart's update [0x004D9690](functions/004D9690.md) calls it first unless the Heart is
  ruined (`+0x9A` bit 0).
- It draws `random(100)` on the world stream (`[0x006ED484] + 0x15A0`); below 30 it walks
  the room's tile chain from its first tile (`+0x2D`, the tile node from world slot
  `+0x1D8`, the next at node word 2).
- On each tile, each object (`+0x12` = 2) of type 3 whose definition has the gold flag
  (`+0xF4` & 0x40) draws `random(100)` again; below 10 it calls world slot `+0x38C` with
  definition `+0x117` (Objects.kwd `+0x358`: 34 for Gold Pile 1, 0 for Gold and Gold Bag),
  the room's word `+0x1D` (owner, LIKELY), the object's position raised by the
  definition's Height `+0xFA`, and heading 0.

So only Treasury and Heart gold sparkles, never a loose pile. Effect 34: generation
type 4, one element a turn, HP 3, element 19 `Gold Sparkle` with art `GSparkle`.
CONFIRMED from Effects.kwd/EffectElements.kwd.

That the glint in the screenshots is this effect, rather than something unread, is
STRONGLY INFERRED: it appears and moves on the chest only, frame to frame.

**Built (ADR-0534).** `StoreSweepSystem` runs each live store's turn: the sparkle, then the
16-turn sweep. Mapping: a store tile holding gold is one Gold Pile 1 (the deposit creates it,
`0x004F4510` deletes it with its last gold), so the chain walk (`SlabChain.Order`, DK2's `+0x2D`
chain) draws once per tile with gold. Every Treasury and unruined Heart draws, whatever its owner
(`Room_Tick` calls the type handler unconditionally), in `RoomIndex` order (ADR-0327). The pile
stands at its tile's centre on the floor, `0x1000` (`0x004F3C60` creates it at z 0 and the
floor step `0x004B5020` raises it, STRONGLY INFERRED), so the request is effect 34 at height
`0x1000` + 1068. The effect's own randomness is the view's. Tests: `StoreGoldTests`.

The dumper's `SparkleEffectTimer` (`+0x14A`, [object-thing-object.md](object-thing-object.md))
belongs to objects with definition flag 0x4 (TypeSpecial), and the one at `+0x151` to
object type 4 (dumper 0x0049D68D, 0x0049D6B5); neither is gold.

## 4. Picking the store gold up — the lift CONFIRMED, built

Gold Pile 1 carries `CanBePickedUp`, and the owner lifts and drops chests and urns and sees
the pickup hand over gold piles, chests and urns (2026-10-23).

**The lift** (hand state 2's enter, [0x004C0CB0](functions/004C0CB0.md), read whole by decompile
2026-10-24; reached from action 59, [0x005135B0](functions/005135B0.md)): the named object goes
into the hand **whole**. For an object in a Treasury or Heart (room types 1 and 5) it unlinks the
object from the room (`0x004E6130`) and takes its whole `+0x14A` gold off the room's tally `+0x49`
and the keeper (`Player_ChangeGold(-gold, 1, 0, 0)`). There is no fixed amount and no gold cap;
the only limit is the hand's 64 things. The tile is left with no pile, so its chest or urn is
gone, not shrunk. The object stays type 3. Also: process state 0x16, `+0x146` bit 0x10, the
amount shown through `0x0050FB70` (presentation), sound `0xC3`. No owner or tile test, as for
any object (ADR-0310); the cursor offers only the viewer's own pile (`0x0049CBB0`).

**The drop.** The landing [0x0049EF90](functions/0049EF90.md) treats types 1-3 alike: onto a
creature it pays half; otherwise it merges into a gold object in its cell or is kept as the
neutral player's; on a Treasury or Heart tile it selects store state 5, which deposits it at once
([gold-on-the-floor.md §11](gold-on-the-floor.md#11-a-store-sweeps-loose-gold-on-its-own-tiles-back-in-built-adr-0285), ADR-0613).
The sweep never takes it ([0x004F47E0](functions/004F47E0.md) skips type 3).

**Built (ADR-0534).** `PickUpStoreGoldCommand` by tile: `HandRules.CanPickUpStoreGold` (a
Treasury or Heart tile with gold, the definition liftable, a free slot) and the cursor's
`HandRules.MayReachStoreGold` (the tile's owner). `HandSystem` zeroes the tile and puts a neutral
Gold Pile 1 with that gold in the hand, as the counter withdrawal does
([hud-resources.md](hud-resources.md)). Deviations: neutral from the lift, not from the landing;
dropped back on a store it is deposited on landing, as DK2's state 5 does (ADR-0613).

## 5. What ours draws (2026-10-23)

- `HoverTint` (src/DK2.Presentation/ObjectHoverHighlight.cs) is §2's hover-id rule; the
  runner tints only what it accepts, in Hand mode off the HUD.
- `ObjectHoverHighlight` holds §1's texture rule; `ObjectView.HoverTexture` marks the gold
  arts.
- `DungeonScene.DrawHoverOverlay` draws a hovered gold object's `_HIGHLIGHT` twin (from
  `ClassicMeshes.HoverTextures`) one render queue after the original, then the red pass on
  its materials, skipping those made from additive or alpha-blended KMF materials
  (`ClassicMeshes.IsKmfBlended`) unless `HoverTint.CoversBlended` (Fear Trap, Hero
  Portcullis), for creatures, objects, doors and traps.
- `DungeonScene.PickHoverEntity` admits the store pile decoration, doors and traps;
  `WorldHoverText` reads a store pile as "N Gold" from its tile.
- `PlayerInputSnapshot` offers an own store pile as a `StoreGold` target carrying its tile; a
  click lifts it (§4), and `DungeonRunner` shows the pickup hand over it.
- The Hand's light aims at a store pile's point, its tile centre (the pile's `Spread` value
  is its heading; it has no offset), as at a simulation object (`DungeonRunner.AimHandLight`;
  the map-edge test of 0x0043BCB8 is `ClassicPointLight.FromHand`'s).
- Tests: `ObjectHoverHighlightTests`.

Deviations: the `_HIGHLIGHT` twin is drawn over the original, not instead of it, which
differs only at coin edges; a faded object's blend comes from its KMF material, not from
DK2's fade flags (§2).

## 6. Open follow-ups

The sparkle (§3), the lift (§4) and the pickup hand over a store pile are built (ADR-0534).

1. **Faded objects (§2).** Which draws carry flags 0x2001 or 0x2, and so lose the red on
   every material while fading, is unread.
2. **The status flower's hover stamp.** It reads the same hover id (0x0043B7F1); ours stamps
   any hovered creature in any tool. In Hand mode it should follow `HoverTint`.

## 7. Owner answers and open questions

Answered from play, 2026-10-23:

- The red goes only to the keeper's own creatures, doors and traps, and to gold chests,
  piles and urns; never a rival's or a neutral one.
- Own creatures and gold get the pickup hand; doors and traps do not.
- Only materials that are not see-through are tinted: the coin cards and a creature's
  translucent parts are not. This corrects the same day's earlier answers (translucent parts
  tinted, chest pink as a whole), which the owner withdrew after seeing ours tint the cards.
- "1000 Gold" is the amount in that chest's tile, not the room's.

Open (the sparkle, lift and pickup-hand questions are Q641):

1. Does the glint come only on Treasury and Heart chests and urns, never on a loose pile,
   in short bursts at random moments?
2. Lifting from a Treasury chest takes the tile's whole amount and leaves no chest (§4, binary).
   Does a lifted chest, dropped back on the Treasury, go back in?
3. ~~Is an own chicken, crate or spell book tinted?~~ Yes (owner, 2026-10-23), as the binary says.
4. Is a rival's door tinted when pointed at?
5. Does an own Fear Trap or Hero Portcullis show red on its translucent parts too (mode 1)?
6. Does a fading creature lose its tint entirely while it fades?
