---
systems: [creatures, imps-tile-work, navigation, simulation-core]
---
# Store gold in steering queries

## Cause

The [454 bed-heading control](bed-heading-draw.md) stops at 1631 draw 1:
modern Imp 455 reaches its deposit before the native log. Its own body and
route still match at 1617; its movement first differs at 1618. The nearby
Imp 444 first differs at 1609, then changes the later separation push on 455.

CONFIRMED in the Level 5 seed 1 capture: native store piles 1435 and 1439
stand at the centres of tiles (18,24) and (18,25), holding 1000 each. At the
1609 movement input, 444 has body position (1201520,1663392), velocity
(-16288,-25584), radius 4576 and speed 30144. Modern steering returns no object
contact because store gold lives in tile fields rather than ObjectState.
The native next body position is (1181770,1640498), velocity (-19750,-22894).

## Binary and implementation

STRONGLY INFERRED from whole rereads:
[the ordinary enumerator](functions/004D12B0.md) forwards eligible objects
in row-major tile order and tile-chain order, using definition half-width and
stored position. Its callback flag toggles for soft-definition objects before
any contact-distance filtering. [The contact callback](functions/004D3020.md)
uses the existing scaled integer-root distance and either records a slide
contact or returns a full-force shove; [the outer routine](functions/004D31D0.md)
keeps its existing arrival/escape exemptions. No force arithmetic changes.
The first store pile in this query is outside contact but still toggles the
flag; the second supplies the missing shove. Adding those entries reproduces
444's captured step and the later 455 movement.

[Store creation](functions/004F4090.md) makes an object 3 at its tile centre
when no store pile exists, even if other object kinds occupy the tile.
[Empty withdrawal](functions/004F4510.md) removes the pile.
[Tile linking](functions/004B4E70.md) puts a newly linked thing at the head.
Therefore a fixed oldest-pile assumption is insufficient for mixed tiles.

The adapter adds each nonempty tile's represented store pile to the ordinary
obstacle stream using the shipped flags/width. A physical object 3 suppresses
the additional representation. StoreGoldLinkOrder records the represented
pile's shared tile-link order: creation allocates it, top-up keeps it, emptying
clears it and recreation allocates a new head. It is hashed, saved and copied;
hash/save 455, ADR-0723. Unknown direct-fixture/partial-restore ranks remain 0.
The existing first-person, Heart-stream and virtual-furniture limits remain
partial; no tile navigation byte is made solid by this correction.

## Controlled scope

The initial native 1400 store piles occupy four tiles with no other objects
on those tiles. Their rank relative to another starting obstacle is therefore
not needed in this control. Existing 27 captured inputs are retained; no future
position or completion tick is forced. The diagnostic retains the prior 22
bootstrap clauses and adds four pre-1400 guards: geometry and three creation
adapters, for 26 in total. Production contains none of those guards. Initial
rank zero needs no additional input here; mixed-rank restoration remains open.

The final `controlled455-v2` replay matches 4106 ordered sites/preseeds
through 1646, versus 3816 through 1630 at 454. Next is 1647 draw 1: native
RenderInfo.cpp:201 versus modern Library.cpp:1068, seed 1428923462, modern
bound 16, Warlock 437/state 1. The [possessed-melee follow-up](possessed-melee-draw.md)
identifies the animation-bridge gap and unresolved native target mapping; this
control remains the last verified prefix.

Imp 444 and Imp 455 each match 247/247 native state/XY samples; 444 matches
246/247 headings. Other component discrepancies retain the scope in the
native receipt; ordered draw agreement does not establish full-state parity.

The navigation counterfactual fails five cases and passes seven controls;
the separate lifecycle counterfactual fails four and passes eight. Each passes
all twelve cases with the correction. The nearby selection passes 503 cases
with no skips. The full Release suite passes 8648, skips 30 and has zero
failures across ten projects (`full455-v2`).
Pin: `0x38611F5ADC795D73`.

Study: `F:/DK2Evidence/studies/q712-imp-deposit1631--2026-11-08--001`.
`trace454/` records the old forces; `gold454/` retains the exploratory geometry
control. `controlled455-v2-draw-verification.json`, `source-input-proof455.json`,
`native-proof455.json` and `verification-summary455.json` bind the final result.
`review-receipt455.json` records the independent acceptance.
The freeze preserves 824 source files and 375 Simulation C# files; its twelve
changes from 454 include the inherited Presentation comment correction. After
stripping all 26 clauses, the source proof matches production except for the
two inherited read-only Simulation observers. All 27 input values are unchanged.
Frozen source, executable payload and 52-entry SHA-256 manifest:
`F:/DK2Evidence/builds/q712-store-gold-steering--2026-11-08--001`.
Draw agreement is separate from component, full-state, unconditional replay
and Heart-outcome parity.
