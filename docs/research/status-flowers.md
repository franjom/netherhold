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
| H-00..H-05 | the petal ring |
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

## 3. Built (pass 1)

`StatusFlowerRules` and `StatusFlowers` (src/DK2.Presentation/StatusFlowers.cs) decide who shows a flower and give its level, owner colour, lift and blink flag. `ClassicStatusFlowers` (Unity) draws the ring and the level badge as camera-facing quads. Both ¾ s windows live in presentation, keyed by creature id: the hover from the Hand's hover id in any tool, the health change from the health seen at each capture. Nothing simulated reads the native `+0x38`.

Approximated:
- The ring is always H-00. The badge is drawn at the ring's texel density, ring and badge are alpha-blended, and the flower is centred at the lift.
- The flower is 0.4 tile across, eyeballed from the owner's screenshot; it is halved while possessing.
- The colour is the owner's `ClassicKeeperPalette` entry. Neutral creatures do not cycle, and a sneaking creature shows no disguise.
- The body height is taken to be the definition's `Height`, as vtable `+0x40`.
- A health change seen at settled turn T is taken to be stamped at T. Native turn numbering is not matched.

Left out:
- The global gate (player `+0x76`, mode 0x0075AD4B), the trigger window `+0x4B7` and the countdown `+0x4B3`: our state has none of them.
- The sleeping and 0x00491B40 lifts.
- Blink drawing and the cycling centre (mood, job, status, the Dollar).
- A possession hover. The adapter has no possession camera, so while possessing, only the creature under the ordinary Hand pointer shows a flower.

Inferred, not read: `+0x3E6` bit 0x200000 ("always") is mapped to our falling/dying states or
health ≤ 0, but 0x004B2CE0 sets it with a positive `+0x40` on collapse, so the bit is not
"health ≤ 0"; the corpse refusal is our model's guard.

## 4. Open

- The ring index's argument.
- The job choice in 0x00596FC0.
- The units of the 333 clock.
- The flower sprite's blend.
- The flower's world size, and whether it is anchored at its centre or its base.
- What `+0x4B7` (trigger window), `+0x4B3` (removal countdown) and state 0x110 map to in our state.
