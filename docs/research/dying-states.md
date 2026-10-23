# Mechanic: the dying states — 216 falls, 217 lies, a corpse has none

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-25: every `SetState`
  call with a literal state, the two entry routines and the two handlers. Addresses
  are facts about the binary in our own words; no decompiler output enters this
  repository (ADR-0046)

Original version: DK2 v1.70 (GOG)

Questions: advances ADR-0135 step 2. `creature-death.md` owns the two timers and
the collapse window; `creature-state-table.md` the table and the transition rule.

Confidence: **STRONGLY INFERRED (binary)**.

---

## 1. The states

- **Entry.** Two routines set state **216** ("dying"): `0x00459560`, with five
  callers, and `0x004596D0`, with one (`0x0048B770`), which also stamps
  `state+0xAB` with `0x00517490`'s value + 10. Both return at once for a creature
  already in 216 or 217, and return 0 if `SetState(216)` is refused. Then they write
  the body's `+0xF4`/`+0x102` words and position, zero three vectors, clear
  `+0x453`/`+0x455`, call `0x0048AD30(1)` when `0x00478050` returns 22 (the state's category, combat;
`training.md` §8),
  and set `+0x3EA` bit 10. The rule admits 216 from every state: group 3 with
  category 23 is its passthrough (`creature-state-table.md` §2.2).
- **216's handler** `0x00459850` returns 1 while an action runs (`0x00496D00`, the
  fall), then sets **217** and places the body on the floor.
- **217's handler** `0x004599D0` plays the fall in two phases (`state+0xA7` 1, then
  2). Each turn it calls the health routine in its dying-countdown mode
  (`0x004B2CE0` with −1, §4), but only while the creature's `BeingDragged` (`+0x3C7`,
  read through vtable `+0x74`, `0x004B5DE0`) is 0 (`0x00459BE8`–`0x00459C00`). The health
  routine refuses any change to a dragged creature as well (`0x004B2E08`). So **the rescue
  window stops while an imp carries the creature** (Q164, read 2026-10-04). The take-hold
  `0x0048B4A0` sets the flag through `+0x6C` (`0x004B5DF0`); the drop `0x0048B500` clears it
  through `+0x70` (`0x004B5E00`). A creature whose health is back above zero leaves it:
  `0x0048AD30(1)`, then the lair-trip chooser `0x0046F390`.
- **Death.** The countdown ending destroys the creature. With `DeadBodyOnDestroy`
  set, a `CDeadBody` thing is left in its place. So a corpse holds no creature
  state.
- **Built** (ADR-0206): collapse enters 216; the first dying second enters 217; a
  rescue leaves through state 1 into the Sleeping state 83; a corpse hands its id
  back. **Ours:** there is no fall animation, so 216 lasts until the next game
  second; a rescue lies down in its bed (ADR-0063) rather than running
  `0x0046F390`; the corpse stays in the creature list.

## 2. A corpse is a thing of its own

The binary carries the RTTI name `.?AVCDeadBody@@`, the creature flag
`DeadBodyOnDestroy` (`+0x3E6` bit 5), a THINGS tag of its own (202, `DeadBody`,
`kwd-record-fields.md`) and a per-room `DeadBodyList` with `UsedDeadBodyCapacity`
(`room-object.md`). So when the countdown ends DK2 destroys the creature and, with
the flag set, makes a dead-body thing. A corpse is neither an object record
(`objects.md` §4) nor a creature in a state. Ours keeps the corpse as the same
creature in `CreatureActivity.Dead`, holding no state id, the one creature that has
none.

**The corpse clock stops while the body is carried** (Q164, read 2026-10-04,
CONFIRMED). The countdown is the body's `+0x40`. `DeadBody_Construct` `0x004B0F20` sets it
to `DeadBodyDiesAfterSeconds` × ticks per second, 60 × 4 turns (`0x004B0FAF`). It is the
only reader of `0x006F25A0` besides the loader. The body's vtable (`0x0066D904`) has a
carried flag `+0x138`: `+0x6C` `0x004B80F0` sets it, `+0x70` `0x004B8100` clears it (the
constructor calls this), `+0x74` `0x004B8110` returns it. The carrier's take-hold
`0x0048B4A0` stores the thing in creature `+0x3C5` and calls `+0x6C` for classes 6, 2 and 0.
The drop `0x0048B500` calls `+0x70`. The body's update `0x004B12D0` (vtable `+0x14`) asks
`+0x74` (`0x004B1649`). While the flag is set it does not decrement. Otherwise it decrements
once when dword `+0x16` is not 2 and the body is in no room (word `+0x132` zero,
`0x004B1654`–`0x004B1664`). On the not-falling arm it decrements again when `+0x132` names a
Graveyard (type 14, `0x004B166B`–`0x004B168E`). The falling arm (`0x004B1693`) moves the body
and does not decrement. So a corpse ages on open floor and in a Graveyard, and not in any
other room. Not read: what `+0x16` == 2 is, and what destroys the body when `+0x40` runs
out. That only carrying sets `+0x138` is STRONGLY INFERRED: the other `+0x6C` call sites
were not classified.

**Built** as ADR-0359: neither countdown runs while the body is carried (Q164).

## 3. Classic corpse art

CONFIRMED (binary and shipped data, 2026-10-11): `0x004B0F20` requests
creature-definition art 41 and installs it on the dead body. `0x00492320`
loads its name from staging +0x187B and compacts it into definition +0x453
(instructions 0x0049323C–0x0049325E). Subtracting the loader's +0x3F0 staging
shift locates the file art record at +0x148B. `0x00494880` resolves absent
art 41 to art 1, without recursively checking that fallback.

The Goblin's resource is `Goblin-DiePose`, type 5: a static KMF corpse mesh.
The classic catalogue loads this separate record; the dead-creature view
uses it as the model and stops living animation. It does not change the
death/rescue/countdown simulation or add a gradual decay effect.

Q622 (2026-10-23 claude): every creature that leaves a body has a type-5 art 41
(38 of them). Only the Lord's is checked by a test, which resolves it to
`LOL-DiePose.kmf` through the install provider; the other 37 are not covered by a test. Level 1's Lord
of the Land (type 29, `GenerateDeadBody` set) dies outright and leaves `LOL-DiePose`;
ours keeps that corpse for its 60 seconds and the view draws it
(`LordOfTheLandCorpseTests`). No missing body or texture was found on our side. What
ours lacked was the shine of his armour: `frontenv`/`KnightBack` are environment-mapped
(file 0x40, shininess 0.2) and DK2 draws them a second time, additively, with
`DefaultEnvMap`. That pass is now built for every model, kmf-materials.md §8. Whether
the owner's missing corpse is still missing after it is a play question (Q622).

## Open questions

- Which of `0x00459560`'s five callers is the ordinary collapse, and what the
  single caller `0x0048B770` of `0x004596D0` is for.
