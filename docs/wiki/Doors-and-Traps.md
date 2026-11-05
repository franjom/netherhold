# Doors and traps

`Research: read` · `Simulation: built` · `Verified vs original: spot-checked` · `Unity: partial`

Door and trap placement, manufacture, triggering and effects: what a keeper's blueprints become, how a door opens, locks and is fought, and how every shipped trap fires.

## Behaviour, in our words

**Placement and construction.** A door or trap starts life under construction: the level loaders seed that state from one door flag bit and, for traps, from the field the parser calls `NumberOfShots`, which DK2 reads only as a yes/no "already built" marker (STRONGLY INFERRED, binary). A keeper places a blueprint on its own open floor, a door only between two walls unless it is a Barricade (STRONGLY INFERRED), and the gold is taken at once (CONFIRMED, owner 2026-09-26). Imps then build it like an authored one.

**Doors.** Whether a door is open is recomputed every update: a running lock-pick countdown forces it open, lock mode 3 shuts it, otherwise it is open while a friendly creature stands on the door tile or the two tiles beside it (STRONGLY INFERRED). The player's lock command turns an unlocked door (mode 0) to mode 3 and any other mode to 0. A hostile creature that walks into a door makes it its forced target and fights it, with no test that the door is shut (STRONGLY INFERRED). The owner saw an enemy stop to attack a door held open by its owner's creature (CONFIRMED, Q454) and saw a door die when the wall beside it was dug out (CONFIRMED). A dead door leaves the world in the turn it dies (STRONGLY INFERRED).

**Traps.** A completed trap picks a victim with a sight, tile or Trigger selector, charges, checks mana and fires its shot; the shot's own kind decides the effect (STRONGLY INFERRED). A Sentry shot a Goblin six tiles away, past its nominal range (CONFIRMED, Q452); the binary explains it, since a turning trap looks two tiles further (STRONGLY INFERRED). Another Goblin, four tiles away, walked toward the trap and fled when hit, and the fear lasted all session (CONFIRMED, Q513 b and c); nothing read clears it. A hidden trap is revealed by firing, or by a Troll, Thief, Rogue or Guard passing it (STRONGLY INFERRED). A creature with nobody else to fight then attacks a revealed enemy trap; level-10 creatures were watched walking up and destroying a Hero Sentry (CONFIRMED, Q362). A hero trap on a Neutral-owned tile is destroyed on its first update (CONFIRMED, Q513a). The Boulder held to its predictions in play for Q520 (a) to (d) (CONFIRMED); (e) is unwatched, and Q737 and Q524 record where ours differed.

**Disarming.** No fresh v1.70 game with shipped data was found to start a disarm, and the advertised NUM 0 control is inert, so we do not build it (STRONGLY INFERRED, scoped: a restored save could carry the state; ADR-0329).

## Numbers from the game database

