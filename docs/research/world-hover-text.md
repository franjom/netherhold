# World hover text

Sources: DKII.EXE v1.70, read-only PyGhidra queries, 2026-10-18; the install's
`Data/Text/Default/Text.str`, font files and EngineTextures cache. Binary output
stays under `reverse-engineering/hover-*`. Function records carry the verdicts.

## Two independent text paths — CONFIRMED

The top strip callback `0x0042E140` runs the world status producer
`0x0042D1F0`, or the GUI status producer when a widget owns the pointer. The
world path checks tile exploration before entity details. Entity hit kinds
2/3, 4, 5, 6, 7 select creatures (class 0), objects (2), doors (4), dead bodies
(6) and traps (3); the live thing must have that class, and its vtable +0x4C
formatter writes the status. Shots have no hit kind. An object with no
definition name string falls through. Without an eligible entity, a room takes
precedence over terrain. A tile with byte +0xB bit 0 reads 2481 / 2480 ("Enemy
Wall") in place of its terrain text, with no owner test. That bit is the
secret-door flag (`0x00450FF0`): set for a Secret Door by the level loader and
by door installation (state 37), cleared by the door's reveal in `0x004A55F0`,
its removal `0x004A5290`, its sale `0x004C3670` and an enemy Imp's state 39.
The Hand resolves only the viewer's own door (`0x005992B0`), so another keeper
hovering an unrevealed Secret Door reads Enemy Wall. The presentation reads the
existing `TileState.SecretDoor` flag (ADR-0536); automatic opening reveal remains
outside that simulation implementation. Unexplored terrain takes
the fog description.

The near-cursor popup renderer `0x0042E590` calls a different producer,
`0x0042D4A0`: short names, rather than the status description. That producer
also supports creatures: a definition's special personal-name string at
runtime `+0x895`, falling back to its type name at `+0x6AA`. `+0x895` is the u16
at Creatures.kwd file `0x14DF` (`0x00568E30` to staging `+0x18CF`, `0x00492320`
to the definition), `CreatureDef.SpecialNameStringId`. It is non-zero for the
thirteen uniques (1765..1777, "Bzzzt".."Zachariah"), the three princes
(1778..1780) and King Reginald (2396), whose type names are also their own. The original does
not exclude creature short-name popups. Objects require nonzero NameStringId;
otherwise the tile's room or terrain name wins. The user's preference is to
preserve this researched behavior (2026-10-18).

`0x00419DF0` schedules a world popup when the hover identity changes. The
renderer waits **strictly more than 1500 milliseconds**; GUI tooltips wait
250 milliseconds. A forced-tooltip setting bypasses the delay. Cursor movement
within one target does not itself reset this identity timer. The original
identity word is the entity/room tag, or x+y+1 for terrain (collisions possible).

## Creature status — CONFIRMED

`0x0048D880` chooses Text.str **2841** for ordinary owned creatures. Its
substitutions, using the string reader's one-based token spelling:

| Token | Meaning |
|---|---|
| `%30` | Creature type name, runtime definition `+0x6AA` |
| `%29` | Personal name |
| `%31` | State label selected by `0x00478090`, fallback 2599, idle |
| `%74` | Efficiency as an integer, followed by a literal percent sign |
| `%33` | Mood: 2340 Happy, 2342 Unhappy, 2344 Angry |

The template labels these fields Name, Job, Efficiency and Mood, in that order
after the creature type. A movement-category state displays the label of its
utility destination state, not the generic walk state. Efficiency is the
fixed-point sum of base and bonus, multiplied by 100, truncated toward zero,
then **one added**. The ordinary constructor value `0xCCC` displays 80.

Hero-owner and neutral-owner creatures use templates 2412 and 2415. Other
nonowned keepers' creatures return empty status. Owned IsHorny creatures use
2793, the volatile mood sentence. Personal names use the indexed name pool,
with separate special-definition and multiplayer paths: the Lord of the Land
(creature29) takes a campaign map-slot string (mapping in [the formatter](functions/0048D880.md)); otherwise a
non-zero `+0x895` replaces the pool name, so Unique Goblin reads "Name: Grubb". The formatter accepts
the resolved identity from the asset provider; it consumes no RNG.

## Personal identities — CONFIRMED

Constructor `0x004872A0` calls `0x00491900` and `0x004919C0` after linking the
new creature to its owner. They clear name index `+0x4ED` and blood-group index
`+0x4EF`, draw once from the main world RNG with bounds 500 and 6 respectively,
then search that owner's creature chain for a free index. Collisions increment
and wrap at 500 or 7; retries draw nothing. The cleared current creature also
participates. Exhausting the pool retains the original random candidate.
Retained corpse objects are outside that owner creature chain and do not reserve
either index; our retained-state model excludes creatures with no active state.
The ordinary executable stores 500 UTF-16 name pointers at `0x0069A118`, then
seven blood-group pointers at `0x0069A8E8`. `CreatureNamePool` reads these from
the installed binary through the identity asset abstraction; no strings are
bundled. The initial-map construction boundary is ADR-0521.

## Rooms and objects — CONFIRMED

`0x004E9250` selects **2579** for an owned Dungeon Heart: heart-object health
percentage and the keeper's mana/ceiling as `%37`, `%40`, `%41`. It selects
**2790** for a Portal: room health, keeper-wide portal count and creature
capacity as `%37`, `%44`, `%43`. These are the live status templates, rather
than the older room-definition tooltip strings 2431 and 2433. A nonowned room
uses 2471 (details unknown), except hero-owned room types 18–23 use 2468 (hero
entrance warning). `2471` also happens to be a shipped bridge definition's
tooltip; its use here is a separately confirmed status branch.

Ordinary owned rooms use the following native templates and live tokens. All
include health `%37`; room-count tokens count only the viewing keeper's rooms.

| room | template | other tokens |
|---|---:|---|
|Treasury|2581|75 stored gold,76 gold capacity,47 count|
|Lair|2435|38 occupied beds,39 bed capacity,45 count|
|Hatchery|2580|46 count|
|Library|2582|77 researchers,78 researcher capacity,38 books/specials,39 shelf capacity,48 count|
|Training|2583|38 holders,39 capacity,49 count|
|Workshop|2584|38 holders,39 capacity,79 crates,80 stored crate capacity,50 count|
|Guard Room|2585|81 assigned guards,82 capacity,52 associated Guard Posts,51 count|
|Prison|2586|83 holders,84 safe capacity,56 maximum Skeletons,57 open/closed,85 count|
|Torture|2451|38 holders,39 capacity,54 count|
|Temple|2459|38 holders,39 capacity,62 count|
|Graveyard|2587|38 lying corpses,39 floor capacity,86 maximum Vampires,61 count|
|Casino|2781|38 holders,39 capacity,87 payout description,63 count|
|Combat Pit|2449|38 fighters on floor,39 floor slots,53 count|
|Wood/Stone Bridge|2463/2465|health only|

Library shelf occupancy counts spell-book objects and objects with the Special
flag in its stored-object chain; shelf capacity is twice the number of
auto-placed type64 bookcases. Its researcher figures come from room+0x6B/+0x67.
Workshop crates use the Crate type flag and stored-object chain. Guard routes
are completed owned traps with definition flag0x800 and an associated room id.
The adapter maps that association through TrapState.GuardRoomTile.
The Prison's status uses Activation at+0x59, mapped to JailBreakRules.Activated,
and strings2454/2455; it does not test the independent door-open bit. Casino
payout nests2508 or2509 from its lever setting. The Combat Pit counts uncarried
fighters only on floor slots. These are facts from the complete0x004E9250 read.

The adapter uses existing capacity/holder rules and object state. Workshop
display capacity+0x59 is produced by0x004E3ED0: three places for every slab
that is neither a floor furniture slot nor a wall furniture slot. The adapter
uses AutoFurnitureRules.FurnitureSlots and CrateRules.CratesPerTile. Guard
assigned count reads stored+0x6B, mirrored by Occupants; it does not add the
separate guard-duty extra used only by the capacity-test function.
Missing Casino lever state displays `?`.
The Hatchery room template has no food-count field; chicken and fixture hover
are separate object branches. A ruined Heart returns empty ordinary status.

`0x0049C8C0` uses object definition TooltipStringId normally. Gold types 1/2
use 2544 and type 3 uses 2543, substituting its gold amount at `%73`. Its jump
table (types 1–116) has two fixture arms, both reading the room tagged at
object +0x138: the Casino lever 95 reads 2508 when the room's payout setting
(`0x004D6880`) is 0, else 2509; the Prison bolt 116 reads 2503 when the room's
activation +0x59 is 0, else 2504. With no linked room the status is empty.
Ours links the room the object stands in and reads the lever's mirrored
Setting; the bolt is not spawned by our simulation yet. Crates, chickens,
spell books and other objects use the default tooltip. Object
NameStringId is a separate field and controls short popup eligibility.
A hovered store pile (the Treasury or Heart gold decoration) reads "N Gold"
(string 2543, the tile's gold), as a type 3 object would; see
[gold-hover-and-sparkle.md](gold-hover-and-sparkle.md) §5.

## Doors, traps and dead bodies — CONFIRMED

Read 2026-10-23 from the vtable slots +0x4C. Tokens are raw id + 1, as above.
Health `%37` is the 12.12 ratio 100 / definition health (`0x00441990`, IDIV,
truncating; 100 when the health is 0) times current health, shifted right 12
toward zero. A 2000-health Wooden Door at full health therefore reads **99%**
(not yet compared on screen).

| thing | formatter | blueprint | other keeper | owned |
|---|---|---|---|---|
| door | `0x004A68C0` | 2512, `%68` name | bare name | barricade (def+0x8B bit 0x40): 2536 `%37`; else 2532 `%68 %37 %72` |
| trap | `0x004A9B70` | 2512, `%68` name | bare name | Guard Post (flag 0x800): 2538; else 2527 `%68 %37 %25 %26 %71` |

Door `%72` is 2515 when lock mode +0x111 is 0, else 2516. Trap `%25` is the
signed upkeep word def+0x96, `%26` mana to fire def+0x9A, and `%71` is always
2514 ("Concealed until triggered."). Both templates end `%72.` / `%71.`, so the
sentence's own full stop doubles (read from the code, not seen on screen: Q624). The "Enemy Door/Trap" strings 2540/2542 are not
used by these formatters. Names: door def+0xA1 (record 0x23C), trap def+0xA8
(record 0x208), now `DoorDef`/`TrapDef.NameStringId`. The popup uses the same names.

**Which arms the Hand can reach (read 2026-10-23).** The status producer takes its hit kind
from interface `+0x1084`/`+0x1088`, the kind and id of the pick struct at `+0x1068` that
`0x0040BFF0` hands to the view's pick (vtable +0x48, `0x005992B0`); the only other store is
`0x0040C600`'s 300 ms restore of a saved kind 2 or 3. `0x005992B0` gives kind 5 or 7 only to a
door or trap **the viewer owns** (thing `+0x28`): another keeper's leaves the tile hit, so the
"other keeper" column above is unreachable through the Hand, and ours now does the same
(`HoverPickRules.Resolves`). It gives kind 6 only to a class-6 thing that won the rendered
first pass, and no dead body is ever in the rendered list ([0x0059B880](functions/0059B880.md)
clears its tag; [0x0059D260](functions/0059D260.md) passes none), while the fallback
`0x0044DEC0` returns only creatures and objects. **So 2791 is unreachable through the Hand
pick**; any other source of kind 6 is unread (Q634).

The dead body (`0x004B1740`) formats 2791, "It's the dead body of %29 a level %88
%30.": `%30` its creature type name, `%88` its level +0x12D. `%29` is filled only
when the definition's special name +0x895 is non-zero, and then with the type
name `+0x6AA` of the definition for the body's creature id `+0x128`, the one
`+0x129` was looked up from (`0x004B0F20`), so "the dead body of Goblin a level 3
Goblin" for Unique Goblin, not "Grubb". Otherwise this function never writes that
buffer, so what DK2 prints there is unread, and ours prints it empty (a GUESS,
Q634). No owner test. The popup name is the type name, with no +0x895 override. `WorldHoverText` formats it for `CreatureDeathRules.IsCorpse`, but our picker,
like DK2's, never returns a corpse, so it is kept unreachable.
The shot formatter `0x004B5C20` writes an empty string.

## Original fonts, artwork and geometry

**CONFIRMED:** status and popup both copy style `0x00741320`, the white
base-10 in-game BF4 font; font width selection is already described by
`InterfaceFonts`. Catalogue `0x006A7DF0`, stride 69, records 72/73 name
`gui/Windows/Bar-00` and `Bar-01`. Their runtime stride is 27. The top strip
tiles the first resource across the screen and draws the second at its right
edge; its virtual draw size is 512×88. Long status text scrolls back and forth.
Its text widget `0x00693318` has local draw rectangle `(0,0,1440,88)` in
menu53 (`0x00693EB0`), whose rectangle is `(1020,20,1540,88)`. The native
text therefore starts after the resource region at virtual x1020, rather than
at the left screen edge. Its 1440-wide text area leaves 100 virtual units for
the right-end decoration. The background's tiling across the screen is
separate from this positioned text rectangle.
The highest cache images are Bar-00MM0 **128×64** and Bar-01MM0 **32×32**;
text_box-tlMM0 and text_box-mMM0 are **16×16**.

`0x00411D00` builds the popup frame from catalogue entries 19–27:
`gui/windows/text_box-{tl,t,tr,l,m,r,bl,b,br}`. The cache retains these nine
pieces and their alpha. Popup rendering starts about 98 reference pixels to
the cursor's right at width 640, flips left near the right edge, and clamps
vertically. Exact piece scaling and all layout helper semantics remain
**UNKNOWN**; the Unity renderer uses a retained nine-slice approximation. DK2 scales
the Hand by width/640 too; ours scales the Hand by height/480, so the adapter scales
the 98/30/40 offsets by height/480 to keep the gap the Hand's size (owner, 2026-10-19). The 98 is read as the
Point hand's reach (82 wide, hotspot 2) plus 18; over the HUD the 42-wide small pointer uses 58 (owner, 2026-10-20). Since the claw tip went on the pointer (owner, 2026-10-24, [hand-cursor-rendering.md](hand-cursor-rendering.md)), the reach is `HandCursor.Reach`, the frame width minus the least tip X over the loop: Point 82 (offset 100), SmallPoint 42 (offset 60), Idle 83 (offset 101).

## Implementation limits

`WorldHoverText` captures immutable strings only on settled ticks or target
changes. The Unity UI Toolkit renderer never reads simulation state and reads
assets through `IClassicHoverAssets`. It retains status/tooltip images and
rasterizes original BF4 text only when strings, fonts or wrapping change.
The wooden bar and its Bar-01 cap sit in their own panel (order 10), from x0 across the
screen under the resource panels (11); the text clip stays at x1020 and stops before the cap.
The cap is a 128-unit square at (W-80, 0), taller than the 88-unit bar ([0x0042E140](functions/0042E140.md)).
Its status clip scrolls the retained text image; nine retained images form
the popup frame. Its physical-pixel panel keeps the approved font readable.

DK2's strip and popup share style 0x00741320, whose file DK2 picks by width through
0x00553690 (base 10 at widths other than 400/512/800/1024; 14 at 1024). Ours is the
owner's choice, not DK2's: the strip uses the largest shipped size within height/60,
vertically centred, and the popup half that, height/120. On 2026-10-19 the owner
approved that size and rejected a DK2-proportioned height/96, top-aligned trial. Scrolling is time-based at 60 pixels
per second, approximating the original's one-pixel-per-callback motion.
Ordinary room templates, doors, traps, dead bodies and the Casino/Prison
fixture branches are built as described above; the dead-body line and the
other-keeper door and trap names are formatted but, as in DK2, no pick reaches
them. The special creature name +0x895 is built (status `%29`, popup, dead-body
`%29` gate), the Lord's campaign-slot name and the tile concealment flag's
Enemy Wall status/name are built. The resolver's object
ownership arm (`0x005992B0` kind 4) is built as `HoverPickRules.ResolvesObject`
(hand-cursor-rendering.md). Unsupported substitutions are omitted rather
than displaying raw localization tokens. See `fog-of-war.md` for fog assets.

The Unity integration captures text after presenting settled poses and before
dispatching worker ticks; retained UI is refreshed before the dispatch too.
OnGUI does not draw this HUD. Picking first tests
Unity renderer bounding boxes up to the picked terrain plane, an approximation
of DK2's rendered hit result. Its fallback scans retained visible poses in a
row-major 3×3 tile neighbourhood, with the researched octagonal distance:
first creature within half a tile, otherwise closest object within a quarter
tile. Within-tile ordering follows retained presentation dictionaries, not
DK2's thing-chain order; interpolated Unity positions supply the distance.
Decorative objects without simulation ids are not entity picks; they fall back
to room or terrain text. Doors and traps are, but only the viewer's own resolve
(`0x005992B0`); another keeper's leaves the tile. The triangle test takes both
windings, where `0x00585AD0` takes one whose mapping onto ours is unread, and a
renderer without a readable MeshFilter mesh gets the centre test only. World tooltips use entity identity, a
room's canonical first tile or the terrain tile itself; this preserves timing
within a room but does not reproduce native tag reuse or terrain x+y collisions.
The asset provider currently uses `Text/Default`, consistent with the existing
subtitle provider; selecting another installed locale remains unimplemented.

Display and Hand/Slap input share `ResolveWorldHover`. It checks the cursor
tile's exploration before entity picking and retains that cursor tile beside
the selected entity id, matching the native status gate even when the entity
stands on a neighbouring tile. `PlayerInputSnapshot.ForHover` narrows the
already permitted target list to that selected kind/id and uses the cursor
tile for the existing translator. It does not admit hidden, held, carried or
otherwise filtered objects again, and does not change simulation commands.
