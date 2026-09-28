# Keeper spell permission and cast boundary

Independent read of `DKII.EXE` v1.70 with Ghidra 12.1.4/PyGhidra,
2026-09-28. Behaviour is **STRONGLY INFERRED**; no matching original cast
sequence was captured. This covers the helpers called by the computer keeper,
not the eventual effects of every shot process. Keeper spell selection belongs
to `computer-keeper-management.md` §4.

## 1. Availability and clocks

`0x004BAFA0` searches the player's spell table by id, returning availability
1 when absent. Entry layout, independently checked against the player dumper,
is in `player-object.md`. This lookup does not turn accrued research points
into availability.

`0x004BE390` returns zero when last-cast tick is zero. Otherwise cooldown is
`max(0, definition[+0x3E] - (currentTurn - lastCast))`. Equality is ready.
The position/creature permission helpers `0x004BB8A0`/`0x004BBA40` repeat this
test and check mana through `0x004C1740`: zero cost always passes; otherwise
cost must be at most current mana. This differs from the keeper's emergency
CTA selection gate, which requires strictly more mana than cost.

The helpers derive cost from descriptor `+0x58`, with the Create Imp
multiplier described in `imps-and-mana.md`. The position helper also refuses
spell 5 when an owned creature has descriptor flag `0x4000`, and spell 14
when player flag `0x1000` is set. Both then call location and target validators.
The creature helper additionally requires a live creature tag before passing
its position to location validation and its tag to target validation.

## 2. Cast side effects

Both cast helpers take three stack arguments: spell id, position pointer or
creature tag, and flags; `RET 0xC` and callers establish this. Their decompiler
output misplaces arguments after virtual calls, so the following bookkeeping
was checked in instructions as well.

`0x004BBD30` (position) uses flag bit 0 to bypass permission/payment. The
ordinary path checks permission and mana, chooses the normal or upgraded
shot descriptor, creates the shot, stores its two process parameters, and
starts it at the requested position. Creation failure returns zero. Successful
ordinary creation debits mana, adds to player `+0x10F2` (one-shot mana loss),
and stamps both last-cast and last-checked with the current turn
(`0x004BBEEE..0x004BBF4F`). Spell 14 also sets player flag `0x1000`.
A zero shot id returns success without creating a shot or charging mana.

`0x004BBF90` (creature) validates, creates a shot at the creature, stores its
target tag and process parameters, then debits mana and stamps both clocks
(`0x004BC1EB..0x004BC259`). Its final return is **zero even after successful
creation** (`0x004BC272..0x004BC27B`). The keeper battle routine ignores that
return after its separate permission test; interpreting zero as a reason to
retry would change its command stream. Positive healing process 3 additionally
rejects a target already at maximum health. Nonzero bypass flags and the
remaining process-specific effects need separate implementation checks.

## 3. Validation dispatch

`0x005176B0` selects location validation from descriptor byte `+0x76`:
six entries at `0x006FCDD8`, stride 17. `0x00517730` selects target validation
from byte `+0x77`: seven entries at `0x006FCE40`, stride 18. These are runtime
tables, initialized by `0x005174A0` and `0x00517580`, not readable static data.
Index zero is null and fails; out-of-range selectors fail. Callback mapping:

| Index | Location callback | Target callback |
|---|---|---|
| 1 | `0x00517B80` | `0x00517800` |
| 2 | `0x00517CB0` | `0x00517930` |
| 3 | `0x00517DE0` | `0x00517990` |
| 4 | `0x00517F10` | `0x005179D0` |
| 5 | `0x00517F60` | `0x00517F60` |
| 6 | invalid | `0x00517AB0` |

All callbacks were read. Location 1/2 use the nonenemy relation, 3 the enemy
relation; 4 omits ownership. They reject the world `+0x20C` predicate.
Selectors 2/3/4 conditionally require tile flag `0x2000` when cast flag bit 1
is set. Location 1 additionally excludes the neutral player. Location 1/2/3
and target 4 apply room-position exclusions for Portal, Heart, Prison,
Temple and Combat Pit. Selector 5 always returns true.

Target 1 normally admits own creatures, with room-owner and Pit-fighter
exceptions, excluding descriptor flag `0x4000` in its ordinary ownership arm.
Cast flag bit 2 instead selects foreign nonneutral creatures. Target 2 reverses
that flag choice; target 3 accepts any live nonneutral creature. Target 6 has
additional state/status restrictions, or accepts an owned type-9 object.
These predicates are not interchangeable with pickup permission.

**Thunderbolt gates resolved:** see `keeper-spell-thunderbolt.md` §2 for the
world `+0x20C` predicate, Heart-position helper `0x004DA150`, and shipped
selectors. Target 6's status fields remain UNKNOWN for other spells.
A cast command must follow the selected callbacks rather than substitute
a generic ownership or range check.