- **`TrapDef`** (from `Traps.kwd`, parsed by [`KwdTraps`](https://github.com/franjom/netherhold/blob/master/src/DK2.Data/Kwd/KwdTraps.cs)): `GoldCost`, `Health`, `HealthGain`, `ManufactureRequired`, `ManaUpkeepPerSecond`, `ManaCostToFire`, `RangeTiles`, `RechargeSecondsRaw`, `ChargeSecondsRaw`, `ThreatSecondsRaw`, `TriggerType` (the selector), `Flags`, and `ShotTypeId` with `ShotData1/2` and the shot offsets. `ShotTypeId` points into `Shots.kwd`, which owns each effect's damage type and process.
- **`DoorDef`**: `Health`, `HealthGain`, `GoldCost`, `ManufactureRequired` and `DoorFlags` (Secret Door, Barricade, Hero Barricade, Hero Portcullis).
- **Global variables**: `DoorPickedTimeSeconds` (lock-pick duration), `TriggerTrapTriggerSpeedTilesPerSecond` (Trigger countdown) and `BoulderToCreatureDamagePercentage`, read through the database and never copied into code.
- **Placed records** come from the level THINGS files; `PlacedThingsOnRealDataTests` compares every one with its source. The per-trap table of selector, recharge, mana and shot is [traps.md section 12](https://github.com/franjom/netherhold/blob/master/docs/research/traps.md).

## Evidence

| What | Where |
|---|---|
| Door update: open order, support, recovery, death | [`004A55F0`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004A55F0.md) |
| Door and trap THINGS loaders (construction markers) | [`00520F40`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00520F40.md), [`00520E50`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/00520E50.md) |
| Trap update, in order | [`004A84E0`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004A84E0.md) |
| Trap readiness and firing | [`004A7D10`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004A7D10.md), [`004A7FD0`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/004A7FD0.md) |
| The target's answer to a trap's shot | [`0045AA20`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/0045AA20.md) |
| Victim selection, including scan D (traps) | [`0045CB00`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/0045CB00.md) |
| Authored state, door opening and locking | [`doors-and-traps.md`](https://github.com/franjom/netherhold/blob/master/docs/research/doors-and-traps.md) |
| Door contact, the door fight and death | [`door-blocking.md`](https://github.com/franjom/netherhold/blob/master/docs/research/door-blocking.md) |
| Trap firing, Trigger, reveal, disarm; play observations (section 14) | [`traps.md`](https://github.com/franjom/netherhold/blob/master/docs/research/traps.md) |
| Boulder and Jack In The Box, with their play sections | [`boulder.md`](https://github.com/franjom/netherhold/blob/master/docs/research/boulder.md), [`jack-in-the-box.md`](https://github.com/franjom/netherhold/blob/master/docs/research/jack-in-the-box.md) |
| Blueprint placement, drawing | [`blueprint-placement.md`](https://github.com/franjom/netherhold/blob/master/docs/research/blueprint-placement.md), [`structure-drawing.md`](https://github.com/franjom/netherhold/blob/master/docs/research/structure-drawing.md) |
| Owner observations | Q454, Q452, Q513, Q362, Q520, Q524 in [`open-questions.md`](https://github.com/franjom/netherhold/blob/master/docs/research/open-questions.md) |

## Our implementation

- **Code:** [`DoorSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/DoorSystem.cs), [`DoorHealthSystem` and `DoorRemovalSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/DoorHealthSystem.cs), [`TrapSystem` and `TrapRemovalSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/TrapSystem.cs), [`AllianceSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/AllianceSystem.cs), [`BoulderSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/BoulderSystem.cs), [`JackInTheBoxSystem`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Systems/JackInTheBoxSystem.cs). Rules: [`DoorRules`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/DoorRules.cs), [`TrapRules`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/TrapRules.cs), [`TrapActivationRules`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/TrapActivationRules.cs), [`BlueprintPlacementRules`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/BlueprintPlacementRules.cs) with [`BlueprintCommands`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Commands/BlueprintCommands.cs), and the fight arms in [`CombatTargetRules`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Combat/CombatTargetRules.cs) and [`FightRules`](https://github.com/franjom/netherhold/blob/master/src/DK2.Simulation/Combat/FightRules.cs).
- **Tests:** [`DoorCombatTests`](https://github.com/franjom/netherhold/blob/master/tests/DK2.Simulation.Tests/DoorCombatTests.cs), [`DoorNavigationTests`](https://github.com/franjom/netherhold/blob/master/tests/DK2.Simulation.Tests/DoorNavigationTests.cs), [`TrapFiringTests`](https://github.com/franjom/netherhold/blob/master/tests/DK2.Simulation.Tests/TrapFiringTests.cs), [`TrapCombatTests`](https://github.com/franjom/netherhold/blob/master/tests/DK2.Simulation.Tests/TrapCombatTests.cs), [`BlueprintPlacementTests`](https://github.com/franjom/netherhold/blob/master/tests/DK2.Simulation.Tests/BlueprintPlacementTests.cs), and against shipped data [`TrapsFireOnRealDataTests`](https://github.com/franjom/netherhold/blob/master/tests/DK2.Behavior.Tests/TrapsFireOnRealDataTests.cs) and [`DoorsOnRealDataTests`](https://github.com/franjom/netherhold/blob/master/tests/DK2.Behavior.Tests/DoorsOnRealDataTests.cs). More are in [`tests/`](https://github.com/franjom/netherhold/tree/master/tests).
- **Scenarios:** none are specific to this system; [`scenarios/`](https://github.com/franjom/netherhold/tree/master/scenarios) holds a few that merely include a door or trap. The play comparisons use the `M7Traps`, `M7Q362` and `M7Q513a` levels from [`tools/levelgen`](https://github.com/franjom/netherhold/tree/master/tools/levelgen).
- **Decisions:** ADR-0159 (lock command, picked lock), ADR-0231 (placement), ADR-0318 (door fight), ADR-0321 and ADR-0331 (traps fire), ADR-0326 (upkeep, trap damage), ADR-0333 and ADR-0337 (Boulder, Jack In The Box), ADR-0338 and ADR-0340 (reveal, scan D), ADR-0329 (disarming deferred); the full list is [[Decisions by System|Decisions-by-System]]. Every shipped trap type fires except the Guard Post, which runs only its gate: 955 of the 959 placed traps ([traps.md section 13](https://github.com/franjom/netherhold/blob/master/docs/research/traps.md)).

## Divergences and open questions

- **Disarming is not built** (ADR-0329, Q455): no initiator was found, so we invent none. Q451, whether a failed disarm fires the trap, waits on it.
- **Left unbuilt by choice or for lack of a read:** a closed door pushing creatures off its tile, the Secret Door reveal, effects, sounds and animations, and shot heights (moved to a later milestone, ADR-0341, Q398). See the "Built and left" sections of [door-blocking.md](https://github.com/franjom/netherhold/blob/master/docs/research/door-blocking.md) and [traps.md](https://github.com/franjom/netherhold/blob/master/docs/research/traps.md).
- **Ours against play (Q732, Q737):** in the watched trap levels our creatures reach a trap sooner than DK2's, one of our Vampires casts where DK2's walks in to melee, and a boulder-struck Troll stays stunned in ours while DK2's goes on to dying. Not explained yet.
- **Q450** lists what is left of trap firing. **Q524** records that DK2's Jack reaches a heart corner that ours first could not; ours settles there since ADR-0627.

All open questions are in [[Open Questions|Open-Questions]]; terms are in the [[Glossary]].

---

*Last verified against `2f91419d` on 2026-11-05.*
