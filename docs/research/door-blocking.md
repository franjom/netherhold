# Mechanic: door contact, the door fight and a door's death

Source(s): DKII.EXE v1.70, PyGhidra and pyghidra-mcp reads on 2026-09-28 and 2026-10-02;
the owner's play (Q454 and the support wall, 2026-10-02); the shipped THINGS files.
Original version: DK2 v1.70 (GOG)
Confidence: STRONGLY INFERRED unless a line says otherwise.
Implementation: `CreatureMovementSystem.DoorContact`, `CombatTargetRules.ForcedDoor`,
`FightRules` (door arms), `ApproachRules.Enter`, `WorldSight`, `DoorRules`,
`DoorHealthSystem`, `DoorRemovalSystem` (ADR-0318).
Tests: `DoorCombatTests`, `DoorNavigationTests`, `DoorsOnRealDataTests`.

Opening, locking and placement are [doors-and-traps.md](doors-and-traps.md).

## Route planning

Writer `0x004A66F0` supplies the door byte to cost function `0x004C8BD0`. The owner
nibble is the compact number at `player+0x10FC`. Open and unfinished doors are floor; a
closed built door contributes its owner, lock bit 4 and tile tag bit 2, and definition
bit `0x40` (a barricade) supplies the lock bit whatever the lock mode. The cost function
compares owner numbers, not alliances: option `0x80` refuses a foreign door, an own locked
door needs option `0x2`, and without `0x80` a foreign door is routed through.
[navigation-mesh-and-search.md](navigation-mesh-and-search.md) has the cost table;
`WorldSteering` and `MeshRouter.TransitionCost` implement it.

## 1. Contact during movement

Creature update `0x00488930`, `0x0048A2EB`–`0x0048A44A`, after the steering step and the
solid test: unless the mover's type byte `+0x36F` is 1 (the Imp), it walks the thing chain
of the tile under the new position (tile record word `+0`, next thing `+0xC`). A door
(`+0x12 == 4`) qualifies when `+0x109` is 0 (built) and either

- its owner is hostile, `0x004BF390(door+0x28)` — **no open test** on this arm, or
- the mover has `+0x3E6` bit 3 and the door's open word `+0x10D` is 0.

Then, for a mover whose `+0x16` is not 2 and whose owner is not the neutral player
`[0x006F2548]`, if `0x0045A6D0(1)` (the forced fight gate) allows:

1. in category 22, `0x0045EF50` lets the fight go;
2. an old forced target `+0x3F9` is let go (`0x0045EF50`) and cleared;
3. `+0x3E6 |= 4` (`AttackAbnormalTargets`) and `+0x3F9` = the door's id;
4. `+0x3E6` bits 17 and 18 cleared (`0xFFF9FFFF`);
5. the route stopped and reset (`0x004D1070`, `0x004D5B30` on the body `+0x124`);
6. `SelectAVictim` `0x0045CB00`;
7. the position commit `0x004B5560` skipped this update.

The chain walk continues past a hit; one door to a tile makes that moot.

**Names.** `+0x3E6` bit 3 is `RebelActive`, bits 17 and 18 `IgnoringDoors` and
`IgnoringLockedRegion`, all three by DK2's own debug dumper (`creature-object.md`).
`RebelActive`'s setters are job 16's two branches and the defection's step 4; it is cleared by
state 89 (`0x0047792A`) and category 26's leave hook `0x004752A0` ([rebellion.md](rebellion.md) §4). A
rebel walking out with own locked doors passable meets its keeper's shut door and fights it.

**CONFIRMED (owner, 2026-10-02):** an enemy stops to attack a door held open by its owner's
creature (Q454), as the hostile arm's missing open test predicts.

## 2. The forced door target

`0x0045CB00`'s forced arm (`+0x3E6 & 4`) takes `+0x3F9` if live in `[0x006ED540]`; a dead
id is cleared by `0x0048F3F0`, and an unbuilt door only prints an assertion. A thing target
skips scan A, the heart scan E (the target is not a creature), the route check and the final
gate. The friendly scan F still runs, so an ally wanting a friendly spell outranks the door;
one the route then refuses ends the call with no fight, the door not taken.

