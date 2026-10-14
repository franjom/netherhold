# Mechanic: creatures leaving and joining through portals

Source(s): `DKII.EXE` v1.70 (GOG), read in the `DK2_RE` Ghidra project on
2026-09-23 and 2026-09-24; `travel-states.md`, `player-object.md` and
`mentor-speech-and-notifications.md` own the shared mechanisms cited below.
Original version: DK2 v1.70 (GOG)
Evidence: handlers `0x00459C20`, `0x00459D50`, `0x00459DE0`, `0x0045A000`,
`0x0045A0D0` and `0x004808E0`; room finder `0x004C61C0`; sacking update
`0x004DC7E0`; gold subtraction `0x0048AF50`; creature vtable slots at
`0x0066D524`; removal path `0x004B2AD0` -> `0x0048AF80` -> `0x004B7A20`.
Observed behaviour: states 194 and 195 start two point routes to the closest
Portal or Hero Portal owned by any player; 196 sacks and removes the creature.
State 197 is a separate arrival gate. State 193 places an arriving creature and
hands it to ordinary idle/lair acquisition.
Hypothesis: none; claims below are control-flow readings.
Confidence: **STRONGLY INFERRED** from the binary.
Implementation: `LeavingRules`, shared by creature anger and defeat; built boundaries in §5.
Tests: `CreatureAngerTests`, `DefeatTests`.
Known exceptions: candidate selection ignores reachability, and a failed route
to the selected portal is retried rather than trying the next portal.
Open questions: the meanings of event ids 61 and 63, the optional effect selected
through definition field `+0x423`, and what enters state 197 remain UNKNOWN.

## 1. State 194 chooses across every player's entrances

`0x00459DE0` sets `creature+0x3E6` bit 14, the executable dumper's `Leaving`
flag, then walks the complete player-object list from `world+0x7A`, following
`player+0x3A`. That includes the creature's owner, rival keepers, the hero player
and the neutral player; there is no owner comparison.

For each player it calls `0x004C61C0(player, 3, creaturePosition, outRoom, 1)`.
That helper walks `player+0x36`, following `room+0x8`. Although the request is
room type 3, the helper deliberately treats types **3 (Portal)** and **25 (Hero
Portal)** as equivalent whenever either is requested. Its mode-1 branch
redundantly repeats the same player's room scan once per player object; the
outer state-194 loop is what changes the player whose rooms are searched.

Both the per-player choice and the final cross-player choice minimize squared
XY straight-line distance to the room coordinates. Neither calls the route
request or measures route length. The winning room is therefore not required
to be reachable. State 194 asks `0x00471890` for a point route to the room's
`(x+1, y)` tile, resuming in state 195, and ignores its return. With route option
8, failure returns zero without installing a route (`travel-states.md` §2.3),
so the creature remains in 194 and retries the same nearest candidate.

**CONFIRMED (whole-body reread, 2026-10-13):** every state-194 handler call
selects an entrance before requesting its route; it has no existing-route
shortcut. A retained route belongs to state 6 resuming in 195, not to state 194.
`LeavingRules.Step` accepts only that leaving-travel route without reselection;
an unrelated idle wander route must not count as successful leaving.

If no Portal or Hero Portal exists in any player's room list, state 194 enters
state 1 and then **87, wandering**. This contradicts the former project
inference that leaving considered only the creature owner's or neutral's portal.

## 2. The four leaving handlers

None of 194–197 integrates a walk itself; the generic point-travel state 6 does
that work and resumes the state supplied by the caller (`travel-states.md`).

| state | address | role |
|---|---|---|
| 194 | `0x00459DE0` | Set `Leaving`, choose the globally nearest entrance, and start state 6 with resume state 195. |
| 195 | `0x0045A000` | At the chosen room, select a point one quarter-tile to either side of its Y centre and start another state-6 route with resume state 196. A failed request returns zero. |
| 196 | `0x0045A0D0` | Final arrival: update sacking state, notify, discard carried gold, and mark the creature for removal. |
| 197 | `0x00459D50` | Alternate arrival gate: stop on its first tick; require an active runtime tile (`tile+0xC == 4`) in a Portal or Hero Portal; clear events 63 and 61, then call state 196 directly. Otherwise return zero. |

