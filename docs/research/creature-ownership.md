# Creature ownership order

**STRONGLY INFERRED**, read-only DKII.EXE in Ghidra 12.1.4/PyGhidra,
2026-09-29. No new original-game observation. Implementation: `WorldState`,
ADR-0273; tests: `CreatureOwnershipTests`, `TortureTests`.

## Claim audit

| Claim | Source | Result / status |
|---|---|---|
| Per-owner list fields | `0x004B4C30`, `0x004B4D40` | STRONGLY INFERRED: category-indexed head at player `+6`, count at `+0x16`; creature category 0; thing previous/next owner links at `+0xE/+0x10` |
| Acquisition order | `0x004B4C30` | STRONGLY INFERRED: unlink first, set next to old head, update old head's previous link, replace head and increment count; newest acquisition first |
| Unlink | `0x004B4D40` | STRONGLY INFERRED: join neighbours or replace head; clear links, owner tag/pointer and decrement count |
| Creation | `0x004872A0`, call at `0x0048735C` | STRONGLY INFERRED: initial creature ownership goes through insertion |
| Ownership change | `0x0048D050`, calls at `0x0048D197/0x0048D19F` | STRONGLY INFERRED: same owner returns without changes; different owner unlinks/reinserts; this function also has other side effects not claimed by the order API |
| Destruction | `0x00487C70`, call at `0x00488468` | STRONGLY INFERRED: unlinks from owner; other destruction side effects remain outside this audit |
| Hand pickup | `0x004BC500` | STRONGLY INFERRED: sets InHand and adds the hand entry without changing owner-list links; owned counts include held creatures (Q268) |
| Mana-removal reader | `0x004C1BC0` | STRONGLY INFERRED: walks this order, so ties after conversion cannot be reproduced by sorting creature tags |

The global thing chain (`+6/+8`) and owner chain (`+0xE/+0x10`) are different.
Changing this owner-order representation does not establish the original global
creature update order. `WorldState.Creatures` and its existing consumers retain
their current update order.

## Integration boundary

`WorldState.OwnedCreatures` returns the verified per-owner order. Creation,
removal, torture conversion and defection maintain it; hand pickup leaves it
alone. The existing corpse representation stays in `WorldState.Creatures` but
is excluded from the owner reader because an original corpse is another thing
category. Dying creatures are still included.

The order is saved and hashed separately from creature values. Snapshot loading
restores the recorded order rather than recreating it from ids. The mana-removal
selector walks this reader (`ManaRemovalLifecycle.Candidates`, ADR-0277), as its
original does. Existing AI observation ordering and other selectors are
unchanged; each consumer needs its own source audit before switching.
