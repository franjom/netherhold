# Creature hover tint and status flowers

Read 2026-10-19 from DKII.EXE (research agent, checked in part by claude) after the owner
compared Unity with DK2: pointing the Hand at a creature tints it pink-red and, in the owner's
screenshots, its status flower appears over its head.

## 1. Hover tint (CONFIRMED, blend on meshes STRONGLY INFERRED)

`Presentation_Update` ([0043B220](functions/0043B220.md)) at 0x0043BE40-0x0043BF31:
- It reads the hovered thing id from bridge `+0x34`, which the cursor selector 0x0040ACD0 publishes.
- The thing's vtable slot `+0x34` must return non-zero (for creatures, 0x00628BC0 returns 1).
- It then saves the RenderInfo flash fields and calls [004B45B0](functions/004B45B0.md) with:
  - the material global 0x006FCD73, `AA_RedTexture` (additive ONE/ONE, no z-write, colour (255,64,65); [tag-highlight.md](tag-highlight.md) §6);
  - period 0;
  - mode 1 only for a trap or door with `+0xF6` == 8, otherwise 0 (which sets `+0x4C` bit 0x80).
- It sets RenderInfo RGB `+0x48..+0x4A` to 255.

After the world render, 0x0043C14F-0x0043C1EB restores the saved fields, so only the frame's current hover target is tinted.

[Draw_FlashOverride](functions/0059D4B0.md) hands the material to the mesh draw as an overlay, with flag 0x200 (0x600 in the door/trap mode). Period 0 means there is no pulse. This is not selection kind 3: the tile outline's red channel is a different path.

Who gets the tint is whatever the picker publishes, own creature or enemy ([hand-cursor-rendering.md](hand-cursor-rendering.md)). The adapter tints creatures only until the slot `+0x34` of doors, traps and objects is read (the door/trap mode above suggests they tint too), and only in Hand mode off the HUD, a gate DK2's code here does not show. That the overlay on an animated mesh is the same two-pass additive draw as on terrain is STRONGLY INFERRED.

## 2. Status flower

### Assets

