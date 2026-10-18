# World hover text

Sources: DKII.EXE v1.70, read-only PyGhidra queries, 2026-10-18; the install's
`Data/Text/Default/Text.str`, font files and EngineTextures cache. Binary output
stays under `reverse-engineering/hover-*`. Function records carry the verdicts.

## Two independent text paths — CONFIRMED

The top strip callback `0x0042E140` runs the world status producer
`0x0042D1F0`, or the GUI status producer when a widget owns the pointer. The
world path checks tile exploration before entity details. Entity hit kinds
2/3, 4, 5, 6, 7 select creatures, objects, doors, traps and shots respectively;
the live thing must also have its expected class. An object with no definition
name string falls through. Without an eligible entity, a room takes precedence
over terrain. Unexplored terrain takes the fog description.

The near-cursor popup renderer `0x0042E590` calls a different producer,
`0x0042D4A0`: short names, rather than the status description. That producer
also supports creatures: a definition's special personal-name string at
runtime `+0x895`, falling back to its type name at `+0x6AA`. The original does
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
with separate special-definition and multiplayer paths. The formatter accepts
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
bundled. The initial-map construction boundary is ADR-0510.

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
use 2544 and type 3 uses 2543, substituting its gold amount at `%73`. Casino
and prison fixtures have special room-state-dependent branches. Object
NameStringId is a separate field and controls short popup eligibility.

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
**UNKNOWN**; the Unity renderer uses a retained nine-slice approximation.

## Implementation limits

`WorldHoverText` captures immutable strings only on settled ticks or target
changes. The Unity UI Toolkit renderer never reads simulation state and reads
assets through `IClassicHoverAssets`. It retains status/tooltip images and
rasterizes original BF4 text only when strings, fonts or wrapping change.
Its status clip scrolls the retained text image; nine retained images form
the popup frame. Its physical-pixel panel keeps the approved font readable.

The readable font choice follows the existing height-based subtitle policy,
not DK2's often tiny width-based native font choice. The renderer's strip and
popup padding follow that readable size. Scrolling is time-based at 60 pixels
per second, approximating the original's one-pixel-per-callback motion.
Ordinary room templates and their supported state substitutions are built as
described above. Special object branches, creature personal-name generation, shots and structure status
producers also remain incomplete. Unsupported substitutions are omitted rather
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
Decorative objects without simulation ids and structures are not entity picks;
they fall back to room or terrain text. World tooltips use entity identity, a
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
