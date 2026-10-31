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
The hand-drop entry and state 197 are recorded in section 2.

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
| 197 | `0x00459D50` | Hand-drop gate: stop at age 1; require an owned Portal or Hero Portal centre (`tile+0xC == 4`), fire events 63 and 61, then directly call the state-196 handler. Otherwise return zero. |

### Hand drop entry and direct call (2026-10-31)

**STRONGLY INFERRED**, whole handlers and focused instructions re-read:
`0x0046EAB0`'s keeper-worker Portal/Hero Portal arm at `0x0046F060..0x0046F0B4`
enters 197 for an owned centre except type 1, the Imp. Non-workers reach the
room-table drop state through `0x004E8DA0(0)`; its test-only arm singles out
Portal type 3, not Hero Portal, for immediate room takeover. The latter can
still reach its table state after getting up. `0x00459D84/0x00459D9A` call
`0x004539B0` with allied acceptance off, enforcing ownership; `0x00459DA3`
requires piece 4. `0x00459DCA` calls the 196 handler directly, without storing
state 196. Both handlers issue the sacked events, so this path issues each twice.

`0x004545C0` returns floor zero for a type-3 Portal piece 4; Hero Portal has
no such floor-height arm. This establishes the hole's floor, not its falling
duration. Ours enters 197 on accepted disposal and runs it the next turn;
the general hand-release fall remains outside this change.
The native update `0x00488930` calls timed effects `0x0048E160` at `0x0048957D`
before dispatching the state at `0x0048A030`; possession skips that dispatch
at `0x0048A026..0x0048A028`. Ours dispatches197 in the job-handler phase,
so chicken expiry and other preceding state changes can prevent the exit.

`0x0045A10E` invokes `0x004DC7E0`: owner last-sacked type `+0xB47` is set and
all matching entrance rows get block `0xA000`. Sound event `0xDF` is requested
at `0x0045A147` from creature bank `def+0x6E3`; notification 20 is posted at
`0x0045A177` except for type 45. The effect at `0x0045A17C..0x0045A1BE`
comes from the **Portal type-3 room definition**, slot `+0x423`, even for a
Hero Portal. `0x004EACC0` copies this from file `+0x384` via staging `+0x458`;
it is `CreatedObjectEffectId`, shipped Portal value 96. The creature bank is
loaded by `0x00492320` at `0x00493D7C..0x00493DAE` from staging `+0x1346`,
file `+0xF56`, a 32-byte name truncated to 31 by the native loader.

`0x0045A1CE` calls `0x0048AF50` with the whole purse before removal. The
field becomes zero without a pile or a keeper credit. Q137 retains the owner's
observation that thrown creatures leave **with** their gold: this matches no
spill, but does not establish the field surviving inside the removed creature.

### State 196: sacking, notification, gold and removal

Arrival first fires creature event 63 and owner event 61. It calls
`0x004DC7E0` on the creature owner's player record with the creature type:
`player+0xB47` (`EntranceLastSackedCreatureTypeId`, the executable dumper's name)
is set to that type, and every matching entry in the player's 46-record
attraction table receives `0xA000`. No room-local portal tally is changed.

The handler requests event `0xDF` from the creature definition's `+0x6E3` bank. Except for creature
type 45, it raises notification **type 20**, addressed to the creature owner and
using the creature type as the variant. Type 20's row at `0x006A0BC8` has flags
`0x2032`: 50 game seconds and flag `0x2000`. Its zero table reference is
overridden by `0x004C4987..0x004C49AF`: creature definition `+0x6D1` becomes
reference `-10000 - LeftSpeechId`, and the message plays that Mentor line.
The row's value 2 at `+6` is its tab graphic. The Portal room's effect `+0x423`
is spawned at the creature.

`0x0048AF50` is then passed the complete carried-gold value at
`creature+0x3FB`. It subtracts that amount with a floor at zero, so the field
becomes zero. There is no drop, player-credit or treasury call: **the gold
vanishes**.

Finally state 196 clears `creature+0x3E6` bit 5 and invokes creature vtable slot
`+0x20`, `0x004B2AD0`, which sets health at `creature+0x40` to zero. The subsequent
finished-object sweep and its precise scheduling remain STRONGLY INFERRED here;
the established removal chain invokes
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

## 5. Built (hash/save 346, ADR-0603)

`LeavingRules` drives both the deserter (`CreatureAngerSystem.Leave`) and a fallen
keeper's creatures (`DefeatRules`): §1's rule — every player's Portals and Hero
Portals, the nearest by squared straight-line distance, no reachability test —
then one route request, retried each call when it fails; on an entrance tile the
creature is removed and its gold vanishes (§2). Deviations, each flagged at the
site: the target is the nearest entrance *tile* that is not the blocked centre,
where DK2 targets the room's `(x+1, y)` and then a quarter-tile point (195); arrival
is any entrance tile; the no-entrance fallback to state 87 is not built (Q41), the
creature standing instead. The sacking table, notification 20, bank event `0xDF`
and Portal room effect are built with the hand-drop gate (hash/save 346). Ours
unlinks immediately; the native deferred finished-object sweep remains unbuilt.
The accepted disposal, get-up and level-placed entry paths share the gate;
non-worker room entry retains the Casino countdown; job 0 is never hated
([room-drop evidence](hand-drop-landing.md#4-0x004e8da00-the-room-drop-and-250--confirmed)).
The creature bank event reaches Unity through `CreatureSoundDirector` and
`ClassicSoundPlayer`; native playback acceptance is pending.

**On state ids** (ADR-0207): the decision sets 194 through the gate, so a creature in
a state the rule refuses 194 from (the captive, device and pit states) does not leave;
a route found starts state 6 resuming 195; a failed one leaves it in 194; the
entrance hands over to 195 and sets 196 before the removal. Ours has no second walk
from 195.
