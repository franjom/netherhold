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
with separate special-definition and multiplayer paths; the simulation has no
equivalent name field. The formatter accepts an externally supplied name and
leaves the slot empty until that producer is implemented; it consumes no RNG.

## Rooms and objects — CONFIRMED

`0x004E9250` selects **2579** for an owned Dungeon Heart: heart-object health
percentage and the keeper's mana/ceiling as `%37`, `%40`, `%41`. It selects
**2790** for a Portal: room health, keeper-wide portal count and creature
capacity as `%37`, `%44`, `%43`. These are the live status templates, rather
than the older room-definition tooltip strings 2431 and 2433. A nonowned room
uses 2471 (details unknown), except hero-owned room types 18–23 use 2468 (hero
entrance warning). `2471` also happens to be a shipped bridge definition's
tooltip; its use here is a separately confirmed status branch.

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
Ordinary rooms currently show their localized name in the status strip;
their room-specific capacity substitution tables are not implemented. Special
object branches, creature personal-name generation, shots and structure status
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