The assets are in `GUI\moods\` (EngineTextures; loader [00596850](functions/00596850.md)):

| Assets | What they are |
|---|---|
| H-00..H-05 | the petal ring: six petals down to one, painted pale pink-grey with white highlights |
| SL-01..SL-10 | pre-drawn level numbers |
| SM-Happy/Sad/Angry | moods |
| SJ-* | jobs and anger reasons; SJ-Guarding is STRONGLY INFERRED as the shield |
| ST-*, SD-* | traps and doors |
| Imprison, Objective, Objective-2, S-Gem | status icons |
| a "Dollar" mesh | the casino winner |

### Draw path (CONFIRMED)

1. [RenderSink_DrawThing](functions/00599C10.md) draws the body through 0x0059B880 unconditionally, then calls [Render_DrawThingOverlay](functions/005953D0.md).
2. The overlay returns at once unless the thing's slot `+0x5C` is true. For creatures that is [0048E830](functions/0048E830.md), the flower-visibility predicate.
3. Scale is 0x400, or 0x200 while possessing.
4. Then:
   - the height (0x005955C0);
   - the colour (0x005956E0);
   - the ring (0x005958F0, then 0x00596EB0; the ring index is unread);
   - the creature centre (0x005961A0).

### When an own creature's flower shows (0048E830, CONFIRMED)

There is a global gate on viewer player `+0x76` bits 0x400000/0x40 and mode 0x0075AD4B.

| Case | Flower |
|---|---|
| Own creature whose def `+0x686` has bit 0x4000 | never |
| State `+0x25E` 0xC1 or 0x110, or `+0x3E6` bit 0 (InHand) | never |
| While possessing (tested before the rest) | only the creature whose id equals the hover id |
| `+0x3E6` bit 0x200000 (CreatureDying), own included | always |
| Enemy or neutral (not possessing) | always |

An own creature shows its flower when any of these hold:
- it is in combat (category 22);
- its mood is Unhappy or Angry;
- health is below max/6 (`health < max·100/600`, unsigned);
- the game turn is ≤ `+0x4B7` (set by a trigger at 0x0049AF1E);
- `+0x4B3` is set: a removal countdown showing SL-05..SL-01 (0x0048F110);
- the turn is before `+0x38` (strictly), read by [004B34F0](functions/004B34F0.md) for creatures, doors and traps.

Nothing in the predicate refuses a prisoner: an enemy in the viewer's prison shows its flower.

`+0x38` has two writers:
- Thing_ModifyHealth (0x004B2CE0, stores at 0x004B3267-0x004B3280) sets it, and `+0x3C`, to now + ¾ s on any health change. A turn is ¼ s (container `+8` = 4), so ¾ s is 3 turns.
- **Presentation_Update stamps the Hand's hover target** at 0x0043B7F1-0x0043B857 (CONFIRMED, claude). It reads the hover id from bridge `+0x34` (the same id as the tint). For a creature, door or trap (`+0x12` of 0, 3 or 4) it writes `+0x38 = turn (0x00517490) + (ticks-per-second·3)>>2`.

So pointing at any creature shows its flower, and the flower lingers ¾ s after the pointer leaves, as the owner's screenshots show.

### Centre, colour, height

**Centre.** 0x00596410 cycles the centre icon every 0x14D (333) units; milliseconds is STRONGLY INFERRED.
- Heroes: their level, or a status icon when 0x00597270 finds one.
- Own creatures: level, then status; or mood, then anger reason; or job (0x00596FC0, unread), then level.
- In level mode the badge is drawn at half colour, then lit across an angle set by experience progress `+0x3FF` / def `+0x6B5` (0x005966B0, 0x0048EA40).
- The casino winner (`+0x3F2`) shows the Dollar.

**Colour** (0x005956E0):
- The owner's player colour (world slot `+0x290`), or the disguise when sneaking.
- Neutral creatures cycle palettes at 0x006E5058 every 333.
- It blinks on a 333-unit triangle wave below max/6 health, or for ¾ s after a health change (`+0x3C`).

**Height** ([005955C0](functions/005955C0.md)):
- Creature vtable `+0x40`, plus level × 0x600/10 (0.15 tile per level).
- Sleeping (category 15): +0x580, or +0x280 on a bed slot.
- +0x80 if 0x00491B40 ≠ -1.

The flower is a world sprite that scales with distance. Facing the camera is STRONGLY INFERRED.

## 3. Built (passes 1 and 2)

`StatusFlowerRules`, `StatusFlowerCentre` and `StatusFlowers` (src/DK2.Presentation/StatusFlowers.cs) decide who shows a flower and what it shows; `ClassicStatusFlowers` (Unity) draws the ring and the centre as camera-facing quads. Both ¾ s windows live in presentation, keyed by creature id: the hover from the Hand's hover id in any tool, the health change from the health seen at each capture. Nothing simulated reads the native `+0x38`.

Read and built (pass 2, 2026-10-20):
- **Ring** ([00596EB0](functions/00596EB0.md)): `H-0n` with entry `clamp(1 − trunc(−4·health/max), 0, 5)`, the loader filling entry i with `H-0(5−i)` ([00596850](functions/00596850.md)). Six petals at full health, one fewer per quarter lost, one while dying.
- **Colour** ([005956E0](functions/005956E0.md)): the owner's palette entry. Neutral creatures cycle entries 1–7 every 333 ms; others blink, a triangle over one 333 ms phase shared by every flower, below a sixth of health or for ¾ s after a health change. The clock is real milliseconds ([005953D0](functions/005953D0.md)).
- **Centre cycle** ([00596410](functions/00596410.md)), per creature every 333 ms: level, then a status if any; an Imp its job unless idle; others the mood when unhappy or angry (then the anger reason), else the job; everything returns to the level. The level badge is drawn at half colour with a full-colour wedge for the share of the way to the next level ([005966B0](functions/005966B0.md), [0048EA40](functions/0048EA40.md)), drawn clockwise from the top as both captures below show; every other icon plain at full colour ([005961A0](functions/005961A0.md)). Both owner captures of 2026-10-20 (a knocked-out creature, a prisoner) show the tinted disc and the wedge.
- **Job icons** ([00596FC0](functions/00596FC0.md)) by resolved category and worker state, No_Go within 5 s of a drop on a room; **status icons** ([00597270](functions/00597270.md)) Unconscious, Stunned, Fear.
- The owner confirmed (2026-10-20) that a knocked-out creature keeps its flower, own or enemy, and that an imprisoned enemy shows one; both follow from [0048E830](functions/0048E830.md).

Approximated:
- The ring is coloured by `saturate(2·luminance)` × the colour, not by the texel: the owner's screenshot shows flat petals at about 0.96 of the palette entry (221,49,49 against 230,50,50), where a plain multiply of the pale H-xx art gives a dull grey-red. The centre is a plain multiply. The engine's sprite blend is unread.
- The flower is 0.36 tile across: 0.4 eyeballed from the owner's screenshot, then 10% smaller on the owner's comparison. It is halved while possessing.
- The body height is taken to be the definition's `Height`, as vtable `+0x40`.
- A health change seen at settled turn T is taken to be stamped at T. Native turn numbering is not matched.

Left out:
- The global gate (player `+0x76`, mode 0x0075AD4B), the trigger window `+0x4B7` and the removal countdown `+0x4B3` ([0048F110](functions/0048F110.md), drawn as SL-05..SL-01 with a flashing ring): our state has none of them.
- The sleeping and 0x00491B40 lifts, the sneaking disguise colour.
- Status icons Leaving (`+0x3E6` bit 14, unmapped) and S-Gem (carrying object type 0x1C, [0048B5C0](functions/0048B5C0.md)); the hero side's status list; the Imprison tag (`+0x3F7`); the hero extra 0x00596770; the casino winner's Dollar mesh.
- A possession hover. The adapter has no possession camera, so while possessing, only the creature under the ordinary Hand pointer shows a flower.

Inferred, not read: `+0x3E6` bit 0x200000 ("always") is mapped to our falling/dying states or
health ≤ 0, but 0x004B2CE0 sets it with a positive `+0x40` on collapse, so the bit is not
"health ≤ 0"; the corpse refusal is our model's guard.

## 4. Open

- The owner's first screenshot (2026-10-20) shows a red keeper's level badge white, where the code and two later captures tint it. It was taken with the Hand on the creature; whether hovering changes the centre's colour is unread.
- The wedge's orientation in the code: 0x005966B0 anchors the full-colour arc at angle 0x400 of a 0x800 turn, and which screen direction that is depends on 0x00594E10 and the quad's texture order, unread. The build follows the captures.
- The flower sprite's blend.
- The flower's world size, and whether it is anchored at its centre or its base.
- What `+0x4B7` (trigger window), `+0x4B3` (removal countdown), `+0x3E6` bit 14 and state 0x110 map to in our state.
