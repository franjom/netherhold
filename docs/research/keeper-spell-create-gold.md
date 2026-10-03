# Create Gold

Read 2026-10-03 from DKII.EXE using an isolated PyGhidra MCP clone, with
decompilation checked against disassembly. Binary conclusions are STRONGLY
INFERRED; no original-game experiment was available for this spell.

## 1. Descriptor and cast

The shipped KeeperSpells.kwd record identifies spell 13: cost 15,000 mana,
zero upkeep and cooldown, location selector 1, target selector 4. Both variants
use shot 5 and object datum 1; the ordinary amount is 1,000, upgraded 2,000.
`CreateGoldOnRealDataTests` pins those values. Production reads the descriptor.

The normal position cast uses `0x004BB8A0` for permission and `0x004BBD30`
for shot creation, payment and both availability clocks. The target metadata
helper `0x005177B0` classifies selector 4 as a place. Permission checks mana
before cooldown and location before target. Selector 1 (`0x00517B80`) requires
non-neutral allied, non-solid land and rejects the special room interiors.
Selector 4 (`0x005179D0`) separately rejects the caster's special room interiors.
Neither the cast nor its Create Object effect checks treasury capacity or gold.
The shared cast boundary's zero-flags limitation remains; see
[keeper-spell-casting.md](keeper-spell-casting.md).

## 2. Gold creation and landing

Process 2's callback `0x004ADE10` uses the shot's position and object datum.
The world's vtable slot +0x200 is `0x00509050`, an x/y bounds check. Within
the world, the callback creates an object through `0x004B7A90`. Only object
type 1 takes the gold arm: `0x0049C760` adds the second shot datum to GoldHeld,
flooring negative totals at zero without a pile-size cap. `0x0049EF60` sets
state 1 and resets its age; owner becomes the neutral global `0x006F2548`.
The remaining effect is display. The Call to Arms-specific positioning and
hand refusal do not apply to gold.

The process dispatcher `0x004AD730` runs the callback for the shot's enabled
event. Shot update `0x004AB180` reaches the death event after its one-turn
countdown, so the existing ShotSystem creates the pile in the cast tick.

The object table initialized by `0x0049E600` assigns state 1 to `0x0049EF90`.
The room getter `0x004E6CD0` supplies a process state: 5 for Treasury/Heart,
4 for Temple. Gold's room-held flag preserves those routes before floor merging.
State 5 (`0x0049F730`) can deposit an unassociated object through the room's
deposit routine when the store is not full. State 4 (`0x0049F600`) requires a
fully surrounded Temple slab, which this spell's location validator excludes.

Otherwise, gold with no creature recipient merges into another live type-1
object in its third-of-tile band, or remains separate, becomes neutral and
reaches state 16. The candidate must be strictly inside the band's bounds;
the band edges are excluded. The incoming position need not be interior.
Object constructor `0x004B7190` installs vtable `0x0066D654`: candidate slot
+0x28 is the common death query `0x004B5C30`, so health must be positive.
Owner slot +0x24 is `0x0049C850`, which links through `0x004B4C30`;
the latter stores the owner at +0x28 and its player pointer at +0x2A.
See [gold-on-the-floor.md §12](gold-on-the-floor.md#12-a-creature-takes-loose-gold-it-walks-over-read-2026-09-30)
for delivery to a creature and the other shared landing arms.

## 3. Implementation boundary

The cast is enabled at the existing command boundary. Create Object gives
type-1 piles the neutral owner and data-driven, nonnegative amount.
`FloorGoldLandingRules` preserves the cast's sub-tile position and band and
merges into a live floor pile using the strict candidate bounds. Held/carried
objects are omitted because our flat object list retains their old tile.
It follows the existing newest-created-first object-order approximation
(boulder.md §10), rather than modelling exact tile relinking.

Spell piles keep their coordinates in the existing PosX/PosY fields; their
nonzero process state distinguishes them from legacy floor piles, whose
represented point is the cell centre or, without a cell, the tile centre.
A Hand drop updates a spell pile's coordinates before landing or delivery.
This collapses the state-1 vertical fall into the current flat model; M9's
height boundary remains. Room states are recorded, but their original object
update lifecycle is not run. Deposit timing is tracked separately in Q530.

Treasury and Heart deposits retain the shared gold-store representation and
capacity rules (ADR-0015). StoreSweepSystem deposits only what fits on its
sixteen-turn boundary; a full store leaves the new pile loose and does not
refuse or refund the spell. `CreateGoldTests` covers ordinary/upgraded output,
cast tile, neutral owner, payment/clocks, refusal ordering, allied land,
full/partly full stores across multiple tiles, descriptor variation and replay.
Landing controls cover repeated casts beyond the pile guideline, nine bands,
both axes' excluded edges, incoming-edge asymmetry, dead/held/carried objects,
other object types/tiles, foreign pile ownership, and a moved pile's snapshot
continuation and position hash sensitivity.

No new state fields or serialization layout were introduced. Baseline scenario
comparison belongs to ADR-0347; this document is the evidence home.
