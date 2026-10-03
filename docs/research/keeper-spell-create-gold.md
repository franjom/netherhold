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
That landing routine first handles room-held objects. Otherwise, gold with no
creature recipient merges into another type-1 gold object in its third-of-tile
band, or remains separate, becomes neutral and reaches state 16. The candidate
must be strictly inside the band's bounds; the band edges are excluded.
See [gold-on-the-floor.md §12](gold-on-the-floor.md#12-a-creature-takes-loose-gold-it-walks-over-read-2026-09-30)
for delivery to a creature and the other shared landing arms.

## 3. Implementation boundary

The cast is enabled at the existing command boundary. Create Object now gives
type-1 piles the neutral owner and the data-driven, nonnegative amount. These
piles enter the existing flat floor-gold representation, with process state
zero, as other floor-gold creation paths do. This deliberately collapses the
original state-1 fall and landing: vertical timing, room-process handover and
the strict sub-tile landing merge are not implemented here. The existing Q3/M9
position/height boundary remains; repeated casts may leave separate piles
where the original would merge them. This is a fidelity limitation, not an
original rule that spell piles cannot merge.

Treasury and Heart deposits retain the shared gold-store representation and
capacity rules (ADR-0015). StoreSweepSystem deposits only what fits on its
sixteen-turn boundary; a full store leaves the new pile loose and does not
refuse or refund the spell. `CreateGoldTests` covers ordinary/upgraded output,
cast tile, neutral owner, payment/clocks, refusal ordering, allied land,
full/partly full stores, descriptor variation and pending-shot snapshot replay.

No new state fields or serialization layout were introduced. Baseline scenario
comparison belongs to ADR-0347; this document is the evidence home.