### State 196: sacking, notification, gold and removal

Arrival first clears creature event 63 and owner event 61. It calls
`0x004DC7E0` on the creature owner's player record with the creature type:
`player+0xB47` (`EntranceLastSackedCreatureTypeId`, the executable dumper's name)
is set to that type, and every matching entry in the player's 46-record
attraction table receives `0xA000`. No room-local portal tally is changed.

The handler plays the creature definition's `+0x6E3` sound. Except for creature
type 45, it raises notification **type 20**, addressed to the creature owner and
using the creature type as the variant. Type 20's row at `0x006A0BC8` has flags
`0x2032`: 50 game seconds and flag `0x2000`; its reference is zero, so it has **no
Mentor speech** under the resolver in `mentor-speech-and-notifications.md` §3.
The role of the row's value 2 at `+6` remains unread. Definition field `+0x423`
may also select an effect spawned at the creature; its meaning is UNKNOWN.

`0x0048AF50` is then passed the complete carried-gold value at
`creature+0x3FB`. It subtracts that amount with a floor at zero, so the field
becomes zero. There is no drop, player-credit or treasury call: **the gold
vanishes**.

Finally state 196 clears `creature+0x3E6` bit 5 and invokes creature vtable slot
`+0x20`, `0x004B2AD0`, which sets health at `creature+0x40` to zero. The normal
finished-object sweep subsequently sees the creature as finished and invokes
vtable slot `+0x30`, the creature-specific wrapper `0x0048AF80`; that calls
`0x004B7A20` with the creature's thing id. Thus this path **does not call the
generic thing deleter `0x004B7B10`**. The wrapper first runs the creature death
bookkeeping `0x00487C70`, including the loss/kill counters documented in
`player-object.md`, then unlinks the creature from its class list and decreases
that list's count.

## 3. State 193, joining your Dungeon

`0x00459C20` waits for the current action to clear, optionally plays the joining
creature's sound for the local player, zeroes three state words, offsets and
maps the creature definition's entrance vector, places the creature there and
copies that position to its previous-position fields. It then enters state **1**
through `0x0048AD30(1)` and immediately calls the lair-acquisition attempt
`0x0046F390`, which may replace state 1 with state **80** when a lair trip can
begin.

## 4. Why defeat preserves states 87, 6 and 7

- **87, wandering (`0x004808E0`):** this is state 194's deliberate no-portal
  fallback, which chooses a random valid point and routes there, so reissuing 194
  each defeat tick would defeat that fallback.
- **6, point travel (`0x00470C80`):** it is the mover used by both leaving legs
  and resumes the stored next state on arrival, so resetting it would discard an
  in-progress leaving route.
- **7, room travel (`0x00471460`):** it likewise owns an in-progress room route
  and resumes its stored next state after validating the destination room, so a
  defeat tick must not replace it.

## 5. Built (2026-09-24, hash-neutral)

`LeavingRules` drives both the deserter (`CreatureAngerSystem.Leave`) and a fallen
keeper's creatures (`DefeatRules`): §1's rule — every player's Portals and Hero
Portals, the nearest by squared straight-line distance, no reachability test —
then one route request, retried each call when it fails; on an entrance tile the
creature is removed and its gold vanishes (§2). Deviations, each flagged at the
site: the target is the nearest entrance *tile* that is not the blocked centre,
where DK2 targets the room's `(x+1, y)` and then a quarter-tile point (195); arrival
is any entrance tile; the no-entrance fallback to state 87 is not built (Q41), the
creature standing instead; the sacking table, notification 20 and the `+0x423`
effect are not modelled.

**On state ids** (ADR-0207): the decision sets 194 through the gate, so a creature in
a state the rule refuses 194 from (the captive, device and pit states) does not leave;
a route found starts state 6 resuming 195; a failed one leaves it in 194; the
entrance hands over to 195 and sets 196 before the removal. Ours has no second walk
from 195.
