# The Hand's dropped-object earmark

Source: `DKII.EXE` v1.70 (GOG), read 2026-09-25. The relevant routines are
the keeper drop handler `0x004BC710`, object initialiser `0x0049B1C0`, object
state dispatcher `0x0049EED0`, its state-1 hook `0x0049EF90`, and the computer
keeper's make-happy path `0x004F9710`.

Confidence: the owner's observation that a chicken or gold dropped onto a
creature is reserved for it is **CONFIRMED** (2026-09-25). Binary findings below
are **STRONGLY INFERRED**.

## 1. Field and writers

The earmark is a **16-bit creature thing id at object `+0x142`**. DK2's object
debug dump reads it at `0x0049D403` and prints it as the recipient of a drop.
The object initialiser zeroes it at `0x0049B389`; it has no file-backed initial
value.

The complete object-field writer set is:

| Address | Writer | Effect |
|---|---|---|
| `0x0049B389` | object initialiser `0x0049B1C0` | zero on creation |
| `0x004BC92B`, `0x004BC994` | keeper drop `0x004BC710` | store the targeted creature id; two optimized branches implement one rule |
| `0x004F97DF` | computer keeper make-happy `0x004F9710` | earmark a newly minted gold object |
| `0x004F9B2E` | same | earmark a chicken taken from a Hatchery |
| `0x0049EFE7` | object state-1 hook `0x0049EF90` | clear an invalid or non-creature recipient |

The ordinary drop handler has a `RET 8` at `0x004BCA2A` and
`0x004BCA31`: besides its player receiver it consumes two stack arguments, one
of which is the drop position. For object types 1–3 it tests level-scaled `Pay`
at creature definition `+0x69A`; for Chicken (type 9) it tests level-scaled
`HungerFill` at `+0x66D`. A nonzero result admits the store. It does not test
current debt, hunger, ownership, distance, or creature state.

`0x004F9710` is the computer keeper's response to unhappy/angry-creature
messages. Its gold branch withdraws a level-scaled wage, creates a gold object,
and writes the recipient; its food branch takes a Hatchery chicken and writes
the recipient. Both then use the keeper's ordinary Hand drop path.

**No drop path clears the field.** In particular, dropping on a tile or with no
active creature target leaves an existing value unchanged. Hand pickup
`0x004BC500` also does not touch it.

## 2. Readers and what honours it

There are two object-field readers:

| Address | Reader | Decision |
|---|---|---|
| `0x0049D403` | object debug dump `0x0049D310` | diagnostic output only |
| `0x0049EFA6` | object state-1 hook `0x0049EF90` | validate the recipient, then deliver chicken or gold directly |

The functional reader has no direct caller. `0x0049E608` installs it in the
state-hook slot at `0x006E4B50`; the object dispatcher indexes the 20-byte table
based at `0x006E4B3C` and calls the selected pointer at
`0x0049EF20`–`0x0049EF3E`. State 1 is assigned to a dropped object by
`0x004BC87D`–`0x004BC881`.

The hook waits while the object remains above its landing height
(`0x0049EF99`–`0x0049EFA0`). Once it can settle, it validates that the stored
thing id is active and still denotes a creature (`0x0049EFC5`–`0x0049EFDB`).
Failure clears the field. Success does not start a fetch or move:

- **Chicken, type 9** (`0x0049F0DD`–`0x0049F13D`): the recipient receives the
  ordinary one-chicken effect through `0x00463A10`—one hunger unit removed and
  the normal health gain when applicable. The hunger anger bucket is cleared,
  another tabled calming amount is applied, and the chicken is destroyed.
- **Gold, types 1–3** (`0x0049F13E`–`0x0049F1CD`): half the pile's `GoldHeld`
  is added to the creature's purse, capped by its level-scaled `MaxGoldHeld`.
  Its **entire current `WageOwed` is cleared**, independently of the pile's
  amount; the pay-attempt bit is set, the pay anger bucket is cleared, another
  tabled calming amount is applied, and the pile is destroyed.

Thus the earmark is stronger than a reservation: the settled object itself
delivers its effect to the named creature. The creature does not seek it.

## 3. Searches that ignore the field

The exhaustive access scan establishes these negative results:

- Hatchery chicken search `0x00463500` neither skips another creature's earmark
  nor prefers its own.
- Prison food search `0x00473550` does not read it. The claim in `prison.md`
  remains correct: states 115–118 reserve no bird when choosing one.
- Imp task-kind-7 selection and pickup, including `0x0046B6D0` and state 27
  handler `0x00466850`, neither skip nor target earmarked gold.
- Wage search `0x00472390` and pickup state `0x00472740` neither skip nor target
  an earmarked pile.

Those routines can select the same object without knowing its recipient. The
usual earmark path instead consumes the object from its own landing-state hook;
whether another updater can seize it in the interval before that hook runs is
not established by this static read.

## 4. Release and persistence

There is one explicit release: on a settled object's update, an inactive thing
id or a thing that is no longer a creature clears `+0x142` at `0x0049EFE7`.
Creature death or departure therefore releases it only after the creature has
left the active thing table. A dying or leaving creature that remains an active
creature still passes the test.

Successful delivery destroys the object, so there is no surviving field to
clear. Eating it or depositing/destroying it by another route has the same
structural result: object deletion ends the earmark rather than releasing the
object for somebody else.

Lifting an object with the Hand does **not** release it. Neither Hand pickup
`0x004BC500` nor a later untargeted drop writes the field, so an object lifted
before delivery retains the old recipient. The imp gold/object paths likewise
contain no field reader or writer: carrying alone preserves it; destruction on
delivery removes it.

## 5. Completeness of the scan

The exact `0x142` operand scan covered both initialized executable blocks:
`.text` `0x00401000`–`0x00652BFF` and `cseg`
`0x00653000`–`0x0066BFFF`. It found 34 instruction operands; base tracing
reduced them to the eight object accesses listed above. `cseg` contained none.
The remaining 26 use another structure, an indexed array, or the literal value
`0x142`. Ghidra exposes no data reference for these register-relative operands;
`Reference.isWrite()` was checked and returned no references, so read/write
classification used operand direction after tracing the base to an object.

A raw all-initialized-block scan of the hook address found one occurrence: its
runtime table installation at `0x0049E608`, with no direct call. The same run's
positive control, variable 19 at `0x006F2590`, returned 226 occurrences. The
object initialiser supplies the field's zero rather than initialized image data.

## 6. Build plan

- Add a 16-bit optional recipient id to object simulation state. Object creation
  starts it at zero; Hand and computer-keeper creature-targeted drops write it.
- Extend `DropCommand` with an optional target creature id as well as the drop
  position. Command validation must resolve an active creature and apply the
  level-scaled `Pay`/`HungerFill` eligibility test.
- Have the landed-object system validate the recipient and directly invoke the
  existing eating, health, purse, wage, and anger rules before deleting the
  chicken or pile. Preserve the id across Hand lift and ordinary carrying.
- Do not add earmark policy to Hatchery/prison food search, imp task kind 7, or
  wage pickup: DK2's implementations do not consult it.
