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
  `+0x453`/`+0x455`, call `0x0048AD30(1)` when the unread `0x00478050` returns 22,
  and set `+0x3EA` bit 10. The rule admits 216 from every state: group 3 with
  category 23 is its passthrough (`creature-state-table.md` §2.2).
- **216's handler** `0x00459850` returns 1 while an action runs (`0x00496D00`, the
  fall), then sets **217** and places the body on the floor.
- **217's handler** `0x004599D0` plays the fall in two phases (`state+0xA7` 1, then
  2). Each turn it calls the health routine in its dying-countdown mode
  (`0x004B2CE0` with −1, §4). A creature whose health is back above zero leaves it:
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

## Open questions

- Which of `0x00459560`'s five callers is the ordinary collapse, and what the
  single caller `0x0048B770` of `0x004596D0` is for.
- What `0x00478050` returns, whose 22 sends a newly collapsed creature to state 1.