The survey drops an attack spell whose flag byte `+0x27` has bit 3 (the status spells) when
the forced target is not a creature. The running best is never set on the forced
path and stays `0x7FFFFFFF`, so the within-one-tile melee rule cannot fire: a creature with a
surveyed attack spell casts at the door (nibble 2, range the spell's), one without swings
(nibble 4). Melee reach is `(MeleeRange + 0x800)² >> 12`, `0xC00` for a Secret Door (flag
bit 5), from `0x0045A6B0`, the melee-range getter. No attacker slot is taken (`0x0045A9A0`
registers only on a creature, as does the event-6 call in the fight-starter `0x0045A550`).
`SetState(199)` unless already in category 22.

The same shape holds for a forced *creature* (the Combat Pit): ours picks melee within a tile
and runs no scan F there (Q510).

## 3. Approach and reach

Approach entry `0x00471D10(target, range, state)`: `0x004B35C0` must accept the target's
position; a route with option word 2 (own locked doors passable). For a door that fails, the
four neighbour centres of table `0x0069A070` (north, east, south, west) are tried with
`0x004D0E60(..., 0)`; the first that routes is pulled a quarter tile back toward the door,
routed with option 0 and state 8 entered whatever that request answers. Ours routed the entry
with option 0 until ADR-0318. State 8's ten-call refresh still routes to the target's position
with option 0; its option word for a thing target is unread.

Reach test `0x0045E8F0`: the target's `+0x16` must not be 2; strictly `d² <
AttackRangeSquared`, and line of sight with the first flag set when any door (thing type 4)
stands on the target's tile.

## 4. Sight

Line walker `0x004512F0(a, b, f1, f2)` asks `0x004511A0(x, y, f1, f2)` per tile; every scan
passes `f2 = 1`. A navigation byte with bit 3 (Solid) blocks. With `f1 = 0` a door thing on
the tile blocks unless it is a barricade (`def+0x8B` bit 6); the test reads the thing chain,
not the open word or the construction word, so an open door and a blueprint stop sight too.
With `f1 = 1` and `f2 = 1` no door blocks, only Solid. Ours read the door from the
navigation byte, so an open door was transparent, until ADR-0318.

## 5. The swing and other damage

Swing action `0x00498E30` gates on `+0x16 != 2` of the action's target and its virtual `+0x34`
(`0x00628BC0`, constant 1 for a door). The blow is `Combat_MeleeDamage` negated; the bearing
bonus is the creature arm's alone; the door arm only picks a material impact pair from
`def+0xB3`; both player modifiers (`player+0x1090`, `+0x1091`) apply; then
`0x004B2CE0(-blow, attacker owner, 0)`, which applies a door's delta flat.

A shot aimed at a door (`ShotRules.HitsDoor`) strikes as the heart's does, within the shot's radius plus a quarter tile
of the door tile (Q363's stand-in; the overlap with a door thing is unread, Q398). Contact
damage applies flat; area damage type 2 hits a door not of the shot's owner unless the shot
is gas, flat (shots.md §4a, ADR-0326).

## 6. Health, recovery and the two kills

Loader `0x004A7100`: file `0x22C` → `def+0x91` health, file `0x21C` → `def+0x93` signed
recovery. Constructor `0x004A5070` copies health to `+0x40` and zeroes `+0x11A`.

Update `0x004A55F0`, in its order:

1. **Recovery.** If not dead (`+0x28`, `0x004B5C30`: health ≤ 0) and the turn is past `+0x11A`:
   with `def+0x93` nonzero, `gain = min(rate, def+0x91 − health)`, applied if its low word is
   nonzero, and `+0x11A += [0x006F2590]` (one game second) — whether or not anything was gained.
   A door made late, deadline 0, recovers on consecutive turns until the deadline catches up.
2. Construction presentation; the lock-pick countdown (doors-and-traps.md).
3. **Owner.** When the tile word `+8 & 0xFFF` (Q247) is not the door's owner: an unbuilt door
   leaves its keeper's blueprint list (`0x005056E0`), and health is set to 0.
4. A closed built door pushes creatures off its tile (`0x004B1F20`); not built.
5. A barricade returns here: alive it only updates sounds; dead it plays `def+0xAE`.
6. **Support.** Orientation `+0x119` 0 tests `(x, y−1)` and `(x, y+1)`, otherwise `(x−1, y)` and
   `(x+1, y)`, through world slot `+0x208` (`0x00509200`, Solid bit). Unless both are Solid,
   the door's `+0x20` (`0x004B2AD0`) sets health to 0 (`0x004A5B0F`).
7. Dead: the linked object `+0x101` is destroyed and `def+0xAE`'s effect played.
8. Opening (doors-and-traps.md); an open built Secret Door within four tiles of an enemy that
   sees it makes it that enemy's forced target and reveals it; not built.

**CONFIRMED (owner, 2026-10-02):** digging out a wall beside a door destroys the door.

**Shipped data (CONFIRMED):** of 940 placed doors on 46 levels, none fails the support test;
one fails the owner test — Level 4's hero Wooden Door at (24,9) on a Neutral-owned tile, which the
read rule kills on the first update (a prediction, not an observation).

## 7. Death and removal

`World_UpdateThings` `0x004B7830` walks the world's eight class lists twice per turn: first
every thing's update `+0x14` (a zero return calls `+0x20`), then every thing's death query
`+0x28`, calling `+0x30` on the dead. The door's `+0x30` is `0x004A66A0` → `0x004B7BD0` →
cleanup `0x004A5290`: release sounds and its effect, clear the tile's bit 15 and the secret flag,
destroy the linked object `+0x103`, rewrite the navigation byte as floor (`0x004A66F0(0)`). A door
killed in a turn is removed in that turn, after every class has updated. Creatures (list 0)
update before doors (list 4); where shots fall is Q398.

## 8. Other forced-door setters

`0x00481400`, from state 76's landing (`0x0046EFC6`) and state 257's stun end (`0x00481337`):
not in category 22 and the gate `0x0045A6D0(0)` passing, `SelectAVictim` first; failing that,
the four neighbours of table `0x006BC350` (north, east, south, west) for a built door owned by
the neutral player or not allied, which becomes the forced target (`0x0048F390`), and
`SelectAVictim` runs again. Not built.

## Built (ADR-0318) and left

Built: §1 except the bits 17/18 clear (nothing here sets them) and `+0x16` (unnamed, Q364); §2;
§3; §4; §5's melee arm and the shot stand-in; §6 steps 1, 3 and 6; §7's removal and its
timing. A door's id is stable and blueprints hold ids, so a removal leaves every other
reference intact.

Left: the push-out (§6.4), the secret-door reveal (§6.8), §8, the linked objects, effects and
sounds, area damage on doors.

## Open questions

Q510 (forced creature arm), Q364 (`+0x16 == 2`), Q398 (shot order and collision).
