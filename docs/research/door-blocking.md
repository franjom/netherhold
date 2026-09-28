# Door navigation, contact and damage

## Evidence and scope

**STRONGLY INFERRED:** DKII.EXE v1.70, PyGhidra reads on 2026-09-28.
Addresses below are evidence, not original-game observations. Opening and
placement remain in [doors-and-traps.md](doors-and-traps.md). Navigation costs
are built; the later contact-to-combat transition is unbuilt.

## Route planning

**STRONGLY INFERRED:** writer `0x004A66F0` supplies the door byte to cost
function `0x004C8BD0`. The owner nibble is the compact number at `player+0x10FC`.
Open and unfinished doors contribute floor. A closed built door contributes
its owner, lock bit 4, and tile tag bit 2. Definition bit `0x40` supplies the
lock bit regardless of the runtime lock mode.

The cost function compares owner numbers, not alliance bytes. Option `0x80`
rejects a foreign door; an own locked door requires option `0x2`. Without
`0x80`, a foreign untagged door can be routed through. See
[navigation-mesh-and-search.md](navigation-mesh-and-search.md) for the cost
table. `WorldSteering` and `MeshRouter.TransitionCost` already implement it.
An ally can open a door while its closed navigation byte still refuses that
ally's route. Replacing the owner comparison with an alliance test would
change the binary rule.

## Contact during movement

**STRONGLY INFERRED:** creature update `0x00488930`, specifically
`0x0048A2EB`–`0x0048A44A`, inspects the predicted destination tile's thing
chain after steering and the solid-tile fallback. Its walking arm excludes
creature type 1, the Imp. A completed door qualifies when its owner is hostile
(`0x004BF390`), or when creature flag `+0x3E6 & 8` is set and the door is shut.
The hostile arm does not require the door to be closed (Q454).

For a mover outside thing state 2, with a non-neutral owner, and passing
`0x0045A6D0(1)` (the forced fight gate), this arm:

- disengages an existing category-22 fight;
- replaces the forced target with the door's thing id and sets
  `AttackAbnormalTargets`;
- clears route flags 17 and 18, stops and resets the route;
- calls `0x0045CB00` to select the forced target;
- cancels the position commit for this update.

The forced arm of `0x0045CB00` accepts a thing id, not just a creature id.
`0x0045A550` enters fighting state 199 after selecting an attack. Adjacent
door finder `0x00481400` tries normal acquisition first, then completed
neutral or hostile doors on four neighbouring tiles. Its calling states
still need an integration audit.

**UNKNOWN:** the complete meaning and setters of creature flag `+0x3E6 & 8`.
It must not be inferred from the lock command or worker status. The mover's
thing-state-2 bypass does not license implementing possession here.

## Approach and reach

**STRONGLY INFERRED:** approach entry `0x00471D10` first tries the target
position with options 2. A failed door route tries four adjacent tile centres
in table `0x0069A070` order, then moves the chosen point a quarter tile toward
the door and routes with options 0. Success enters travel state 8.

Range check `0x0045E8F0` uses strict squared distance. It passes a different
line-of-sight flag when a door occupies the target tile. Reusing ordinary
creature-target `WorldSight` would wrongly reject the target door itself.
The option's complete per-tile contract needs a fresh read of
`0x004511A0/0x004512F0` before a door fight can be enabled.

## Health, damage and removal

**STRONGLY INFERRED:** definition loader `0x004A7100` has a `0x74` source
prefix. Source `+0x2A0` (file `0x22C`) supplies initial health to runtime
definition `+0x91`; source `+0x290` (file `0x21C`) supplies signed recovery
to `+0x93`. Constructor `0x004A5070` copies health to common thing `+0x40`.
Virtual death query `0x004B5C30` tests health below 1.

Swing action `0x00498E30` calculates the attacker's melee damage and calls
common health routine `0x004B2CE0`. The door arm selects a material impact
effect from definition `+0xB3`; creature-specific armour and direction
adjustments are not applied to a door. Common health applies the signed delta
directly for non-creatures. `0x004A55F0` recovers signed definition `+0x93`,
capped by missing health and narrowed to a word, strictly after deadline
`+0x11A`, then advances that deadline by one game second when gain is nonzero.

The update also kills a door when its tile changes owner, and checks supporting
walls against stored orientation. Removal `0x004B7BD0` calls cleanup
`0x004A5290`, including auxiliary objects, tile occupancy and navigation.
The full death dispatch and its timing relative to movement remain unbuilt.

## Blocking reads before integration — Q453

**UNKNOWN at the integration boundary:** the forced thing-target attack survey
and its virtual reach input (`0x0045A550`, `0x0045A9A0`), the door-specific sight
contract above, and the common death dispatcher calling `0x004B7BD0`.
Read those with the approach neighbour table and the setters of creature flag
bit 3. Then add stable door identity, health and forced thing targets to
hash/snapshot state, combat, shots, placement and crate references. Making a
door a solid wall would bypass the researched combat transition. Hero gates
were not established as this mechanism and remain outside scope.
