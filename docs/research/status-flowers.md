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

Who gets the tint is the Hand's hover id, which Hand_UpdateHoverTarget 0x0040C290 publishes per class for the viewer: a creature the viewer may pick up, an own pickable or slappable object or loose gold on own land, any door, a trap on an own or neutral tile (CONFIRMED). So a rival's, neutral or hero creature is never tinted. The owner (2026-10-23, play): own creatures, doors and traps and gold are tinted, never a rival's or a neutral one; both mesh draws skip the pass on additive and alpha-blended materials unless the draw flags hold 0x400 (only a Fear Trap or Hero Portcullis), so a creature's translucent parts stay untinted: CONFIRMED by binary and owner (2026-10-23, correcting the owner's earlier answer); see [gold-hover-and-sparkle.md](gold-hover-and-sparkle.md) §2. The adapter tints by that rule (`HoverTint`) only in Hand mode off the HUD, a gate DK2's code here does not show. That the overlay on an animated mesh is the same two-pass additive draw as on terrain is STRONGLY INFERRED.

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
   - the ring ([005958F0](functions/005958F0.md), then 0x00596EB0);
   - the creature centre (0x005961A0).

**Size, anchor, blend (size CONFIRMED; anchor and blend inferred; read 2026-10-23).** Every mood icon is loaded as a type-2 sprite
with descriptor size 0x1000, or 0x2000 for Imprison and both Objectives, on an `A_` material
([00596850](functions/00596850.md); only its kind-2 argument is read, so that the material is
`A_` is inferred): SRCALPHA/INVSRCALPHA, depth-tested, no depth write, texture
times vertex colour ([0057C420](functions/0057C420.md), [00589250](functions/00589250.md)). The
billboard draw [0059C360](functions/0059C360.md) makes it scale × descriptor in 12.12 over 4096:
**a quarter tile across** (half for the large three; halved again while possessing), a square.
Whether the overlay's sprite takes the tile's light list (Q609) is unread.

**Every layer is centred on one point (read 2026-10-24).** The ring ([005958F0](functions/005958F0.md)),
the centre and Imprison tag ([005961A0](functions/005961A0.md)) and the Objective
([00596770](functions/00596770.md)) all draw through one RenderInfo (flower object `+0x3D`) at one
position (`+0x25`/`+0x31`); only the descriptor at `+0xED` changes. The descriptor
([00596850](functions/00596850.md)) holds a size, a type and a material, no origin, hotspot or
UV rectangle (CONFIRMED). The sprite path ([0059C360](functions/0059C360.md)) offsets the quad by
RenderInfo `+0x57`/`+0x59`, and the half-height move needs art flag 0x40, which no mood art has; no
store to `+0x57`/`+0x59` (`+0x94`/`+0x96` of the flower object) exists in the flower code
(StoreAt over the binary), so they stay at their initial value, LIKELY 0. The quad
([0057F3D0](functions/0057F3D0.md)) is ±w/2 by ±h/2 about those offsets with UVs (0,0), (1,0),
(1,1), (0,1): the whole texture, no sub-rectangle (CONFIRMED). The level badge's fan
([00595990](functions/00595990.md)) pivots on UV (0.5, 0.5) at the same point, so the wedge turns
about the texture centre, as our shader's does. So DK2 places a centre icon with no offset, and
layers the icons only by draw order (no depth write).

The 32×32 centre icons are centred art: the disc of SL-01 and SJ-Fighting (opaque, bright) spans
x 6..25, y 6..25 of the decoded image (row 0 at the top), centre (15.5, 15.5). What makes their
alpha centroid (14.3, 17.5) sit low-left is a soft black drop shadow (alpha up to about 200)
painted to the lower left of the disc, which DK2 draws too. SHOT0068 shows the Lord's "1" disc
concentric with his star.

Ours was off-centre because it lifted each later layer 0.01 tile toward the camera **along the
view axis** (centre 0.01, Imprison 0.02, Objective 0.03) to sort them. Off the view axis such a
step moves a sprite sideways on screen by the lift × (lateral offset / depth): at the side of the
view, about 0.016 tile between the ring and the star, two texels of the 64-texel star, whose hole
the ring exactly fills, so the points touched the ring on one side and left a gap on the other.
Ours now lifts along the ray to the eye and scales by the depth ratio
(`StatusFlowerRules.LayerPlacement`), which keeps every layer on the anchor's screen point at its
own screen size.

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
- Heroes: their level, or a status icon when 0x00597270 finds them in a world slot (trigger action 42): SJ-Fighting, SJ-Prison or SJ-Torture by the slot's value. A hero in a slot also gets the `Objective` ring over its centre, loaded at 0x2000 against the centre's 0x1000, its drawn size matched to SHOT0068 (0x00596770; `Objective-2` when its `+0x3F7` holds the viewer's bit). Built 2026-10-21 (ADR-0523); Level 1's Lord is the shipped case, his star cycling "1" and the sword in SHOT0075/0076.
- Own creatures: level, then status; or mood, then anger reason; or job (0x00596FC0, unread), then level.
- In level mode the badge is drawn at half colour, then lit across an angle set by experience progress `+0x3FF` / def `+0x6B5` (0x005966B0, 0x0048EA40).
- The casino winner (`+0x3F2`) shows the Dollar.

**Colour** (0x005956E0):
- The owner's player colour (world slot `+0x290`), or the disguise when sneaking.
- Neutral creatures cycle palettes at 0x006E5058 every 333.
- It blinks on a 333-unit triangle wave below max/6 health, or for ¾ s after a health change (`+0x3C`).

**Height** ([005955C0](functions/005955C0.md)):
- Creature vtable `+0x40`, plus level × 0x600/10 (0.0375 tile per level).
- Torture (category 15): +0x580 when the cell under it is not a floor slot, else +0x280, plus
  0x400 more while 0x00759ADC is 0. That global (23 reads, no direct store found) also gives the
  Torture Wheel and Electric Chair their `g_` art unless it is 1. Its vanilla value is unknown
  (Q635): a zero-initialised global would be 0.
- +0x80 when 0x00491B40 finds the creature in a world slot (trigger action 42).

## 3. Built (passes 1 to 3)

`StatusFlowerRules`, `StatusFlowerCentre` and `StatusFlowers` (src/DK2.Presentation/StatusFlowers.cs) decide who shows a flower and what it shows; `ClassicStatusFlowers` (Unity) draws the ring and the centre as camera-facing quads. Both ¾ s windows live in presentation, keyed by creature id: the hover from the Hand's hover id in any tool, the health change from the health seen at each capture. Nothing simulated reads the native `+0x38`.

Read and built (pass 2, 2026-10-20):
- **Ring** ([00596EB0](functions/00596EB0.md)): `H-0n` with entry `clamp(1 − trunc(−4·health/max), 0, 5)`, the loader filling entry i with `H-0(5−i)` ([00596850](functions/00596850.md)). Six petals at full health, one fewer per quarter lost, one while dying.
- **Colour** ([005956E0](functions/005956E0.md)): the owner's palette entry. Neutral creatures cycle entries 1–7 every 333 ms; others blink, a triangle over one 333 ms phase shared by every flower, below a sixth of health or for ¾ s after a health change. The clock is real milliseconds ([005953D0](functions/005953D0.md)).
- **Centre cycle** ([00596410](functions/00596410.md)), per creature every 333 ms: level, then a status if any; an Imp its job unless idle; others the mood when unhappy or angry (then the anger reason), else the job; everything returns to the level. The level badge is drawn at half colour with a full-colour wedge for the share of the way to the next level ([005966B0](functions/005966B0.md), [0048EA40](functions/0048EA40.md)), drawn clockwise from the top as both captures below show and the owner confirmed (2026-10-20); every other icon plain at full colour ([005961A0](functions/005961A0.md)). Both owner captures of 2026-10-20 (a knocked-out creature, a prisoner) show the tinted disc and the wedge.
- **Job icons** ([00596FC0](functions/00596FC0.md)) by resolved category and worker state, No_Go within 5 s of a drop on a room; **status icons** ([00597270](functions/00597270.md)) Unconscious, Stunned, Fear.
- The owner confirmed (2026-10-20) that a knocked-out creature keeps its flower, own or enemy, and that an imprisoned enemy shows one; both follow from [0048E830](functions/0048E830.md).

Approximated:
- The ring is coloured by `saturate(2·luminance)` × the colour, not by the texel: the owner's screenshot shows flat petals at about 0.96 of the palette entry (221,49,49 against 230,50,50), where a plain multiply of the pale H-xx art gives a dull grey-red. The centre is a plain multiply. The blend itself matches DK2's (§2); a plain multiply would be DK2's too unless the sprite adds the tile's lights (Q609), which may be what brightens the petals.
- The body height is taken to be the definition's `Height`, as vtable `+0x40`.
- A health change seen at settled turn T is taken to be stamped at T. Native turn numbering is not matched.

Read and built (pass 3, 2026-10-23), each from state ours already keeps:
- **Removal countdown**: `+0x4B3` is our `ManaRemovalStamp`. It shows the flower; the centre is `SL-0(6 − step)` with step ([0048F110](functions/0048F110.md)) 1 at the stamp, one more a game second, 5 from five seconds; the cycle is not stepped; the ring shows only on the second half of each game second ([0048F0F0](functions/0048F0F0.md)).
- **Imprison tag and Objective-2**: `+0x3F7` is our `RescuePriorityMask`. With the viewer's bit set the Imprison icon is drawn over the centre ([005961A0](functions/005961A0.md)) and a slotted hero's ring is `Objective-2` ([00596770](functions/00596770.md)).
- **Leaving and S-Gem** ([00597270](functions/00597270.md)): S-Gem when the carried object is the Portal Gem (object 28, [0048B5C0](functions/0048B5C0.md)); Leaving in states 194–196. DK2's bit 14 is set on entering 194 and cleared only by a rebellion, so a leaver moved out of those states keeps its icon in DK2 and not here.
- **Lifts**: torture and +0x80 for a slot holder. The on-slot torture lift takes 0x00759ADC as 1, to match the plain device art ours loads; that is a GUESS, since the global has no writer found and may be 0 in vanilla, which would lift the flower 0x400 (a quarter tile) higher (Q635).
- **Size**: 0.25 tile from the descriptor, replacing the 0.259 the owner's three comparisons had reached by eye; icons are no longer sized by their texture width.

Left out:
- The global gate (player `+0x76`, mode 0x0075AD4B): unread meaning.
- The trigger window `+0x4B7`: creature trigger action 30 sets it to turn + seconds × the action value ([0049AC80](functions/0049AC80.md), arm 0x0049AF08). Ours does not run that action, so there is no field to read.
- The casino winner (`+0x3F2`, our `JackpotDeadlineTicks`): no ring, no centre, an animated `Dollar` mesh (kind 6) at render scale 0x1000. Needs animated-mesh playback in the flower draw; its size and rate are unread.
- The sneaking disguise colour.
- A possession hover. The adapter has no possession camera, so while possessing, only the creature under the ordinary Hand pointer shows a flower.

Inferred, not read: `+0x3E6` bit 0x200000 ("always") is mapped to our falling/dying states or
health ≤ 0, but 0x004B2CE0 sets it with a positive `+0x40` on collapse, so the bit is not
"health ≤ 0"; the corpse refusal is our model's guard.

## 4. Open

- The owner's first screenshot (2026-10-20) shows a red keeper's level badge white, where the code and two later captures tint it. It was taken with the Hand on the creature; whether hovering changes the centre's colour is unread.
- The wedge's orientation: [00595990](functions/00595990.md) puts angle 0 at camera +y and 0x400 at −y, and [005966B0](functions/005966B0.md) grows the full-colour arc from 0x400 by the share, so the wedge starts at the top and grows clockwise **if** camera +x is right and +y down (the billboard reading, [object-drawing.md](object-drawing.md) §10; 0x00594E10's matrix is not traced). LIKELY, and it agrees with the captures and the owner.
- What state 0x110 is.
