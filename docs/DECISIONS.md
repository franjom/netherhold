# Architecture Decision Records

## ADR-0001 — Integer/fixed-point gameplay math
**Date:** 2026-08-29 · **Status:** accepted

**Context.** DK2 v1.70 mixes integer math with x87 floats (Flame ships `fpu_control.h`). Bit-exact x87 reproduction on modern hardware is not tractable.
**Decision.** Integer/fixed-point for every gameplay-affecting quantity; floats are presentation-only. Trades unachievable float-exactness for achievable cross-platform determinism.
**Consequences.**
- Positions cross the presentation boundary as `Fp32` tile units; the conversion to floats happens on the renderer's side (ADR-0035, ADR-0036).
- Sub-step fractions are carried as integer remainders (`CreatureState.MoveRemainder`, `TileState.WorkRemainder`), never as floats.

## ADR-0002 — Simulation independent of Unity, enforced at compile time
**Date:** 2026-08-29 · **Status:** accepted · amended 2026-09-04: the netstandard2.1 half was unenforced until then

**Context.** Assemblies Unity loads must target netstandard2.1 and must not reference Unity.
**Decision.** Simulation-side assemblies target netstandard2.1 and may not reference any assembly named `Unity*`. `ArchitectureTests` fails the run on a Unity reference.
**Consequences.**
- The no-Unity-reference half always held. The framework half did not: `Directory.Build.props` selected the framework behind a `Condition` on a `DK2UnityBound` property that each project set in its own body, which MSBuild evaluates after importing the props file — so every assembly built as net10.0 from M1 to 2026-09-04 and `src/Shared/Polyfills.cs` (`#if NETSTANDARD2_1`) was never compiled. Surfaced as `NU1201` when the Unity adapter was compiled (ADR-0036).
- Fix: `src/Shared/UnityBound.props`, imported explicitly from each Unity-bound project's body. Cost of the silence: one `Enum.IsDefined` overload, one `Stream.ReadExactly` polyfill; the pinned determinism hash did not move.
- Now enforced from the built artefact: `ArchitectureTests.UnityLoadableAssemblies_TargetNetStandard21` reads `TargetFrameworkAttribute` off the compiled assembly. A constraint expressed only in build configuration is a claim that itself needs a test.

## ADR-0003 — Clean-room use of reference projects
**Date:** 2026-08-29 · **Status:** licence half superseded by ADR-0041 (2026-09-04); source-use rule amended 2026-09-04 (owner; the "second amendment" cited across the repo): OpenKeeper may supply implementation, with attribution

**Context.** Written while the project was MIT. OpenKeeper is GPLv3; Flame carries no licence.
**Decision.**
- **Flame:** no reuse rights at all. Facts only; never one of its identifiers in `src/`, `tests/` or `unity/`, comments included (`FlameIdentifierTests`). A document may cite a Flame name as Flame's (ADR-0080).
- **OpenKeeper:** originally facts only, no code copied or transliterated. Owner, 2026-09-04: *"if any of your doubts can be answered from OpenKeeper, feel free to copy, reuse, whatever, we are now on the same license as them."* OpenKeeper may now be a source of implementation wherever it answers a question the shipped data cannot. Two non-optional obligations: (1) every borrowed implementation gets an entry in `THIRD-PARTY-NOTICES.md` naming the upstream class and what was taken (GPLv3 notice retention); (2) a rule taken from OpenKeeper is STRONGLY INFERRED, never CONFIRMED — OpenKeeper is itself a reimplementation and can be wrong.
- Every research note in `docs/research/` records provenance, so a borrowed fact can be traced to its source.
**Consequences.**
- First use: the portal (ADR-0042) took three composition rules from OpenKeeper's `CreatureSpawnSystem`, plus its note that two portals producing on one tick is an original quirk replicated deliberately.
- Caution from the same use: OpenKeeper's portal hands a creature to whoever claims an idle one only because it resets the clock to Java's `Double.MIN_VALUE` — the smallest positive double, not the sentinel it is read as. A bug is indistinguishable from a decision without reading the code closely; reuse is permitted, trust is not.
- The licence boundary table lives in `docs/research/external-resources.md`.

## ADR-0004 — xoshiro256** as the simulation RNG
**Date:** 2026-08-29 · **Status:** accepted

**Context.** DK2's original generator is UNKNOWN.
**Decision.** xoshiro256**: public-domain, fast, trivially serialisable, platform-independent. Deliberately not DK2's.
**Consequences.** If fidelity work shows the original sequence matters observably, a second generator goes behind the same `SimRandom` surface. The RNG position is hashed and snapshotted (ADR-0006, ADR-0007).

## ADR-0005 — .slnx solution format
**Date:** 2026-08-29 · **Status:** accepted

**Context.** .NET 10 default.
**Decision.** `DK2.slnx`; no tooling here requires the legacy `.sln`.
**Consequences.** `unity/DK2.Unity.Compile` is deliberately outside the solution (ADR-0036).

## ADR-0006 — Determinism is gated by a pinned state hash, exercised by a chaos system
**Date:** 2026-08-29 · **Status:** accepted

**Context.** Two runs can share visible state while having consumed different RNG draws; they are about to diverge.
**Decision.**
- `GameSimulation.StateHash()` is FNV-1a (`DK2.Core.Hash64`) over the world plus the RNG position; CI pins one exact value on Linux and Windows (`DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines`).
- `GetHashCode` is forbidden in the hash path: .NET randomises string hashing per process.
- A test-only `ChaosSystem` (`tests/DK2.Simulation.Tests/TestWorld.cs`) mutates state and consumes randomness every tick. It implements no DK2 mechanic; without it no system would run and the gate would pass with a broken loop, hash or snapshot.
**Consequences.**
- A deliberate change to simulation state bumps `GameSimulation.HashVersion` and updates the pinned literal in the same commit. Any other failure of that test is broken determinism and must not ship.
- New state must be reachable from `WorldState`, hashed in `HashInto` and written by `WorldSnapshot` (ADR-0007), or it is silently non-deterministic.

## ADR-0007 — Our own save format; DK2 save compatibility is not a goal
**Date:** 2026-08-29 · **Status:** accepted

**Context.** Reproducing DK2's save layout buys nothing behavioural (plan risk R13) and our state is not shaped like the original's.
**Decision.** `WorldSnapshot` writes a private binary format with its own `FormatVersion`. `save_struct.bt` is used only as a checklist of what state exists.
**Consequences.** The invariant is continuation, not byte equality: restoring a snapshot into a fresh simulation must produce identical state hashes indefinitely, which proves the snapshot captured everything the simulation depends on, RNG position included.

## ADR-0008 — YamlDotNet for scenario files (tooling only)
**Date:** 2026-08-29 · **Status:** accepted

**Context.** Scenarios are YAML (`DK2_REIMPLEMENTATION_PLAN.md` §6.2). A hand-rolled YAML subset would misread the regression corpus.
**Decision.** YamlDotNet (MIT) in `DK2.Harness` and the test projects only. No simulation assembly may take it; those stay dependency-free and Unity-loadable.
**Consequences.**
- The loader reports unrecognised keys instead of ignoring them; the runner reports recognised-but-unapplied setup. A scenario that quietly does less than it claims would corrupt the corpus.

## ADR-0009 — Tile transitions are triggered by work, not by health level
**Date:** 2026-08-29 (M3) · **Status:** accepted · amended 2026-09-07: fill-in is no longer one of the converting jobs (Q219)

**Context.** Each terrain carries `maxHealthTypeTerrainId`, which reads as "what this tile becomes at full health". Six terrain types ship at maximum health while pointing at a different type — Claimed Path is 660/660 and points at Rock — so a level-triggered rule would turn every claimed floor tile into rock on the first tick.
**Decision.** Conversion belongs to the work. Only the upward jobs (claiming, reinforcing) convert a tile on reaching maximum; repairing clamps and stops. Each lands where the data points, which answers Q18: the field has no second meaning.
**Consequences.**
- `TileWorkRules.CompletesUpward` (`src/DK2.Simulation/TileWork.cs`) is the single place the decision lives; no terrain is special-cased.
- Code now: `TileWorkKind.FillIn` was removed 2026-09-07 — `FillInHealth` has no reader in DKII.EXE and the owner does not recognise filling in as a DK2 action (Q219; see ADR-0039). The data shape (`FillInHealth` = 5, `TerrainFlags.FillInable`, Claimed Path → Rock) is recorded in `docs/research/tile-work.md`.

## ADR-0010 — Work orders are a queue on the world, filled by whoever is working
**Date:** 2026-08-29 (M3) · **Status:** accepted

**Context.** Tile mechanics were ready before the creature that performs them; job selection (Q2) and pathfinding (Q5) should not block them.
**Decision.** `WorldState` carries a FIFO `TileWorkOrder` queue. `TileWorkSystem` drains it every tick and applies the rules. Producing orders is somebody else's job: a working creature, or a test or the scenario harness.
**Consequences.**
- A scenario can inject work no creature would do, so `ScenarioRunner` reports harness-injected work as bypassing the worker in every run that uses it, and `ScenarioTests` asserts that it does.
- The queue is a list, hashed and snapshotted like all other state. Order is behaviour: it is never sorted or deduplicated.

## ADR-0011 — Ticks are related to seconds in exactly one place
**Date:** 2026-08-29 (M3) · **Status:** accepted · amended 2026-09-07 by ADR-0089: `TicksPerSecond` is 4, read from the binary, no longer a placeholder

**Context.** Q1 (DK2's tick rate) was unmeasured and Q19 (what the health deltas are per) depended on it.
**Decision.** `SimulationTiming` holds every tick-to-second relationship. No tick count that means a duration may be written anywhere else.
**Consequences.**
- Answering Q1 was a one-line change: `SimulationTiming.TicksPerSecond = 4` (one tick is 250 ms; ADR-0089). `OriginalTicksPerGameSecond = 4` is kept as a separate constant of the original.
- Per-second work is scheduled as whole applications, so the determinism gate never depended on the placeholder value.

## ADR-0012 — The pathfinder is an admitted placeholder behind an interface
**Date:** 2026-08-29 (M3) · **Status:** accepted · amended 2026-08-30 by ADR-0025: connectivity and step cost are now DK2's; tie-breaking remains ours · amended 2026-09-16 by ADR-0155: the original's architecture is now known and is not a grid search

**Context.** DK2's routing is Q5, the project's largest open question (plan §9, R1). A 36-byte `Follow_Path` class was known by name only, and nobody had reconstructed it.
**Decision.** `DK2.Pathfinding.IPathfinder` defines the two questions the simulation asks — route to a known tile, and route to the nearest tile satisfying a predicate. `GridPathfinder` answers them with a deterministically tie-broken grid search and documents what is not DK2. `ImpTaskSystem` takes the pathfinder by constructor, so a reconstruction drops in without touching the systems.
**Consequences.**
- Originally four-connected, uniform-cost, chosen as the conservative option. Code now: eight-connected with octile cost (Q28 CONFIRMED, Q48; ADR-0025); no terrain weighting (Q30 CONFIRMED — the shortest route wins even through water).
- Which of several equally short routes comes back is decided by a fixed neighbour order and then tile index — reproducible, but ours (Q5). The harness says so in every run that places a worker.

## ADR-0013 — One declared system order, in one place
**Date:** 2026-08-29 (M3) · **Status:** accepted

**Context.** System order is part of the simulation's definition; it was being written out twice (test fixture, scenario harness).
**Decision.** `DK2.Simulation.Systems.SimulationSystems.Default()` is the single declaration of which systems run and in what order. Tests and the harness both use it.
**Consequences.**
- The original four (tags, movement, task selection, work) have grown to the full list in `SimulationSystems.Default()`, each with its rationale; the rule is unchanged.
- DK2's own per-tick subsystem ordering remains UNKNOWN; ours is explicit.
- A system added later is added to one list, and the pinned determinism hash moves in the same commit.

## ADR-0014 — Worker behaviour covers marked work only
**Date:** 2026-08-29 (M3) · **Status:** superseded by ADR-0018 (same day)

**Context.** A DK2 imp digs what is marked but also claims, reinforces, carries gold, drags bodies and flees. Each unprompted job needs a rule for when it starts and how it ranks; that order was UNKNOWN (Q24).
**Decision.** `ImpTaskSystem` implemented marked work and nothing else — the one job whose trigger is not in doubt.
**Consequences.**
- Every scenario run stated which jobs were missing and that their priority was UNKNOWN.
- Superseded when the owner supplied the order (ADR-0018); refusing to guess is why adopting the real order cost one afternoon and five test rewrites. Later refinements: ADR-0056, ADR-0058.

## ADR-0015 — Gold lives in tiles; a player has no gold counter
**Date:** 2026-08-29 (M3) · **Status:** accepted

**Context.** DK2's balance table has no variable for a player's gold, a starting balance or a maximum. It has four per-place limits: `MaxGoldPerTreasuryTile` (3000), `MaxGoldPerDungeonHeartTile` (1000), `MaxGoldPileOutsideTreasury` (2000), and the CREATURES record's per-creature `maxGoldHeld`.
**Decision.** Gold is stored on tiles (`TileState.GoldHeld`); `PlayerState` has no gold field. `GoldStore` is the only way gold moves; a player's spendable total is the sum over tiles they own. Mana stays on the player (`PlayerState.Mana`): its variables are ceilings and rates on one pool.
**Consequences.**
- A keeper with no treasury cannot keep gold; destroying a treasury takes the gold with it; a first treasury is paid for out of the dungeon heart.
- `GoldStore.Deposit` returns what it accepted; `TileWorkResult.GoldSpilled` reports what a destroyed room lost. Nothing is silently kept or dropped.
- `Total` and `TryWithdraw` scan the tile array; a renderer wanting the number every frame should cache it.
- A creature's hands became the fourth place (ADR-0019); a floor pile the fifth (ADR-0055).

## ADR-0016 — Where a room may go is read from the room's own flags
**Date:** 2026-08-29 (M3) · **Status:** accepted

**Context.** The ROOMS record carries `PlaceableOnLand`, `PlaceableOnWater`, `PlaceableOnLava`; the TERRAIN record carries `Water`, `Lava`, `Solid`, `Room`.
**Decision.** `RoomRules.IsSuitableGround` asks the flags and never names a room — the terrain-id discipline (ADR-0009) extended to rooms.
**Consequences.**
- No code knows what a bridge is; the three shipped bridges fall out of the flags. `RoomsOnRealDataTests` asserts the partition against the install.
- The one place a room type is named is `GoldStore.Capacity` (`RoomTypeId.Treasury`, `RoomTypeId.DungeonHeart`), where the two variables are themselves named for those rooms.

## ADR-0017 — A per-second rate from the balance table is applied once a game second
**Date:** 2026-08-29 (M3) · **Status:** accepted

**Context.** Mana income is one mana per claimed tile per second; spread over `TicksPerSecond` ticks it is a fraction, needing fixed-point mana or a per-player accumulator in the hash and snapshot.
**Decision.** Rates named `…PerSecond` are applied in whole, once every `SimulationTiming.TicksPerSecond` ticks, on the last tick of each game second. `ManaSystem` does this and holds no state of its own.
**Consequences.**
- Integer arithmetic, no remainder, no new field in `PlayerState`; totals are independent of `TicksPerSecond`.
- The counter moves in steps. Whether DK2 accrues smoothly is UNKNOWN (Q39); if it matters the change is local to `ManaSystem` plus one accumulator field.
- Generalises `SimulationTiming.WorkApplicationPeriodTicks`; ADR-0011 is unchanged.

## ADR-0018 — Imps take jobs in a strict, confirmed priority order
**Date:** 2026-08-29 (M3) · **Status:** accepted · supersedes ADR-0014 · amended 2026-09-05 by ADR-0056 and ADR-0058: the order stands, what it is part of changed

**Context.** ADR-0014 restricted workers to marked excavation because the order an imp prefers its jobs in was UNKNOWN (Q24). The owner supplied the original's order: **marked excavation, then claiming, then reinforcing**, then wandering the dungeon and interacting with scenery and creatures.
**Decision.**
- `ImpTaskSystem` implements those three in that order as a **strict** priority: the whole reachable map is searched for excavation before a single claimable tile is considered. Three sequential nearest-searches rather than one scored search over a mixed candidate list, because what was described is an ordering, not a weighting.
- Carrying mined gold is deliberately **not** in the list: an imp mines into its own hands and delivers when full, so carrying is the tail of a mining job (ADR-0019).
- Claiming and reinforcing are taken only from ground the keeper already holds, which is what makes territory spread outwards rather than in islands (STRONGLY INFERRED, Q42).
**Consequences.**
- An imp with nothing marked claims and reinforces outward from its keeper's territory; five tests that had encoded the old restriction changed.
- Wandering is not implemented: an imp with none of the three stands still (Q41). It has no simulation consequence we model, and inventing a walk would put imps where DK2 would not.
- The original's cap of three imps a face and twelve a tile (Q43) was unimplemented here; ADR-0026 and ADR-0030 built it.
- Code now: this order is one tier of a two-tier chooser (ADR-0058), and `ImpTaskKind` carries DK2's own task-kind bytes read from the binary — `None` 0, `Excavate` 1, `Claim` 3, `Reinforce` 6, `FetchGold` 7, `CarryObject` 8, `CarryCorpse` 12, `CarryUnconscious` 13, `ClaimFreshlyDug` 14 (`docs/research/imp-task-priority.md`).

## ADR-0019 — A creature's hands are a place gold can be
**Date:** 2026-08-30 · **Status:** accepted

**Context.** ADR-0015 named four places gold can be, three of them tiles; the fourth is a creature's hands, bounded by the CREATURES record's `MaxGoldHeld`, unimplemented at M3 because nothing carried anything. Q31 (owner): an imp mining a seam **takes the gold into its own hands** up to that capacity, runs to a treasury or dungeon heart, and puts it down — a floor pile is the fallback for when there is nowhere to put it, not the first step.
**Decision.**
- `CreatureState.GoldHeld`, bounded by the creature's own `MaxGoldHeld`, hashed and snapshotted, and surviving `ClearJob`: what is in an imp's hands is not part of its job.
- `TileWorkOrder` carries the id of the creature that swung, so the resolver knows whose hands the gold goes into. An order with no creature behind it (a test, the harness) has its gold reported and dropped.
- Crediting the keeper at the rock face was rejected: the walk is why gold arrives in lumps, why destroying a treasury mid-run loses a specific amount, and why the counter lags the seam.
**Consequences.**
- A keeper's spendable total dips while imps are in transit; `GoldStore.Total` is right to exclude it.
- `GoldStore.DepositInto` fills the one tile the imp stands on and the imp looks again if it is still carrying, so spillover between treasury tiles is a consequence rather than a rule — as close to Q35 as observation has got. `GoldStore.Deposit`'s tile-index order survives only where no carrier is involved: paying for a room, and refunds.
- Where an imp stops mining is ours (Q50, HYPOTHESIS): when one more swing would free more than it can hold.
- An imp with nowhere to unload keeps mining, which the original does, and **loses** what it cannot hold, which it does not — DK2 leaves a lump. Standing still would stop a keeper with no treasury mining at all. The lump needed the OBJECTS record; ADR-0055 delivered it.
- Not decided: whether `InitialGoldHeld` (500 on the Imp) means a creature spawns holding gold. Nothing reads it and nothing should until somebody knows.

## ADR-0020 — Work lands a fifth of the tabled delta at a time
**Date:** 2026-08-30 · **Status:** accepted · amended 2026-09-05: the fifth is Efficiency, not a cadence · superseded in part 2026-09-07 by ADR-0089: the rate is four applications a second, not five

**Context.** The ten tile-work deltas carry no unit, unlike their `…PerSecond` and `…Seconds` neighbours, and M3's reading — the whole delta once a second — ran everything visibly too fast (Q47). The owner then measured one: **an imp mining a gold seam frees 273 gold a swing**, and a seam being 3000 gold over 660 health makes that 273 × 660 / 3000 = **60 health**, so **11 swings**, and 60 is exactly a fifth of `MineGoldHealth`'s −300.
**Decision.** One application is a **fifth** of the tabled delta; `TileWorkRules.PerApplication` divides by it. The per-second rate is unchanged, only the granularity, so **the count of swings a tile takes is the measured quantity and is independent of Q1**.
- Corroboration 1: **eight of the ten tabled deltas divide by five exactly** — 825, −140, 125, −300, −5000, −130, 5, 50 — where chance would give two, and **825 is odd**, which rules out twice-a-second, four and ten.
- Corroboration 2: `ClaimTileHealth` 825 at a fifth is 165 a swing against a Dirt Path running 1→660, so **four swings** — the top of Q46's "2-3-4 jumps MAX", measured a day earlier on a different mechanic.
- `DigEnemyWallHealth` (−8) and `RepairWallHealth` (22) do not divide, so `TileState.WorkRemainder` carries the fraction between applications, the device `CreatureState.MoveRemainder` uses (ADR-0001); without it an enemy wall takes 900 swings instead of 563. The remainder is on the **tile**, not the worker, because two imps on one wall should between them remove what the table says per second.
- **Gold released is the difference of two running totals** — how much has come out by the time a seam is down to *this* health, minus how much had at the last — because truncating each swing independently loses a coin a swing and makes a seam worth 2992 of its stated 3000. The running-total form yields **exactly** `goldValue`, needs no state, and reproduces the measurement: 273 on eight of the eleven swings, 272 on the other three.

**Amendment, 2026-09-05 (CONFIRMED by the owner, off DK2's own status bar).** **The fifth is the worker's Efficiency divided by the four ticks of a game second.** DK2 prints the stat next to a creature's name — *"Imp. Name: Snipe. Job: idle. Efficiency: 80%. Mood: Happy."* — and 80 % is the `0xccc` = 0.8 fixed-point multiplier the work handlers read from `creature[0x3af]`, so an application is `delta × 0.8 / 4`. Efficiency is a **variable**, which this ADR assumed away: a slap raises it to 90 % for 25 game seconds (ADR-0062, Q159), dropping an imp next to a tile it has marked raises it to 90 % too, and both at once give 100 % — measured at 273, 309–310 and 341 gold a swing respectively (`docs/research/tile-work.md`, "Efficiency").
**Consequences.**
- Every application count changed: claiming a tile is 4 swings where it was 1, reinforcing rock 50 where it was 10, attacking floor 24 where it was 5.
- An imp fills its hands after **five** swings (1363 of 1400) and a seam takes **three trips** — 1363, 1364, 273 — which answers Q50 by arithmetic.
- `DigRockHealth` is the **only** number in the tile mechanics that does not fit; reproduced, not corrected (ADR-0023).
- The pinned determinism hash and the snapshot format both moved.
- Not a measurement of the tick rate: it fixes the ratio between an application and a tabled per-second delta.
- Code now: amount and rate are separate constants — `SimulationTiming.BaseApplicationDivisor` is **5** and sizes an application (`OriginalTicksPerGameSecond × 100 / EfficiencyRules.BasePercent`); `SimulationTiming.WorkApplicationsPerSecond` is **4** and schedules it, once a tick (ADR-0089).

## ADR-0021 — Imps come from a floor and a price, and mana finally has an outgoing
**Date:** 2026-08-30 · **Status:** accepted · amended 2026-09-05 by ADR-0057: the free-imp clock is a deadline, not a countdown

**Context.** Nothing generated imps and nothing spent mana. The owner supplied the mechanic: a keeper starts with four imps, four is a **floor** rather than an allowance — drop them all into the dungeon heart and they come back at roughly five-second intervals — and beyond that an imp is bought with the Create Imp spell at 1500 mana, rising by 1500 each time. Every number is in DK2's data: the balance table names the first two in its own words (`MinimumImpThreshold` 4, `TimeBeforeFreeImpGeneratedSeconds` 5), which is why the free supply is CONFIRMED rather than inferred.
**Decision.**
- The 1500 is `KeeperSpells.kwd` offset **0x11C**: 13 records over 5278 bytes, exactly 406 each, and that offset reads Possession 500, Create Imp **1500**, Sight of Evil 5000, Thunderbolt 6000, Call to Arms 10000, Create Gold 15000, Turncoat 20000, Tremor 30000, Inferno 50000, Summon Horny 100000. Thirteen plausible prices in an order matching how the game plays, one independently measured: a structural validation, not a lucky hit.
- `ImpSupplySystem` hands a keeper below `MinimumImpThreshold` one imp every `TimeBeforeFreeImpGeneratedSeconds`, at their dungeon heart, and stops. **Amended by ADR-0057:** right for the second replacement onwards, wrong for the first — the original keeps a **deadline** that only moves when an imp is made, so a keeper at full strength replaces their next loss **immediately** (owner's film; the binary said why).
- `CreateImpCommand` buys one for `CreateImpBaseCost × (imps − minimum + 1)` — 1500 for the fifth, 3000 for the sixth. Owner: *"four free (starting) imps don't raise mana cost… if you drop down to 4, cost also drops to base 1500"*. **The price falls back**, so the cost is a function of the imps a keeper has now rather than a tally of what they have bought: no counter, and it cannot drift from the roster. CONFIRMED again from the binary (ADR-0057), which computes the same expression clamped to at least one step, on a creature type read off the power's own record.
- `WorldState.RemoveCreature` exists so that an imp count can fall: a floor that cannot be tested by dropping below it is not a tested floor.
**Consequences.**
- **Mana can go down** — the first outgoing in the project.
- Any world with a dungeon heart grows imps, which surprised two existing tests that had heart tiles for their gold capacity and quietly acquired a workforce. Both were re-timed rather than exempted.
- `ImpSupplySystem` runs **last**, after the mana it spends, so a keeper who earns the price on tick N can cast on tick N.
- Code now: `CreateImpBaseCost` is a lookup out of `KeeperSpells.kwd` rather than a hardcoded constant (ADR-0022); `WorldState.RemoveCreature` is called by `CreatureHealthSystem`, `CreatureAngerSystem`, `HandSystem`, `ImpTaskSystem` and `PrisonSystem`, because creatures now die and leave (ADR-0051).
- What this ADR could not build: the ~7 mana a second an imp costs to keep, which was in no file we parse. Built the next day (ADR-0022).

## ADR-0022 — An imp costs mana to keep, and the number was on the spell
**Date:** 2026-08-30 · **Status:** accepted · supersedes ADR-0021's closing paragraph, which said this could not be built · amended the same day: the mana ceiling sees the net

**Context.** DK2 charges about 7 mana a second per imp and this simulation charged nothing. The owner measured it from two mana-orb screenshots — four imps read **−28**, and after summoning a fifth, **−35** — so the *unit* is confirmed as well as the rate. The field is not on the creature: a rescan of all 5449 bytes of the CREATURES record found four offsets holding the Imp's 7, and every one is an ArtResource frame count, an array-length header or uninitialised padding.
**Decision.**
- `KwdKeeperSpells` parses **two** fields of the 406-byte KEEPERSPELLS record and no more: the cast cost at 0x11C and the per-second upkeep at **0x00E2**. Parsing a record badly is worse than not parsing it; the other 400 bytes wait for M7.
- **The upkeep offset is accepted on structure, not on the anchor.** It reads non-zero for exactly the five spells whose result persists — Create Imp 7, Summon Horny 2000, Call To Arms 90, Possession 25, OneShotReaper 125 — and zero for all eight instantaneous ones, and a spell that is over when it lands has nothing to keep. Second anchor in the same column: 2000 is the community's Horned Reaper figure, which had been the right number in the wrong record.
- **Income and upkeep stay separate through the whole calculation.** The original shows them separately, and `MaximumManaGainPerSecond` (500) caps only the income, so netting first would let a big enough workforce raise a keeper's income cap.
- **Every imp is charged, including the free four**; the owner's starting complement drains 28. The floor is free to fill and not free to keep, and that is the pressure the imp economy is built on.
- **Mana floors at zero and nothing else happens.** What the original does when a keeper cannot pay is UNKNOWN (Q57); `InsufficientManaImpEvaluationPeriodSeconds` (10) says *something* does, and guessing it would put imps in graves DK2 might not.
- **Keeper spells are keyed by their data-file name**, via `KeeperSpellName`: the record's id field has not been identified, and an ordinal from record order would break silently if the shipped order differed.

**Amendment, same day, from a third screenshot.** `ManaRules.ApplyOneSecond` first clamped income to the ceiling and *then* charged upkeep. A keeper photographed at full mana reads exactly **200000** while the orb shows **+400** and **−297**, which under that ordering would sit permanently at 199703. So the ceiling sees the **net**: cap income at `MaximumManaGainPerSecond`, subtract upkeep, then clamp to the threshold. The separation still matters, for the income cap rather than for the ceiling.
**Consequences.**
- ADR-0021's hardcoded `CreateImpBaseCost = 1500` is gone; it is a lookup, and the exception lasted one session.
- A synthetic database must supply keeper spells; three test fixtures gained a Create Imp definition. `GameDatabase.RequireKeeperSpell` throws with the missing name in the message, the loud-at-the-seam convention the balance variables use, and it is how the third fixture was found.
- `GameDatabase`'s constructor takes spells as an optional last parameter, so data with none still loads: `KeeperSpell` returns null, only `RequireKeeperSpell` throws.
- The pinned determinism hash did not move, correctly: `TestWorld` has no workers and no mana-generating terrain, and no hashed field changed shape, so no `HashVersion` bump.
- Still open: what a trap draws (Q59 — confirmed to exist, unmeasured, `Traps.kwd` unparsed); whether possession really drains 25 (Q58 — the data disagrees with itself and with the owner); what happens at zero mana (Q57).

## ADR-0023 — One measured behaviour overrides one shipped number
**Date:** 2026-08-30 · **Status:** accepted, owner-approved

**Context.** `DigRockHealth` ships as **−5000**, which against Rock's 165 health is 1000 a swing and fells a plain wall in **one**; the owner dug one out in the original and counted **two**, distinguishing the two swings that do the damage from the third animation playing over the block already crumbling. Two applications against 165 health bounds the per-application damage at **[83, 164]** — under 165 and one swing would finish it, under 83 and two would not — so the per-second figure is in **[415, 820]** and the shipped value is six to twelve times too big.
**Decision.** Correct it to **−500**, in one named place.
- Three checks that could have made it a misreading came back negative: **no alternative variable** (six table values fall in [415, 820] — two payday intervals, a boulder fear amount, a tremor damage, a music threshold, the mana gain cap; none is a candidate); **the model around it is confirmed** (the same reading gives a seam 273 gold a swing and claiming four swings, measured independently on different days); **the value is an outlier in its own table** (every other tile delta is in the tens or low hundreds — 825, 125, −300, −130, −8, 5, 50, 22 — and −5000 is the sole four-digit one).
- **`ObservedCorrections` is the only place in the project that may contain a number the data does not.** It carries the measurement, the arithmetic, the negative search and the rules for adding to it, and `TileWorkRules.PerApplication` reads through it rather than off the database.
- **−500 and no other value in the window:** the only round number in [415, 820] and exactly a tenth of the shipped figure, and an extra zero is the most ordinary way for a number to be wrong. It gives 100 a swing: 165 → 65 → destroyed, two applications, no coincidence required.
- **The correction fires only on the exact shipped −5000** (`ObservedCorrections.ShippedDigRockHealth`), so a mod or patch carrying a workable number is left alone: a correction to one known-bad value, not a policy about digging.
- **The raw value stays readable:** `db[VariableTypeId.DigRockHealth]` still returns −5000, and tests assert both it and the corrected behaviour.
- **Four rules govern adding a second entry**, written on the type: the behaviour must be measured rather than inferred; the shipped value must be shown incapable of producing it, with the arithmetic; the table must have been searched for an alternative variable and come back empty; and the correction must be guarded on the exact shipped value.
**Consequences.**
- Five tests changed, all of which had encoded the one-swing behaviour, including two that were only incidentally digging.
- This corrects the behaviour, not the explanation. Whether the engine reads a different field, applies a divisor nobody has found, or ships a typo is UNKNOWN (Q20); finding out would let the override be deleted.
- A deliberately narrow precedent: the two other candidates, the imp mana upkeep and the Create Imp cost, turned out to be findable in the data. **Look for the field before reaching for this file.**

## ADR-0024 — An upward job costs the terrain's whole MaxHealth
**Date:** 2026-08-30 · **Status:** **reversed 2026-09-07 by ADR-0089** — an upward job costs the **deficit**

**Context.** Claiming and reinforcing convert a tile to its `MaxHealthTypeTerrainId` and did it by healing, so the work required was the gap `MaxHealth − Health`. The owner stopwatched one imp reinforcing one wall at **13.38 s and 13.77 s**, both carrying reaction time at each end; at five applications a second that is 66–67 swings, 13.2 s is 66 exactly, and `ReinforceWallHealth` at 10 a swing makes the work 660 — Rock's whole `MaxHealth` — against the 495 its 165 starting health gives, where the gap rule predicted 50 swings and 10.0 s.
**Decision (as accepted, now withdrawn).** An upward conversion needs `terrain.MaxHealth` worth of work, counted from zero in `TileState.UpwardWork` rather than in `Health`; reinforcing does not heal; repair jobs raise `Health` and stop at `MaxHealth`; `UpwardWork` resets on any terrain change along with `WorkRemainder`. `HashVersion` 7 → 8 and snapshot `FormatVersion` 7 → 8.
**Consequences.**
- **Reversed.** DK2's tile applier (`FUN_00452f60`) adds the delta into the tile's **own health word** and converts when that health reaches the terrain record's `MaxHealth`, so an upward job costs the **deficit**: reinforcing Rock is 660 − 165 = 495, which is **50** applications, and claiming Dirt Path is 659 ÷ 165 = **4**. `TileState.UpwardWork` no longer exists — the progress *is* the tile's health, so a half-reinforced wall is a tougher wall. Read ADR-0089 before citing anything here.
- The stopwatch fit was made under Q47's mistaken five-a-second cadence and **the two errors cancelled** in wall-clock terms, which is why no test caught either. The read model's residuals are all under a second and in the direction a hand timer errs; this ADR's rule required an *under*-read, which reaction time cannot produce.
- What stands: **which** terrain a tile becomes is still `MaxHealthTypeTerrainId` selected by the work (Q18), and claiming is still 4 applications — which is why the two rules were indistinguishable there, Dirt Path shipping at **1** health so that its gap (659) and maximum (660) are within one swing of each other.

## ADR-0025 — Movement is eight-connected, and a diagonal costs √2
**Date:** 2026-08-30 · **Status:** accepted · supersedes the four-connected choice on `GridPathfinder` since M2 · amended 2026-09-05 by ADR-0059: a diagonal leg is walked surplus-first

**Context.** `GridPathfinder` was four-connected as the conservative option, because whether DK2 lets a creature squeeze diagonally between two solid tiles was UNKNOWN. The owner watched the original (Q28): it **will**, so every route this project produced was an overestimate, in the system every other system depends on.
**Decision.**
- **Eight-connected, and corners may be cut.** No corner-cutting check: a diagonal neighbour is reachable whatever its two orthogonal neighbours are, which is what the owner observed.
- **The diagonal cost is forced rather than measured** (Q48, which was queued as a stopwatch task and should not have been). A creature's speed is in *tiles per second* — Q29, three variables name that unit in their own words — so a creature moves continuously through world space at a constant rate, so a step to a diagonal neighbour covers **√2 tile-widths** and takes **1.41× as long**. Uniform cost was never a live option; it would have been the invention.
- **Octile step cost: `StraightCost` 10, `DiagonalCost` 14**, the integer stand-in for 10√2 = 14.142. Integers throughout, so no square root, no floating point, no cross-machine drift.
- **The heuristic is octile distance**, the exact cost of the cheapest unobstructed route — admissible and consistent, so A\* still returns shortest routes.
- **`TryFindNearest` becomes Dijkstra**, not breadth-first: once steps stop costing the same, a breadth-first sweep returns the goal reachable in the fewest *steps* rather than the nearest one. The owner confirmed an imp takes the marked tile nearest **by walking distance** (Q25), and three diagonal steps (42) are further than four straight ones (40).
- **The mover charges the same 14/10.** `CreatureMovementSystem` measured a leg with `|dx| + |dy|`, which charges 2× for a diagonal, and a router and a mover that disagree produce creatures that arrive early.
**Consequences.**
- Every route in the game is shorter. Scenario hashes moved; the pinned determinism hash did not, because the fixture behind it has nothing that walks.
- Three tests changed, each with a four-connected assumption written in: a path-step adjacency assertion using Manhattan distance, a detour length of 8 that is now 4 diagonals, and a gold-carrying test pinned to a fixed tick count that the faster imp overran, rewritten to run to the event it is about rather than to a clock.
- **What the *pathfinder* charges is still not evidence about DK2** (Q5). The mover's √2 is forced and the router's 14 is the only choice consistent with it, but whether the original's own search weights diagonals is unobserved and probably unobservable — in open uniform terrain both cost models produce paths of the same shape. **ADR-0155 makes the question moot:** the original searches a triangle mesh, so it has no per-step diagonal cost to weight.
- Code now: with `TicksPerSecond = 4` (ADR-0089) the 14/10 ratio no longer survives tick quantisation — a four-tile walk is 9 ticks straight against 13 on the bias — so `ImpTests.WalkingOnTheBias_TakesRootTwoTimesAsLong` asserts within one tick and says why. How a leg's surplus is spent inside a tick is ADR-0059.

## ADR-0026 — Three workers to a face, and the fourth goes elsewhere
**Date:** 2026-08-30 · **Status:** accepted · refined by ADR-0030: the cap follows the stance

**Context.** The owner watched a block being dug in the original (Q26/Q43): **three imps to a face, twelve to a block**, and an imp that cannot get onto a face **goes and finds other work — it does not queue**. Ours converged without limit, so a marked tile could soak up every imp a keeper had.
**Decision.**
- **The cap is on the face, not on the block.** `ImpCrowding.FaceIsFull` counts workers whose *target tile* is this one and whose *face* is this neighbour, and the job search skips a full face. **The twelve is not a rule and nothing counts to it** — it is the three, four times, because a tile has four orthogonal neighbours and work is orthogonal (Q27). The twelve is how the owner established that work is orthogonal: eight-connected work would have given eight.
- **Rejecting the face rather than the tile is what makes the fourth imp leave.** The search carries on to the next standing tile — another side of the same block, or another block — and falls through to the next job priority if nothing is left. Nothing waits.
- **A worker's face is derived, not stored**: the end of its route if it is still walking, the tile under it if it has arrived. Counting the *intended* face stops four imps setting off for the same side and discovering the crowd on arrival; deriving it keeps a field out of the determinism hash and makes it impossible for the face to disagree with the route.
- **The asking creature is excluded from the count**, or the third imp on a face would drop its own job the instant it re-evaluated it.
- **Counted across every player**: two keepers' imps on the same side of a block are in each other's way exactly as much as one keeper's would be. (ADR-0030 keeps this and replaces its stated reason.)
**Consequences.**
- **The three is an observed number with no source in the data, which is its own category.** `ObservedCorrections` exists for a shipped value that cannot produce the observed behaviour; here the files carry **no** value at all. The balance table has no per-face, per-tile or crowding limit for workers — the nearest thing, `OvercrowdingJailBreakPercentagePerExtraCreature`, is about prisons — and it is not derivable from geometry, since CREATURES gives a creature a `Height` and no width or radius. It lives in `ImpCrowding`, named, documented, with the search recorded. Q34's 50 % sell refund looked like the second member and was not (ADR-0029).
- Imps now spread: a crowd on one marked tile pushes the surplus onto other faces and then into other jobs.
- No test broke, because every existing imp test uses one or two imps and none ever reached the cap. Three new tests cover it.
- The count is O(creatures) inside a search predicate — acceptable at tens of creatures, worth revisiting if a level ever carries hundreds.

## ADR-0027 — A room is an instance, and it is derived rather than stored
**Date:** 2026-08-30 · **Status:** accepted

**Context.** A room was terrain, tile by tile, so nothing could ask *how big is the room this tile belongs to, and where is this tile within it?* Five measured findings sat unimplementable on that gap:

| | |
|---|---|
| **Q52** | a dungeon heart holds **16000**, because 9 of its 25 tiles are the platform |
| **Q60** | a temple's capacity is its **rim** — 8, 12, 16 for 3×3, 4×4, 5×5 |
| **Q60** | a graveyard's graves are its **interior ring** |
| **Q45** | `RecommendedSizeX/Y` is the size below which a room **does nothing** |
| **Q61** | creature attraction wants a room's **tile count** (Bile Demon: hatchery ≥ 25) |

**Decision.** `RoomInstance` — a connected group of tiles with the same room terrain and the same owner — found by `RoomIndex`.
- **Derived, never stored**, for three reasons and the second decided it: stored membership must be maintained on **every** path that changes a tile (built, sold, dug out, destroyed, claimed by a rival) and the first one anybody forgets leaves a room disagreeing with the map; **splitting and merging come free** — cut a treasury in half and it is two treasuries on the next query, and a tile joining two rooms merges them, both with no logic to write and both tested; and it stays **out of the determinism hash**, being a pure function of state already hashed, so **no new field, no snapshot format change, no `HashVersion` bump**.
- **Connectivity is orthogonal**, and **owner counts**: two keepers' treasuries meeting along a line are two treasuries, or a rival's tiles would inflate your capacity.
- **Two entry points, agreeing by construction** because they share one flood fill: `RoomAt` walks a single room, `Build` sweeps the map once. A caller looping over tiles uses `Build`; the test suite asserts they agree.
- **`GoldStore.Capacity` gained a room-aware overload.** The tile-only one survives for the single caller that has a tile and no map — the spill calculation when a tile stops being a store — where the difference cannot bite, because a platform tile can never have had gold to spill.
**Consequences.**
- **Q52 fixed.** A keeper's heart on the real campaign map holds exactly **16000**, asserted against `level1`'s actual heart. The platform is found from the bounding box's centre, so it needs nothing from `TileConstruction` (`FiveByFiveRotated`, which we do not read) and a half-destroyed heart degrades sensibly instead of by a hardcoded 16.
- **Q45 implemented** as `RoomInstance.IsFunctional`, pinned against the real record in both directions — Graveyard 3×3 and Temple 5×5 as the owner observed, and Treasury/Lair/Guard Room at 1×1 so that "every room needs to be big" could not fit the same evidence.
- **Q60's temple half expressed** as `EdgeTileCount`, counted rather than computed from `2(w+h) − 4`, so an L-shaped room answers sensibly; tested against the owner's three measurements.
- **Q60's graveyard half not implemented:** two data points and an ad-hoc rule (interior tiles, less one for the centrepiece) is not enough; it needs one more graveyard size.
- `RoomIndex.Build` is O(tiles) and `RoomAt` is O(room). `GoldStore.Deposit` and `SpaceFor` build the index once rather than per tile.
- The fixture now transcribes `RecommendedSizeX/Y` and the real-data drift guard checks them, because a rule reads them.
- No behaviour changed except the heart's capacity, and no scenario hash moved.

**Amendment (2026-09-11) — the graveyard half is in, and Q45's rule is refuted.**
The owner read five graveyard capacities off the game's own tooltip: **0, 1, 4, 3
and 8** for 2x2, 3x3, 4x4, 5x3 and 5x5. The interior ring above fits all five, so
it is built as `RoomInstance.InteriorRimTileCount` — the same "wholly surrounded"
test this ADR already used for the interior, applied once more *to* that interior,
so both rings answer an L-shape by counting rather than by a bounding-box formula.
The deferral above asked for one more size and got three.

**`RecommendedSizeX/Y` is not the size below which a room does nothing.** A 4x4
prison holds 5 against its 5x5 record (Q179) and a 3x3 temple prints a capacity of
8 against the same, while the 2x2 graveyard that really does nothing is explained
by its own ring reaching zero rather than by a gate. The field reads as authored
documentation of the smallest *useful* size — which is why it matches the manual on
16 of 17 rooms — and `RoomInstance.IsFunctional` is a live divergence, not a rule.
Unaffected: whether a room's *effect* needs more than capacity is not tested here.

## ADR-0028 — Floor is worked from on top of it, walls from beside
**Date:** 2026-09-01 · **Status:** accepted · refined by ADR-0030: the crowding cap follows the stance

**Context.** Everything was worked from an orthogonal neighbour, a model taken from Q27 — CONFIRMED, and about **walls**. The owner's observation of floor (Q44, CONFIRMED) is unambiguous: *"imps claim tiles by jumping on them"*, staying roughly half a second afterwards, then moving to an adjacent unclaimed one. The ownership condition moves with the stance, because claiming spreads from ground the keeper already holds (Q42) and was implemented as *the tile the imp stands on must be owned*, which an imp standing on the tile it is claiming can never satisfy.
**Decision.** A worker works from the target tile itself when it can stand there **both while it works and when the job finishes**; otherwise from an orthogonal neighbour. `WorkStance` is the whole rule, and it is *derived* rather than a list of work kinds:

| Clause | What it rules out | Why it is not a guess |
|---|---|---|
| standable **now** | dig, mine, reinforce, repair-wall | those tiles are `Solid`; nothing may stand on them |
| standable **when it finishes** | fill in | fill-in turns floor into rock (Q18) — a worker on it would be **entombed by its own work** |

- Standability is asked of the same `IPathGrid` the mover will route over, so the stance is per worker and cannot pick a tile the pathfinder then refuses.
- **The ownership condition follows the stance:** worked from beside → the **standing** tile must belong to the keeper; worked from on top → the **target** must touch a tile that belongs to the keeper. Both say territory grows outwards and never in islands; they differ only in where the imp's feet are.
- **Confidence, split.** CONFIRMED for claiming, watched. STRONGLY INFERRED for repairing and attacking floor — identical geometry, a walkable tile that stays walkable, and no observation distinguishes them (Q49). STRONGLY INFERRED for fill-in, from the entombment argument. Two physical clauses answer every work kind, where a hand-written list would have been the invention.
**Consequences.**
- `ImpTaskSystem.TryJobFrom` (was `TryJobBeside`) considers the tile underfoot before the four around it: a job that can be done where the worker already is beats one it must reach for.
- The crowding cap applies unchanged — for a job done on the tile, the face *is* the tile. (ADR-0030 splits the number.)
- One `WorldPathGrid` is built per worker per tick and threaded through, so routing, stance and arrival are answered against the same world.
- Visible in the scenarios: a claim in `imps-carry-gold-home-001` reads `steps=2` where it read `steps=1` — the imp walks onto the tile.
- `TileWorkRules` gained `PerSecond` and `OutcomeTerrainId`, so "what will this tile be when the job is done" is askable without simulating it.
- No new state, so **no `HashVersion` bump** — but imp positions differ, so scenario hashes moved.
- **Deliberately absent:** the ~500 ms an imp lingers on a tile it has just claimed. Whether that is a cooldown or the claim animation finishing is UNKNOWN and the two are indistinguishable from outside (Q49); an imp here goes straight on to the next job.
- Code now: `TileWorkKind` has **nine** members and `FillIn` is not among them (removed 2026-09-07, Q219; ADR-0009, ADR-0039), so no work kind currently reaches the second clause. The clause is live code in `WorkStance` and is what would stop a future converting job standing in its own result.

## ADR-0029 — Selling a room returns half its cost, and the data said so all along
**Date:** 2026-09-03 · **Status:** accepted

**Context.** Q34 asked what selling a room refunds; the owner observed **50 % of the cost, per tile** on 2026-08-29. This project recorded it with a warning that the number appeared in *neither* candidate variable, since `RoomDef.ReturnPercentage` and `RoomSellValuePercentageOfCost` "both say 100" — a transcription error of our own, because `RoomSellValuePercentageOfCost` is **50** and `docs/research/game-database-dump.md`, generated from the install, has said 50 since it was written.
**Decision.** Selling is implemented entirely from the data.
- **The refund.** `RoomRules.RefundPerTile` = `Cost` × `ReturnPercentage` % × `RoomSellValuePercentageOfCost` %. With the shipped numbers that is half the cost per tile for every buildable room — a treasury 100 of its 200, a temple 1500 of its 3000 — exactly what the owner measured.
- **`ReturnPercentage` is a per-room gate, not a rival refund rate**, the reading that makes the two variables agree rather than compete: **100** for every room a keeper can build (17 of them), **0** for exactly the hero gates, which a keeper cannot sell, and nothing in between. That the zero rows coincide precisely with the unsellable rooms is the internal check, asserted in both directions so that "the field is always 100" could not fit the same evidence. Whether the two numbers multiply or the per-room value overrides the global is **indistinguishable on shipped data** — no room carries a middle value — so the behaviour is settled and the composition is not. They are multiplied, the reading that still behaves sensibly on data we do not have.
- **Sellable means buildable.** The same `Buildable` flag that decides what a keeper may put down decides what they may take up, so the two rules cannot disagree and nothing in the code names a portal or a dungeon heart.
- **The tile reverts to its terrain's own `DestroyedTypeTerrainId`**, Claimed Path for every room terrain in the shipped data — **CONFIRMED by the owner the same day**: a sold treasury tile becomes claimed floor. Ownership is not cleared, because selling your own room does not hand the ground to nobody. (Narrowed by ADR-0045: cleared where the terrain left cannot be owned.)
- **Ordering:** `RoomSellSystem` runs **before** `RoomBuildSystem`, so a keeper can sell one room to afford another on the same tick. Within a command, tiles are sold one at a time and each refund is banked before the next tile goes, so a keeper selling their whole treasury runs out of somewhere to put the money at a point the run can name.
**Consequences.**
- **Gold on a sold treasury tile is CONFIRMED and was half implemented.** The owner: the gold **drops onto the claimed floor tile that replaces the store** and comes off the keeper's spendable total until somebody fetches it. Two of the three were reproduced — the tile reverts and the total falls by exactly that much, so the *economy* of a sale is right — and the pile waited for objects (Q31; ADR-0055). The run reports `gold_displaced`, and a refund with nowhere to go `refund_lost`.
- Doors and traps come free: `DoorSellValuePercentageOfCost` and `TrapSellValuePercentageOfCost` are **50** as well. Neither was implemented here, both being objects.
- **Selling a bridge was known wrong, and the data was why** (Q64): a wooden bridge's destroyed type is Claimed Path like every other room terrain, so selling a bridge over water left walkable floor where there should be water. Reproduced as the data had it rather than special-cased, and the event named it; answered the same day by ADR-0031.
- Selling is **instant**; in DK2 an imp walks over and dismantles the tile, the same missing worker job as building (Q24).
- The harness gained a sell command, and `scenarios/rooms/sell_a_room_back.yaml` (`rooms-sell-a-room-back-001`) runs the whole thing, including a dungeon heart being refused in the middle of a selection.
- No new state, so no `HashVersion` bump. The economy has its first sink that runs backwards.

## ADR-0030 — The crowding cap follows the stance: three to a wall, one to a floor tile
**Date:** 2026-09-03 · **Status:** accepted · refines ADR-0026

**Context.** ADR-0026 justified the three-to-a-face cap as *"the limit is physical space on a tile"*, and ADR-0028 reused it for floor jobs because standing on the target *is* standing on a tile. The owner's next observation (2026-09-03) refutes that: *"only one claiming imp jumping per unclaimed tile is allowed"*. Three imps really do share a single tile to get at a wall and only one may stand on a floor tile to claim it, so the cap is per job rather than about how many bodies fit on a tile — if it were the latter, the two numbers would agree.
**Decision.** `ImpCrowding.MaxWorkers(workedFromTheTile)` — **1** on the tile, **3** from a neighbour — and `FaceIsFull` takes the stance. `ImpTaskSystem` already knows which stance a job uses (ADR-0028), so nothing new is computed and no work kind is named.
- **CONFIRMED for both numbers, and neither is derived.** The tidier story ("a tile holds three workers") is refuted by the very observation that gave the second number; both are here because both were watched, and neither is in any file we read.
**Consequences.**
- **A test had been passing for the wrong reason.** `AnImpDoesNotCountItselfOutOfItsOwnFace` ticked four times past its crowding assertions and checked three imps still held the block — but three imps swinging together put 300 damage into a plain wall's 165, so the block had already fallen and the tile had become claimable floor, and the assertion actually read **three imps claiming one tile**, which the original does not do.
- **Also CONFIRMED the same day:** *"imps claim tiles adjacent to claimed tiles"* — Q42 exactly as implemented since ADR-0028, the ownership condition on the target's neighbour rather than on the imp's feet. **Whether "adjacent" includes diagonals is UNKNOWN** and now matters, because movement is eight-connected (ADR-0025) so a corner-touching tile is reachable. Orthogonal is what is implemented, matching the way work spreads (Q27). Q66.
- Visible in the scenarios: `imps-carry-gold-home-001` previously had both imps claim the tile they had just mined out; now one claims it and the other reinforces, so its hash moved.
- ADR-0026's claim that the cap counts across every player still holds; its stated *reason* is replaced by this one.
- No new state, so no `HashVersion` bump.

## ADR-0031 — A bridge reverts to the hazard it spanned, and the MAP record says which
**Date:** 2026-09-03 · **Status:** accepted · answers Q64 and the bridge half of Q16

**Context.** ADR-0029 took a sold room's tile back to its terrain's own `DestroyedTypeTerrainId`, Claimed Path for **every** room terrain including the two bridges, so selling a bridge left walkable floor where there had been water or lava. The owner then confirmed (2026-09-03) that **a sold bridge reverts to the water or lava it spanned**, so the hazard *is* recorded somewhere.
**Decision.** It is `TileState.Flags` — the MAP record's third byte per tile, carried since M2 as "bridge-related; exact meaning UNKNOWN, preserved verbatim" — and **2 marks a bridge over lava.** The evidence, swept over all **66** shipped level maps:

| | |
|---|---|
| the byte takes **no value but 1 or 2** anywhere in the corpus | so there is nothing else left for it to mean |
| **2 occurs only on bridge terrain** | never on any of the other 38 terrain types |
| every `2` bridge tile **borders lava** | across all three bridge types |
| every `1` bridge tile that touches a hazard **borders water** | the two sets never cross |

- `BridgeRules` holds the reading and `BridgesOnRealDataTests` pins all four claims, so a data set breaking any of them fails loudly rather than quietly reverting a bridge to the wrong thing. A bridge cannot be built on land — neither bridge carries `PlaceableOnLand` — so for a bridge tile the byte is a straight binary, and that is the ROOMS record rather than an assumption.
- **Confidence, split.** The **behaviour** is CONFIRMED, watched by the owner. That **this byte** is where the original keeps it is **STRONGLY INFERRED** from a clean partition over 66 maps; it could be a rendering hint that correlates perfectly. Nothing depends on being right about the original's storage, only on the revert being correct, and the project's brief puts internal representation outside what must match.
- **One rule, one place.** `BridgeRules.RevertsTo` is the only thing that decides what a removed tile leaves, and both callers go through it: selling (`RoomRules.Unmake`) and being beaten down (`TileWorkRules.Apply`'s destroyed path). Those two used to consult the destroyed-type field separately and **both were wrong in the same way**, a rival being already able to attack a bridge tile since `AttackTile` applies to any non-solid ownable terrain somebody else owns.
- **A player-built bridge writes its own marker:** the level file speaks only for bridges it shipped with, so `RoomRules.Place` records the hazard it is covering and `Unmake` clears the marker, so a tile bridged twice cannot inherit the first hazard.
**Consequences.**
- **Independently corroborated, 2026-09-03.** A 2013 KeeperKlan reverse-engineering thread — reached through web.archive.org, since the forum now returns 403 — documents the on-disk element as `{BYTE idTerrain; BYTE idPlayer; BYTE flags; BYTE unused;}`, notes that **"only a '2' bit here is interpreted to do anything special at load, 1 may indicate 'valid'"**, and adds that the 2 bit *"assigns an as yet unknown flag in the runtime map element"*; the author had been "upside down inside of this executable for the past year and a half". That confirms three inferences — the byte **is** a flags field, **2 is the only value that means anything**, and the fourth byte is **unused**, which closes the second half of Q16 — and supplies no meaning, which was explicitly open there. Facts quoted with provenance, no code taken (`docs/research/external-resources.md`); the rating stays STRONGLY INFERRED, because the field is confirmed and our reading of its purpose is ours.
- **No new state and no `HashVersion` bump.** The byte was already in `TileState`, hashed and snapshotted.
- `GameDatabase` gained `WaterTerrain` and `LavaTerrain`, found **by flag** and not by id. The install has exactly one of each, which is asserted.
- The owner byte was **not** cleared when a tile reverted to water, matching `TileWorkRules` — narrowed by ADR-0045, so a sold bridge over water no longer leaves you owning the water.
- **Q67 became implementable.** `WoodBridgeLifeOnLavaSeconds` is 30 and that rule needs to know which bridges are over lava; `BridgeRules.SpannedTerrain` can now tell (ADR-0032).
- The test fixture gained a Lava terrain, because a fixture with one hazard cannot tell a correct revert from a lucky one.
- **The near-miss worth keeping.** The first attempt compared each level's *remaining* water and lava counts against its bridges and appeared to refute the reading, Furnace having bridges of both markers and apparently no water at all — because **a fully bridged pond leaves no water to count**, the bridge tiles *are* the pond. Asking what each tile **borders** is what made the partition visible.

## ADR-0032 — A wooden bridge over lava burns, and the clock starts when it is built
**Date:** 2026-09-03 · **Status:** accepted · answers the tile half of Q67

**Context.** `WoodBridgeLifeOnLavaSeconds` is 30 in the balance table, a variable that names its own mechanic and that nothing read until ADR-0031 established which bridges are over lava. The **shape** was still missing — the name fits the clock running from the build, from the first footstep, or as damage to the tile's health — and the owner settled it (2026-09-03): **the clock starts when the bridge is built.**
**Decision.** A countdown of its own — `TileState.BurnTicksRemaining`, set by `RoomRules.Place`, spent by `BridgeBurnSystem`, cleared by every path that takes the tile away.
- **Not health decay.** `TileState.Health` is what a rival attacking the bridge reduces, and a wooden bridge over **water** carries the same 2000 health and never burns, so modelling fire as health would make combat damage and fire the same quantity.
- **Which bridge burns is read from the room type**, the one rule in the bridge code that names one, because **DK2's own variable names it** (`WoodBridge…`) and the terrain table cannot help: Wooden Bridge and Stone Bridge carry *identical* flags (0x30000034) and identical health, and neither carries `TerrainFlags.Decay`. Asserted against the install, so a data set where they diverged would say so.
**Consequences.**
- `HashVersion` 8 → 9 and snapshot `FormatVersion` 8 → 9, with the pinned literal updated in the same commit.
- `BridgeBurnSystem` runs **after** `TileWorkSystem`: a bridge somebody was repairing on the tick it ran out gets the repair and burns anyway, so the fire is final rather than something a keeper can outrun by one tick.
- A stone bridge over lava is permanent, which is what makes the wooden one a real decision — 200 a tile and temporary against 500 and forever.
- A level-authored wooden bridge over lava does not burn here, because the clock is set at build and a shipped bridge has no build event. Moot in practice: the campaign contains **no** wooden bridge over lava anywhere.
- **CONFIRMED and deliberately not implemented — the larger half.** The owner described the whole mechanic: everything on the tile **falls into the lava**; a dead body **disappears**; a living creature **burns until it gets out of the lava or its health reaches zero**; some creatures are **immune**; gold is **UNKNOWN**. None was built here, because creature health did not exist. Lava's harm is not a bridge rule in any case — it is `TerrainDef.Damage`, 100 a second, the field that also makes a Hero Lair heal at −67 — so it belongs with creature health and terrain damage (ADR-0048, ADR-0051).
- **The immunity is already in the data and it extends the owner's list.** `CanWalkOnLava` falls on exactly seven creatures: the **Salamander** and **Giant** they named, the two unique variants, plus **Firefly**, **Fairy** and **Horny**. Two fliers and the Horned Reaper is the set one would guess, so it corroborates an admittedly partial list. Pinned in both directions — the **Imp is not immune**, which is what makes a cheap wooden bridge a real trap.
- Gold on a burning bridge is not worth an observation yet: it is blocked behind objects either way.
- **A creature caught on a burning bridge is unharmed here but not stranded.** The pathfinder enters a mover's starting tile without testing walkability, so a creature standing in lava can still walk out — a rule written long before anything could turn the ground under a creature impassable, and this is its first real cause. Tested.

## ADR-0033 — A rival claims your ground directly, and hostile expansion falls out
**Date:** 2026-09-03 · **Status:** accepted · answers Q68 · refutes the two-step reading carried since M3

**Context.** `TileWorkKind.Claim` applied only to terrain that was **not `Ownable`**, so a rival's claimed floor was not claimable at all and the implied model was two steps: damage the tile to neutral with `AttackTileHealth` (−140), then claim it with `ClaimTileHealth` (825). The owner settled it in two messages (2026-09-03) — a bridge can only be "attacked" by a **rival keeper's imps, who jump on it and claim it if it is adjacent to their keeper's territory**, and, asked directly, **"no it doesn't go to neutral first, it is immediately claimed by an imp."**
**Decision.**
- **`Claim` applies to any non-solid tile that is not already yours** — neutral or a rival's. The old `!IsOwnable` test is replaced by `tile.OwnerPlayerId != playerId`, plus a "nowhere to go" guard: either the terrain is already the claimable kind, or it converts into something.
- **Claiming ground that is already `Ownable` changes only the owner** (`TileWorkRules.UpwardDestination`). Not a nicety: a claim normally converts to `MaxHealthTypeTerrainId`, and for Claimed Path that field is **Rock** — its fill-in destination (Q18) — so following it would have turned a rival's floor into a wall. Decided by the terrain's own flag, so the rule reads identically for a rival's floor, bridge and room.
- **`Convert` no longer resets `BurnTicksRemaining` when the terrain is unchanged.** A rival claiming a burning wooden bridge takes the bridge, fire and all; putting the fire out by capturing the tile would be a reward for capturing it (Q67).
- **Confidence.** CONFIRMED for a rival's **claimed floor** and for a **bridge**, both measured. STRONGLY INFERRED for a rival's **rooms** — the same rule applied uniformly, because a bridge *is* room terrain and singling other rooms out would need evidence nobody has.
- **Rate:** `ClaimTileHealth` (825) for neutral and enemy alike, because the table offers nothing else. A mana vault has *dedicated* neutral and enemy claim rates (4 and 20) which nothing reads, so DK2 differentiates for at least one thing and our generic rate is certainly wrong for a vault (Q69).
**Consequences.**
- **Two derived rules predicted the observation, which is the strongest evidence either has had.** `WorkStance`'s on-the-tile stance is the owner's *"jump on it"*, and the ownership condition on the **claimed tile's neighbour** is their *"adjacent to their keeper's territory"* — both written for floor claiming (ADR-0028), without a thought for bridges, and both now pinned by tests that say so.
- **Hostile expansion came free.** Claiming is already second in the confirmed priority order (ADR-0018), already requires the tile to touch the keeper's territory, and is already done standing on the tile, so imps take a rival's border tiles **unprompted, with no new decision logic and no new job kind** — a two-line change to one predicate. (Rooms were taken out of `Claim` again by ADR-0044: a room is claimed whole.)
- **No scenario changed**, verified by running the corpus with and without the change: identical hashes. No shipped scenario has two keepers with adjacent claimed floor, so the new behaviour is unit-tested rather than scenario-tested.
- **`AttackTileHealth` now has no user at all.** It must have one in the original; the obvious candidate is creatures other than imps smashing enemy floor during an invasion, which would make attacking and claiming two actors' jobs rather than two steps. HYPOTHESIS, Q69.
- A tile of `Ownable` terrain owned by id 0 is now claimable, which was the first rule whose reach depended on Q17 (then answered by ADR-0043). The rule is right either way — id 0 is either nobody, making claiming neutral floor correct, or a keeper, making it hostile claiming.
- No new state, so no `HashVersion` bump.

## ADR-0034 — An asset is named by a logical key, and a provider chain resolves it
**Date:** 2026-09-04 · **Status:** accepted

**Context.** Goal 4 (`AGENTS.md`) draws the pipeline as `Game Definition → Asset Reference → Asset Provider → Actual Asset`, and `docs/GOAL_ALIGNMENT.md` gap 3 puts a deadline on it: **the asset layer must land in the same milestone as the first Unity view, not after it.**
**Decision.** `DK2.Assets` holds four things and no more.
- **`AssetRef`** — a validated logical key, `category.subject.role[.variant]`, built from a record's **name** and never its id: an id is a fact about the reference install, a name survives a mod and a re-extract.
- **`IAssetProvider`** — given a key, answer or say you cannot. **Never throw for a key you do not have**, because "no asset" is a normal condition in a project that must run with no assets at all.
- **`AssetProviderChain`** — first answer wins, in the declared order `replacement → converted → placeholder`. Order *is* the policy; a "best answer" rule would need a judgement no code here can make and would make the result depend on what happens to be installed. Same reasoning as ADR-0013.
- **`PlaceholderProvider`** — **total**. Answers every valid key with a coloured box whose colour is derived from the key's hash, so a type is the same colour in every run, on every machine and in every renderer.
**Consequences.**
- **The placeholder is load-bearing, not a fallback.** Plan §7.2 asks that the game *run* with no DK2 install and no art pack, so that "shippable without proprietary assets" is continuously tested rather than aspirational — which is only true if the provider is total. `AssetCatalogue` makes it checkable by enumerating every key the shipped database implies, and `AssetCatalogueTests` resolves them one by one with nothing installed.
- **No filename literal exists outside `DK2.Assets`** (plan §4.8). One type turns a key into a path, mechanically: `creature.imp.mesh` → `creature/imp/mesh.glb`. An art pack's layout is knowable from the key alone, with no manifest to fall out of date.
- **`AssetRef`'s validation is a security property, not tidiness.** A key segment is `[a-z0-9_]` only, so it cannot be `..`, absolute, or contain a separator, which is what makes the dot-to-separator mapping safe. Refusing an unmappable character also catches the real bug: a name carrying `.` or `/` did not come from a record, and sanitising it would mint a believable key for a wrong thing.
- **The separator set is measured, not assumed.** The first version allowed only space, dash and apostrophe; the shipped terrain table carries `Hero Gate [ Final ]` and refuted it on the first run against real data.
- **The catalogue claims only what the game asks for.** DK2's creature record names about thirty animations; ours lists the activities the simulation can distinguish, checked equal to `CreatureActivity` by a test. Enumerating DK2's full set would put keys in a converter's work list that nothing requests.
- **`DK2.Assets` must not reference Unity or the simulation**, asserted by `AssetArchitectureTests`.
- **Not decided:** how many assets a terrain needs. `AssetCatalogue` asks for one mesh per solid terrain, a **known simplification** — DK2 picks wall pieces by the size and shape of the room behind them (`docs/research/wall-rendering.md`, Q71). `AssetRef` already carries a variant segment, so the shape is right; the variant list waits for evidence.

## ADR-0035 — Rendering goes through one view, and the harness draws it as text
**Date:** 2026-09-04 · **Status:** accepted · amended 2026-09-05: objects cross the boundary too

**Context.** Plan §4.7 puts the presentation boundary at `SimulationEvent` out and `InputFrame` in, with Unity holding no authoritative state. That says what may cross; it does not say who decides *which asset a tile needs*.
**Decision.** `DK2.Presentation` owns that translation exactly once. `WorldView` turns `WorldState` into `TileView` / `CreatureView` — readonly structs carrying asset keys, fixed-point positions and the few state bits a player can see — and every renderer consumes those.
- **Why not let each renderer read `WorldState`:** three decisions would then be repeated per renderer — which asset key a terrain implies, which animation an activity implies, and how a room's floor differs from its furniture — and two renderers disagreeing about those looks exactly like a simulation bug. The view must also agree with `AssetCatalogue`, because **a key the catalogue never enumerates is a key no converter ever produces**: a real failure mode with no error anywhere, and now a test.
- **The harness draws it as text.** A second renderer is the only thing that proves the boundary is one — if the view types were secretly Unity-shaped, `TextRenderer` would not compile — and it **runs in CI**, which the Unity view will not, so "the presentation layer still works" is a test result. Every scenario run writes `world.txt` and a picture into its report, with a line saying where the art came from, normally "all placeholders".

**Amendment, 2026-09-05: objects cross the boundary too.** `ObjectView` joins `TileView` and `CreatureView`, `AssetRef` gains an **`object`** category with one mesh per definition, `IRenderTarget` gains create/update/remove for objects, and both renderers draw them. This was a hole rather than an omission: objects had been in the world since ADR-0054 — beds, coops, eggs, and since ADR-0055 piles of gold — and every renderer drew bare floor where one stood, so a scenario picture of a sold treasury showed the gold vanishing and the Unity view of a claimed lair showed no beds. Three decisions inside it:
- **an object is a category of its own, not part of `room`**, because it outlives the room it was placed for: a bed belongs to the creature that dropped it, and a pile of gold on bare floor belongs to no room at all;
- **a mesh and no variants.** DK2 gives an object a state machine and a `MoveTo` block, so a pecking chicken and a rolling boulder will want animations, but nothing here can distinguish one object state from another yet and a variant list invented now would put keys in a converter's work list that correspond to nothing (the same restraint the wall pieces get, Q71);
- **objects are pushed before creatures and drawn under them**, because a creature stands on an object. The order lives in `ScenePresenter` so the Unity target and the text renderer cannot disagree about what occludes what.
**Consequences.**
- Positions cross the boundary in `Fp32` tile units, untouched. A view that helpfully divided would throw away the sub-tile position a renderer needs to interpolate, and ADR-0001 puts that conversion on the renderer.
- A tile reports `HealthPermille` rather than health, so a renderer needs no terrain table. DK2 shows wall damage by swapping textures, which corroborates both the fraction and the eventual asset kind (`docs/research/wall-rendering.md`).
- A scenario report shows *where* two runs differ, not only that they do — the gap the hash series has always had.
- **Observation is a convention here, not a compiler guarantee:** `WorldState.Tile` hands out a `ref`, so a determined renderer could still mutate. The view copies by value and a test asserts that building and rendering a whole view leaves the state hash untouched. The guarantee the plan wants — deleting every `GameObject` changes nothing — holds by construction, since the simulation references none of this.
- An object has no sub-tile position, because the simulation has none — `ObjectView` carries a tile. DK2 places a pile at one of eight fixed points around a tile centre and a dying creature's gold anywhere in its tile (`docs/research/gold-on-the-floor.md` §7); reproducing that needs the field, and the field arrives with the first mechanic that moves an object.

## ADR-0036 — Unity 6 LTS, and the adapter is nine methods
**Date:** 2026-09-04 · **Status:** accepted · amended three times on 2026-09-04: the adapter exists and compiles, Unity runs it headless, and the owner pressed Play

**Context.** `DK2_REIMPLEMENTATION_PLAN.md` §11 says *"Unity version: latest LTS; decide at M3, not now"*. It is M3 and the presentation boundary is built.
**Decision.** **Unity 6 LTS**, `6000.6.0f1`, which is what `winget install --source winget --id Unity.Unity.6000` installs. The previous LTS (2022.3.62f3) is the fallback if Unity 6 misbehaves; nothing in this project depends on a Unity 6 feature, because nothing depends on Unity at all.
- **The version barely matters, which is the point of ADR-0002.** Every assembly Unity loads targets `netstandard2.1`, enforced by the build and by `ArchitectureTests`, and both LTS lines support that profile — so the choice is tooling comfort, and switching later is a Hub download rather than a port.
- **Most of the work is the boundary** (ADR-0035, extended): `ScenePresenter` owns every decision a renderer could get wrong — what counts as a visible change, what order things are pushed in, which asset key each thing needs, create versus update versus remove — and is tested against a real campaign level. `IRenderTarget` is **nine** methods with no logic in them: create/update a tile, create/update/remove a creature, create/update/remove an object, flush. (Six when this was written; objects joined on 2026-09-05.)
- **This project does not commit code it has not compiled**, which for Unity code means `DK2.Unity.Compile` has to exist.
**Consequences.**
- **The adapter.** `unity/DK2.Unity/` is the Unity project (`ProjectVersion.txt`, `manifest.json`, an asmdef); `Assets/DK2/DungeonScene.cs` is `IRenderTarget` in GameObjects, arithmetic and `Instantiate` with no judgement; `Assets/DK2/DungeonRunner.cs` is the `MonoBehaviour` that owns a simulation, ticks it and presents it; `unity/DK2.Unity.Compile/` compiles the adapter against Unity's real assemblies; `unity/sync-assemblies.ps1` builds the netstandard2.1 DLLs into `Assets/Plugins/DK2`.
- **`DK2.Unity.Compile` is deliberately outside `DK2.slnx`** — a machine without Unity cannot build it and CI has none — and is run by hand: `dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj`. It caught three things reading could not: the seed is `ulong` and not `int`, the map tile type is `MapTile` and not `LevelTile`, and referencing `UnityEngine.dll` *alongside* the module assemblies makes every Unity type ambiguous (Unity's own asmdef compilation references only the modules, which is why the project now does too).
- **Unity runs it headless, two ways.** `Unity.exe -batchmode -quit -nographics -projectPath unity/DK2.Unity` proves Unity's own compiler builds `DK2.Unity.dll` against the netstandard2.1 plugin assemblies — stronger than `DK2.Unity.Compile`, which is our compiler against Unity's DLLs. `... -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest` runs the whole pipeline inside Unity: load campaign level 1 from a real install, build the simulation, present into a `DungeonScene`, tick 20 and present again, failing unless there is one GameObject per tile. Result: **30×38, 1140 tile objects, 1178 assets all placeholders, hash `aae0f0e1…` → `8e38409c…`**. What it exercises could not fail at compile time: whether `GameDatabaseLoader` can read a DK2 install through Unity's Mono runtime, whether netstandard2.1 assemblies load there at all, and whether the presenter produces objects.
- **Zero creatures in the smoke test is correct:** a level's creatures come from the THINGS record, unparsed at the time (ADR-0101); the harness scenarios place their own.
- **It renders.** The owner pressed Play and got campaign level 1 — recognisably the starting dungeon — on the first attempt. The diagnostics line cross-checked three asserted claims: **1140 tile objects** (30 × 38, matching the headless run); **pushed 0 tiles / 0 creatures this frame**, so the diffing works and a settled dungeon costs nothing to keep on screen; and **2 creatures at tick 203** on a map that places nobody, produced by `ImpSupplySystem` from the dungeon heart (ADR-0021) at roughly the five-second cadence the data specifies. The asset count closes too: 1178 at tick 20, 1182 with two creatures at four assets between them.
- **The screenshot found a bug nothing else could.** `LookAt` framed the level using `world.Width` for the camera height, and level 1 is 30 wide and **38 deep**, so the near end of the dungeon fell off the bottom of the screen. Now derived by fitting the level's bounding sphere to the vertical field of view, so framing cannot depend on which dimension is larger; `cameraPitch` became a slider.
- **Two properties, both tested:** presenting an unchanged world pushes nothing (over 60 ticks of a running campaign level the presenter pushes well under a tenth of the tile count), and presenting never changes the world (asserted per tick with the determinism gate's own state hash).
- **For whoever writes an adapter:** read `TextRenderer` first — the same job against a character grid, running in CI, so a disagreement between the two is a bug in one of them.
- Framing, scale, colour and legibility are outside what a test suite can assert; they need a person and a screenshot.

## ADR-0037 — The 1999 manual is a corroborating source, never an overriding one
**Date:** 2026-09-04 · **Status:** accepted

**Context.** The owner supplied the shipped game manual (`original-reference/Dungeon_Keeper_2_Manual.pdf`, gitignored). Extract: `docs/research/manual-1999.md`.
**Decision.** The manual is admitted with a **fixed rank**: below the shipped v1.70 data and below the owner's observation of the running game, above every community source.
- Where the manual and the shipped data agree, the claim is **corroborated** and its confidence rises.
- Where they disagree, **the data is implemented** and the disagreement is recorded as a question rather than resolved by preference.
- Where the data is silent, the manual may supply *what a mechanic is for* — never a number the data could have carried and does not.

- **Why the rank is written down.** It shipped with **1.0** and this project targets **v1.70**: it disagrees with the shipped `Creatures.kwd` five times, all in creature attraction, and in one of those it describes a **third** attraction slot the record has no bytes for (the eight bytes after the second slot decode as two fixed-point values in 0.1–1.0). A source describing a record layout the shipped file does not have is describing a different build, and the ADR-0023 / Q53 precedent applies: a plausible external number is a *hypothesis to check against the install*, never an anchor.
- **Reliability was established first, on ground where we already had an answer.** Its room pages print a **Cost** and a **Minimum Recommended Size** for each of the seventeen rooms, both fields parsed out of `Rooms.kwd` by offsets found without reference to it. **17 of 17 costs matched. 16 of 17 sizes matched** — the exception is the Workshop, where the room page says 5×5 and the record says 3×3, and the manual's *own* Bile Demon page says 3×3, so it contradicts itself and the record is right. That bought an independent cross-validation of our `Rooms.kwd` offsets and a third source for Q45: the manual states the semantics of `RecommendedSizeX/Y` outright (*"At this size the room is most likely to function effectively"*, and *"you can create smaller rooms"*), which is `RoomInstance.IsFunctional` and Q36 together.
**Consequences.**
- **Corroborated independently:** the gold overflow chain treasury → heart → floor (Q31, ADR-0018/ADR-0019), the 50 % sell rate (Q34), `TrainingRoomMaxExperienceLevel` = 4, `CombatPitMaxExperienceLevel` = 8, `RoomDef.CreatedCreatureId` (Graveyard → Vampire, Prison → Skeleton), `MinimumImpThreshold` = 4 (ADR-0021), room tiles generating the same mana as plain claimed tiles, wooden bridges burning over lava (Q67), payday every 600 seconds (Q9), and the meaning of `Fear` and `Threat`.
- **Settled, where nothing else had it:** research is stored **in the library** and lost with it; `ModifyPlayerColdWhileTrainingPerSecond` is **Gold**, not "Cold"; the casino's payout lever is the `Smiles`/`Money` variable pairs; the creature cap is **per portal**; a gold treat is a **prepayment** of wages; a lair holds **beds**, one per creature, not tiles.
- **Opened Q74 through Q81**, including two that push on existing decisions: whether there is a player-level starting-gold stockpile after all (against ADR-0015), and whether the mana ceiling is flat or per-heart.
- Room research now starts from prose plus a rate rather than from a blank sheet — the single biggest effect.
- `manual-1999.md` carries the page-referenced extract; nothing else should quote the PDF directly.
- The manual is **not** an oracle for timing: it says "each turn" throughout and never defines a turn, so it says nothing about Q1.
- The PDF stays out of the repository — EA's copyright, a public repository — and lives under `original-reference/`. Our own licence grants nothing over EA's work either way (ADR-0041).

## ADR-0038 — A creature never enters a tile it could not have walked into
**Date:** 2026-09-04 · **Status:** accepted

**Context.** The owner, unprompted, after watching imps walk through rock in an OpenKeeper gameplay video: *"creatures CAN NOT pass through walls, they can only move through dug out tiles, not through solid rocks"*.
**Decision.** `CreatureMovementSystem` re-checks each waypoint against `WorldPathGrid` **at the moment it steps onto it**, and a creature that finds one blocked stops and drops the route. The tile it is *standing on* is deliberately not re-checked.
- **Why a second check, when the router already refuses walls.** `WorldPathGrid.IsWalkable` has refused `TerrainFlags.Solid` since the first worker, so no route has ever been *planned* through rock — but **a route is planned once and the world keeps moving**, and a shipped mechanic moves it the wrong way: a wooden bridge over lava burns out thirty seconds after it is built and the tile reverts to lava (Q67, ADR-0032), so a creature already walking that route would have strolled over the lava. `BridgeBurnSystem` had anticipated half of this — a creature *caught* on ground that turned impassable must be able to leave, which is why the pathfinder does not test a mover's starting tile — and nothing had stopped one *entering* such ground.
- **Dropping the route rather than re-planning.** The mover clears the path and stops; it is not the mover's job to decide what a creature wants. `ImpTaskSystem` already handles an empty path in both states, differently and correctly in each: a worker walking to a job **abandons the job and looks for another**, and a carrier delivering gold **re-routes to somewhere else that will take it**, keeping what is in its hands. Four lines in the mover and no change anywhere else.
**Consequences.**
- `PathfindingTests` proved the *search* will not cross a wall on a hand-drawn grid; nothing proved that the *simulation*, running the real systems on real terrain over many ticks, never puts a creature inside one. `WallsBlockMovementTests` asserts across a 4000-tick dig with four imps: no creature ever stands on a solid tile, no route in flight ever crosses one, and a staged case where lava appears ahead of a walk stops it.
- **Two existing tests had to be fixed to make this pass**, and they are why the invariant was worth writing. `ImpTests.WalkingOnTheBias_TakesRootTwoTimesAsLong` and `ImpTests.SpeedDoesNotDriftFromRoundingItAwayEveryTick` hand-fed routes that ran straight across the fixture's rock. They were measuring movement arithmetic, the arithmetic was right, so they passed — while walking imps through walls, unnoticed for four months because nothing looked.
- The rule is CONFIRMED (owner). What the original does *at the instant* the ground fails — stop dead, or re-route — is UNKNOWN and deliberately not invented.

## ADR-0039 — A dug-out tile is never closed again, and two terrains are never opened
**Date:** 2026-09-04 · **Status:** accepted · amended 2026-09-07 (Q219): DK2 has no fill-in mechanism at all, and `TileWorkKind.FillIn` was deleted

**Context.** Two rules the owner made explicit, 2026-09-04.
**Decision.**
1. **Nothing in a running simulation ever turns an open tile solid.** Asserted, not merely true.
2. **Impenetrable Rock and Gems can never be dug through**, by the two different mechanisms the shipped data already gives them. Asserted, and already correct.

Owner: *"there are 2 categories (that I know of) of indestructable tiles, that can't be dug through by imps, solid rock and a gem tile. gem tile is an infinite gold source and solid rock is the boundary to limit expansion on the map."* The terrain table says exactly this. **Impenetrable Rock** is `Solid, Impenetrable` and **not taggable**, so a keeper cannot mark it and no rule ever runs. **Gems** are `Solid, Impenetrable, Taggable` with a gold value of 3000 and a `DestroyedTypeTerrainId` of *themselves*, so mining applies forever and the tile never goes. `TileWork`'s dig rule requires `IsSolid && IsTaggable && !IsImpenetrable`, which excludes both without a special case; mining requires `IsSolid && IsTaggable && GoldValue > 0`, which includes gems. Nothing changed — tests were added, on the fixture and on the shipped table.

Owner: *"once a tile is dug out, and maybe claimed, there is no possible way to fill in that tile to rock again. DK2 doesn't have a game mechanic for filling up dug out tiles."* Three shipped fields describe filling in — `FillInHealth` = 5, `TerrainFlags.FillInable` on exactly the five floor terrains, and Claimed Path's `MaxHealthTypeTerrainId` pointing at Rock — which Q18 and Q22 read together into `TileWorkKind.FillIn`. Both were accepted as true: the engine carried the concept, the shipped game exposed no way to reach it, on the argument that three *coordinated* pieces of data are not an accident while the manual, which documents every keeper affordance down to the casino payout lever, never mentions filling anything in.
**Consequences.**
- **`ALTER_TERRAIN_TYPE` is the mechanism that does exist, and the install settled what it does.** OpenKeeper is in the same position: it carries `FILL_INABLE(0x00008000)` (`Terrain.java:55`) and `FILL_IN_HEALTH(11), // value=5` (`Variable.java:333`) in its file-format layer, **both declared once and referenced nowhere else**, and its worker task list has no fill-in task (cited facts, GPLv3, no code copied — ADR-0003). Its trigger-action enum does have **`ALTER_TERRAIN_TYPE(22)`**, a direct terrain write with no health and no rate, so `dk2tools kwd alterterrain` was written to decode every such action in all 66 trigger files: **5408 action records, 160 of type 22**, and all 5408 decoded action types fall inside the enumerated 1..60 range, so the layout hypothesis self-validates. The finding is the opposite of what was sought. **Ninety-four of the 160 writes are `Impenetrable Rock → Dirt Path`** — scripts *opening* passages sealed against digging; **level 1's three Rock writes are also opening** — (15,18), (16,18), (17,18), all Impenetrable Rock softened into diggable Rock, the tutorial handing the player a route; and **exactly two writes in the whole campaign turn an open tile solid** — `Secret3` at (13,20) and (5,25), both Claimed Path → **Reinforced Wall**, never Rock. So a level script can wall a tile up as a set-piece, twice in sixty-six maps, and **nothing anywhere uses the fill-in *health* mechanic**.
- Recorded for the trigger work: `ALTER_TERRAIN_TYPE` must be able to write solid terrain onto an open tile. The invariant here is about **workers**, not about the engine, and level 1's three writes are a ready-made first test.
- **The behavioural claim is executable.** `ImpTaskSystem` only ever chose excavation, claiming and reinforcing, so no imp filled anything in — but that was an absence, and absences rot. `DugOutTilesStayDugOutTests` pins both halves over a 4000-tick dig: **no tile ever goes from open back to solid**, and **no work order that reaches the queue is a fill-in**, both checked non-vacuously (the run is asserted to have queued digging and claiming), so adding a fill-in job priority later would fail loudly.
- The walkable set only ever grows, with the single exception of a wooden bridge burning. Anything that caches reachability may rely on that.
- **Code now: there is no fill-in mechanism at all, CONFIRMED from the binary 2026-09-07 (Q219).** `FillInHealth` has **no reader** anywhere in DKII.EXE; "fill in" is not DK2's word — the executable holds one bare `fill` in 2.9 MB and zero hits for any fill-in spelling across 507 shipped data files, so the name is OpenKeeper's label, inherited here and never checked; and there is no fill-in task or handler. `TileWorkKind.FillIn` was deleted, and `TileWorkKind.RepairTile` with it, because `RepairTileHealth` has no reader either. **This ADR's decision not to delete anything is therefore withdrawn:** the data model mirrors the data, and the data turned out not to carry the mechanism. The data *shape* is recorded in `docs/research/tile-work.md`.
- **Q22 is not withdrawn.** This ADR withdrew it because the experiment does not exist, and the prediction it carried (660 swings, ~132 seconds) died with ADR-0024's reversal. The assertion has now held three answers — instant, then 660 applications, now instant again — and **not one was ever watched in the game**, so ADR-0089 keeps it live as Q22/Q214, with the doubt carried in the test's own name.

## ADR-0040 — A level plays on its own VARIABLES, layered over the global table
**Date:** 2026-09-04 · **Status:** accepted

**Context.** `GameDatabaseLoader.Load` read `GlobalVariables.kwd` and nothing else. Every shipped level also ships `<name>Variables.kld`, in the identical record format, and **26 distinct variable ids are overridden by at least one level** — so every campaign run used the wrong numbers wherever a level disagreed. One disagreement was already live: **`MinimumImpThreshold` is 0 on four of the five secret levels and on the front end**, against a global 4, which is exactly right for a fixed-roster puzzle level and which `ImpSupplyRules` (ADR-0021) would have overridden with four. Level 1 doubles both payday variables, 600 to 1200. `docs/research/kwd-variables.md` had recorded the per-level override since 2026-08-29 and no code read it — the third instance in a week of a claim written down and enforced nowhere, and `docs/GOAL_ALIGNMENT.md` carries it as a standing review question rather than as three separate bugs.
**Decision.** `GlobalVariables.MergedWith(level)` produces a new table; `GameDatabase.ForLevel` produces a new database around it; `GameDatabaseLoader.LoadForCampaignLevel` does both in one call and is the shape to prefer whenever a level is named. Two merge rules, because the file holds two kinds of thing.
1. **Plain values replace by id, and ids only the level has are added.** That is what an override file is for, and the only reading under which the secret levels' 0 means anything. Levels also introduce ids the global table lacks entirely — `GameTicks`, `LevelRating`, `AverageTime`, and two unnamed ones — so "replace" has to include "add".
2. **The four lists replace wholesale when the level provides any, and are kept when it does not.** STRONGLY INFERRED, and the shipped data argues for it: the **global availability list is a template keyed to player 0** (every room AVAILABLE, every trap, door and spell RESEARCHABLE) while a level's list names *its own* player ids and states, so a union would leave a level-1 keeper holding "every room available" beside its own "treasury unavailable", which is not a state the game can be in; conversely **no level ships sacrifice recipes or creature-stat rows**, and the temple's 44 recipes and the 320 stat rows are plainly game-wide, so silence has to mean "keep". The one case this cannot distinguish is a level that meant to *add* a single availability entry, and no shipped level looks like that — every level carrying any availability carries a full set across the same five kinds — so the question does not arise on this data (Q82).

**Immutability, deliberately.** A merge returns a **new** `GlobalVariables` and `ForLevel` a **new** `GameDatabase`, sharing the parsed definitions, because a running simulation holds a database and one level's numbers must not reach another level's run. `GlobalVariablesMergeTests.TheMergeNeverMutatesEitherSide` pins that in both directions. `ForLevel(null)` returns `this`, so a caller can hand it whatever the loader found without checking.
**Consequences.**
- `ScenarioRunner.Run` resolves the level's variables first and delegates to a runner built around the level's database, rather than threading a second database through every helper. The run's balance table is reported as **provenance, not as a shortfall** — a `ScenarioResult.BalanceTable` property and a line in the report header, not an entry in `Unsupported` — because a reader of a campaign report should not have to guess which numbers produced it. Level 1's line reads `GlobalVariables.kwd + level1Variables.kld (202 values, 31 availability entries, 1 creature-pool entry)`.
- **No scenario hash moved**, the expected result and worth stating: level 1 overrides nothing that any then-implemented system read. The fix lands before the mechanics that need it rather than after.
- **What it unblocks:** the per-level file carries the two lists the global table leaves empty, and they are what a **portal** needs — `CreaturePool` for who may come (level 1: fifteen Goblins), `Availability` kind 2 for whether they are unlocked, `EntranceGenerationSpeedSeconds` for how often, against `CreaturesSupportedByFirstPortal` = 15 for the cap (ADR-0042).
- **Decoding `Availability` was a large by-product.** Its `kind` and `value` enums were derived **from the shipped data before any external source was consulted** — kind by how many type ids appear under each (eleven traps, six doors, thirteen spells), value by how the states differ between the global template and levels 1, 5, 13 and 20 — and OpenKeeper names both fields exactly the same way. It explains the shape of a campaign: level 1 starts its keeper with almost everything UNAVAILABLE and one creature available, level 20 with nearly everything AVAILABLE, and `Secret1` with no availability entries and no pool at all (`docs/research/kwd-variables.md`).
- **A gap nobody had written down:** which rooms a keeper may build is per level, and `RoomRules` does not model it — a keeper here can build any buildable room on any level.
- **Corrects Q1's evidence.** The id-74 `GameTicks` variable was recorded on 2026-08-29 as carried by "no shipped file"; it is carried by **all 66 per-level files, at 340 in every one**. The sweep looked at the `*.kwd` map files and the value is in the `*Variables.kld` siblings, which nothing was loading. **What 340 means is UNKNOWN and is not being guessed** (Q83): 340 a second is implausible, the id shares its "no trailing fields" shape with the two front-end metadata ids, and OpenKeeper's comment on the same id says `value=4`, matching nothing here, so even the name is suspect. (Q1 itself was answered from the binary by ADR-0089: a tick is 250 ms.)

## ADR-0041 — The project is GPLv3, and the notice is enforced by a test
**Date:** 2026-09-04 · **Status:** accepted

**Context.** The repository was MIT by default, never decided. Changed to GNU GPL version 3 at the owner's request, to match OpenKeeper. **A free choice, not an obligation:** `THIRD-PARTY-NOTICES.md` and ADR-0003 record that OpenKeeper was consulted as documentation only at the time, with no code copied or transliterated, so nothing here was under a copyleft obligation. The MIT-licensed material that *is* here — the GLKeeper WAD decompression adaptation in `WadDecompressor.cs`, and YamlDotNet — is GPL-compatible and keeps its own attribution.
**Decision.** `LICENSE` is the GNU General Public License version 3, verbatim, 674 lines. `license.txt` is the per-file notice as a FreeMarker header template, the same form and purpose as OpenKeeper's `license.txt` — which is where the file came from, and why a lower-case `license.txt` sits next to an upper-case `LICENSE`. IDEs that stamp a notice into new files read it. **The notice is three lines, not fourteen:**

```
// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
```

Under the GPL the per-file notice is what makes `LICENSE` apply to a file rather than merely sit beside it, so this is not cosmetic. **The short form does not narrow the grant** — the original prose said "either version 3 of the License, or (at your option) any later version" and `GPL-3.0-or-later` says exactly that, in a token tooling can read (the SPDX/REUSE convention) — but the whole grant now rests on one token with no prose to fall back on, so two tests hold it in place: `LicenseHeaderTests.TheNotice_DeclaresGpl3OrLater` pins the identifier, because `GPL-3.0-only` or a bare `GPL-3.0` is a one-word edit and a narrower licence, and `LicenseHeaderTests.TheLicenceFile_IsTheVersionTheNoticeClaims` parses the version out of the identifier and asserts `LICENSE` really is that version. Under a long notice each file quoted the licence; under a short one each file only names it, and naming can go stale silently.
**Consequences.**
- **The defect this uncovered.** Roslyn's IDE0073 with `file_header_template` in `.editorconfig` is the natural way to enforce a file header in .NET, and with `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors` already set it becomes a build error for free. It works — for MIT. **It could not express the GPL's prose notice: an `.editorconfig` value ends at the first `;`, and that notice has one** — *"but WITHOUT ANY WARRANTY; without even the implied warranty of"* — with no escape for `;` in the format. The template silently truncated there, Roslyn enforced, and `dotnet format` wrote into seven files a notice whose warranty disclaimer had been cut in half, with the build green because the files matched the truncated template exactly. **The MIT text contains no semicolon**, which is the only reason IDE0073 looked like the answer: the mechanism was never correct, only untested against text that exercised it.
- **The enforcement is a test, not an analyser.** `tests/DK2.Core.Tests/LicenseHeaderTests.cs` renders `license.txt` and asserts that every source file starts with it. Three reasons: it covers `unity/DK2.Unity/Assets`, which no project in `DK2.slnx` compiles — only `unity/DK2.Unity.Compile` does, and that needs an installed editor (ADR-0036) — so a build-time analyser is structurally blind to exactly the files most likely to be forgotten; it has no `;` problem and no dependence on a template format's parsing (**the three-line notice contains no semicolon, so IDE0073 would work today, and it is still not used, because "correct as long as nobody lengthens the text" is the same accident as before and it fails silently both times**); and CI already runs `dotnet test DK2.slnx` on Linux and Windows, so it is gated with no new workflow step.
- **The sweep asserts its own file count is above 100 before checking anything.** A search that finds nothing passes every content assertion, which is how a check becomes a decoration; this project has enough of those in its history (ADR-0036, ADR-0039, ADR-0040) to write the guard in up front.
- `.editorconfig` keeps the whole account of the IDE0073 failure, next to the `end_of_line = lf` that stops `dotnet format` rewriting whole files to CRLF on Windows — which it did, twice, while this was being built.
- **Open, not decided: GPLv3 and Unity.** OpenKeeper builds on jMonkeyEngine, which is BSD; this project builds on Unity, which is proprietary. Distributing GPLv3 game code that links a proprietary engine runtime is a known friction point, unresolved because nothing is distributed yet. It needs an answer before the first public build, not before the next commit.

## ADR-0042 — The portal produces creatures, and three of its rules come from OpenKeeper
**Date:** 2026-09-04 · **Status:** accepted · amended by ADR-0047: four gates were not enough, a creature's own flags are a fifth · interval half superseded 2026-09-07 by ADR-0086

**Context.** The portal is the first room whose behaviour is implemented, chosen first for a structural reason: **nothing else could have occupants.** THINGS was unparsed so no level's creature roster loaded, and `ImpTaskSystem` skips anything without `IsWorker`, so a non-worker dropped into the world stood still forever. Every other room's behaviour needs creatures in it; the portal's behaviour *is* producing them, and it needs no creature health, hunger, mood or experience — none of which existed.
**Decision.** A portal produces a creature when four gates all pass: the creature is unlocked for that keeper (`Availability` kind 2), the pool has room for another of its type (`CreaturePool`), something in the dungeon attracts it (`CreatureAttraction`), and the keeper is under their portals' capacity (`CreaturesSupportedByFirstPortal` + 5 each after). Every number is CONFIRMED from the shipped v1.70 data, and the 1999 manual states the capacity rule independently.

**How they compose is STRONGLY INFERRED, and three of those inferences are OpenKeeper's rather than the data's.** Itemised in `docs/research/portals.md` and listed in `THIRD-PARTY-NOTICES.md`, a GPLv3 attribution obligation rather than a courtesy:
1. **The interval grows with the dungeon** — `max(base, base × creatures ÷ 2)`. Nothing in the balance table expresses this and it was the highest-risk inference in the mechanic: the difference between fifteen goblins in four and a half minutes and fifteen in fifteen. Unfalsifiable from the files (Q86), and **superseded by ADR-0086**, where the owner's stopwatch established the interval as the square of the portal's own tally.
2. **The pool is a standing per-type limit, not a draining quota**, so sacking a creature lets another in. Corroborated by the manual's "replaced by a new creature", and indistinguishable on level 1 where the pool and the capacity are both 15 (Q87).
3. **A two-slot creature needs both rooms, and the size means one room's tile count** rather than the keeper's total.

**One quirk is reproduced rather than fixed** (fidelity priority 4): the keeper's creature count is read once per tick, so two portals ripe on the same tick both produce and a multi-portal keeper can end a tick over their stated maximum. OpenKeeper documents this as original behaviour and replicates it deliberately; ADR-0086 leaves it untouched.

**The spawn clock lives on the portal's centre tile** (`TileState.EntranceIdleTicks`), counts *up*, and runs only while a keeper owns the portal. It cannot live on `PlayerState` because the clock is per portal, and it cannot live on `RoomInstance` because a room instance is derived fresh from the tiles and remembers nothing (ADR-0027). Counting up is what lets the requirement change mid-wait: a creature dying shortens the wait, and a portal blocked for want of a lair banks its wait and produces the moment one appears. `HashVersion` 9 → 10 and snapshot `FormatVersion` 9 → 10 in the same commit.
**Consequences.**
- Uniform random selection among eligible creatures is a placeholder. The data carries a per-type percentage that shifts with sacking (`DecreaseEntrancePersentageOfLastSackedCreatureType` = 33) and neither sacking nor the weighting exists (Q90).
- **First consumer of `SimulationContext.Rng`**, carried, hashed and snapshotted since M1 with nothing drawing from it. The draw happens even when only one creature qualifies, so the RNG position cannot become a function of the keeper's rooms.
- Level 1 produces nothing until its keeper builds a lair, because the Goblin's only attraction is a Lair. The tutorial's order is a mechanic, not a script.
- A portal could not be **claimed** by an imp here: room conversion is a real job with its own balance value (`ConvertRoomHealth` = −4000) and had no `TileWorkKind`, so a scenario said "claimed portal" by re-owning the tiles and the harness reported that as a shortfall (Q92). Built the same day by ADR-0044.

## ADR-0043 — Not every player is a keeper: DK2's player ids are reserved
**Date:** 2026-09-04 · **Status:** accepted

**Context.** Found while asking whose portal an unclaimed portal is. Level 1's portal ships owned by **player 2**, not player 0, and the assumption that every id in `WorldState.PlayerIds` is a keeper had been made silently everywhere.
**Decision.** Four reserved ids in `PlayerIds`: **0** unowned terrain, **1** the hero side, **2 neutral — which is what an unclaimed room belongs to**, and **3 to 7** keepers. `PlayerIds.IsKeeper(id)` is `id >= 3`.

**CONFIRMED from three independent directions**, only one of which is OpenKeeper's `Player` constants. The campaign's own data says it twice: across all 66 levels every dungeon-heart tile belongs to a player between 3 and 7, and every creature pool and creature-availability entry names a player between 3 and 7. Level 1 shows all four at once. Asserted in `PortalOnRealDataTests.TheReservedPlayerIdsAreWhatTheCampaignSays`.

**The obvious argument for it is wrong, and that is the part worth keeping.** "Unclaimed things belong to player 0" fails twice over: solid rock carries a non-zero owner all over the campaign, because DK2 colours the walls bordering a keeper's territory in that keeper's colour (`TerrainFlags.PlayerColouredWall`), so ownership of rock is a rendering fact rather than a claim about who holds the tile — and an unclaimed room is player 2. The argument that holds is the narrower one about hearts and pools.
**Consequences.**
- `PortalSystem` runs only a keeper's portals, so an unclaimed portal's clock stays at zero and claiming one does not hand over a banked wait.
- This ADR left `ImpSupplySystem` looping every player, which would give free imps to 0, 1 and 2, and the test suite using player 1 as "a keeper" and 2 as "a rival" throughout — both Q91, with the fix named and deliberately deferred because it touched five hundred tests. **Code now: `ImpSupplySystem` gates on `PlayerIds.IsKeeper` in both its free-imp and its Create Imp paths (ADR-0052, closing Q91's implementation half).** The deferred reasoning — "harmless on shipped data, because none of those three has a dungeon heart" — was wrong twice: a level that gave the hero side a heart would have staffed it, and `PlayerState.FreeImpCountdownTicks` is hashed state that was counting down for players 0, 1 and 2 on every level, which is why seven scenario hashes moved when the gate went in.
- It answered Q17, which several rules had been resting an open question on — see ADR-0045 for what that cost.

## ADR-0044 — A room is claimed whole, never tile by tile
**Date:** 2026-09-04 · **Status:** accepted

**Context.** **CONFIRMED by the owner**, unprompted, the day the portal landed: *"once an imp claims a portal then the entire portal gets claimed, all tiles that are assigned to that portal are immediately claimed … that is a game mechanic that works on claiming enemy/neutral rooms too, rooms are instantly claimed by imps, not tile by tile."*

It arrived as an answer to Q92 — "how does a keeper claim a portal?" — and **it was a defect report about something much older.** `TileWorkKind.Claim` applied to room terrain, because room terrain is `Ownable` and not `Solid` and that is all the claim rule asked for, so a rival's treasury came over one tile at a time at `ClaimTileHealth`'s rate and a half-taken portal was a reachable state. Nothing checked it, because nobody had asked.
**Decision.**
1. **`Claim` excludes room terrain** (`!terrain.IsRoom`).
2. **`TileWorkKind.ConvertRoom` takes rooms**, reading `ConvertRoomHealth` — **−4000**, until then an unused entry in `VariableTypeId`. At a fifth of the delta that is 800 an application against a room tile's 10000 health: **thirteen applications**. The propagation is instant, the work is not.
3. **Completion is room-scoped.** `TileWorkRules.Apply` converts the worked tile; `TileWorkSystem.SpreadTakeover` reads the `RoomInstance` *before* the work lands and hands the rest of it over. It has to be before — a room instance is tiles of one terrain with **one owner** (ADR-0027), so the moment the worked tile changes hands the rest of the room is unreachable from it.
4. **Only the owner is written on the other tiles.** They were never worked, so resetting their health would repair a room by taking it. A rival who has been chipping at your treasury gets the damage with it — the reasoning that keeps a claimed burning bridge burning (Q68).
5. **Gold changes hands with the room.** Nothing spills: a tile's capacity is a property of its terrain and the terrain did not change. Exactly what Q40 observed on defeat — rivals claim the rooms *and gold*.

**The mixed direction is the data's, not a choice.** `ClaimTileHealth` is **+825** and builds a floor tile *up* to its maximum; `ConvertRoomHealth` is **−4000** and beats a room *down* to zero. Two jobs that both end in "this is mine now", from opposite directions, which is why `TileWorkRules.CompletesAsTakeover` exists: without it the downward path would send a converted room to its `DestroyedTypeTerrainId` — Claimed Path for every room in the data — and claiming a treasury would demolish it. Demolishing is `AttackRoomHealth`'s job (−1500) and has no work kind yet.
**Consequences.**
- **Two things fell out rather than being written.** A worker takes a room by standing **in** it, because the stance rule asks what the tile will be when the job finishes and a takeover leaves it alone (ADR-0028). And an imp holding gold with a rival's treasury next door now **takes it and then uses it**, which prompted the owner to confirm the rule it might have broken: *"imps fill only their own keeper's treasury … for SP my imps NEVER fill anyone's treasure but my own."* That holds structurally — `GoldStore`'s store lookup skips every room whose owner is not the depositor, and it is the only path into a tile's gold — and is asserted both as an ordering (the takeover precedes the first delivery) and as an invariant over every tick.
- **Two rules are ours rather than the owner's, both flagged:** a dungeon heart **cannot** be taken this way (a heart is beaten down, and Q40's "rivals claim the rooms" happens *after* it falls — Q93), and a multi-tile bridge converts whole (a bridge is a room in every way the data can tell, but the owner said "rooms" — Q94, since **CONFIRMED by the owner, 2026-09-07: yes, like a room**, closing it with no change).
- Q92's *rate* is still unobserved; only the propagation is confirmed. The thirteen applications are **3.25 s** at four applications a second (ADR-0089); the 2.6 s recorded when this was written assumed five a second.

## ADR-0045 — A tile that cannot be owned does not carry an owner
**Date:** 2026-09-04 · **Status:** accepted · supersedes the never-clear-ownership rule stated in `TileWorkRules.Convert` and `RoomRules.Unmake`

**Context.** Found by a scenario, not by review: a run that dug from level 1's dungeon heart toward its portal did **nothing at all for 130 game-seconds** — it dug the three tiles it was told to and then sat there.

**The cause was four months old and it walled a level-1 keeper in permanently.** The owner byte survived every terrain conversion, on a justification recorded in two places: *"which owner id means 'nobody' is UNKNOWN (Q17), and every rule that cares asks `TerrainDef.IsOwnable` first, so a stale byte on unownable terrain is inert."* **Both halves were false by the time it mattered:**

| the claim | why it failed |
|---|---|
| "which id means nobody is UNKNOWN (Q17)" | **Answered the same day, in ADR-0043**: 0 is unowned, confirmed three ways across all 66 shipped levels. The reason for not writing a neutral id had expired, and nothing connected the two facts. |
| "every rule that cares asks `IsOwnable` first, so a stale byte is inert" | **`TileWorkKind.Claim` does not.** It tests `OwnerPlayerId != playerId` and nothing else about ownership. |

So a keeper who dug through **their own reinforced wall** got Dirt Path still stamped with their id — neither claimable by them (the owner already matched) nor their own land (`ImpTaskSystem.StandsOnOwnLand` requires `IsOwnable`), so their territory could not grow through it, ever. On level 1 that is fatal rather than untidy: the only route out of that dungeon heart is through the keeper's own wall, so nothing beyond it was reachable — not the portal, not a lair, not a treasury.
**Decision.** `TileWorkRules.Convert` and `RoomRules.Unmake` clear the owner to `PlayerIds.Unowned` when the terrain they leave cannot be owned. The original reason for keeping it still holds where it applied: a treasury's destroyed type is Claimed Path, which *is* ownable, so **selling your own room still keeps the floor it stood on**. What changes is the cases nobody had in view — a dug wall, a destroyed claimed path, and a sold bridge over water, which no longer leaves you owning the water.
**Consequences.**
- Eight tests in `TileOwnershipTests` and one on the shipped level-1 map. Their point is that **every existing claiming test opened *unowned* rock**, which never carried the stale byte: the gap was specifically a wall the keeper already owned, and nothing dug one and then tried to grow past it.
- One scenario hash moved (`tiles-mine-gold-001`): a mined seam's tile no longer carries the owner the rock happened to have.
- **A third standing review question.** The first two are *what would fail if this stopped being true?* and *does anything read this fact at all?* This adds **is this justification still true?** — a reason resting on an open question rots silently the day that question is answered somewhere else, and neither the answer nor the reason knows about the other. `docs/GOAL_ALIGNMENT.md` carries it.

## ADR-0046 — DKII.EXE may be decompiled; its output may not enter the repository
**Date:** 2026-09-04 · **Status:** accepted · amended 2026-09-04: the binary is the *primary* source to consult

**Context.** Q96 asked whether Ghidra could be used on `DKII.EXE`. The owner: *"you are free to use it and decompile it, put all decompiled resources under reverse-engineering folder."* The brief requires clean-room behavioural reimplementation and forbids copying original source merely because reverse engineering makes it available; decompiler output is source code derived from the binary (EA's control flow and operation order), while a fact about behaviour (`EntranceGenerationSpeedSeconds` = 25) is the same whether read from a KWD, a stopwatch or `.text`. Extends ADR-0003.

**Decision.** The boundary is fact / expression, not decompile / do-not-decompile.
1. Facts, formulas, thresholds, comparisons, orders of operation and descriptions of control flow may cross into `docs/research/`, in our own words, with provenance naming the binary and the address.
2. Decompiler output never crosses: not into `src/`, `docs/`, a commit message or a test fixture, not renamed, reformatted or tidied. It stays in `reverse-engineering/`, which is gitignored except `reverse-engineering/README.md` (`.gitignore` lines 60–61); a `git status` showing a file there is a bug.
3. Binary-derived facts rank below owner observation of the running game. Where they conflict, the observation wins and the conflict is written down. Amendment: rule 3 may not be used to defer a question the owner cannot easily observe — an open question owes a Ghidra pass before it is recorded UNKNOWN with a "please play the game" note.
4. Every binary-derived fact is tagged as such, so a reader can tell which of the four evidence classes a claim rests on: shipped data, owner observation, community research, the binary.
5. `[Dk2Behavior]` confidence may rise on binary evidence to STRONGLY INFERRED, never to CONFIRMED. CONFIRMED is reserved for the running game.
- **Amendment (owner, 2026-09-04):** *"make using ghidra and decompiled sources the primary source and go-to when in doubt."* Source order becomes: the binary first, then the shipped data, then the running game, then community sources (previously OpenKeeper → Flame → Ember → RE docs → experiments, `AGENTS.md`). Rule 2 is unchanged; rule 5 is unchanged.
- Licence, flagged not answered: the GOG EULA is a contract question distinct from copyright (EU Directive 2009/24/EC Art. 6 and Art. 5(3) protect interoperability RE and observation/testing; contractual prohibitions are of doubtful enforceability in the EU). What protects the project is rule 2 — nothing derived from the binary is distributed — the same posture as toward the game's assets.

**Consequences.**
- The first Ghidra pass under this rule returned, from one debug-dump function, EA's own names for about 220 creature-instance fields, including `InWater`, `IsSlowed`, `Leaving`, `TurncoatPlayer`, `TurncoatTimer`, `HasBeenKnockedOut`, `DieInstantly`, `PortalGeneratedFromId`, `ContributesToPortalLimit`.
- Unblocked, in order: Q1/Q83 the tick rate via the four imported timer APIs (`QueryPerformanceCounter`, `QueryPerformanceFrequency`, `timeGetTime`, `GetTickCount`); Q86's interval slope (`Entrance.cpp` assert strings); per-tick subsystem ordering (`SimulationSystems` is ours); Q90's entrance percentages; ADR-0023's `DigRockHealth` override.
- Code now: `SimulationTiming.TicksPerSecond` = 4, no longer the placeholder 20 named here (see ADR-0089). Q86's slope: ADR-0086.
- Document-side citation of Flame names: ADR-0080.

## ADR-0047 — A portal produces only creatures whose own flags allow it
**Date:** 2026-09-04 · **Status:** accepted · amended 2026-09-07: the gate is keyed on the portal's room type and a hero-portal roster (ADR-0086 amendment 2)

**Context.** ADR-0042's portal had four gates (availability, pool, capacity, interval) and let through creatures DK2 never produces from a portal. On Frosty's Castle, Frosty's Lair and Frosty's Realm the hero roster — Knight, Giant, Guard, Royal Guard, Monk, Wizard, Thief, Fairy, Elven Archer — is AVAILABLE and pooled for players 3–6, so a keeper's portal produced Knights. Evidence: the binary (`docs/research/binary-entrance.md` §1).

**Decision.**
- `PortalRules.CanComeFromPortal` is the first gate in `PortalSystem.Pick`: a creature may come out of a portal only if it carries `CreatureFlags.AvailableViaPortal` (bit 10) and does not carry `CreatureFlags.IsUnique` (bit 27). The binary tests the creature's flags before consulting the level.
- The `IsUnique` refusal rests on the binary alone: no shipped level pools or unlocks a unique creature, and all thirteen uniques carry `AvailableViaPortal`, so the second test is what stops a Unique Dark Angel arriving.

**Consequences.**
- Both premises are tests: `NoShippedLevelEverPoolsOrUnlocksAUniqueCreature`, `TheThirteenUniqueCreaturesAllCarryThePortalFlag` (`tests/DK2.Behavior.Tests/PortalOnRealDataTests.cs`).
- A flag nothing reads is a rule nothing enforces — third instance after ADR-0045; feeds `GOAL_ALIGNMENT.md`'s review clause *does anything read this fact at all?*
- Availability is not producibility: a creature can be in the roster and the pool and still not be portal-produced, because DK2 has other ways to acquire creatures.
- Code now: `CanComeFromPortal(CreatureDef creature, RoomDef portal)` — room type 25 requires `CreatureFlags2.AvailableViaHeroPortal`, anything else `AvailableViaPortal`; the `IsUnique` refusal is common to both (ADR-0086 amendment 2).

## ADR-0048 — A creature has health and a hunger clock
**Date:** 2026-09-04 · **Status:** accepted · amended: lair healing replaced by ADR-0049; the death branch by ADR-0051; the hunger accumulator by ADR-0067

**Context.** M4's second item after the portal. Everything unbuilt reads health: the balance table has 21 health variables covering the lair, prison, torture, praying, fire, possession and the heart; `CreatureSleepsWhenBelowPercentHealth` makes sleep a function of health. Evidence: `docs/research/creature-needs.md`.

**Decision.**
- `CreatureState` gains `Health`, `HungerTicks` and `ChickensNeeded` (now `HungerNeed`, ADR-0067). `CreatureHealthRules` holds the rules; `CreatureHealthSystem` applies them once a game second. `HashVersion` 10 → 11, snapshot `FormatVersion` 10 → 11.
- `SpawnCreature` takes the `GameDatabase` so health is seeded from the record on every path — no overload without it, no lazy initialisation, no "0 means unset" (25 call sites, 16 files). `EveryCreatureHasHealth` asserts no shipped record has zero `Hp`.
- Per second, not per tick: every rate is written per second in the data; applying a fraction per tick would invent precision and tie the result to `SimulationTiming.TicksPerSecond`. Same cadence and phase as `ManaSystem`.
- 32767 means "never", per need: eight creatures never get hungry, six never sleep, neither set contains the other (the Vampire sleeps and never eats; the Dwarf eats and never sleeps). No single undead flag can produce that.
- Own-land regeneration applies to two of 46 creatures: Imp 67 a second, King Reginald 400, all others 0. Implemented as data; tested with one creature that has it and one that does not.
- A chicken can over-heal: Jack has 240 health and a chicken worth 250, so the clamp is load-bearing.
- Going unfed costs no health: 21 health variables and nine anger variables hold no starvation; the cost is anger (`angerNoFood`, per creature). Tested as a negative.
- Lair healing (the weakest reading, flagged): standing on a lair tile the creature's keeper owns heals — knowingly wrong twice (awake, any lair), written as one expression so replacing it is one edit. Q101; also Q99 (is zero health death or a 60-second window), Q100 (does regeneration care what the creature does), Q102 (when does the hunger clock start).

**Consequences.**
- Eating needs a Chicken (object 9) and something to lay one; the Hatchery is ADR-0066, eating ADR-0067.
- Binary route (2026-09-05): `HungerState.cpp` and `LairState.cpp` are dispatched through vtables so Ghidra's reference database shows zero referring functions; scanning initialised memory for the 32-bit value of the assert string's own address finds them (`LairState.cpp`: one raw hit, inside the lair-acquisition code). `ScanForAddr.py` is the tool; `source-file-map.md` the index; `lair-acquisition.md` the result.
- Code now: lair healing is `AsleepInOwnBed ? 100 : 0` (ADR-0049); zero health collapses (ADR-0051); `HungerNeed` accumulates (ADR-0067); `TicksPerSecond` = 4 (ADR-0089).

## ADR-0049 — A creature has its own bed, and heals only while asleep in it
**Date:** 2026-09-04 · **Status:** accepted · amended: `LairTileIndex` replaced by a bed object carrying `CreatureId` (ADR-0054); the remote claim replaced by a walk (ADR-0060)

**Context.** Owner, CONFIRMED: *"A lair is a room with tiles, on each tile only one bed/chair/whatever per creature can be present. When a creature enters a dungeon through a portal it goes to a lair, finds a free tile and occupies it with its bed. When a creature is injured it heals while it sleeps in its own bed."* Also: a skeleton has no lair; Horny is a summon, not a roster creature — both consistent with `LairObjectId` = 0, and the second is why Horny carries no `AvailableViaPortal`.

**Decision.**
- `CreatureState` gains `LairTileIndex`, `AwakeTicks`, `SleepTicks`. `LairRules` holds the rules; `CreatureSleepSystem` claims a bed, walks the creature there, sleeps and wakes it. `HashVersion` 11 → 12, `FormatVersion` 11 → 12.
- `CreatureHealthRules.LairHealPerSecond` = `AsleepInOwnBed ? 100 : 0`. Replaces ADR-0048's proxy, which was wrong twice: awake creatures do not heal; a creature heals only in its own bed, never on its keeper's lair floor.
- A lair's capacity is its tile count (one bed per tile). Not the general rule for rooms: a temple's capacity is its rim `2(w+h) − 4` (Q60); a heart stores gold in an outer ring. Capacity is per room type.
- `CreatureSleepSystem` reuses the imps' pathfinder and movement system; registered after `ImpTaskSystem` and before `CreatureHealthSystem`, so a creature that falls asleep this tick is healed the same game second. Workers are skipped by an explicit `IsWorker` check, not because their record says 0.
- `BedStillValid` is checked every tick (the lair can be sold, dug out or claimed by a rival); a dead creature gives up its bed. Both tested.
- Occupancy is derived (a tile is taken iff some creature points at it), never stored. Free tile chosen lowest tile index first across lairs in `RoomIndex` order — deterministic (ADR-0001); DK2 likely picks nearest.

**Consequences.**
- Cycle: `TimeAwake` 240 and `TimeSleep` 9 read as seconds — nine seconds at 100 a second is 900 of a Goblin's 1500 (Q103). `CreatureSleepsWhenBelowPercentHealth` = 50 sends a wounded creature home.
- Q104: the Dwarf and Stone Knight carry a bed object with `TimeAwake` 32767 — they would take a bed and never use it. Resolved by `LairRules.NeedsBed` testing `TimeSleep ≠ 0` too (ADR-0060).
- Code now: the bed is an `ObjectState` with `CreatureId`; `CreatureState.LairTileIndex` is gone (ADR-0054). The room is chosen by distance and the tile on arrival (ADR-0060).

## ADR-0050 — The six anger fields are signed, and anger waits for its unit
**Date:** 2026-09-04 · **Status:** accepted · the deferral is superseded by ADR-0053 (ceiling measured 32767)

**Context.** `AngerSlap` and `AngerInHand` were parsed unsigned and `kwd-creatures.md` documented the run of six that way. Unsigned, a Goblin's `AngerInHand` is 65286 and Jack's `AngerSlap` is 60536; signed they are −250 and −5000. Evidence: shipped data; the ladder CONFIRMED by the owner (Q9, Q105). See `docs/research/creature-anger.md`.

**Decision.**
- All six anger fields are `short`. Slapping angers (+3000 Goblin, +7000 Warlock, +8000 Horny); being picked up calms (−250 Goblin, −500 Warlock); Jack is the inverse (slap calms, hand annoys). The first four hold 0..150 on every shipped creature, so signed reads them identically. Had anger been built unsigned, a pick-up would have added 65286.
- Anger is not implemented until Q106 (the unit) is answered. The ladder is CONFIRMED twice — *"unpaid → unhappy → angry → leaves"* (Q9) and *"can't get a lair → unhappy, can't sleep → angry → leaves"* (Q105) — and every rate is in the data (`AngerNoLair` 100/s Goblin, lair −550, slap +3000, nine more), but `UnhappyThreshold` is 50 on all 46 creatures (a percentage) and no ceiling exists in the 214 balance variables, the creature record, or OpenKeeper (whose `CreatureMoodSystem` is an empty stub). 65535 makes a lairless Goblin leave in eleven minutes; 10000 in two. This is Q47's shape: ranges are not counts. One stopwatch (time to leave with no lair) or one slap count pins it.

**Consequences.**
- Until then: a bedless creature stays awake for ever and never recovers; a hungry creature that cannot eat is unharmed for ever; nothing can slap or pick up a creature, so the two largest sources have no trigger.
- Code now: `CreatureAngerRules.Ceiling` = `short.MaxValue` (ADR-0053); six counters (ADR-0081).

## ADR-0051 — Zero health is a sixty-second window, and the dying creature is a creature while the corpse is not
**Date:** 2026-09-04 · **Status:** accepted · amended 2026-09-04: a creature that leaves no body dies on the spot; vampire reincarnation is at the Graveyard · Confidence: CONFIRMED (owner, Q99) · supersedes the death branch of ADR-0048

**Context.** `CreatureHealthSystem` removed a creature at zero health and emitted `creature_died`, a placeholder against Q99. Owner: *"When a creature drops … it remains lying on the ground for a period of time, that gives imps an opportunity to return a creature to its bed in the lair for healing, or to prison if it is an enemy creature. Once that timer runs out a creature dies and its corpse remains there lying, that gives imps an opportunity to carry it to graveyard. If that timer runs out and no one carries the corpse it just disappears."* Two clocks, already in the data:

| Variable | Id | Value | Times |
|---|---|---|---|
| `CreatureDyingStateDurationSeconds` | 26 | 60 | collapsed, rescuable |
| `DeadBodyDiesAfterSeconds` | 23 | 60 | corpse, carryable |

**Decision.**
1. Zero health collapses: the creature enters `CreatureActivity.Dying` (= 6), keeps owner, tile, gold and bed, and dies when `CreatureDyingStateDurationSeconds` elapses.
2. A dying creature does nothing — no movement, work, sleep, hunger or regeneration.
3. The only route back is a bed. `CreatureDeathRules.Rescue` puts the creature into its own bed as `Sleeping` at unchanged health; the lair heals it at `ModifyHealthOfCreatureInLairPerSecond`. (Caller: ADR-0063.)
4. The dying creature stays in `WorldState.Creatures`; the corpse is not modelled as a fake creature. A corpse is a creature in a state — none of the 145 OBJECTS records is a corpse (nearest: the untyped props Skeleton 48, Skull 49, Bone 50); both timers are creature variables, `DecomposeValue` is a creature field, the creature dump carries `HasBeenKnockedOut` and `DieInstantly`. What a corpse waits for is a carry task and a graveyard (`docs/research/objects.md` §4; built by ADR-0063).
5. `GenerateDeadBody` governs, not `DecomposeValue`.
- Amendment (owner, same day): *"Imps are dead right away, they don't have a lying state. Vampires also don't drop a body, they reincarnate at their sarcophagus and sleep to recover, but they lose a level once they reincarnate. If they were killed at level 1 they are permanently dead. Exception … monks: if a monk kills a vampire it is permanently dead."* `CreatureDeathRules.HasDyingState` = `LeavesBody` — CONFIRMED from two directions: the owner for the Imp, and OpenKeeper `HealthSystem.processHealthDepleted`, which removes a creature with no `GENERATE_DEAD_BODY` before any unconscious handling. One flag decides both halves: the dying state is a live body on the floor.
- Vampire (`CanBeResurrected`, bit 23, set on exactly the two Vampires) is data-driven and deliberately unimplemented: the cost is a level and levels did not exist; "a monk killed it" needs an attributed killer (combat, M7). Instant death is therefore correct rather than a placeholder — every creature is level 1, and a level-1 vampire is permanently dead by the owner's rule. The flag is read, tested, branched on by nothing (Q111). OpenKeeper neither implements nor reads it.
- Owner retraction: *"sarcophagus at the graveyard, I made a wrong claim earlier."* Reincarnation is at the Graveyard, not the bed. The data agrees: `Rooms.kwd` gives the Graveyard `CreatedCreatureId` 8 (Vampire) — only two rooms create a creature, Graveyard (8) and Prison (9, Skeleton); the Graveyard's object ids are 31–34 and the Vampire's `LairObjectId` is 26. The binary's `Object.cpp` union dump names `Lair` and `TombStone`; no Sarcophagus or Coffin string exists. Q113 closed.

**Consequences.**
- `HashVersion` 12 → 13, `FormatVersion` 12 → 13: `CreatureState.DyingTicks`.
- Guards are on `CreatureDeathRules.IsDown` (no health, or already dying), not on the state flag: the collapse is booked once a game second, and in the window a zero-health creature near its bed slept and healed (never collapsing), and a zero-health Imp regenerated 67 a second and could not be killed at home. Combat will subtract per tick.
- Own-land regeneration is suppressed at zero health; lair healing is not (it *is* the rescue). Inside `CreatureHealthSystem` healing applies before the collapse test. After the amendment the suppression's subject is King Reginald — 400 a second on own land and a dead body — the only creature with both properties; the test asserts he is the only one.
- Eight of 46 leave no body: Imp, Vampire, Skeleton, Horny, Stone Knight, Unique Skeleton, Unique Vampire, Jack. Never-eats and no-body share seven, differ one each way (Unique Firefly leaves a body; Jack eats); never-sleeps is a third set (the Dwarf). No undead flag; each set asserted by name. Horny and Jack carry `DecomposeValue` = 200 that can never be read.
- Rejected: a corpse as a creature with a second timer; granting a rescued creature 1 health (ordering achieves it); letting a collapsed creature claim a replacement bed when its lair is sold (claiming is a walk and a placement; cost Q107 — such a creature cannot be rescued, which since ADR-0063 decides whether an imp sets off at all).
- Fixture: `Dk2TerrainFixture`'s Goblin lacked `GenerateDeadBody` — third transcription error the drift guard caught in this record (after the Imp's hunger rate, wrong by 4096, and its own-land healing, transcribed 0).
- Code now: corpses (`CreatureActivity.Dead`) and carrying — ADR-0063; capture — ADR-0064.

## ADR-0052 — Only a keeper is staffed, and the test suite stops calling the hero side "Keeper"
**Date:** 2026-09-04 · **Status:** accepted · Confidence: CONFIRMED (structural — `PlayerIds`, ADR-0043) · closes the implementation half of Q91

**Context.** `PlayerIds`: 0 unowned, 1 hero side, 2 neutral, 3–7 keepers. `PortalSystem` gated on `PlayerIds.IsKeeper`; `ImpSupplySystem` predated it and looped every player, so 0, 1 and 2 would receive free imps and could cast Create Imp. Recorded as harmless because none has a heart on shipped data — a claim nothing checked: a level giving the hero side a heart would have staffed it, and `PlayerState.FreeImpCountdownTicks` was hashed state counting down for players 0–2 on every level.

**Decision.**
1. `ImpSupplySystem` gates on `PlayerIds.IsKeeper` in both paths: the free top-up skips non-keepers (countdown held at zero), and `Buy` refuses with `reason=not_a_keeper` before the cost is looked up. Distinct from `reason=no_such_player`: Neutral exists and owns 167 of the campaign's portals.
2. Tests stop using non-keeper ids as keepers: fifteen files' `Keeper = 1` / `Rival = 2` become `PlayerIds.FirstKeeper` and 4; bare `(byte)2` rivals become `PlayerIds.Neutral` where they meant neutral.
3. `Dk2TerrainFixture.Map()`'s starting room belongs to the first keeper (it belonged to player 1).
4. Seven scenarios re-keyed, one of which used `id: 0` as its keeper.

**Consequences.**
- Seven scenario hashes moved — all `FreeImpCountdownTicks` pollution, verified by re-running one with ids stashed: event log identical apart from the player number.
- Four tests give a non-keeper a heart and the whole starting room and assert it gets nothing; `AKeeperWithTheSameHeartIsStaffed_WhichIsWhatMakesTheGateAGate` is the control.
- Re-keying the constant alone produced 38 failures, every one the fixture map handing the room to id 1 — two errors that agreed with each other.
- Not done: `ManaSystem` runs for every player. Mana comes from owned terrain and non-keepers own terrain; "only keepers accrue mana" has no source. Left as a decision, not an oversight.

## ADR-0053 — Anger's ceiling is 32767, measured twice by two methods
**Date:** 2026-09-04 · **Status:** accepted · amended 2026-09-05: the draw-once rule is superseded by ADR-0061; the pooled counter by ADR-0081; the applied-sources table extended by ADR-0079 and ADR-0082 · Confidence: CONFIRMED (owner, two independent methods) · closes Q106, Q114; supersedes ADR-0050's deferral

**Context.** Every anger rate was known (`AngerNoLair` 100/s Goblin), `UnhappyThreshold` = 50 on all 46 creatures, and the ceiling appeared nowhere: not in the 214 balance variables, the creature record, or OpenKeeper (`CreatureMoodSystem` empty; `CreaturesController` comments `// I guess the initial mood is 10000`).

**Decision.**
- Measurement one, stopwatch: a Goblin with no lair (only `AngerNoLair` = 100/s running) — unhappy at ~2:45 (165 s, 16 500 accrued vs 16 383 = 32767 ÷ 2), angry at ~5:30 (330 s, 33 000 vs 32 767). Ratio exactly 2: accrual is linear, angry is 100 % of the ceiling, `UnhappyThreshold` is a percentage; 10000 refuted (predicts 50 s / 100 s).
- Measurement two, slap count (duration cannot separate 100/s into 32767 from 200/s into 65535): a Salamander (`AngerSlap` 3000) took exactly 6 slaps to unhappy, 5 more to angry. 5 not enough / 6 enough ⇒ ceiling ∈ (30000, 36000]; 10 not / 11 enough ⇒ ceiling ∈ (30000, 33000]. 65535 would need 11 and 22. With the ceiling pinned, 16383/165 = 99.3 ⇒ 100 once a second, no sub-second cadence.
- `CreatureAngerRules.Ceiling` = `short.MaxValue`. Not a balance variable: a property of the field width (six signed 16-bit fields, ADR-0050); 32767 is already DK2's sentinel for "never" in `HungerRate` and `TimeAwake`.
- Ladder `Content → Unhappy → Angry` at 0, 50 %, 100 %; confirmed from Q9 (pay), Q105 (lairs) and the bed-loss answer.
- An angry creature's job comes from its record: a Goblin's `AngryJobs` is `Leave(50%)` / `Rebel(50%)`, drawn from the simulation RNG. Originally drawn once and kept; amended 2026-09-05 (Q122, binary): DK2 re-rolls on every allowed selection, gated at five game seconds (5 × variable 19 = 20 ticks) on `LastGameTickSuccessfullyChoseAngryJob`, and falls through the list when the chosen job fails — built as ADR-0061.
- Only sources the player's own choices can turn off are applied:

| Source | Applied |
|---|---|
| `AngerNoLair` +100 | yes — lairs are built, sold, filled |
| `ModifyAngerOfCreatureInLairPerSecond` −550 | yes — recovery is 5.5× the accrual |
| `AngerNoFood` +100 | no (then) — no Hatchery lays a chicken |
| `AngerNoWork` +50 | no (then) — non-workers have no job system |
| `AngerNoPay` +60 | no (then) — no payday |
| hated company +150 | no — relation parsed, unused |

**Consequences.**
- `HashVersion` 13 → 14, `FormatVersion` 13 → 14: `CreatureState` gained `Anger` and `AngryChoice`.
- A creature that is down is skipped (ADR-0051).
- Imps need no special case: `AngerNoLair` = 1 against 32767 is nine hours, and an imp is owed no bed. The Skeleton is the only creature whose four `angerNo*` fields are all zero (see ADR-0087: the rule is a flag, and Jack's values are a decoy).
- Not done: rebelling (`RebelLeavingPercentage` 70, `RebelBecomeGoodPercentage` 15, `RebelDefectPercentage` 15 have nothing to act on); where a deserter goes (DK2 sends it to a portal; the wiki's "rebellion is the fallback" conflicts with the record's weighted table — Q116); the zero floor is ours (Q115 — without it a combat-pit victor at −16400 would be immune for three minutes).
- Code now: `Anger` is six counters and the mood is the maximum (ADR-0081); `AngryChoice` is replaced by `LastAngryChoiceTick` (ADR-0061); `AngerNoPay` (ADR-0079), `AngerNoWork` (ADR-0082) and the prison's `AngerNoFood` (ADR-0074) are charged.


## ADR-0054 — An object is one type with per-kind state, and a bed knows whose it is
**Date:** 2026-09-04 · **Status:** accepted · Confidence: STRONGLY INFERRED for the shape (read from `DKII.EXE`); the storage is ours and not a fidelity requirement · closes Q130; Q131 CONFIRMED 2026-09-05

**Context.** OBJECTS was parsed (145 definitions, twelve beds, Chicken 9, three gold objects) and `WorldState` had no place for an instance. `LairRules` already modelled the bed as `CreatureState.LairTileIndex`; an object carrying the relation from the other end would have held two copies of one fact.

**Decision.**
- `ObjectState` is one type with a definition id, an owner, a tile and per-kind fields added as mechanics need them; `WorldState.Objects` is one list in placement order. `CreatureState.LairTileIndex` is removed; the bed carries `CreatureId` and `LairRules` derives everything from that.
- A bed exists only while owned: claiming a lair tile creates an object of the creature's `LairObjectId`; releasing destroys it. A lair tile is taken exactly when a bed object stands on it.
- Binary evidence: the object dump prints `Union Data:` then one of 23 blocks, every one reading from offset 0x14A; the next field is at 0x15C, so the union is 18 bytes of per-kind state — one type with a tag. The tag is the definition id, branched on with literals; 21 literals checked against our `Objects.kwd` parse all agree (9 Chicken, 35 Chicken Coop, 74 Boulder, 31–34 Tomb Stones). Five type flags confirmed from the dispatch: `TypeGold` 0x40, `TypeSpecial` 0x04, `TypeLair` 0x20, `TypeCrate` 0x10, `TypeSpellBook` 0x08. Flags beat ids: the `Lair` branch is reached by flag alone, so `ObjectDef.IsLair` is the right question.
- Ownership on the object: a bed can be destroyed (`FlagDestroyWhenDropped` sits beside `CreatureId`), a bed outliving its creature is a leak the creature-side model cannot express, twelve bed objects serve 46 creatures, and presentation needs the object.
- Flat record rather than a union: every field documents which kind writes it, every other kind leaves it zero, and the hash covers all fields unconditionally.

**Consequences.**
- `HashVersion` 16 → 17, `FormatVersion` 16 → 17 (composition only: one field out of every creature, a count and an id counter in).
- `GameDatabase.ForLevel` had silently dropped `Objects` since the parameter was added; a campaign scenario threw for want of object 22 while all tests passed. A reflection guard now sweeps every record collection.
- Q131 CONFIRMED by the owner (2026-09-05): *"a lair is bare, green lair texture on the floor tile, only when a creature enters a lair and claims a tile by dropping its bed does that tile get furniture on top of it."* The code already did this.
- Unblocked in order: Hatchery (ADR-0066), gold on the floor (ADR-0055), graveyard via a carry task (ADR-0063).

## ADR-0055 — Gold on the floor is an object, and the limit named after it is not a limit
**Date:** 2026-09-05 · **Status:** accepted · Confidence: STRONGLY INFERRED (rules read from `DKII.EXE`; ours: which tile two call sites choose, and a pile's owner) · closes Q65; opens Q136–Q140

**Context.** `GoldStore` names four places gold can be (treasury tile, heart tile, creature's hands, floor pile) and implemented three; three paths threw gold away and logged the loss: a sold treasury tile, a destroyed one, a creature dying while carrying. Evidence: `docs/research/gold-on-the-floor.md`.

**Decision.**
- A pile is an object of type 1 "Gold" carrying its amount in `ObjectState.GoldHeld` (the union's first field); `FloorGold` has the rules.
1. Nothing merges — every path creates a new object, as the original does; the one merging site is an imp choosing where to put a load down.
2. Nothing is clamped. `MaxGoldPileOutsideTreasury` (2000) has exactly three readers in the binary, all imp put-down code, all testing that a candidate spot holds a pile not strictly greater than the variable. Selling a 3000 treasury tile makes a 3000 pile. Second plausibly named cap that governs something else (after `MaxFreeRangeChickensPerPlayer`): expose, do not enforce.
3. A pile is on nobody's books: `GoldStore.Total` counts tiles; the original subtracts the amount from the player when it makes a pile out of a treasury tile.
4. Treasury gold stays in the tile (the original stores it as object type 3, one per tile; ADR-0015 unchanged — no behaviour separates the representations).
- A creature destroyed while carrying leaves `piles = 1 if gold < 1000, 2 if gold < 3000, else 3`, each `gold / piles` (integer division); the remainder is destroyed (2999 → two piles of 1499, one gold lost). Reproduced with its bug.

**Consequences.**
- `HashVersion` 17 → 18, `FormatVersion` 17 → 18 (composition only).
- Three event messages stopped claiming piles unimplemented; two tests asserting the gap now assert it closed.
- Open: who owns a pile (Q136), whether a portal drops one (Q137), whether mining leaves one (Q138), which tile an overflowing refund uses (Q139), whether a blocked imp puts its load down (Q140).
- Fetching a pile: ADR-0056/ADR-0058.

## ADR-0056 — Gold on the floor is the first thing an imp does
**Date:** 2026-09-05 · **Status:** accepted · amended 2026-09-05 (same day): fetching is below marked excavation · placement superseded by ADR-0058 · Confidence: CONFIRMED (owner observation; mechanism is the existing mining tail) · closes Q141; opens Q142–Q147

**Context.** Where fetching sits in an imp's priority could not be guessed; the observed order excavation → claiming → reinforcing (Q24, ADR-0018) did not contain it. Owner: *"If there is gold on the ground an imp will first take from that gold pile and take it to a treasury, until all of it is gone"* — from a dungeon where imps had mined a gem tile and nobody was mining the still-marked tile.

**Decision.**
- `CreatureActivity.FetchingGold` (= 8); the fetch search runs ahead of the `JobPriority` loop. It is not a `JobPriority` member (not tile work: no `TileWorkKind`, nothing damaged or converted).
- The pick-up is instant on arrival — no health delta, no balance number (Q143). One imp to a pile tile (`ImpCrowding` caps a stood-on tile at one, Q44). `DeliveringGold` is unchanged. Oldest pile first where a tile holds several (ours, Q142).
- Amendment (same day, owner asked for a Ghidra pass; `docs/research/imp-task-priority.md`): DK2 keeps a keeper-wide task list whose entries carry a kind, tried in a fixed order. Digging and mining a marked tile are kinds 1 and 2, raised by the tile-marking code on whether the terrain's `GoldValue` is zero; gold on the floor is kind 7, sixth of nine. So Q24 stands, fetching moved to second (above claiming and reinforcing was ours, Q146), and an imp will not take gold that is not on its keeper's own ground or on water (answers Q144 — the earlier "reachability reproduced the owner's stranded-imp report" is withdrawn; it reproduced the outcome, the rule is ownership).
- Q147: the kind chain is a callback invoked from a scan around a position, so DK2's primary key may be *where* — settled by ADR-0058.

**Consequences.**
- Hashes unmoved (no pinned run meets floor gold). The load-bearing test asserts `work_applied` does not appear before `gold_picked_up`.
- Scenario `scenarios/imps/an_imp_fetches_dropped_gold.yaml` (id `imps-fetch-dropped-gold-001`).
- The imp finishes the tile it is working on; whether DK2 interrupts a swing is Q145.
- Code now: floor gold is found by `ImpTaskSystem.TryTakeFloorGold` inside the positional sweep `TrySweep` (Tier B), below claiming and not ordered against reinforcing — see ADR-0058.

## ADR-0057 — The free-imp clock is a deadline, and it was a countdown for three weeks
**Date:** 2026-09-05 · **Status:** accepted · supersedes the Q56 half of ADR-0021

**Context.** `FreeImpCountdownTicks` was held at zero while a keeper was up to strength and restarted from a full interval when they fell short; its comment said the alternative was indistinguishable in the steady state. The owner filmed thirty seconds at four imps, one dropped into the heart, and the replacement walking about 1.4 s later — then five-second gaps.

**Decision.**
- `PlayerState.NextFreeImpTick`: the earliest tick a keeper may be handed a free imp. `ImpSupplySystem` writes it in exactly one place, immediately after making one, as `now + interval`. A keeper at or above `MinimumImpThreshold` is skipped without the deadline being cleared, reset, paused or banked — so a keeper long at full strength has a deadline in the past and the next loss is replaced next tick; only later replacements are an interval apart.
- Binary (eight instructions in the keeper's per-frame update): the deadline advances whether or not the imp is placed (no test of the call's result); a free imp skips the spell-availability check, the mana check and the deduction; `TimeBeforeFreeImpGeneratedSeconds` is multiplied by variable 19 (the 223-reader seconds-to-ticks global, Q132).
- Q54 confirmed twice: Create Imp costs `max(1, imps − MinimumImpThreshold + 1) × base`, clamp included; the creature type counted is read off the power's record.

**Consequences.**
- `HashVersion` 18 → 19, `FormatVersion` 18 → 19.
- No "window before the first free imp": a keeper with a heart and no imps is staffed on tick zero. `ManaTests` runs without `ImpSupplySystem`; two `ManaUpkeepTests` re-based on starting land.
- Open: does an imp in the hand count toward the threshold (Q148); is the ~1.4 s simulation or animation (Q149).

## ADR-0058 — An imp chooses in two tiers, and gold is near rather than last
**Date:** 2026-09-05 · **Status:** accepted · supersedes the gold placement in ADR-0056; amends ADR-0018 (the order stands; what it is part of changed)

**Context.** ADR-0056 placed gold second on the argument that placing it last would make the mechanic dead because an imp can nearly always find a wall to reinforce. The premise is true and the conclusion false: the order is not a ladder. The owner ran Q147's experiment: a far wall beats gold underfoot, a near wall beats gold a few tiles off — consistent with "global", not "positional". The second read (`docs/research/imp-task-priority.md`): the nine-step chain is the per-tile callback of a two-tile scan run only after a drop; the real routine walks a byte table of kinds (`0e 08 09 01 03 0d 0c`) whole-map first, then ring-scans with a four-step per-tile chain containing reinforce and gold.

**Decision.**
1. Tier A — kind first, whole map: marked excavation, then claiming; any marked tile anywhere beats any claimable tile anywhere.
2. Tier B — position first, only when Tier A is empty: one nearest-first search asking at every standing tile *is there a wall to reinforce from here?* then *is there gold under my feet?* A wall beats gold on the same tile; a nearer pile beats a farther wall.
- Gold sits below claiming and is not ordered against reinforcing at all.
- Kind 14 — claim the tile you have just dug, before anything else, once no neighbour still carries a dig task — first in the original's table. Built the same day (Q154): `TileState.FreshlyDug` stands in for the original's task (set when excavation destroys a tile, cleared by the next terrain change); `JobPriority.ClaimFreshlyDug` is Tier A's first step with the eight-neighbour test on every tagged kind but mining. `HashVersion`/`FormatVersion` 19 → 20. Rank and neighbour rule read; raising at dig completion STRONGLY INFERRED. Exposed the mover defect fixed in ADR-0059.
- Not built: task stealing by distance (Q155); kinds 8, 9/10, 12, 13 (objects, traps/doors, corpses, carrying creatures — later ADR-0063/0064/0065).

**Consequences.**
- `ImpTaskSystem.TryTakeJob` is Tier A; `TrySweep` is Tier B, a single pathfinder search with a two-part predicate. `JobPriority.Reinforce` is in no ordered list.
- Three tests changed meaning (gold-fetching tests pre-reinforce the room's walls); four tests pin the competition. The floor-gold scenario's duration doubled to show the second trip. Determinism pin unmoved.
- The distance metric is ours (steps, Q25) where the original uses rings; stated at the top of `TrySweep`.

## ADR-0059 — A diagonal leg is walked surplus-first, and the corner is the only point it touches
**Date:** 2026-09-05 · **Status:** accepted · amends ADR-0025 (the mover's half)

**Context.** `WallsBlockMovementTests.NoCreatureIsEverStandingOnASolidTile` failed once imps claimed freshly dug tiles: on tick 440 of a four-imp run, imp 4 stood inside the impenetrable gem tile at (6,5) for five ticks. The route (6,4) → (5,5) was a legal corner-cut; the mover walked the leg 45° first, then straight, from a start at x ≈ 6.96 (a creature stops where it crossed into its tile, not at the centre), and the 45° phase swept through (6,5).

**Decision.**
- A leg is walked surplus of the longer axis first, straight; then both axes together at 14/10. From any point inside the starting tile that path runs exactly through the corner the two tiles share and touches no other tile — Q28's *"passes diagonally between two solid tiles"* once the creature has a width.
- `LegLength` is octile: a leg from an off-centre start is not square, and `max × 14/10` made creatures arrive late.
- `KeepOffTheCorner`: if a tick's budget ends exactly on the shared corner, integer division hands that point to the tile with the larger coordinates (solid, between two solid tiles); one position unit (1/4096 tile) toward the next waypoint is inside the destination. The only place the mover adjusts a position for a reason other than speed.

**Consequences.**
- Both wall invariants hold on the four-imp run with corner-cutting still allowed. Determinism pin unmoved (no off-centre diagonal leg in the fixture). Where a leg does start off-centre, position differs by up to half a tile-width in one axis for a few ticks; arrival times unchanged within octile rounding.
- Routing remains ours (Q5).

## ADR-0060 — A bed is walked to: the room by distance, the tile on arrival
**Date:** 2026-09-05 · **Status:** accepted · extends ADR-0049, ADR-0054 · the finder moved into `LairSeeker` (ADR-0061)

**Context.** `CreatureSleepSystem` gave a creature the lowest free lair tile index on the first free tick, with no walk. The owner filmed creatures walking into a lair, rejecting occupied tiles and moving on; in a 1×5 lair the tiles went 1, 3, 5, 4 (`docs/research/lair-acquisition.md` §1); the binary was read to the tile (§4, §5) and a third party's plan checked against it (§7).

**Decision.** What the original does (STRONGLY INFERRED from the binary; outcome CONFIRMED by recordings):
1. Trigger: idle without a bed, whatever the mood (Q150), plus the sleep need every `TimeAwake`. Gated on `LairObjectId ≠ 0` and `TimeSleep ≠ 0` (Q104).
2. Room first, by distance to the room's centre: nearest reachable lair with a free slot, preferring one holding no bed of the other alignment (Q151). Capacity is the tile count (Q156).
3. Tile on arrival: the tile underfoot, then eight neighbours in order (−1,−1), (−1,0), (−1,+1), (0,+1), (+1,+1), (+1,0), (+1,−1), (0,−1), then a walk of the room from a random start; a candidate must be the keeper's lair tile with no bed of any kind. Nothing is reserved.
4. The bed is created at the tile's centre; the creature sleeps at once only if health is below `CreatureSleepsWhenBelowPercentHealth` (variable 88, 50); otherwise idle.
5. A room found full on arrival re-runs the finder; a failed attempt is retried at the next idle decision.
- Built: `CreatureActivity.SeekingLair` (= 9), distinct from `GoingToSleep` (= 4, walking to an owned bed). `LairRules.NeedsBed` tests both fields (the Dwarf and Stone Knight, hero bed with `TimeSleep` 0, are never sent). `LairRules.TryFindFreeTile` (neighbour table from the binary), `LairRules.IsFreeLairTile`, `LairRules.HoldsBedOfOtherAlignment` (reads `IsGood`/`IsEvil` both ways). Reach is a real route over `WorldPathGrid`, walked by `CreatureMovementSystem`; failure mid-walk, destination sold/captured, or room full all end in idle with an event. `TryClaimBed` renamed `TryPlaceBedInAnyLair` — placement without a journey, for tests and level setup; no system calls it. Moving house (finding a tile while owning a bed elsewhere destroys the old one) kept as a guarded branch with an event, unreachable here.
- Ours, marked in code: `LairRules.IdleRedecisionTicks` = 12 (the original's idle state wanders 12 of its ticks; whether its tick is ours was not established); the arrival tile (Q157) is the room tile nearest the bounding-box centre, lowest index among equals (`LairRules.ArrivalTile`); the room walk is ascending tile index (the original's join order is unobservable), random start kept; no sub-tile jitter (the original displaces the target about a third of a tile); the unhappy/angry choosers' lair-first line needs no code while every bedless non-worker is idle.

**Consequences.**
- No new persistent field: `HashVersion`/`FormatVersion` stay 20; pin unmoved. A random draw is consumed by each on-arrival room walk past the neighbour ring — the anger scenario's third goblin's draw moved from `Rebel` to `Leave`.
- A failure event is emitted at every attempt (530 lines in the 6700-tick anger scenario).
- `LairAcquisitionTests`: no remote claim; unreachable rejected; nearest non-full chosen; alignment preference and fallback; occupancy on arrival; ring order and random-start walk; two creatures one room; full on arrival then retry; destination sold mid-walk; bed loss by the same journey; sleep only if hurt; the two-field gate; snapshot mid-journey; same seed same beds.
- Open: Q157 and the cadence are readable and unread. Prediction for the owner (§5 of the note): with a large empty reachable lair before the portal fires, every creature beds down before its first job.

## ADR-0061 — The angry job is rolled again, and a lair is tried before it
**Date:** 2026-09-05 · **Status:** accepted · corrects ADR-0053; extends ADR-0060 (the lair attempt is shared code)

**Context.** ADR-0053 drew once from `AngryJobs` and kept it. Q122 read the chooser (`FUN_004784c0`): nothing is kept but a timestamp; the creature rolls afresh on every allowed selection, allowed five game seconds after the last one that came to something, gated on the field EA's dump calls `LastGameTickSuccessfullyChoseAngryJob`. With `Leave(50%)` re-rolled an angry Goblin does walk out.

**Decision.** The original (STRONGLY INFERRED; `docs/research/creature-anger.md`, "The chooser, read from the binary"):
1. a five-game-second gate against the timestamp, checked first;
2. a lair attempt before any roll — the same `FUN_0046f390` the idle state uses (`lair-acquisition.md` §5, §8); a creature that sets off has spent its selection;
3. cumulative ranges over the table in record order (the original's sort comparator never returns negative, so it cannot reorder an ascending array);
4. `rand(100)` = `% 100`, 0..99;
5. the first entry whose range holds the roll, inclusive at both ends, dispatched;
6. fall-through in record order when that started nothing;
7. the timestamp stamped only on success.
- Built 1–7. `CreatureState.AngryChoice` → `CreatureState.LastAngryChoiceTick`; the lair finder moves from `CreatureSleepSystem` into `LairSeeker`, shared by the idle and angry paths.
- Stop: an unbuilt job ends the selection. The original's fall-through is for jobs whose start routine fails (no training room, no shrine); falling through an unbuilt `Rebel` to a built `Leave` would make half of every angry Goblin a deserter DK2 would not produce. The creature says so, stands, and rolls again when its gate opens. The fall-through itself is built and reachable (a table summing below 100 leaves rolls matching nothing).
- Ours: five game seconds is `5 × SimulationTiming.TicksPerSecond` (the original: `5 × variable 19` = 20 ticks; ours was 5 × 20 = 100 ticks then — now 5 × 4 = 20, ADR-0089); the selection is offered once a game second (the original at every "think"); `Leave` releases the bed when it starts (Q121) and a leaving creature is carried rather than re-choosing.

**Consequences.**
- An angry Goblin now leaves. A Vampire never leaves: its table is `Rebel(100%)`.
- `HashVersion`/`FormatVersion` 20 → 21.
- The lair attempt appears under `[CreatureAnger]` as well as `[CreatureSleep]`.
- `CreatureAngerTests` covers the overlapping ranges, the fall-through and the gate's cadence.
- Open: the per-creature bit at the top of the chooser is unidentified. The definition flag `0x4000` is `IsHorny` (bit 14; Q158, ADR-0062): the slap handler tests the same bit first, and across 46 creatures it is set on Horny alone. The unhappy chooser (no jobs to run then; built ADR-0082) stamps the *angry* chooser's timestamp on a successful lair attempt.


## ADR-0062 — The Hand of Evil, and Efficiency becomes a number a keeper can move
**Date:** 2026-09-05 · **Status:** accepted · amended: the refusal of a collapsed creature is DK2's own rule (ADR-0064); the drop bonus rule is superseded by ADR-0069; the creature drop is restricted to owned ground (ADR-0077); `DropCreatureCommand` is `DropCommand` (ADR-0075)

**Context.** Efficiency (ADR-0020's 2026-09-05 amendment) was a constant because both things that move it — a slap and dropping a worker onto work — need the hand. `AngerSlap`, `AngerInHand`, `SlapDamage`, `MaxNumberOfThingsInHand` were parsed and unread. Research: `docs/research/hand-of-evil.md`.

**Decision.** Three commands — `PickUpCreatureCommand`, `DropCommand`, `SlapCreatureCommand` — one `HandSystem` running before every system that makes a creature act, and `EfficiencyRules` feeding every work application. Read from the original (§ = research doc):
- The hand is an array on the keeper, capacity `MaxNumberOfThingsInHand` = 64; dropping is last in, first out — the drop handler reads back the slot it is about to free (§2). The drop takes a position and no id.
- `PickupTick` is written on the drop as well as the pick-up (§2), which is what makes the mood grace period work.
- `AngerInHand` is a rate: once a game second from 20 game seconds after the pick-up (§7). Parking a Goblin in the hand calms it at −250.
- The slap sets `WorkFasterTimer` unconditionally, so slaps do not stack (§4) — matching the owner's measurement.
- Efficiency is two independent ten-point bonuses on an 80 % base (§6): 100 % reachable, 110 % not.
- There is no slap cooldown: `now < LastSlapTick + var19` guards only the knock-back impulse; anger, damage and the raised Efficiency land regardless. The six slaps that pinned the ceiling (Q114) were six because a hand is slow.
- Percents, not 4096ths: DK2's constants `0xccc` = 0.7998 and `0xccc + 0x199` = 0.8997 give 67.47 → 67 a swing at 90 %, which would show 304 gold on the owner's counter; it showed 309 (68). The displayed percentages are the exact quantity; `EfficiencyRules` works in whole percents with halves rounded away from zero and reproduces all three measured numbers. `EfficiencyRules.Scale` returns its input unchanged at 80 % (`delta / 5` = `delta × 0.8 / 4`), which is why every existing test and scenario hash survived.
- Ours: `HandSystem` runs first (system order is UNKNOWN in the original); only the keeper's own creatures may be handled (DK2 refuses an enemy under one condition, this under all); the aiming test is the tile underfoot and four neighbours where the original scans a two-tile radius (Q162); a held creature climbs the anger ladder but decides nothing until it lands. The refusal of a collapsed creature (Q161) was recorded as ours — it is DK2's (bit `0x200000` `CreatureDying`, ADR-0064).

**Consequences.**
- `HashVersion`/`FormatVersion` 21 → 22: `CreatureState` gained five fields, `PlayerState` the hand.
- `Dk2TerrainFixture` had the Imp's `SlapDamage` transcribed as 0; it is 100 against 500 health (five slaps kill an imp). Guarded by `CreatureNeedsOnRealDataTests`. The fixture Goblin lacked `CanBePickedUp` and `CanBeSlapped`; two hand tests had silently tested the refusal path.
- Harness commands `pick_up`, `slap`, `drop`; `scenarios/creatures/the_keeper_picks_up_an_imp_and_slaps_it.yaml` uses `imps_dig_what_is_marked` as control (corridor falls at t62 instead of t67).
- `HandTests`; two tests assert numbers read off the running game.
- Open then: slap bonus duration 25 s vs owner's "after a minute" and `IncreasedWorkRateDurationFromSlappingSeconds` = 60 (Q159 — closed by ADR-0069 at 25 s; the 60 is the research bonus, ADR-0090); `TimeInHandBeforeCreaturesBecomeAngrySeconds` = 1000 vs the hardcoded 20 (Q160); the third Efficiency term, 0.05 per unit of a keeper counter (probably the torture tally); the hand holding gold, chickens, bodies (ADR-0075; bodies are never hand-carried, ADR-0064).
- Code now: `EfficiencyRules.BasePercent` = 80 lives in `src/DK2.Simulation/Efficiency.cs`.

## ADR-0063 — Carrying: an imp picks a body off the floor
**Date:** 2026-09-05 · **Status:** accepted · completes ADR-0051; the enemy branch of kind 13 is ADR-0064

**Context.** Dying is three states with two sixty-second timers — health → 0; COLLAPSED (`CreatureDyingStateDurationSeconds` = 60, an imp → its own bed); CORPSE (`DeadBodyDiesAfterSeconds` = 60, an imp → a graveyard); gone. Only the first existed; the stated blocker "a corpse is an object" was false (none of 145 OBJECTS is a corpse). Research: `docs/research/creature-death.md`, `docs/research/imp-task-priority.md` §4.

**Decision.**
- `CarryRules`, activities `FetchingBody` (= 11) and `CarryingBody` (= 12), corpse state `Dead` (= 10), and the last two entries of DK2's Tier A table `14, 8, 9, 1, 3, 13, 12`: kind 13 carry an unconscious creature (own → Lair, enemy → Prison), kind 12 carry a corpse → Graveyard. Carrying sits below claiming and above everything found by distance; an imp buries nobody while there is unclaimed ground (`an_imp_buries_a_corpse`: corpses claimable at t1219, the imp comes at t1311).
- A collapsed creature goes to its own bed (kept through the dying window for this); arrival calls `CreatureDeathRules.Rescue`. A corpse goes to the nearest Graveyard tile and is consumed. The destination is settled before the walk starts.
- `DyingTicks` counts the rescue window while `Dying` and the corpse window while `Dead`; reset at the seam.
- Both ends stored: `CarriedCreatureId` on the carrier, `CarriedByCreatureId` on the body; `TargetBodyId` is the body a worker walks towards.
- At acceptance: no prison then (Q163; ADR-0064); the corpse clock did not pause in an imp's arms (Q164, UNKNOWN in the original); a graveyard kept no tally until ADR-0151; nearest by walking (Q25) where the original scans rings.

**Consequences.**
- A creature that leaves a body no longer vanishes; four tests that asserted vanishing now assert the corpse.
- `HashVersion`/`FormatVersion` 22 → 23. `CreatureDeathRules.BodyWindowTicks` has its first caller.
- Fixture gains the Graveyard (terrain 23, room 14, 2000 to build). Three animation keys in `WorldView.ActivityKeySegment` (a switch; a new activity does not compile without one).
- `CarryingTests`; `an_imp_buries_a_corpse`; `a_goblin_collapses_and_dies` runs to t2419 and shows the corpse rotting.
- Open then: prison and capture (ADR-0064); kind 8 (ADR-0065); what a graveyard does when it fills (vampires).

## ADR-0064 — The Prison: capture, and the half of carrying that was missing
**Date:** 2026-09-05 · **Status:** accepted · amended 2026-09-13: full-prison capacity is checked at delivery; arrival health is `MaxHealth / 6` (ADR-0071); capacity is interior + 1 (ADR-0070); the drain is hunger (ADR-0068); prisoners wander and eat (ADR-0073, ADR-0074); a captive takes no need-anger (ADR-0079); **the Temple is the release rule's one exception (ADR-0154)**

**Context.** Owner (2026-09-05): *"a corpse can not be picked up by hand of evil and dropped to a graveyard, only an imp can carry it to its grave, also an injured creature, lying on the floor bleeding out, can't be picked up by hand of evil and dropped to its lair, only an imp can carry it to its bed to recover, likewise only an imp can drag an enemy creature to prison, a hand of evil can then pick up that creature from prison and drop it around the dungeon, back in prison, or onto a torture rack in the torture chamber."* The pick-up handler refuses bit `0x200000` of the first state-flag word, which DK2's creature dump names `CreatureDying` — so ADR-0062's refusal was DK2's rule. Research: `docs/research/prison.md`, `imp-task-priority.md` §4, `manual-1999.md` §Prison.

**Decision.**
- `PrisonRules`, `PrisonSystem`, `CreatureActivity.Imprisoned` (= 13), `CreatureState.PrisonOwnerPlayerId`, the enemy branch of `CarryRules.TryDestinationFor`.
- Capture is an imp's job only. `HandRules.CanPickUp` refuses anything that is down — corpse or dying, anybody's. The hand's role is moving a prisoner an imp has already taken.
- `PrisonOwner` is a second owner, not a change of owner (DK2 creature field at `0x3cb`). A prisoner is still its keeper's creature; dropping it back in a prison re-imprisons it, anywhere else frees it.
- Prison choice prefers one with space over a full one. If all are full, an imp still drags the enemy to a prison, then leaves it bleeding on the rim (owner play, 2026-09-13). Tie-breaking and the exact rejection tile remain UNKNOWN. Ours incorrectly requires a free cell before starting, so it omits the all-full journey.
- `CreatureHealthSystem` skips prisoners; `PrisonSystem` runs immediately after it, same cadence and phase: free anyone whose cell is no longer a cell (sale, dig, rival claim, destruction — derived each second, no hooks), roll an overcrowded prison's jail break, wither everyone left by `PrisonModifyCreatureHealthPerSecond` (−3). A prisoner that runs out is replaced where it stands by the Prison's `CreatedCreatureId` — 9, the Skeleton — belonging to the jailer. No collapse, rescue window or corpse.
- Ours: one prisoner per tile (superseded by ADR-0070); arrival health is a sixth (ADR-0071). Jail break rolls once a game second (Q168). Prisoners are freed where they stand rather than at DK2's door. There is no keeper-set door bar, torture rack or Combat Pit effect. A prisoner is not slappable.

**Consequences.**
- Q161 answered against what this project believed; two tests assert the hand's refusal directly.
- `HashVersion`/`FormatVersion` 23 → 24.
- Fixture gains the Prison (terrain 20, room 11, 750 to build) and the Skeleton (creature 9, no needs — the control against the Goblin). `RoomDef.R()` transcribes `CreatedCreatureId`.
- `PrisonSystem` reads its balance variable inside the prisoner loop so a level with no prison does not demand it.
- `PrisonTests`; a real-data test pins the two variables and the Skeleton; `an_imp_drags_an_enemy_to_prison`.
- Open then: torture and conversion; chickens for prisoners (ADR-0074/0075); combat.

## ADR-0065 — Kind 8: an imp puts a book back on the shelf
**Date:** 2026-09-05 · **Status:** accepted · the third of DK2's carrying tasks after ADR-0063 and ADR-0064

**Context.** Tier A is `14, 8, 9, 1, 3, 13, 12`; kind 8 (carry a loose object to its room) was recorded as "nothing to act on". An object needs a room, a room needs a list of its objects, and `Rooms.kwd` has carried that list since Q120. Research: `docs/research/objects.md`, `imp-task-priority.md` §4.

**Decision.**
- `ObjectCarryRules`, activities `FetchingObject` (= 14), `CarryingObject` (= 15), one new Tier A entry — second, above marked excavation and claiming (the original's order; a test fails if it is moved down).
- The carry slot is not the drag slot: DK2 names `CarryingId` at `0x3c3` and `DraggingId` at `0x3c5`; an imp cannot hold a book and a body at once.
- The room → object mapping is read backwards: each room lists its objects (Library 64 and 58, Workshop 16/15/46, Prison 49/50/51, Graveyard 31–34); `HomeRoomFor` walks the rooms. The original goes from the object's definition, so OBJECTS presumably has an unidentified room field (second pair read from the wrong end, after the bed's owner Q130).
- Delivery makes the object the keeper's; "loose" means owned by nobody. No "shelved" flag.
- A worker that stops being a worker (picked up, killed) puts its load down where it stands, still loose. Fixed a latent ADR-0063 bug: an imp lifted while dragging a body kept hold of it and the body stopped being anybody's problem.
- Ours: one object per tile (DK2 stores `UsedObjectCapacity` on the room and `RoomCapacity` per object definition, unread); the first room in table order wins an ambiguous object (Q169 — 49 and 50 are listed by both Prison and Combat Pit); own floor only, not "own floor or water" (Q170).

**Consequences.**
- `HashVersion`/`FormatVersion` 24 → 25.
- Fixture gains the Library (terrain 15, room 6) and object 64, Book Case; `R()` transcribes `ObjectIds`.
- `ObjectCarryingTests`. Kind 8 acts on chickens once the Hatchery lays (ADR-0066).
- Open: kind 9/10 (fetch a crate from the Workshop for a placed trap or door — needs traps, doors, manufacturing); Tier B's loose-object question (the ring scan's third question) — only the whole-map Tier A kind is built.

## ADR-0066 — The Hatchery lays, and the blocker had already been removed
**Date:** 2026-09-06 · **Status:** accepted · Research: `docs/research/hatchery.md` §4, Q132, Q134 · Scenario `scenarios/rooms/a_hatchery_lays_and_hatches.yaml`

**Context.** `HatcheryRules` knew coops per room (`floor(tiles / 6)`, capped at 36), their spacing (never two within squared distance 3), that the room lays an Egg (object 47) at a random coop which becomes a Chicken (object 9) after two seconds, and one bird per tile — but had no clock, "blocked on Q132" (a multiplier no shipped file supplies). Q132 was answered 2026-09-05: the multiplier is variable 19, ticks per game second, supplied by the level header; all 66 shipped levels carry 4. Fifth instance of a claim nothing checks: a stated blocker already removed.

**Decision.**
- `HatcheryRules.LayIntervalSeconds`, `HatcherySystem`, two fields of state.
- Interval, read from the original: `interval = (int)( ChickenGenerationTimePerHatchery / pow(tiles, 0.315477776) ) * V19`, returned in whole game seconds; the caller converts to ticks (truncation before the multiply).
- A fixed-point table rather than `Math.Pow`: eggs are hashed and IEEE-754 `pow` is not bit-identical across runtimes. `HatcheryClockTests` regenerates all 256 entries from `Math.Pow` and compares.
- The 3×3 quirk is reproduced: `pow(9, e)` = 2.0000566, so `16 / 2.0000566` = 7.99977 truncates to 7, not 8. `hatchery.md`'s table says 7.
- State: rooms are derived (`RoomInstance`) and DK2 keeps `GameTickLastGenerated` on the room object, so the laying clock is on the room's lowest tile index (`TileState.HatchTicks`, following `EntranceIdleTicks`) and each egg's countdown on the object (`ObjectState.TimerTicks`, where DK2 keeps it in the union). A hatchery whose lowest tile changes restarts its clock (at most one interval).
- Hatching runs before laying, so an egg is not aged on the second it is laid.

**Consequences.**
- Kind 8 has something to carry. `HashVersion`/`FormatVersion` 25 → 26.
- `HatcheryClockTests`; the scenario runs a 3×3 from empty to full and shows it refusing twice.
- Open then: nothing eats (ADR-0067); free-range chickens — `MaxFreeRangeChickensPerPlayer` (12) caps birds outside a room in the original; nothing here takes one out.

## ADR-0067 — A creature eats, and the hunger clock stops saturating
**Date:** 2026-09-06 · **Status:** accepted · Research: `docs/research/eating.md` · Scenario `a_goblin_eats_a_chicken` · opens Q174 (answered by ADR-0072), Q175, Q176

**Context.** A creature has had a hunger clock since ADR-0048 with nothing to do about it (`a_goblin_gets_hungry` ends *"and then nothing"*). The binary was read: `HungerState.cpp` and the creature state table (recorded then as 239 records; it has 281 — ADR-0072, which does not touch this decision).

**Decision.**
- `EatingRules` + `CreatureEatSystem`, following the original's states one for one: walk to the room, choose the bird on arrival, walk to the bird, eat it.
- The hunger clock accumulates: `ChickensNeeded` (set to `HungerFill` when the period elapsed) becomes `HungerNeed`, DK2's own name, with `HungerNeed += HungerFill` every period, capping nothing. A creature with nowhere to eat owes every helping it missed.
- Two tests, not one: setting out needs `HungerNeed >= HungerFill`; carrying on eating needs `HungerNeed > 0` (the original computes `HungerNeed != 0` the instant a bird goes down).
- One bird fills a prisoner: the hatchery path subtracts a literal 1, the prison path the level-scaled `HungerFill` — a caged Bile Demon is filled by one chicken where a free one eats five. `EatingRules.EatInPrison` carries the number (caller: ADR-0074).
- Nothing is reserved: the chosen bird's id lives on the creature only; two hungry creatures may pick the same one and the loser searches again (as the owner watched prisoners do, Q172).
- Hunger is asked before sleep: `CreatureEatSystem` is registered above `CreatureSleepSystem`, as the original's needs routine asks hunger first and returns early.

**Consequences.**
- `HashVersion`/`FormatVersion` 26 → 27. `CreatureState` gains `HungerNeed` and `FoodAttemptTicks`; activities `SeekingFood` (= 16), `FetchingChicken` (= 17).
- `CreatureStatId.HungerFill` = 9, 100 % at every level in the shipped table — applied anyway, with a real-data test asserting the row is flat.
- Fixture gains a Bile Demon (`HungerFill` 5, the only value that tells the two pay-off paths apart).
- `EatingTests`; `CreatureNeedsOnRealDataTests`. A hatchery that had stopped laying starts again because eating takes birds out (scenario t3919) — the first place two mechanics feed each other untold.
- `TypeFood` falls on the Torture Pendulum (65) as well as the Chicken (Q128). The hatchery search matches object id 9; the prison search matches the flag, so a pendulum in a prison is food to a prisoner by the original's predicate. `HatcheryRules`' comment "only the Chicken carries `TypeFood`" was wrong; a real-data test counts the flag; `eating.md` §6 states both predicates.
- Open: the post-meal wander (Q175); whether a sleeper wakes to eat (Q176 — conclusion survives via the group tag, ADR-0074). Q174 (how the original walks a hungry creature to the hatchery) was an off-by-one in the state frame — `CreatureEatSystem` matches the original's shape (ADR-0072).

## ADR-0068 — The prison drain is hunger, and a Skeleton never withers
**Date:** 2026-09-06 · **Status:** accepted · Research: `docs/research/prison.md` §4b, `eating.md` §10 · closes Q173

**Context.** ADR-0064's withering routine `FUN_0047b7c0` had no callers — its only reference a `MOV` immediate in a run of stores filling a table. Reading `HungerState.cpp` turned up the creature state table and behind it a table of 32 state categories at `0x00699688`, each with an enter, update and leave hook — the table those stores filled.

**Decision.**
- `FUN_0047b7c0` is category 14's update hook; category 14 is exactly the six states of `PrisonState.cpp`. No room-type test needed.
- Its gate is `HungerFill`: the drain fires only when the creature's level-scaled `HungerFill` is non-zero. The four creatures that never eat never wither; `PrisonRules.WitherPerSecond` takes the creature and returns 0 for one that cannot get hungry.
- The Skeleton is one of them: a prison turns a starved prisoner into a Skeleton, which is immune to the room that made it. A cell of skeletons is stable for ever.
- Owner (2026-09-05): *"probably from hunger"*; (2026-09-06): *"that was my speculation, I don't know … decompiled source is the top authority."* The binary says the speculation was right.

**Consequences.**
- One test in `PrisonTests` with both halves on one map: a Skeleton at its arrival health for sixty seconds, a Goblin not. `CreatureNeedsOnRealDataTests` counts the never-withering set against the install's `HungerFill == 0` set.
- No hash change (every prisoner in existing tests and scenarios is a Goblin).
- Not built: the four-second lump — the original reloads a counter with `ticksPerSecond << 2` and applies `PrisonModifyCreatureHealthPerSecond << 2` (−12 every four game seconds vs our −3 every one; same average); the same routine applies `ModifyAngerInCompanyOfHatedCreaturesPerSecond` (150) under reason 9 (reason channel unmodelled); prisoners could not yet move (ADR-0073).


## ADR-0069 — The aimed bonus is a task kind, and it lasts as long as that kind of work
**Date:** 2026-09-06 · **Status:** accepted · closes Q159, Q162 · opens Q177 · Research: `docs/research/hand-of-evil.md` §6

**Context.** Owner, off DK2's status bar: an imp dropped within 3 tiles of marked gold goes to 90% and *stays* at 90% through the walk and the dig, dropping back to 80% only if the mark is removed before it arrives; and *"timeout for efficiency to return back to 80% after slap is 25s, not 60s"*. The second closes Q159 with no code change — 25 s is what ADR-0062 built from a hardcoded 100 ticks in the slap handler, against a shipped `IncreasedWorkRateDurationFromSlappingSeconds` of 60 that nothing in the binary reads. The first cost three changes. Evidence: `ImpState.cpp`, `ImpUtils.cpp`.

**Decision.**
- **The bonus field is the task kind.** DK2's worker branch adds its ten points on `ImpJobData.DroppedTask`, a task kind, zero when there is none. `ImpJobData.JustDropped` — what this project had been reading — is a different field four bytes earlier and is a one-shot ticket the next job evaluation spends unconditionally. Both sit in the creature's state object, pinned at creature + `0x24E` (the creature debug dump hands that address to the state dump; two independent reads in the creature update land where that base puts them).
- **The look is a bounded scan with its own priority order.** A drop sets the ticket; the next evaluation clears it, skips its idle throttle, skips the ordinary priority chain, and runs a ring scan around where the worker landed. The predicate is nine task kinds in an order that is *not* the priority chain's — excavation, claim, crate, unconscious body, reinforce, gold, object, corpse — so a worker dropped beside a marked wall digs rather than shelving a book (second in the ordinary chain, ADR-0065). Kind 14, claim-the-just-dug-tile, is absent. `DroppedTask` is set only if that scan takes something.
- **Three tiles, and it is DK2's own off-by-one.** The scan is handed `0x2000`, a radius of two against a `0x1000` tile; its ring walk tests the bound *after* scanning a ring, with a strict greater-than, from a counter starting at 1 — so rings 1, 2 and 3 all scan. Reproduced, not fixed (`AGENTS.md`, Fidelity: a strange DK2 behaviour is reproduced with the deviation documented, never quietly corrected). The bound is measured to the tile the *work* is on, not the tile the worker stands on: a wall four away, reachable from a floor tile three away, still does not pay.
- **One clear in the whole binary,** at the top of a later evaluation: if `DroppedTask == LastTaskType`, retry `TakeJob(LastTaskType)` at unlimited range and return with the bonus intact; only on failure is `DroppedTask` zeroed. The bonus survives the walk, the dig and finishing the tile, and is lost only when a worker that still calls itself an excavator can find nothing left to excavate anywhere on the map — the owner's unmark case from the other side. It is *not* worth one job.

**Consequences.**
- `CreatureState.DroppedTask` (an `ImpTaskKind`, DK2's own numbering) carries the Efficiency bonus; `JustDropped` remains as the one-shot ticket; `LastTaskKind` is added because the clear is guarded on it.
- `HandSystem` decides nothing about the bonus: it sets the ticket for a worker and zeroes the idle timer. Its old "is there marked work beside this tile" test is deleted — wrong distance, wrong set of kinds.
- `ImpTaskSystem` gains `TryTakeDroppedJob` (the drop scan, in DK2's drop order, bounded to three tiles of the work) and `TryTakeJobOfKind` (the same-kind retry at unlimited range), plus the clear. `ClearJob` no longer clears the bonus — giving a job up is not the clear.
- Cost: `HashVersion` 27 → 28 and snapshot `FormatVersion` with it. Not defaults-only: the determinism fixture is full of imps taking jobs and every job taken now records its kind. No behaviour moved — nothing in that fixture is picked up, so the retry branch is never entered.
- Open: **Q177** — mark two blocks far apart, drop an imp on one, watch the status bar after the first falls. The binary says 90%; the older owner report (*"mines its first seam at 90% and the next at 80%"*) fits only if that seam was the last marked work on the map.
- Open, and the line that would be wrong: whether reinforcing really counts in the drop scan. It is one of the nine and it was read, but it is available almost anywhere a dungeon meets rock, which would make *"drop it on a random tile and it remains at 80%"* hard to arrange. This ADR assumes a drop inside a finished dungeon has no rock within three tiles, which the test fixture models. 90% from a drop next to a plain unmarked wall would refute it.

## ADR-0070 — A prison holds its inner tiles plus one, so a 3×3 holds two
**Date:** 2026-09-06 · **Status:** accepted · closes Q166 · opens Q179

**Context.** Owner, with a screenshot of DK2's status bar (`Captives held: 0 / 2 … Maximum Skeletons: 2`): *"minimum usable size is 3x3 tiles, that gives capacity of 2 prisoners … Any smaller size and it doesn't render like a prison, it only occupies those tiles."* This project held one prisoner per tile — nine on that room, a four-fold error, and everything that counts prisoners was downstream of it (delivery, overcrowding, the jail-break roll, Skeleton count).

**Decision.**
- DK2's room-capacity routine switches on room type; the Prison is case 8 (type 11 − 3). Unlike a lair or treasury, which walk the room's *object* list and count furniture, the prison walks its own **tiles**, counts those a general "nothing is standing here" predicate accepts, **adds one if that count is non-zero**, and stores the result in the field the room debug dump calls `SafeCreatureCapacity`.
- A prison's bars and gate take the rim, so a 3×3 leaves one tile: count 1, capacity **2**. Below 3×3 no such tile exists, the "+1" never fires, and the room has no capacity — the owner's second sentence is the same arithmetic reaching zero, not a separate rule.
- `TotalCapacity` is left at **−1** for a prison: the case `break`s without touching it and the sentinel survives. The generic room-occupancy check therefore does not apply to a prison, which is *why* a keeper can overcrowd one by hand. Manual overcrowding (ADR-0064) turns out to be a consequence rather than a rule of its own.
- `PrisonRules.Capacity(prison)` = interior tile count + 1, and 0 when there is no interior. `RoomInstance` already counted the rim for the Temple (Q60), so no new geometry was needed.

**Consequences.**
- Capacity stops being a per-tile rule deliberately: two prisoners on a one-inner-tile room is DK2's arithmetic, and smoothing the "+1" away would fit the model to our habits. `FreeTileIn` still puts each arrival on its own tile, because that is where a body is laid down — it is not what limits the room.
- `PrisonTests` used a 1×3 column of cells, which under the corrected rule is not a prison at all; the fixture is a real 3×3 with paved floor around it, and the scenario's prison likewise.
- Opens **Q179**: the Prison's `RecommendedSizeX/Y` is 5×5 and a 3×3 works, so Q45's reading of that field as "the size below which the room does nothing" (fitted on a 3×3 graveyard and a 5×5 temple) now has a counterexample. Recorded rather than resolved per the conflict protocol — nothing depends on it: `IsFunctional` is read only by `PortalRules`, and a prison's capacity comes from its own geometry.
- Ours, unread in the original: whether an L-shaped prison's rim is furnished the same way (we count the rim rather than compute it from the bounding box, so an L-shape answers sensibly; for rectangles the two agree). Maximum Skeletons equals capacity here because a Skeleton replaces the prisoner that died; that DK2 gets the same number *for the same reason* is inference.

## ADR-0071 — A knocked-out creature has exactly one sixth of its health, and DK2 says so
**Date:** 2026-09-06 · **Status:** accepted · closes Q167 (health half) · supersedes the 20% in ADR-0064

**Context.** A prisoner's arrival health was guessed three times: full health (500 game seconds for a Goblin to wither); 20% of maximum, after *"its flower gets two petals"* met a shipped `CreatureCriticalHealthPercentageOfMax` of 20 (100 s); and now a sixth of maximum, read from the binary (83 s). Both legs of the second reading failed the same day — the flower has **six** petals, not ten (Q171, owner screenshot), and a raw `.text` scan for that variable's cached global finds exactly one reference, the loader writing it. The two errors cancelled to something plausible: 20% sits inside the 2-petals-of-six band, so the guess looked confirmed from both sides while resting on nothing.

**Decision.**
- DK2's `ModifyHealth` applies the delta, returns early if the `CreatureDying` flag is already set (so a second hit can kill), and otherwise, on health below zero, sets `health = maxHealth / 6` (level-scaled maximum) and calls `KnockOut()`, which sets `CreatureDying`. The sixth is computed as `100 * max / 6 / 100`, two magic-number divides — the arithmetic of "a sixth expressed as a percentage", and the clearest sign the sixths and the six petals are one decision. Some creature types are destroyed outright instead: the `DieInstantly` flag this project already reads.
- Imprisonment does not touch health at all. The prison's category-enter hook sets the drain countdown and nothing else; a prisoner arrives carrying whatever the knockout left it.
- `CreatureHealthRules.CriticalHealth` becomes `KnockoutHealth` = `MaxHealth / 6`, with `KnockoutFraction = 6` named beside it. `PrisonRules.Imprison` is the only caller and keeps calling it: DK2 sets the value at the collapse rather than at the prison door, and for the only path DK2 allows into a cell the two coincide.

**Consequences.**
- `IsCriticallyHurt` keeps reading the shipped variable and keeps having no callers, now noting that DK2 does not read it either.
- No hash change (the determinism fixture imprisons nobody). Two prison tests moved from 300/100 s to 250/83 s; the scenario's prisoner perishes at t2619 instead of t2939.
- Loose end, and it is a display question: a sixth is exactly one petal of six and the owner counts two on a freshly dragged-in creature. Either the flower is not a plain proportion of six, or something between the knockout and the cell adds a little. Neither moves the health number, and the residue belongs to whatever draws a flower, not to `PrisonRules`.

## ADR-0072 — The state table has 281 records, and the 239 was a scan window
**Date:** 2026-09-06 · **Status:** accepted · Research only, no code change · Research: `docs/research/creature-state-table.md` §5.4

**Context.** The creature state table's record count was left explicitly unsettled ("239 was counted under the shifted frame; Flame says 281; don't quote 239 without re-deriving it"). It is **281**. The frame shift — base `0x006DA8B0`, not `0x006DA8C0`, which had put every handler one state late — was a separate defect and did not move the count. The count was wrong because `eating.md` recorded the recovery method as "list every write into `[0x006DA000, 0x006DC000)`" while the table runs to `0x006DC420`: record 238 starts at `0x006DBFFE` inside the window and record 239 at `0x006DC017` outside it, so the walk stopped at a boundary belonging to the scan. The published code range carried the same truncation — the fill was recorded as ending at `0x0047FAEB`; it ends at `0x004805E4`.

**Decision.** Six checks, no two sharing a failure mode:
1. **Offset histogram.** Every reference into the table's address range falls on exactly nine offsets modulo 25, no strays — recovering stride and frame without assuming either.
2. **Exact accounting.** 2,529 writes = 281 × 9; every field of every record written exactly once.
3. **Contiguity.** All nine fields span records 0–280 with no gaps.
4. **Self-consistency.** 277 records write their id as a literal and each equals its index; the four using a register are records 0, 1, 3 and 8, and the smallest literal in the table is 2 — exactly what remains once those are removed.
5. **The labels resolve.** All 281 `+0x14` values are valid `Text.str` ids.
6. **An outside reading agrees on the numbering, 58 of 58** (2026-09-06). Flame's map names 58 of the 264 handlers in a form carrying the state's decimal id; every one equals our record index, none disagrees. This raises confidence rather than adding a class of evidence — Flame is a reading of the binary, not the binary.

**Consequences.**
- Every state is named: the `u16` at `+0x14` is a `Text.str` string id resolving to the activity label the game prints on the flower over a creature's head (the line the owner read as *"reinforcing walls"*, Q171). All 281 resolve; 70 distinct labels.
- The two bytes are different things. `+0x16` is a **group tag**, only ever compared for equality (which is why it holds 50 and 100 without being out of range); `+0x17` is the **category**, indexing the 32-entry table at `0x00699688`, and its range across all 281 records is 0–31.
- **Q174 is answered, same off-by-one.** The original does walk a hungry creature to the hatchery: `FUN_004633d0` calls `SetState(0x5B)`, and `0x5B`'s handler finds the room and calls `MoveTo(target, resume = 0x5C)`. Under the old frame that handler was filed under `0x5A`, which is near-unreferenced, so a working walk looked orphaned. `CreatureEatSystem` matches the original's shape; ADR-0067's stated-composition caveat is withdrawn.
- `docs/research/creature-state-table.md` is the table's home and carries all 281 records. `eating.md` §2.1 keeps a superseded marker and a corrected summary, because that is where a reader who followed ADR-0067 looks first.
- No simulation code changed and no hash moved: nothing in the project ever read a record count.
- **The rule this leaves.** In both errors the thing that would have caught them was the thing that produced them: a fixed-stride table read at the wrong offset yields plausible values at every field, and a table counted with a scan window yields a plausible count at whatever boundary the window has. Pin a table's *frame* from the code that indexes it and its *extent* from something other than the scan — the next referenced global, an exact field-count identity, or a value that must be valid elsewhere.
- Legal, flagged not assumed: the new document reproduces 70 short UI strings from the install's `Text.str` as state labels. They are research documentation and the code loads them at run time from the user's own install, so nothing is redistributed. If judged too far, the `Text.str` id column alone keeps the table usable without re-deriving anything.

## ADR-0073 — A prisoner shuffles about its cell, one orthogonal tile at a time
**Date:** 2026-09-06 · **Status:** accepted · amended by ADR-0074: state 117 is a search and a walk, not eating where it stands · Q172, first of three

**Context.** The owner watched a prison on 2026-09-05 and reported three things: a prisoner moves about its cell, prisoners race each other to a chicken dropped among them, and one of them eats it (Q172). The project's answer was to do none of it — `CreatureMovementSystem`, `CreatureSleepSystem` and `ImpTaskSystem` each carried the same paragraph saying so. This builds the first.

**Decision.** Under the frame corrected by ADR-0072 the prison is **states 114–126**, all category **14**; three are the loop.
- **114** (`0x72`) arrival: finds the room of type `0xb`, checks it with `FUN_00454110` (the same tile predicate the capacity routine counts with), records the jailer, enters 115.
- **115** (`0x73`) the decision: if hunger clears a threshold *and* a cooldown has elapsed, go to state 117 (the food search and walk — ADR-0074); otherwise ask `Room.cpp:1717` for a neighbouring tile, issue a move to it, and enter 116.
- **116** (`0x74`) the walk and the pause after it: each tick roll `random(100)` and return to 115 when the roll is under **20**.
- **The step, read from disassembly** (Ghidra garbles `Room.cpp:1717`, typing the random result as a struct pointer): take a neighbour **count**, draw `random(count)` as a **starting index**, then scan `(start + i) % count` forwards through a table of offset pairs — the four orthogonal offsets at `0x006BC350` when the count is 4, the eight at `0x006BC370` otherwise. The prison passes 4. The first candidate that passes wins; a candidate needs a non-zero room id, the **same room instance** the creature stands in, and a clear terrain flag.
- Table order is behaviour: north, east, south, west. It decides which way a prisoner goes when its drawn direction is a wall.
- `PrisonRules.TryWanderStep` carries the rule (four offsets in DK2's order, the wrap, the same-room test); `PrisonerWanderSystem` runs it every tick — a prisoner standing still rolls `WanderPercentPerTick` = 20 and steps on a hit. The two draws happen in the original's order, the hundred before the direction, so the random stream is consumed the same way round. Registered **after** `PrisonSystem`, so a prisoner freed or turned to bone this second is not handed a step it will never take.

**Consequences.**
- The room-instance test *is* the containment: nothing in DK2 says a prisoner may not leave the prison — a tile outside the cell is simply never a candidate. `CreatureMovementSystem`'s blanket refusal is therefore deleted rather than narrowed.
- `MotionlessInPrisonSeconds` (shipped value 8) is dead: a raw `.text` scan for its global `0x006F25E9` returns one hit, `MOV [0x006f25e9],EAX`, the setter stub. Nothing in DK2 reads it, and the "presumably how long one stands still between moves" note built on it is gone. Third shipped balance variable found dead here and the second to have had a mechanic built on it (`CreatureCriticalHealthPercentageOfMax`, ADR-0071; `CriticalHealthPercentageForHeroLair` is the third). A named variable in the table is not evidence the engine consults it, and the check costs one scan.
- No hash change (the fixture imprisons nobody, and the system draws no randomness with no eligible prisoner). Two tests changed and both had been asserting the gap: `APrisonerDoesNotWalkWorkSleepOrGrowHungry` lost the two lines asserting a prisoner never leaves its arrival tile and never holds a path, and lost the *Walk* from its name; `APrisonerThatWithersAwayRisesAsASkeletonForItsJailer` now tracks where the prisoner actually stood rather than the arrival tile.
- The scenario shows it: between imprisonment at t959 and perishing at t2619 the Goblin takes 50 steps and stands on all nine tiles of the 3×3, never leaving, and rises as a Skeleton at (7,5) rather than the (5,3) it arrived on. The clock is untouched.
- Left of Q172: the bird, tracked as **Q195** (renumbered from Q187, which collided with the save-format session's allocation) — now waiting only on a keeper being able to drop a chicken into a cell. Every other piece exists, including `EatingRules.EatInPrison` (one bird pays off the whole of `HungerFill`) and the original's search, state 117's `FUN_00473550`. **Q188** records the one unread branch: a second path in state 116 halving the step roll to 10 in 100 when a per-creature flag is set.

## ADR-0074 — A prisoner gets hungry, hunts the cell, and races the others to the bird
**Date:** 2026-09-06 · **Status:** accepted · Q195 and the rest of Q172 · Research: `docs/research/prison.md` §7

**Context.** The other two of the owner's three sentences: *"they rush toward that chicken, the first one to reach it eats it, and its health replenishes."*

**Decision.** ADR-0073's "state 117 eats where it stands" was written from the transition alone; the handler gives three states.

| State | What it does |
|---|---|
| **115** | Hungry **and** past `TimeBetweenReattemptingHungerSeconds` → stamp the clock, go to 117. Otherwise wander |
| **117** | Walk the prison's own tile chain, then each tile's object list; keep every object of kind 2 whose definition carries flag `0x80`. Rank by **straight-line squared distance**, take the smallest, issue a move to it resuming in 118. Found none → apply `AngerNoFood`, tell the interface, drop back to 116 |
| **118** | A food-flagged object underfoot: destroy it, pay off **the whole of `HungerFill`**. Still owing → 117. Nothing there → also 117 |

- **The race is the absence of a reservation.** Nothing marks the chosen bird — the chooser writes it on itself and nowhere else — so two hungry prisoners pick the same chicken and the loser arrives at an empty tile, falls through 118's loop into 117 and looks again. Same absence as the hatchery path (ADR-0067).
- **One bird fills a prisoner.** 118 passes the level-scaled `HungerFill` where the hatchery's state passes a literal 1, so a caged Bile Demon owing five helpings is filled by one chicken. `EatingRules.EatInPrison` had this two sittings before anything could call it.
- **The anger is a quarter of the tabled figure.** The creature loader divides every `angerNo*` field by the level's ticks per second as it loads it — pinned by the identical register multiplying `TimeAwake` and `TimeSleep` into ticks two dozen instructions earlier — so the engine holds anger per DK2 tick. A routine adding one per tick delivers the tabled figure a second (the lair's case, and what the owner measured). State 117 adds one per *attempt*, and attempts are gated to one a game second, so a hungry prisoner in an unstocked cell angers at **25 a second where a bedless creature angers at 100**.
- **A prisoner does get hungry**, answering part of Q185: the needs routine suppresses the hunger clock on `flags & 8` of the state record's byte at `+0x18`, and bit `0x10` beside it suppresses tiredness. The fifteen states that set them are exactly drinking, gambling and dancing — the Casino, not the Prison. Every prison state has both clear.
- `PrisonerEatSystem` runs the three states, registered **before** `PrisonerWanderSystem` because the original's single handler asks about hunger first. `EatingRules.NearestFoodIn` is the flag-based search, kept separate from the hatchery's id-9 one because the two are genuinely different sets — a Torture Pendulum is food in a prison and not in a hatchery.

**Consequences.**
- `SimulationTiming.OriginalTicksPerGameSecond` = 4 exists because of the anger arithmetic: four is a constant of DK2, in the same class as a balance variable, and using our own `TicksPerSecond` there would make the number an artefact of a separate question (Q1).
- A prisoner stays in `CreatureActivity.Imprisoned` throughout, marked only by `TargetObjectId`, because 117, 118 and 119 carry the same label, group tag and category as 114, 115 and 116. That is why `PrisonSystem` keeps draining a prisoner walking to its dinner, and why a keeper can lift one out mid-stride.
- `CreatureHealthSystem`'s blanket skip of prisoners is narrowed to health; it used to take their hunger clock with it, a side effect of ADR-0064 rather than a rule.
- Reachability: a prisoner never gets hungry *in* a cell — a Goblin's `HungerRate` is 180 game seconds and the drain turns it to bone in 83 — but `HungerNeed` accumulates over a creature's whole life and is paid off only by eating, so anything dragged in after a few minutes on the map is hungry when it lands. The tests set that up rather than waiting for it.
- Corrections carried: `eating.md` §2.2 was still on the pre-ADR-0072 frame and every state id in it read one too low (handler addresses were right); the five hatchery states are `eating` and the six prison ones `being held captive`, the cheapest check that the frame is straight. The needs routine's first gate is the **group tag**, not the category — group 7 is exactly the fourteen states that *are* a need (lair-seeking, sleeping, eating, pay), so the rule reads *a creature already attending to a need does not reconsider its needs*; **Q176's conclusion survives and its explanation does not** — a hungry creature still does not get out of bed, because sleeping is one of the fourteen.
- Divergence, named: `FUN_004633d0` routes on the room, not on imprisonment — it asks which room the creature stands in and jumps to the prison search when it is type `0xb`, without asking whether the creature is a prisoner, so in DK2 *any* hungry creature standing in a prison eats off the floor rather than walking to a hatchery. Ours is prisoner-specific; nothing observable turns on it, since only prisoners loiter in prisons. And the route to the bird is restricted to the cell's own tiles, where DK2 issues an ordinary move and relies on the bird being in the room the prisoner stands in — ours would otherwise route an L-shaped prison's corner through the corridor outside, which would look like an escape.
- No hash change; the system draws no randomness. `APrisonerDoesNotWorkSleepOrGrowHungry` reversed its answer and became `APrisonerDoesGrowHungry`, carrying the arithmetic for why no prisoner reaches a full helping inside a cell.

## ADR-0075 — The Hand of Evil holds a thing, not only a creature
**Date:** 2026-09-06 · **Status:** accepted · amended by ADR-0077: the *creature* drop is restricted to owned ground · Q196

**Context.** ADR-0074 left the prisoner's meal complete and unreachable in play: a chicken only gets into a cell because a keeper puts it there, and `PlayerState.Hand` held creature ids. Owner, same day, unprompted: *"I can pick up a chicken and drop it in the prison, or anywhere in the dungeon, on my owned tiles"* — with a screenshot of four chickens held at once as four separate slots, and the drop offered on a Claimed Path whose tooltip reads *"Your territory"*.

**Decision.**
- DK2's hand is one array of **thing ids** on the player record, with a count and a capacity beside it; both handlers branch on the thing's kind, so four chickens and a goblin share the slots. Our creature and object ids are separate spaces, so a slot carries which kind it is — `HandItem`.
- **Last in, first out**, now an observation as well as an inference: the owner confirmed it and the drop handler reads back the slot it is about to free. It holds across kinds, because there is one array.
- **What may be lifted is `CanBePickedUp`** — 26 of the 145 OBJECTS records: the three golds, the Chicken, Call To Arms, all fifteen specials and six odds and ends. Absent: spell books, crates and creature beds. A keeper does not hand-carry a finished trap to a workshop; an imp does.
- That flag has **exactly one reader in the executable**, and it is not the pick-up handler: it is the cursor's target search, which sweeps the 3×3 tiles around the point, measures with an **octagonal approximation** (`max + min/2`) rather than true distance, allows an object within a quarter tile where a creature gets half, admits the **Dungeon Heart** by id regardless of the flag, and returns a creature in preference to an object whenever both are in reach. None of it is here, because none of it is simulation.
- `PlayerState.Hand` becomes `List<HandItem>`; `ObjectState` gains `InHand`, the object half of `CreatureState.InHand`, and a held object is out of the dungeon — nothing may find, eat or decay it. `DropCreatureCommand` becomes **`DropCommand`**, because DK2 has one drop that branches on what it read back. `PickUpObjectCommand` joins it.

**Consequences.**
- **The near miss.** `CanBeDroppedOnAnyLand` is set on exactly one record — Call To Arms — which is exactly the object you would expect to be exempt from an owned-ground rule, and it was one edit away from being the drop rule. It is tested nowhere in the executable: a raw scan for the mask against the definition's flag dword returns nothing. The name is OpenKeeper's reading of a bit, and the fit is a coincidence of naming. Fourth dead data field found here, after `CreatureCriticalHealthPercentageOfMax` (ADR-0071, a wrong prisoner clock for a day), `CriticalHealthPercentageForHeroLair` and `MotionlessInPrisonSeconds` (ADR-0073). The rule that catches all four: scan the field and count its readers before building on it.
- The owned-ground rule is reproduced **as an observation**; where it lives in the original is unread, since the drop handler contains no ownership test, which puts the gate in the input layer that draws the marker.
- Read and deliberately not built, both needing a target our drop cannot name: a chicken dropped into somebody else's Hatchery becomes theirs (the handler tests object id 9 against a tile owned by another keeper in a room of type 4 and reassigns the bird); and a chicken or a pile of gold dropped **onto a creature is earmarked for it** — the handler writes that creature's id into a field on the object when the creature's level-scaled `HungerFill` (or `Pay`, the field at `def + 0x69a`, which the loader fills from record `0x0F1C` and `kwd-creatures.md` calls `u16 pay`; the same field and level-percentage table drive the wage in `payday.md` §3) is non-zero. That is a reservation, and the one case where a bird is not up for grabs. ADR-0074's prison search does not consult the field, so a race for an earmarked bird would still be a race; whether anything else consults it is unread.
- The scenario harness gains `setup.objects` and `setup.creatures.hunger`, both named as shortcuts in the run report. The second is not a convenience: stating the hunger a creature accumulated *before* capture is the only way a scenario reaches the mechanic.
- Cost: `HashVersion` 28 → 30, skipping 29, and `FormatVersion` with it. Both are composition: the fixture's keeper picks nothing up, so no slot's kind byte is written, and the world holds no objects, so `InHand` is never hashed for one — a field added to an empty list still has to change the hash. 29 was skipped on purpose because Codex's fog-of-war branch claimed it in its own worktree (ADR-0076); two branches holding one version number is exactly the failure a version number exists to catch.
- `AMixedHandSurvivesASnapshot` found a real gap rather than confirming one: `ObjectState.InHand` was hashed and not snapshotted.
- `a_keeper_drops_a_chicken_in_a_prison` runs the whole loop: two goblins dragged in by imps, angering at 25 a second in an empty cell, a keeper lifting a bird off its own floor into the cell, and one prisoner eating for +375. The second prisoner loses a clock rather than a race — captured 50 ticks apart, the two once-a-second looks are ten ticks out of phase, the bird lands at 1401, the first looks at 1409 and eats it, the second's next look is 1419. The contested case (both choosing the same bird, one arriving at an empty tile) is pinned by unit test, because staging it in a scenario would mean choreographing the wander.

## ADR-0076 — Fog of war is simulation state, and it cost a hash bump
**Date:** 2026-09-06 · **Status:** accepted · Q190–Q194 · Built by Codex on `codex/fog-of-war`, merged after independent re-derivation · Research: `docs/research/fog-of-war.md` §8

**Context.** Brief 8 carried a Phase 0 stop rule: find out whether fog is per-keeper simulation state or per-client renderer state, and if the former, report and stop — because in `WorldState` fog enters `HashInto`, `WorldSnapshot` and the pinned cross-machine literal, while beside `WorldView` it costs nothing.

**Decision.**
- Phase 0 answered **simulation**, by the route Q190 predicted: DK2's world-save coordinator invokes the runtime map's save operation, which walks every tile and writes ten contiguous bytes of the live 16-byte tile record — including the byte whose bit 6 gates fog rendering. A save persists the simulation. The saved-game session had already seen three lanes of that 10-byte tile move between any two saves and recorded them as "consistent with visibility or fog state, and still UNKNOWN"; this is what they were. The owner reviewed the result and authorised continuing; the stop commit is retained in the branch's history.
- Accepted: per-keeper discovery on every tile, hashed and snapshotted; a monotonic circular reveal read from creature data rather than frozen as a constant; a keeper-specific `WorldView` that leaks nothing about a hidden tile; and the `RevealThroughFogOfWar` exception applied through the flag rather than an id list.
- Verified on merge from the shipped data with a tool written for the purpose rather than by reading the branch's tests back to themselves: `PerceptionRange` is `0x6000` for all 46 creatures — six tiles at 4096 to the tile — against `DistanceCanSee` and `DistanceCanHear` at `0x4000`, four tiles, for all 46, so there really are two ranges and the branch took the right one (this turns the owner's *"about 6 tiles"* into a number). Six terrains carry `RevealThroughFogOfWar` — Impenetrable Rock, Water, Lava, Gold, Gems and Edge of Map — exactly the six the branch named, which is why an unexplored map still shows a keeper where the gems are.

**Consequences.**
- Fog defaults to off, as a world and scenario setting, so nothing that existed before behaves differently — which is what makes the hash bump one on new composition rather than changed behaviour.
- `kwd creatures -s` was added to `dk2tools` for the range count, because a "they are all the same" claim is only worth anything counted.
- `HashVersion` and `FormatVersion` resolve to **30**, not 29: the branch took 29 in its worktree while ADR-0075 was taking it in the main tree, so master goes 28 → 30 across two commits with 29 spent on a branch.
- The branch report arrived as a repository-root `README.md`, which master did not have; its content is now `fog-of-war.md` §8 and this ADR, and the file is deleted.
- The branch's UNKNOWN list is kept intact in `fog-of-war.md` §8.3; the most important entry is that persistence proves the state belongs to the simulation but does **not** prove any AI reads it — Q190's second half is open.
- Q192 is open: `All Owned Reveal`, `Re-shroud` and `Re-fog` are real strings and the configuration value behind fog is boolean, so they are not one enum behind that value, and where they *are* selected is unread. The campaign's *Stay Revealed* is the one mode the owner has confirmed and the one mode built.

## ADR-0077 — A creature goes down on the keeper's own ground, and only a worker may go anywhere else
**Date:** 2026-09-06 · **Status:** accepted · supersedes a paragraph of ADR-0075's reasoning, not its decision · Q198 · Research: `docs/research/hand-of-evil.md` §8

**Context.** Owner, 2026-09-06, CONFIRMED in the original: *"imps are the only creature that can be dropped onto dirt path tiles, those are tiles that are unclaimed by any keeper"*. `HandSystem.Drop` refused exactly one thing, a tile the creature could not have stood on, so a keeper could put a Goblin down on unclaimed dirt, on a rival's claimed floor, or in the middle of somebody else's dungeon — a wrong rule, wrong since the hand was built (ADR-0062).

**Decision.** `HandRules.CanDropCreatureOn(world, database, tile, playerId, mover)` allows a drop when
1. the tile is on the map, and
2. its owner is the dropping keeper, or
3. the creature carries `IsWorker` **and** the terrain is unclaimed floor.

- `HandRules.IsUnclaimedFloor` is structural: not `Solid`, not `Ownable`, not `Room`, and neither `Water` nor `Lava`. In the shipped table of 41 terrains that is **Dirt Path and the Mana Vault**, which carry the identical flag word `0x10008000`, and whose claimed counterparts — Claimed Path and Claimed Vault — are `Ownable`. That pair is what makes "unclaimed" mean something.
- The walkability gate stays and runs first, so the two refusals stay distinguishable in the event log: `not_walkable` and `not_owned`.

**Consequences.**
- The inference this replaces: ADR-0075 and `hand-of-evil.md` §8 argued the drop was not restricted to owned ground because DK2's handler has a branch that changes the creature's owner when the tile belongs to neither keeper, which would be unreachable if the drop were refused. It proves only that the handler does not refuse — already known and already written down for the *object* rule three paragraphs away (`FUN_004bc710` contains no ownership test, so the gate is in the input layer that draws the drop marker). A handler that does not refuse is not a UI that offers, and whatever reaches the owner-changing branch is not the ordinary drop. 14th recorded instance of a claim nothing checked; it differs in that the inference was a real argument from the binary that answered a different question than the one asked.
- Keyed on `IsWorker`, which is wider than what was watched: the owner said imps, and the flag falls on the Imp and the **Dwarf**. Keying worker behaviour on the flag rather than the id is the third such decision (`imp-tasks.md`, `portals.md`), and a hero keeper dropping a Dwarf is not yet reachable, so the widening costs nothing today.
- Water and lava are excluded by name, and that is a stop rather than a reading: both are unownable non-solid floor and would qualify structurally, and 44 of the 46 creatures can wade, so without the clause a keeper here could drop an imp into a lake. Whether the original offers that is **UNKNOWN** — item 5 on the observation checklist, with a bridge and a rival's unclaimed room floor. The test that pins it states that it is the narrower reading and what would change it.
- A prisoner is not a way round the rule: `CanPickUp`'s one ownership exception, lifting a captive out of a cell (ADR-0064), does not extend to putting one down, and a refused drop does not free the captive.
- Six tests in `HandTests`, one existing only to prove the imp's water refusal comes from this rule and not from the walkability gate above it.
- `scenarios/creatures/a_keeper_drops_a_warlock_and_an_imp_on_open_ground.yaml` — a Goblin refused on unclaimed dirt, the same Goblin landing on its own lair, and an Imp landing on the tile the Goblin was turned away from. The run turned up the reason DK2 has the exception at all: the dropped imp takes a **Claim** job on the tile it lands on, in the same tick.
- No hash change; a rule got stricter. `hand-of-evil.md` §8 and `HandRules.CanDropObjectOn`'s doc comment are corrected in place.

## ADR-0078 — Payday is a journey, and the money comes off the tile underfoot
**Date:** 2026-09-06 · **Status:** accepted · amended by ADR-0079: `AngerNoPay` is charged, and the nine-to-one conclusion here is retired by ADR-0081 · Q9 · Research: `docs/research/payday.md`

**Context.** The last of M4's original seven items, resting on ADR-0015 (no player gold counter) and ADR-0053 (the anger ladder). A keeper's wage bill falls due every `PayDayFrequencySeconds`; every creature it owns is credited `Pay` scaled by its level; each then walks to a treasury and lifts its own wages off the floor.

**Decision.**
- **Payday is a journey, not a transaction, and that is the original's shape.** DK2 gives collecting three creature states: one finds the room and issues the walk, one notices the arrival, one takes the money. The journey is where the behaviour lives — a creature can fail to arrive, arrive to find the tile empty, or be interrupted.
- **The gold comes off the one tile the creature is standing on.** The original's collecting state passes the creature's own coordinates to the withdrawal — not the room, not the player. This is the strongest independent confirmation of ADR-0015: payday is exactly where a global balance would be most convenient, and the engine still reaches for a tile.
- **Wages accumulate.** The accrual *adds* to the field rather than assigning it, so a creature that misses a payday is owed two, and a keeper who cannot pay builds a compounding debt.
- **An imp is unpaid because its record says `pay=0`.** Nothing in `PaydayRules` names the Imp; its wage computes to zero, so it is never owed anything and never goes to collect. Same shape as the Casino's fifteen states falling out of a flag byte rather than a list.
- **`SkipNextPayment` is a one-shot ticket, spent whether or not it was needed.** A creature holding it is not credited a wage at all and the flag is cleared in the same breath — exactly the manual's account of a treat (§7.6.2.2) buying off *"wages on the next Payday"*. Q9 had the name from Flame's symbol map and guessed a mood input; it is neither. Nothing sets it (keeper spells do not exist), so it is carried, hashed, and written by tests only.
- **The level scale is the shipped table's `Pay` row**, to 250% at level 10 — parsed since variables were first read and looked at by nothing (Q119); this is its first consumer.

**Consequences.**
- `PaydayRules` (interval, first payday, wage, paying-room test, single-tile withdrawal); `PaydaySystem` (clock and collecting, registered after the needs systems and before `CreatureAngerSystem`); `CreatureState.WageOwed` / `PayAttemptsFailed` / `SkipNextPayment`; `PlayerState.NextPaydayTick`; `CreatureActivity.SeekingWages`.
- Divergences, each stated: three states folded into one activity (`SeekingWages`), as `SeekingFood` folds the hunger walk. The arrival tile is the room's gold-bearing tile nearest the creature, because the original searches the arrival tile's *object list* for a gold pile and gold here is a field on the tile (ADR-0015) — ours makes the empty-tile case rarer, not impossible, since somebody else can empty the tile during the walk. The Dungeon Heart is admitted unconditionally where the original tests one undecoded flag bit beside the type test; the heart matters because a keeper's first treasury is bought out of it. `NextPaydayTick == 0` means "not scheduled yet" and the system seeds it, where the original seeds it in an initialiser and reads zero as *never* — `WorldState.FromMap` has no `GameDatabase` to seed from, and nothing needs *never*. A prisoner is not on the payroll: the original's loop walks the player's own creature chain and a captive has been moved off it, so our skip is explicit.
- **Not built here, and it is the important one: nothing charges `AngerNoPay`.** Both ends exist — the owner's consequence (Q9: unhappy, then angry, then leaves, or rebels if converted) and the anger ladder (ADR-0053) — and the connecting routine was not found in the payday path, so it was not invented. Wrong *visibly*: `wages_unreachable` fires twice per creature per payday and the scenario header says nobody gets angry about it. Standing hypothesis: the needs routine, in `AngerNoFood`'s shape. **Code now: `AngerNoPay` is charged on the debt alone (see ADR-0079).**
- `PayDayCutOffTimeSeconds` is unread. Q9 guessed a window for reaching the treasury; it is live (three references) but neither reader is in the payday path, which is evidence against that reading. Left UNKNOWN.
- 13 tests in `PaydayTests`, and `scenarios/creatures/a_keeper_meets_its_wage_bill.yaml` — two keepers, one with gold and one without.
- Two fixture lessons kept in the scenario header: with an imp per keeper, two imps spend twelve thousand ticks re-claiming every tile including each other's treasuries, and since gold belongs to whoever owns the tile the broke keeper ended the run holding the other's money (correct behaviour, useless scenario). And with no lairs every Goblin spent the run in the lair search and was never idle when payday fell — **a creature has to have somewhere to live before it can be paid**, because the collecting decision only fires on an idle creature, here and in the original.
- `HashVersion` and `FormatVersion` 30 → **31** on the composition (three creature fields, one player field); pin `0xC026542547419606`.

## ADR-0079 — Being owed is the whole trigger, and a captive is charged nothing
**Date:** 2026-09-06 · **Status:** accepted · amended by ADR-0081: the bed-versus-wage conclusion below is retired, the buckets are separate · closes the hole ADR-0078 left · Q9, Q185

**Context.** `FUN_00478d00`, reached from the creature update immediately *before* the needs routine, is DK2's need-anger routine.

**Decision.** It charges all three standing sources one after another and independently:

| Source | Condition |
|---|---|
| `AngerNoLair` (`def+0x6c1`) | no bed **and** the room type is available |
| `AngerNoFood` (`def+0x6c3`) | `HungerNeed` has reached the level-scaled `HungerFill` **and** a hatchery is available |
| `AngerNoPay` (`def+0x6c5`) | `WageOwed != 0`, and the creature's own level-scaled `Pay` is non-zero |

- `def+0x6c5` was pinned the way `Pay` was: the loader's anger block at `0x00493964` stores five fields in a row from consecutive frame offsets, and at the established `0x3F0` frame-to-record delta they land exactly on `0x0F28..0x0F30` — `angerNoLair`, `angerNoFood`, `angerNoPay`, `angerNoWork`, `angerSlap`. Side finding: the first four are divided by ticks-per-second and `angerSlap` is not, which is the per-tick/per-event distinction ADR-0074 inferred for the prison, visible directly in the loader.
- **`AngerNoPay` has no treasury clause.** Its two siblings each test that the room exists; the pay charge tests nothing but the debt. A keeper with no vault at all and a keeper whose creature has not walked over yet are charged identically, from the moment the clock runs until the money is in hand. You cannot excuse yourself by having no treasury — and it is why the wage accumulating (ADR-0078) matters twice: the debt is the anger source, and it persists.
- **`CreatureAngerRules.PerSecond` becomes additive.** It was an early-return chain, right while there was only one source; the original charges each independently, so a bedless unpaid creature takes both.
- **A prisoner takes no need-anger at all**, a behaviour change on work predating payday. The need-anger routine refuses outright when the current state carries bit 1 of the state record's flag byte, and all eleven "being held captive" states carry it. A captive's anger is the prison's business alone: it charges `AngerNoFood` per failed search for a bird and nothing else (ADR-0064, ADR-0074).

**Consequences.**
- **Advances Q185.** Bit 1 of the state flag byte is "do not charge need-anger in this state", and its 43 states read as one idea: being held captive (11), stunned (6), joining your dungeon (4), fleeing (4), idle (3), torturing (3), and freeing comrades, being tortured, in battle, responding to a threat, reporting danger, celebrating (2 each). A creature busy with something urgent, or not properly yours yet, does not stop to be resentful. Bits 3 and 4 were the Casino (ADR-0074); this is the third bit named.
- `a_keeper_meets_its_wage_bill.yaml` shows no mood change on either side, because `ModifyAngerOfCreatureInLairPerSecond` is −550 and a Goblin's `AngerNoPay` is 60. Both numbers are right; **the conclusion drawn from them here — that wages alone can never drive a well-housed creature out — is wrong, because the bed drains bucket 5 and the wage fills bucket 4 and the maximum never sees the trade (see ADR-0081).** `PaydayTests.ABedOutpacesAnUnpaidWageNineToOne` was replaced by `ABedDoesNotPayOffAWageBecauseTheyAreDifferentCounters`.
- Not built: `AngerNoWork` (non-workers have no job system, so "no work available" is a fact about this simulation rather than the dungeon — built in ADR-0082); `AngerNoFood` in the general case, still charged only by the prison, because the original's clause needs a hatchery-availability test; and the routine's remaining guards — `creature[0x482]`, `creature[0x3cb]`, three bits of `creature[0x3e6]`, `def[0x686] & 0x4000`, and state category 22 (fleeing, battle, threat-response, celebrating) — each a narrowing that would only ever charge less.
- Four tests in `PaydayTests`. Their first draft ran the full ten minutes to a real payday and every one failed the same way: a bedless Goblin takes `AngerNoLair` at 100 a second, so after 600 seconds it is pinned at the 32767 ceiling and has already drawn Leave from its angry job table. **Anger is measured over seconds, not over paydays.**
- Hash unchanged: no field moved; behaviour did.

## ADR-0080 — A Flame name may be cited in a document, and never appears in code
**Date:** 2026-09-06 (owner decision) · **Status:** accepted · Context: the boundary §2.2.1 of `DK2_REIMPLEMENTATION_PLAN.md` left "flagged rather than decided"; ADR-0003, ADR-0041

**Context.** The keeper-AI merge swept for Flame identifiers and reported the source tree clean; widening the sweep to the whole repository found otherwise.

| Where | What | Count |
|---|---|---|
| `DK2_REIMPLEMENTATION_PLAN.md` | Flame class and global names, each cited as Flame's | about a dozen, in eighteen lines |
| `docs/DECISIONS.md` ADR-0001 | a Flame header file name | 1 |
| `docs/research/open-questions.md` Q4 | a Flame class name | 1 |
| `docs/research/external-resources.md` | a Flame global, quoted *as* the example of what not to adopt | 1 |
| `src/DK2.Core/GameTick.cs`, `TilePos.cs` | two Flame field names, prefix and all, in `<summary>` comments | 2 |
| `THIRD-PARTY-NOTICES.md` | the sentence "no Flame identifier appears anywhere in this repository" | false since revision 1 of the plan |

The project had a rule stated in absolute terms in three documents, and nothing checked it. It was false in a source file for the whole life of the repository.

**Decision.**
1. **In documents, a Flame identifier may be named as a citation** — the plan, the ADRs, the research notes and the handoff may write *"Flame calls this X"* provided the sentence makes Flame the source.
2. **In `src/`, `tests/` and the Unity adapter, never** — not as a member, not as a type, not in a comment, not in a string. Where a comment needs to say what Flame established it does so in this project's words.
3. **Offsets, sizes, vtable slots and decoded values are facts about DK2** and are used freely, with provenance (ADR-0003, `external-resources.md`).
4. **No Flame code, comment, header layout or file is ever copied**, in whole or in part, anywhere.
5. The commit-message clause is dropped: a commit message is a document.

Legal basis: copyright protects expression, not names, titles or short phrases, and in the EU *SAS Institute v World Programming* (C-406/10, 2012) held that a program's functionality, its language and its data formats are not protected expression either. Flame publishing no licence means all rights reserved on its **code**, not a claim over saying what the code is called. Risk begins where a reference becomes a copy: lifting Flame's headers, comments, or its **set** of field names and layouts into C#, which could be argued as taking the selection and arrangement of the work — a weak argument, since the offsets are dictated by the DK2 binary, but the only one there is, and the one place it could be made is the source tree.

**Consequences.**
- `FlameIdentifierTests` in `DK2.Core.Tests` sweeps the same files `LicenseHeaderTests` does and fails on Flame's field-naming style — a lower-case `f`, two to four hex digits, an underscore — and on its `sData_` serialisation prefix. It matches the **shape** of Flame's names rather than a list, because a denylist of Flame identifiers would itself be Flame identifiers pasted into the repository. It is a floor, not a fence: a Flame class name typed into a comment would slip through, so the rule still rests on review.
- `THIRD-PARTY-NOTICES.md` now says what is true: Flame is consulted and cited, and none of its names are adopted into code.
- Not decided: whether Flame's `mapping/*.map` files may be vendored as research data (plan §10.1 decision 3) — still open, still recommended against; the clone at `D:\Flame` stays outside the repository.
- **This does not move Flame up the order of sources.** Owner, same day: the primary source of truth is Ghidra and its decompiled sources; Flame is a secondary reference to help determine whether findings hold. A finding is made from the binary and checked against Flame, never the other way round (`AGENTS.md` "Existing research", ADR-0046 amendment).

## ADR-0081 — Anger is six counters and the mood is the largest, not the sum
**Date:** 2026-09-06 · **Status:** accepted · supersedes ADR-0079's bed-versus-wage conclusion · ADR-0050, ADR-0053; Q106, Q114, Q185, Q202 (new)

**Context.** `FUN_00487030` is DK2's anger-add, reached through the wrapper `FUN_00478b80` that every one of the sixty anger sites in the executable calls. Its arguments are (bucket index, amount, message id) and its receiver is an `int[8]` at `creature+0x26e`.

**Decision.**
- `[0..5]` are six independent anger counters; `[6]` is the creature's unhappy threshold; `[7]` is the mood band it writes back. The routine adds the amount to **one** counter, clamps that counter to `[0, 0xFFFD]`, then walks all six for the **maximum** and bands that one value: below `[6]` content, above `0x7FFF` angry, between unhappy. **Nothing anywhere sums them.**
- This simulation kept one pooled `CreatureState.Anger`, wrong in both directions. Two half-grievances do not add up to desertion: a creature at 20 000 hungry and 20 000 unpaid is unhappy in DK2 and was angry here. And a kindness in one bucket does not pay off a grievance in another — **ADR-0079's nine-to-one bed-versus-wage conclusion is retired**: the bed drains bucket 5, the wage fills bucket 4, and a keeper who does not pay loses its creatures however comfortable it has made them.
- **Four of the six are certain.** Exactly one creature-record field feeds each: `angerNoFood` → **1**, `angerNoLair` → **2**, `angerNoWork` → **3**, `angerNoPay` → **4**. Those four are precisely the four ADR-0079's need-anger routine charges, so taxonomy and routine agree without being told to.
- **Two names are ours (Q202)** and the division between them is UNKNOWN. What is read is which sources reach which:

  | Bucket | Reached by |
  |---|---|
  | **5**, called `Treatment` here | `angerSlap`, `angerInHand`, `ModifyAngerOfCreatureInLairPerSecond`, `ModifyAngerOfCombatPitVictor`, both `Gambling…Anger…PerSecond`, and the torture sites |
  | **0**, called `Company` here | `ModifyAngerInCompanyOfHatedCreaturesPerSecond`, a share of the casino and of pit spectating, and — not fitting the name — `angerNoWork` at one site inside the combat pit |

  Bucket 5 is coherent as *what is done to you, and resting it off*: slapping, carrying, torture and the pit fill it, a bed drains it, which makes "sleep it off" specifically the answer to slapping. Bucket 0 is less coherent and its name is flagged as ours, on the same footing as `Unk8` → `IsGood`.
- **The Temple reaches all six.** `ModifyCreatureAngerWhilePrayingPerSecond` (−275) is applied in six consecutive calls, one per bucket. Praying is the only thing in the game that takes back a grievance it did not cause.
- **The counters saturate at 65 533, not at the angry band.** Each clamps to `0xFFFD` and only the maximum is compared against `0x7FFF`, so a grievance keeps climbing for a second full ceiling's worth after the creature has turned angry, and a long-neglected creature does not calm the moment a keeper does one thing right. `CreatureAngerRules.Saturation` is now distinct from `Ceiling`.
- Angry is `> 32767` where this was `>= 32767`, from the routine's own `CMP`/`JLE`.

**Consequences.**
- Nothing about the owner's stopwatch moves: Q106 measured the *band* a creature crosses — 16 383 unhappy, 32 767 angry — which is still what `Ceiling` and `UnhappyAt` express. The saturation above it is invisible to any stopwatch, which is why five sittings of measurement could not have found it and the binary could.
- `CreatureAnger` (new), `AngerBucket` (new); `CreatureAngerRules.Apply` takes a bucket; `PerSecond` returns per-bucket deltas; `WorldSnapshot` writes six.
- Three tests changed their claim rather than their spelling: `ABedOutpacesAnUnpaidWageNineToOne` → `ABedDoesNotPayOffAWageBecauseTheyAreDifferentCounters`; `ABedFixesItInInstalmentsAndNotAtOnce` no longer ends Content, because the Lair bucket is frozen where the bed found it; and the Vampire that draws Rebel is now crosser after thirty seconds than it was, where before it sat pinned at the band.
- The hand tests got simpler: two existed to cancel out the lairless ladder a held creature was climbing at the same time as the hand's own delta — one by spawning a twin Goblin to subtract. The effects are different counters now, so the arithmetic is gone and the twin stays only to demonstrate the separation.
- `HashVersion` 31 → **32**: one `int` on the creature became six. Pin `0x30722F6054924CC5`.
- The standing shape of a claim nothing checked: a single number every mechanic wrote into, never questioned because it had been right for the one mechanic that existed when it was written.

## ADR-0082 — A creature that is not a worker now has a day, and M4 is closed
**Date:** 2026-09-06 · **Status:** accepted · closes Q2 · ADR-0061, ADR-0081 · Research: `docs/research/job-selection.md`

**Decision.**
- **Three choosers, one per mood** — happy `FUN_004780e0`, unhappy `FUN_004782c0`, angry `FUN_004784c0` (built since ADR-0061). Each gates on five game seconds against **its own timestamp** — the creature carries three side by side — builds cumulative ranges from that mood's table, rolls `rand(100)`, tries the job whose range holds the roll, and on failure tries every job in the table in record order. A failed selection stamps nothing.
- **One dispatcher, `FUN_00478800`** — a 28-way switch to the job's handler, and every handler is *(a cheap test on the keeper or creature) → (find the nearest room) → (enter the job's state, which can still fail)*. That boolean is the whole of job availability, which closes the clause Q2 had open: what makes Research or Train *fail* so the list falls through.
- **Only the happy chooser charges `AngerNoWork`.** Its tail, when every job has failed, charges `def+0x6c7`; the unhappy chooser's identical tail is a bare return, and so is the angry one's. A creature already unhappy is never made crosser by idleness. Same shape as ADR-0079's `AngerNoPay` having no treasury clause: twice now, a plausible symmetry the game does not have, and both times the symmetric version would have been the natural thing to write.
- **Sulking is going to your lair and moping in it.** `FUN_00482ef0` is the Sleep handler with one digit changed — same three preconditions, same room, state `0x55` where sleep uses `0x51`. A creature with no bed cannot sulk at all, and every unhappy table in the shipped game is Sulk and Drink.
- **The mood band is stored on the creature**, as DK2 stores it in slot 7 of its anger object and rewrites it on every add. Anger reaches a creature from four places and three run before `CreatureAngerSystem` in the same tick — a slap, the prison, and now having no work — so a "mood before" worked out from the anger visible at the top of that system's pass missed every crossing it did not cause itself. The scenario found it: two Goblins went from content to unhappy on pure `AngerNoWork` and nothing said so.

**Consequences.**
- **Seven more dead balance variables, a whole family**: `MotionlessWhileResearching/Manufacturing/Praying/TrainingSeconds`, `StateCounterWhileGuarding`, `CasinoModifyCreatureGoldPerSecond` and `MinimumJobDurationSeconds` are each written by the loader stub and read by nothing — count from five to twelve. The last is a cross-check rather than a curiosity: all three choosers gate on a hardcoded `5 × variable 19` while the shipped variable that looks like it should govern job length sits unread, which is **evidence that there is no job timer**, not merely the absence of one.
- **Praying is the one job with an effect**, and since ADR-0081 the Temple is the only source in DK2 that drains an anger bucket it did not fill — every other calm reaches bucket 5 alone, so a month of unpaid wages in bucket 4 has exactly one cure. −275 from all six buckets and +33 health, once a game second, both verified live against the seven dead ones beside them.
- Research, Train, Guard and Drink are chosen, walked to and stood in, logged `effect=unbuilt`. Not a stub: every second at a job is a second not idle and not taking `AngerNoWork`, which is the half the choosers depend on. Missing is production; training's rate carried the per-second/per-minute ambiguity, so it was left for its own read (Q205). **Code now: training pays, and the per-minute reading is withdrawn (see ADR-0083).**
- **M4 is seven of seven**: portal spawn, lair claim, hunger→hatchery, sleep, payday, job selection with logged reasons, and training — the one item *selected* but not yet *worth* anything.
- 19 tests in `CreatureJobTests`; `two_keepers_find_work_for_a_goblin.yaml`, whose log is the mechanic in five lines: one keeper's Goblins take Train and go; the other's are refused, take 50 a second, cross to Unhappy at 5m28s with `worst=Work` naming the bucket, and are never charged again because the unhappy chooser does not charge it. Its first draft inverted its own demonstration — the Vampire, placed to be the third creature for two beds, was nearer the lairs than the Goblins, so it got a bed and one of them did not (correct behaviour, nothing being reserved, ADR-0060); the Goblins now start standing on the beds.
- **The determinism pin could not see any of this (Q206).** These fields moved the pinned value through `HashVersion` alone, because the gate's world spawned no creatures at all — so no creature field had ever moved it, and the hash-history notes explaining a move by what the fixture's creatures were doing described creatures that were not there. Fixed on 2026-09-10, well after the fact: the gate's world now holds two.
- `HashVersion` 32 → **33** (`CurrentJob`, `JobTileIndex`, two choice timestamps, the stored mood band). Pin `0x21A7E4D24F37DF48`.

## ADR-0083 — Training pays, and the experience model underneath it was wrong in three places
**Date:** 2026-09-07 · **Status:** accepted · answers Q205, Q79; corrects both readings of Q127; promotes one clause of Q124 from choice to fact; advances Q186; adds two to Q203 · Research: `docs/research/training.md`

**Context.** ADR-0082 gave a non-worker a day but left what it was worth unread — the one loose thread in M4. The rate sat in the creature record as `ExpPerSecondTraining` and was unused because the name could not be trusted: the neighbouring `ExpPerSecond` reads 1200 for an Imp against a balance-table `ImpExperienceGainPerSecond` of 20, and 20 × 60 = 1200 had been written down as STRONGLY INFERRED evidence that the record's rates are per minute.

**Decision.**
1. **The rate is per second and the loader says so.** DK2's creature loader stores `ExpPerSecondTraining` as `file value / variable 19`, flooring at 1 when the source is non-zero. Three siblings go through the same divide — `ExpPerSecond`, `ResearchPerSecond`, `ManufacturePerSecond` — and their immediate neighbour `ExpForNextLevel` is stored **raw**, which is the tell: a threshold is not a rate. `SimulationTiming.LoaderRatePerTick` is the one definition of that conversion, and `PrisonRules` uses it instead of its own copy. Known membership of that loader idiom goes from six fields to ten.
2. **The 1200 was a coincidence between two unrelated mechanics.** `ExpPerSecond` belongs to state **category 22** — battle, fleeing, threat-response — so it is **combat** experience, which is the owner's own *"to get to level 10 they need to fight actual fights"* (Q124). The per-minute reading is withdrawn. Third instance in this project of two correct numbers that never meet.
3. **The payment hangs off the state's category, not the state.** None of the six training states awards anything. The 32 category records at `0x00699688` each carry an enter hook at `+0x04`, an update hook at `+0x14` and a leave hook at `+0x24`, and the per-tick dispatcher calls the current category's update hook before the state's own handler. Category 13 is training; its update hook is the payment. All 32 records are now decoded (Q186).
4. **Training bills the keeper five gold a game second, and poverty ends the job.** `ModifyPlayerColdWhileTrainingPerSecond` is −5 and "Cold" is the shipped spelling of gold. The original tests the keeper's gold before entering the state and before every charge, and returning failure from a category update hook makes the dispatcher put the creature back to idle. Training is the only job in DK2 a keeper can be evicted from by being poor. `JobRules.Refused` makes that test too, and *before* the level test, because that is the order in the handler.
5. **`CreatureLevelRules.TryLevelUp` is replaced by `Grant`, and Q127's two readings were both wrong.** DK2 promotes **once** and **zeroes** the balance — the overshoot is discarded, not carried, and a large award cannot buy two levels. A negative award larger than the balance is a **no-op**, not a clamp. An `ExpForNextLevel` of zero refuses everything, losses included.
6. **Q79: imp experience is per tick of work, not per job.** `ImpExperienceGainPerSecond` is read at nine sites, all in the imp's task unit, all awarding the raw 20 with no divide, each guarded by a predicate that decompiles to *"is this tile still a valid target"* rather than *"the job finished"*. It is the one rate in the family **not** divided by variable 19, so its name is wrong by a factor of four in the opposite direction to everything around it: **80 a game second**, and an imp reaches level 2 in about **37 seconds** of work rather than the 150 asserted in a test name for three weeks. (The owner leaned "per job done" and named exactly the right activities.)
7. **Both awards are paid on the original's tick, not once a game second** — `SimulationTiming.OnOriginalTick`. Because the promotion zeroes the balance, four awards of ten and one award of forty leave different numbers behind on any second that crosses a threshold. The cadence is behaviour.

**Consequences.**
- `TrainingRules` and the `Train` branch of `CreatureJobSystem.Working`; `JobRules.EffectIsBuilt` now names two jobs.
- **The truncation is reproduced, not rounded away.** 30 / 4 = 7 gives 28 a second, and 26 of the 46 shipped creatures lose something — the heroes' lords and Horny lose a fifth. So does the floor, which is reachable: the Skeleton's `ResearchPerSecond` of 1 and the Dark Angel's 3 both become 4 a second. A fixture drift caught by needing the number: the fixture Goblin had been inheriting the Imp's 30 and 1200; its own are 40 and 200.
- **One more inert balance variable (Q203), and a new kind of inert.** `EPLossSecondWhileNotTraining` has a real reader and ships as zero: a per-second hook draining experience from any creature whose state category is not 13. Experience was built to decay unless you kept creatures training, and the feature was **turned off rather than cut** — distinct from the twelve with no reader at all, because the mechanism is intact and a mod that set the rate would work. (`MotionlessWhileTrainingSeconds` is dead too, but ADR-0082 found it.)
- **41 of 46 creatures have `ExpPerSecond` exactly 5 × `ExpPerSecondTraining`**, which argues the two are the same kind of quantity without reading any code. The exceptions are three uniques whose combat rate was raised without the training column following, the Maiden Of The Nest, and the Imp at forty times — the one creature that neither trains nor fights, and the source of the misleading number.
- The scenario shows the arc: two Goblins train, both reach level 2, the treasury empties at about t2500 and both lose the job on the spot, and 2500 ticks later they are the same unhappy Goblin the keeper who built no room has. Player 4 had to be *given* gold for its refusal to still say `no_room`.
- `HashVersion` unchanged at 33 — no hashed field changed shape — but hashed *values* move wherever experience is earned.
- Ours, flagged: a capped creature keeps the job for up to one five-second gate instead of being ejected by a state handler, because we have one working state where DK2 has six; the gold comes out of the keeper's tiles rather than the tile underfoot; no floating "−20", no message `0x37`, no combat-pit fallback for a creature that has outgrown the training room. Q79's factor of four rests on `tile-work.md`'s per-tick accrual rather than a caller read — the probe: find what iterates the imp task table at `0x006E4198`.

## ADR-0084 — Portal disposal is for non-Imps; Imp disposal is through the Heart
**Date:** 2026-09-07 · **Status:** accepted · amended 2026-09-13; closes Q88, opens Q209; confirms Q137 · Research: `docs/research/structure-blocks.md`

**Context.** Q88 asked whether a portal's centre tile is walkable. Owner: *"no it is not walkable, dungeon heart's center is also not walkable. portal's center is reserved for creature's 'arriving' animation and for hand of evil to throw creatures in, removing them from dungeon"* — the third clause a keeper power this project did not have.

**Decision.**
1. **A portal blocks its single centre tile; a dungeon heart blocks its centre 3×3.** `StructureBlocks`, consulted by `WorldPathGrid.IsWalkable` — the one part of walkability here not derived from terrain flags, which is why it lives in its own type. The heart's nine is borrowed evidence: Q52 established from the gold counter that 16 of a heart's 25 tiles store gold against `MaxGoldPerDungeonHeartTile` of 1000, so nine are structure, and `RoomInstance.IsCentrePlatform` is the same predicate the gold rules use — "16 storable" and "16 walkable" are one fact.
2. **A room with no middle has no structure.** Both cases refuse a room narrower than 3. That is `IsCentrePlatform`'s own guard for the heart; for the portal it was added because the first version of this change walled off a deserter — the anger tests paint a 1×1 portal, and blocking its only tile left nothing to walk to.
3. **Dropping a non-Imp onto the hole removes it with its gold.** This includes converted goodly creatures (owner, 2026-09-13). It reuses `creature_left` with `via=thrown_in`.
4. **An Imp is refused at the portal and discarded through the Dungeon Heart instead** (owner, 2026-09-13). Thus both structure centres are exceptional Hand destinations, selected by creature kind. The current implementation accepts an Imp at the portal and refuses the Heart; both are known divergences.
5. **An enemy prisoner is refused at the portal** (owner, 2026-09-13). The built refusal is therefore confirmed: the prisoner stays in the hand, still imprisoned, and ADR-0064 is untouched.

**Consequences.**
- Arriving on the hole still works, by luck rather than design: `GridPathfinder` tests the destination and every neighbour it expands but never the **start**, so a creature spawned on the hole routes off it while nothing routes onto it, and `KeepOffTheCorner` already nudges a creature standing somewhere unwalkable. Both were written for other reasons; both are now asserted by a test.
- 10 tests, `StructureBlocksTests`; the two that failed use a 1×1 portal, which is how the missing minimum-size guard was found. Verified on real geometry by `a_portal_fills_a_dungeon`, which runs campaign level 1 — a genuine 5×5 heart and 3×3 portal — with imps digging, claiming the portal and four Goblins arriving, no route failure and nothing stuck. `dk2tools` gains **`kwd objecthex`**, the object twin of `creaturehex`, which read the candidate radius column.
- **The mechanism is NOT this (Q209).** DK2 has no room-level walkability override; it has an obstacle system driven by the object table, and object **100 is named "Dungeon Heart Obstacle"**. Established: `objDef+0xf4` is the flags word, `objDef+0xf8` a 4096-fixed size used as `size >> 1`, that size's file offset is **`0x32C`** (from the loader's frame slot at a `0x80` frame-to-record delta), **15** objects carry `SolidObstacle` and all fifteen are room furniture, **58** carry `Obstacle`. Both obvious candidate rules are refuted by the shipped data, which is why nothing data-driven was built: `Obstacle && size > 0` would block every creature bed, and `SolidObstacle` alone would not block a portal's centre because the Portal Crystal carries `Obstacle` without it. `FUN_004D12B0` is a collision *enumerator* feeding a callback, gated on `Obstacle` and consulting `FirstPersonObstacle` behind a mode flag — not tile pathing.
- So this covers two tiles and no more. The fifteen `SolidObstacle` objects are the workshop's anvil, the library's bookcases, the casino's tables and the graveyard's tombstones; every one of those rooms is coming, and guessing a rule now would silently decide the other thirteen. When the real predicate is found, `StructureBlocks` should be **deleted** and the path grid should learn about objects.

## ADR-0085 — Ten mechanics DK2 built and switched off, and the audit that could not see them
**Date:** 2026-09-07 · **Status:** accepted · amended 2026-09-07: a zero percentage can be fully live, the scan range was never the whole image, and a level file can switch a disabled mechanism back on · Q203, Q106 (a clause explained), Q212, Q84/Q85 · Research: `docs/research/balance-variables-in-the-binary.md` (final section), `docs/research/variables-corpus-sweep.md`

**Context.** Balance variables were retired as *dead* by one method: take the cached global, raw-scan for readers, find only the loader's setter stub, retire it. The blind spot was found by accident while reading training (ADR-0083): `EPLossSecondWhileNotTraining` has a real per-second reader — a hook draining a creature's experience unless its state category is the training one — and a shipped value of **0**. A reader-scan cannot surface that, because the reader is there.

**Decision.**
- **Run the audit the other way round**: list every balance variable whose shipped value is zero, then scan those globals for readers. Twelve are zero; **two are dead outright, ten have live readers.**
- A variable with no reader was **cut**; a variable with a reader and a no-op value was **disabled** — the second still has its mechanism, and a mod that set the rate would work.
- A zero is a reason to look harder, not to stop: nothing about the value distinguishes the two cut from the ten, only the scan does. The same shape will exist for the creature and object record fields, where no equivalent audit has been run.

**Amendment (2026-09-13) — "inert" requires the reader's arithmetic, not the reader's existence.** A zero rate is inert for nine of the ten, but id 112's reader computes `x += x * (pct + 100) / 100`, so zero doubles the amount. The reader is now identified as the Temple prayer hook: it doubles a converted creature's prayer mana, corroborating the manual, and no sacrifice path reads it. `PercentageOfManaGainedBySacrificing` is an inherited misname. The other nine readers lack this arithmetic. Q212 and `sacrifice.md` carry the evidence.

**Amendment (2026-09-07) — scans must cover every initialised block, not `.text`.** `ScanAddr.py` hardcoded `0x00401000`–`0x00652C00`. The image has **two executable blocks**: `.text`, and **`cseg` at `0x00653000`–`0x0066BFFF`**, 102,400 bytes of code no reader count had covered — so every "no reader in `.text`" sentence was silent about 100 KB of executable image. `reverse-engineering/ScanAll.py` replaces `ScanAddr.py` and walks all thirteen initialised blocks, naming the block on every hit; re-run over the cut list it returns **one hit each, all setters, and zero hits in `cseg` for any query**, so the conclusions stand. The block names around `cseg` (`idct_dat`, `lbmpeg_d`, `uva_data`) say it is the video decoder.

**Amendment (2026-09-07) — the folded-neighbour trap is bounded, and now swept.** A wider load at a neighbour's address can only carry a variable if the whole variable fits inside it, and 32-bit integer loads reach four bytes, so **a dword variable cannot be read through another variable's load** — seven of the fourteen are immune by construction. All **57** zero-reader globals in the map were swept rather than the fourteen: 13 have a covering neighbour load, and reading every one leaves **exactly one live case**, `CreaturesSupportedPerAdditionalPortal`, extracted with `MOV DL,CH` (ADR-0086). Twelve follow the wide load with `AND …,0xff` or `AND …,0xffff`, discarding the neighbour's bytes. The discriminator is the **extraction, not the load width**: what matters is whether any instruction afterwards touches bits above that variable's width, and both are visible in one disassembly window.

**Amendment (2026-09-07) — a level override can switch a disabled mechanism back on.** "Nothing to build" was about **`GlobalVariables.kwd`** and was read as being about the game; the 66 level files override these ids too, in both directions:

| id | variable | global | per level |
|---|---|---|---|
| **218** | `TimeBeforeDungeonHeartConstructionBegins` | **0** — listed "disabled" | `level2` = 15, `level8` = 16 |
| **94** | `MinimumImpThreshold` | 4, ten readers | **0** on five levels |
| 55 | `PrisonModifyCreatureHealthPerSecond` | live | `Level6b` = −5 |

"Shipped as zero" is an incomplete claim; the checkable form is *"zero globally, and not overridden in any of the 66 level files"*, which `dk2tools kwd varsweep` answers in one command. The other nine survive the sweep — none is overridden anywhere.

**Consequences.**
- **Nothing is built, and that is the finding.** The value runs the other way: it names **ten mechanics a reimplementation might "helpfully" add**, every one of which would be a divergence from DK2.
- Three are whole features with the switch at zero. **Walls decay over time** — `NumberOfRandomWallDeteriorationsPerSecond` and `WallDeteriorationDamage` are read by one routine, `FUN_004BB6D0`, a count of deteriorations a second and a damage per deterioration: complete, inert, and the obvious trap, because it looks like an oversight. **A creature that cannot sleep takes damage**, and the reader is the needs routine `FUN_00477C20` (Q185, ADR-0074) — so the punishment for a bedless creature was meant to be health as well as anger and only the anger shipped, which explains a clause of **Q106**: a lair that turns a creature away was supposed to hurt it too. **Torture can kill on conversion**, rolled inside category 15's own per-tick hook at a chance of zero.
- The other seven: `CannotLeaveModifyCreatureHealthPerSecond`, variable 112 (misnamed; converted-creature prayer-mana bonus), `HypnotismManaDrainPerSecond`, `EPLossSecondWhileNotTraining`, `BackOffMinimumDurationSeconds`, `TimeBeforeDungeonHeartConstructionBegins`, `TortureChanceOfDyingWhenConverted`.
- `CreatureCriticalHealthPercentageOfMax` is confirmed cut against the whole image rather than against `.text`, so `CreatureHealthRules.IsCriticallyHurt` keeps its note and loses its caveat.
- Nothing built changes under the level-override amendment either: id 218 and the prison drain are not implemented, and `MinimumImpThreshold` already reads through `GameDatabase` with per-level layering (ADR-0040), so the five levels that zero it are handled. Third time a true statement about one file was generalised to the game, after the `(13,1)` reading withdrawn in the same sweep and the `MaxGoldPileOutsideTreasury` name confusion. Q84 and Q85 were both catalogued low priority; the sweep that answered them found this.
- The fourteen on the cut list were re-derived rather than re-read, and the list holds in full.

## ADR-0086 — A portal's arrival interval is the square of its own tally
**Date:** 2026-09-07 · **Status:** accepted · supersedes the interval half of ADR-0042 · amended 2026-09-07: the tally reset is an ownership change, and both portal types are one implementation with two rosters (Q211 closed, CONFIRMED in play) · Q86 · Research: `docs/research/portals.md`

**Decision.**
- `PortalRules.SpawnIntervalTicks` computes `n² + EntranceGenerationSpeedSeconds` game seconds, where **`n` is how many creatures this portal has already generated** — a new `TileState.EntranceCreaturesGenerated` on the portal's centre tile, beside the clock already there, incremented once per arrival and never decremented. It replaces `max(base, base × the keeper's living non-worker creatures ÷ 2)`.
- Three independent sources, taken formula-first with the prediction committed (`af4038d`) before the measurement:
  1. **The binary.** The portal's per-tick routine at `0x004DBBE0` forms the due time in six instructions: `(n * n + base) * variable19 + room[0x49]`, with `room[0x4D]` the per-room tally incremented once per generation (`FUN_004DBF90`) and `room[0x49]` the tick of the last one.
  2. **Two hand-timed runs on the original** (owner, 2026-09-07, base 25): gaps of `25, 26, 30, 40, 41, 51, 59` and `25, 26, 32, 33, 42` seconds against a predicted `25, 26, 29, 34, 41, 50, 61` — mean absolute error **1.4 s** and **1.0 s**, where the old formula gave 6.4 s and 4.0 s and diverged.
  3. **A second portal**, claimed after the first had produced five creatures, started its own series again: gaps of `27, 30, 31, 33`. A per-room tally predicts `25, 26, 29, 34` (error **2.3 s**); a keeper-wide tally predicts `50, 61, 74, 89` (**38.3 s**); the old formula predicted `62, 75, 87, 100` (**50.8 s**).
- The old formula was wrong three ways at once: the **shape** (a multiple of the base rather than an addition to it), the **slope**, and the **counter** — it read *living* creatures, so a portal sped back up whenever one died. DK2's tally cannot fall: 129 references to a `+0x4D` field across the whole executable and not one is a `DEC` or a `SUB`.
- The requirement is computed **inside** the per-portal loop rather than once per tick. The once-per-tick read that remains is the capacity check, where ADR-0042's deliberate quirk lives, so two portals ripe on the same tick still both produce.

**Amendment 1 (2026-09-07) — the reset is an ownership change, and both portal types count.** Owner: *"if an enemy keeper takes one of the portals from me, and if they have the room to attract more creatures, then yes, it resets to zero for that portal and that keeper."*
- DK2 keeps per-room-type handler records in initialised `.data` — base `0x006a1e9f`, **41 bytes per record**, addressed `base + roomTypeId * 41`, valid for ids 1..26, all present and in order; each record is a type-id byte then ten packed unaligned dwords. **Portal (3) and Hero Portal (25) hold identical handler sets.** The room constructor (`FUN_004E3800`) calls slot `+0x01`; the ownership change (`FUN_004E5650`) calls slot `+0x0d`; for a portal both stamp the room's clock and zero its tally. `FUN_004E5650` re-links the room between the two players' lists, writes the new owner into `room[0x1d]`, re-skins it, ejects its objects, fires the reset, credits the new owner and sends two notifications, one to each side. A rival taking a worn portal and the first keeper to claim a neutral one both get it at the base interval.
- Built: `PortalRules.OnOwnerChanged` zeroes the tally **and the clock**, called from `TileWorkRules.Convert` whenever a portal-terrain tile changes hands. Without the clock a rival could inherit a portal already most of the way to its next creature.
- **Both portal types raise the creature cap.** `FUN_004DC760` walks the player's own room list twice, counting room type 3 then type `0x19`, returning `first + (total - 1) * perAdditional`. This project counted only type 3, so a keeper holding a Hero Portal received its arrivals and not its five extra places. The comparison at the attraction site is **strict**, and nothing anywhere culls creatures — so losing a portal drops the cap while the creatures stay, and the keeper receives nothing until they are under it again (owner: *"creatures do not disappear if that happens"*).
- Two method corrections. **"Reached only through a vtable" was wrong**: both zero-stores had no direct caller and no absolute reference in `.text`, and the vtable was inferred from that silence — but the scan covered `.text` alone and a handler table lives in `.data`. A scan over every initialised block finds both addresses at once. **A variable adjacent to another can be read as a word and will scan as dead**: `CreaturesSupportedPerAdditionalPortal` (`0x006F28B5`, value 5) has exactly one reference, the loader's setter, so by the dead-variable rule it is cut — and it is live, and it is the rule the 1999 manual states in prose. Its byte sits immediately after `CreaturesSupportedByFirstPortal`'s, and the capacity routine loads a **word** at `0x006F28B4` and takes the high half with `>> 8 & 0xff`; the second variable's address is never assembled into any instruction, so no scan for it can find a reader. Before recording a variable as cut, check whether its global is adjacent to another's and read the neighbour's reader.

**Amendment 2 (2026-09-07) — the two portals are one implementation with two rosters.** Owner: *"hero portal IS AN ENTRANCE, but for goodly heroes (rival faction), until it is destroyed by evil keepers by digging and claiming tiles around it"*; *"'hero portal' is not something a keeper can use, nor can an 'evil' keeper summon 'good' creatures"*; *"they have different textures."*
- Model both types with the one code path DK2 uses and put the distinction in the creature gate, keyed on **room type**: `PortalRules.CanComeFromPortal` takes the portal's `RoomDef`, and room type 25 requires `CreatureFlags2.AvailableViaHeroPortal` where anything else requires `CreatureFlags.AvailableViaPortal`. The `IsUnique` refusal stays common to both, because in the original it sits outside the or. `CreatureDef.Flags2` parses the record's **last dword** (`0x1545`), four bits wide. `PortalSystem` counts a portal toward the creature cap **before** testing whether it can produce, because DK2's counter tests the room type and nothing else.
- Room type and not owner: the binary decides it in one condition — `(def+0x686 & 0x400 && room+0x18 == 3) || (def+0x68a & 8 && room+0x18 == 0x19)` — and the handler records for rooms 3 and 25 are byte-identical across all ten slots. There is no hero-portal *implementation*, only a roster. Keying on the owner would break Frosty's Castle, where keepers are given the hero roster on a map whose only entrances are Hero Portals. The capacity routine read for ADR-0085's amendment counts room type 3 and type `0x19` and adds them, which is this choice read straight off the instructions.
- Reproduced deliberately: a keeper's claimed Hero Portal produces nothing on any normal level (the "dead room" the owner describes, reached through the data rather than a special case); claiming a dead Hero Portal still raises the creature cap, and a portal a rival has cut in two counts twice (fidelity priority 4); on Frosty's Castle, Frosty's Lair and Frosty's Realm a keeper's Hero Portal produces hero creatures.
- **CONFIRMED IN PLAY, 2026-09-07** (owner, Frosty's Castle): *"hero units (in this case mercenary units) arrive through the portal and they are keeper's units, no dwarf comes."* Both pre-registered failure modes stayed away — creatures arrive, so the gate is the room type and not the owner; no Dwarf, so the flag is on the right bit rather than bit 30 of the first dword, which selects the same nine *plus* the Dwarf. The room is called **"Mercenary Portal"** in game; *"Hero Portal"* is only the editor's label, so the identifiers name the record and not the game's own word for it. Q211 closed. `PortalRules.CanComeFromPortal` had been marked `Confidence.Confirmed` on the binary read alone, premature by this project's convention reserving CONFIRMED for owner observation.
- **The bit that was nearly wrong.** Bit 30 of the *first* flag dword selects the nine hero-portal creatures **plus the Dwarf** and was already parsed and named (`MpdRandomInvader`, from OpenKeeper); using it would have had a Hero Portal emitting the heroes' worker. What rejected it was searching the record for the roster instead — one byte in 5449 has a bit selecting exactly the nine, it is bit 3 as the binary tests, and Frosty's Castle pools those same nine while omitting the Dwarf. **An available, named, plausible flag is not evidence; the roster is.**
- "Cannot be sold, cannot be repurposed, indestructible" needed no decision: no `Buildable` on either portal record, `CanBuild` refusing room terrain, no room-level `Attackable` — the same three rules that already covered the Dungeon Heart (Q207).

**Consequences.**
- **A portal wears out.** On a base-25 level the fifteenth creature takes 250 seconds where the old formula said 187, and the second and third arrive a second and four seconds later than they used to instead of on the base interval. A freshly claimed portal runs at full speed however worn out its neighbour is, which the old formula could not express because its counter was the keeper's.
- Moved `HashVersion` and `FormatVersion` 33 → **34**.
- The one gap outside tolerance is named in the test data rather than absorbed by a wider tolerance: run 1's fourth gap is 40 s against a predicted 34 and every gap after is back inside tolerance, so the cumulative error steps once and stays flat — the signature of a one-off block with nothing eligible rather than a wrong slope. Widening the tolerance to admit it would also have admitted the old formula on three of six gaps. The refuted test was renamed `…AndThisSlopeIsRefutedByQ86` before deletion, and broke on its first line when the real formula went in.

## ADR-0087 — A creature that never gets angry says so with a flag, and Jack's anger values are a decoy
**Date:** 2026-09-07 · **Status:** accepted · Q210

**Context.** The second creature flag field's **bit 2** means *this creature never becomes angry*. Skeleton and Jack carry it. DK2 tests it in the one wrapper every anger site in the game calls (`FUN_00478b80`, 60 call sites) and returns immediately, so a flagged creature takes nothing from any source — no hunger, no lair, no wage, no slap, no hand — and neither unhappiness message ever fires for it.

**Decision.** `CreatureAngerRules.CanBeAngered` reads `CreatureFlags2.NeverAngered`, and the gate moved into `CreatureAngerRules.Apply`, the single adder, because that is where DK2 puts it: one wrapper in front of one adder means there is no second door. The four callers that already asked `CanBeAngered` keep their checks, now only to skip work they would do for nothing.

**Consequences.**
- The predicate had been inferred from the data — *"false for the Skeleton, the only creature whose four `angerNo*` fields are all zero"* — which is true, gives the right answer for the Skeleton, and the wrong one for Jack:

  | field | Jack | Skeleton |
  |---|---|---|
  | `angerNoLair` | 120 | 0 |
  | `angerNoFood` | 110 | 0 |
  | `angerNoPay` | 60 | 0 |
  | `angerNoWork` | 50 | 0 |
  | `angerInHand` | +500 | 0 |
  | `angerSlap` | −5000 | 0 |

  Not one of those six is ever read: a fully populated, plausible decoy, with the flag on the same record deciding. Our Jack accumulated anger on the two paths that did not consult `CanBeAngered` (`CreatureJobSystem`'s no-work charge and the praying calm), and — because his slap is negative — a keeper could *calm* him by slapping him, which DK2 cannot do because it never reaches the adder. **An available, populated, plausible set of values is not evidence of a live mechanism** — the mirror of ADR-0086 amendment 2's flag.
- **The Imp is a third mechanism and stays as it was.** The Imp, the Dwarf and Horny are **not** flagged; they carry **1** in their anger rates, a hundredth of a Goblin's 100, so a lairless Imp would need nine hours to turn angry. *"Imps never get angry"* is expressed by a small number rather than a flag, and is kept distinct in the code.
- Six behaviour tests added; the real-data test that asserted "only the Skeleton" now asserts "the Skeleton and Jack" **and keeps the old value-based query as the discriminator**, so the thing that was wrong is asserted to be wrong rather than deleted. The fixture Skeleton carries the flag as well as the zeros, because the fixture reproduces the record and not the inference.
- `HashVersion` unchanged and the determinism pin holds: no shipped scenario contains a Skeleton or a Jack, and every other creature's answer is the same under both rules.
- **Still open in Q210.** Bit 0 (King Reginald, Stone Knight, Jack) makes its carrier immune to whatever the impact routine at `0x004af1e0` applies — that routine damages a creature and **doubles the damage** when the target's tile carries terrain flag `+0x17b & 0x80`. Which attack it is has *not* been established; the routine is installed into a table rather than called. Bit 1 (Stone Knight alone) is a **bounded** negative: no instruction in either executable block tests it, and none reads a wider field at `+0x687`, `+0x688` or `+0x689` that would cover the byte without naming it.

## ADR-0088 — The room → job map is confirmed, and `JobRules` stops being an inference
**Date:** 2026-09-07 · **Status:** accepted · Q210

**Context.** DK2's per-room-type handler table (`0x006a1e9f`, 41 bytes a record, ids 1..26 — the table ADR-0086 amendment 1 opened) ends each record with a **job id**. `FUN_004E8DA0` loads it, asks the creature's own definition whether that creature may take the job, and writes it into `creature+0x2bc`.

**Decision.**
- `JobRules.RoomFor` is now `Confidence.Confirmed`. All 26 records give Hatchery 2, Library 3, Training Room 4, Workshop 5, Guard Room 6, Torture Chamber 7, Temple 8, Casino 9 — **eight rows matching this project's `JobType` exactly, including both values that are not sequential** (`Drink` is 9, not 10). Eight independent agreements replace the "which finder does each handler call" reasoning, where only Guard was certain.
- **One row added, and it is inert:** Prison → `JobType.Wait` (22). No creature's job tables list `Wait` and nothing here asks for it, so the mapping changes no behaviour; it is present because the table states it, and a missing row is harder to notice than an unused one.
- **Two rows deliberately not touched.** The **Lair** and **Combat Pit** records carry no job id, so `Sleep`, `Sulk` and `CombatPitSpectate` are reached by another route. They keep the evidence they had — Sulk's Lair requirement still rests on its own handler — and this table is recorded as **one dispatch path rather than the whole map**. Reading the empty slots as "no job" would be the same over-reading a zero rate invites (ADR-0085).

**Consequences.**
- `HashVersion` unchanged.
- All twelve of the table's indexing sites now have their slot. `FUN_004E4B00` is the per-tick caller — the slot map had that row with no caller against it — and it treats the handler's return as a gate, ending the routine on zero.
- Three trivial accessors cover the integers at `+0x19`, `+0x1d` and `+0x21`, which remain **UNKNOWN** and are tabulated for all 26 rooms in `portals.md` rather than guessed at: `+0x1d` copies `+0x19` wherever it is set, and `+0x21` is 3–6 everywhere except the two portals, which carry 52.

## ADR-0089 — A tick is 250 ms, and Q47's fifth was never a cadence
**Date:** 2026-09-07 · **Status:** accepted · reverses ADR-0024 and amends ADR-0020 · closes Q1/Q47

**Context.** `SimulationTiming.TicksPerSecond` had been the placeholder **20** since the project's first week, because two load-bearing readings contradicted each other: **Q1** read a game second as four ticks (variable 19, the engine's seconds-to-ticks multiplier, which the level loader copies out of the LEVEL chunk and which all 66 shipped levels carry 4 in), and **Q47** read work as landing five times a second, from the owner's measurement of 273 gold a swing on a gold seam. Five applications cannot be scheduled inside four ticks — `WorkApplicationPeriodTicks` would have been `4 / 5 = 0` — so 20 was the least common multiple that could host both.

**Decision.**
- **A tick is 250 ms**, `TicksPerSecond` is **4**, and `OriginalTickPeriodTicks` collapses to **1**, from three independent readings:
  1. **Four ticks a game second**, as Q1 had it: variable 19, 4 in every shipped level.
  2. **A game second is a real second**, CONFIRMED by the owner's portal stopwatch (Q86, ADR-0086) — seven consecutive arrival gaps against `(n² + base) × variable 19` ticks, mean absolute error 1.0 s across a series running out to 59 s. A game second of 0.8 or 1.25 real seconds would have shown as an error growing with the series, and did not.
  3. **The creature update runs exactly variable-19 times a game second**, from inside the update itself. `FUN_00488930` is the creature's own per-tick virtual method (vtable `0x0066d524`, slot `+0x14`). It sets two effect timers to `variable 19 × 10` and decrements each once per call while a visual effect plays: a counter initialised to 40 and decremented once per call is a ten-game-second effect if and only if the call happens four times a game second. Two instances in one function, with a watchable effect attached. Independently, the same function gates work on `tick % variable 19 == 0` and on `(tick + id × 2) & 7`, and neither gate is correct at more than one call per tick value.
- **Q47's five is the size of an application, not its rate.** The work handlers read their rate global already divided by variable 19 (the loader does that division at load time) and then multiply by the worker's Efficiency in 4096 fixed point (`0xccc` = 0.8 at spawn), so one application is `tabled ÷ 4 × 0.8` = `tabled ÷ 5`, and the 273 gold a swing measures that fifth to three significant figures — as ADR-0020's 2026-09-05 amendment already said. What the amendment did not do was follow the consequence to the schedule: the fifth is the *amount*, the rate is once a tick, four a second. The two are now separate constants that cannot be confused — `SimulationTiming.BaseApplicationDivisor` (5, sizes an application) and `SimulationTiming.WorkApplicationsPerSecond` (4, schedules it).
- **ADR-0024 is reversed: an upward job costs the deficit.** DK2's tile applier `FUN_00452f60` adds the delta straight into the tile's own health word at `tile+6`, clamps a positive result at 32000 (`CMP EBX,0x7d00`), and converts the tile the moment its health reaches the terrain record's `MaxHealth` at `terrain+0x1ad`. There is no separate accumulator.

  | job | terrain | cost | applications |
  |---|---|---|---|
  | reinforce | Rock, 165 of 660 | **495** | 495 ÷ 10 = **50** |
  | claim | Dirt Path, 1 of 660 | 659 | 659 ÷ 165 = **4** |

  ADR-0024 charged an upward job the terrain's whole `MaxHealth` — 660 for Rock, 66 applications — on the strength of a stopwatch: 66 applications fits the owner's 13.3–13.4 s if work lands five times a second. **Both halves were wrong and they cancelled**, which is why no test caught either and the wall clock could not separate them. Re-checked against every timed measurement, the read model wins on the *direction* of the residual:

  | measured | read model predicts | residual |
  |---|---|---|
  | reinforce, unslapped, 13.3–13.4 s | 50 applications, 12.5 s | +0.85 s |
  | reinforce, slapped, 11.5–11.9 s | 45 applications, 11.25 s | +0.25–0.65 s |
  | claim, 1.30–1.40 s | 4 applications, 1.0 s + linger | +0.3–0.4 s |

  All three over-read by under a second, which is what a hand timer does. Under ADR-0024's rule the slapped reinforce required 12.0 s against 11.5–11.9 observed — an *under*-read, which reaction time cannot produce.
- **`TileState.UpwardWork` is deleted.** The progress *is* the tile's health, so a half-reinforced wall is a tougher wall — and it looks it, because the applier compares `(oldHealth − 1) ÷ stageSize` against `(newHealth − 1) ÷ stageSize`, where `stageSize` is `MaxHealth ÷ terrain+0x1c3` and `+0x1c3` is a count of **visual stages**.

**Consequences.**
- Every duration in the project is now a wall-clock claim: a tick is 250 ms, and scenario logs in ticks are timings.
- **Resolution is coarser and that is faithful.** Four steps a second makes movement quantisation visible — a four-tile walk is 9 ticks straight against 13 on the bias, so the exact 14/10 diagonal ratio no longer survives the rounding. `ImpTests.WalkingOnTheBias_TakesRootTwoTimesAsLong` asserts within one tick and says why: DK2 steps at the same rate and has the same quantisation, so asserting equality would assert a precision the original does not have.
- Scenario tick budgets now cover five times as much game time; nothing breaks and every hash is stable, but several are longer than their author intended.
- `TicksPerSecond` and `OriginalTicksPerGameSecond` stay separate constants despite both being 4. One is our step rate, free to be refined; the other is a fact about DK2, in the same class as a tabled balance variable. Collapsing them would make facts about the original depend on a choice of ours.
- The stage count predicts something separate and checkable: a tile's *appearance* steps `terrain+0x1c3` times as it is worked, which need not equal the number of swings. A claim that Q46's *"2-3-4 jumps MAX"* had therefore been counting stage crossings rather than swings was **withdrawn the same day**: the owner's *"an imp will go to it, jump a few times on it and claim it"* has the **imp** jumping, once per application, so four applications and two-to-four visible jumps corroborate the swing count as originally recorded. Ask the owner what an observation meant rather than reinterpreting it to dissolve an inconsistency.
- **This ADR's fill-in paragraph is obsolete.** DK2 has no fill-in mechanism: `FillInHealth` has no reader and `TileWorkKind.FillIn` is deleted (Q219). The max-health compare really is unguarded; caller reachability keeps it safe (Q215, `tile-work.md`).
- `HashVersion` and the snapshot format both 34 → **35**, pin `0x368FDF9DF3D0A6EA`.
- Rejected: keeping 20 as a sub-tick substrate. It would have kept every existing number and the accidental 13.2 s reinforce, but that fit was the product of two errors and the substrate would have preserved a cadence the binary contradicts.

## ADR-0090 — The Library accrues research to the keeper, and three bonuses are not what their names say
**Date:** 2026-09-07 · **Status:** accepted · corrects a claim in ADR-0062/Q159's reasoning and one in `SimulationTiming` · Research: `docs/research/library-and-research.md`

**Context.** The Library was the last room a creature could walk into and stand in with no effect. `JobRules.RoomFor(JobType.Research)` was confirmed against the room handler table (ADR-0088), the job chooser already sent a Warlock there, and nothing happened when it arrived.

**Decision.** Build the **accrual** and not the tree, from DK2's per-tick hook for state category 4 (`sub_0047b000`) and the keeper-side adder it calls (`FUN_004c2dc0`).
- `ResearchPerSecond` (runtime `def+0x6BB`, identified exactly by the loader pair `src=0x12F8 → dst=0x6BB` against file offset `0xF08 + 0x3F0`), divided by variable 19 at load time, scaled by the level table's row at `+0x201`, credited **to the keeper** — the hook ends `MOV ECX,[ESI + 0x2A]`, the creature's player pointer. Every tick. Nothing charges for it.
- `PlayerState.ResearchPoints` is the accumulator (`player+0x20F`), only ever added to. The original keeps a *current target* beside it and completes that target when the points reach its cost, and the points are credited whether or not a target is selected, so the accrual is faithful on its own.
- **Research is the first job whose entire effect lands on the player.** Training and praying change the worker; this changes only the keeper. The Library and the Training Room are not symmetrical: training also bills the keeper once a game second and can evict a creature for poverty (ADR-0083), and the research hook has no cost path at all.
- **Three bonuses, read from the adder, all added once a game second on top of a base of four per-tick amounts:**

  | bonus | variable | tabled | in the code | effective |
  |---|---|---|---|---|
  | casino big win | 143 | 35% | `4 × 35 × 4 / 100` | **+140%** |
  | tortured type | 142 | 25% | `(4 + 25) × 4 / 100` | **+29%** |
  | slapped, within 60 s | 164 | 50% | `4 × 50 × 4 / 100` | **+200%** |

  Two different slips in three adjacent lines: the casino and slap lines multiply by `variable19` twice — one factor more than "a percentage of the per-second rate" needs — while the tortured-type line **adds** where the others multiply (`ADD ECX,EAX` against `IMUL ECX,EAX` in the same position), so its 25 behaves as 29. Two of three siblings wrong in unrelated ways is the strongest argument that none of it is deliberate. Reproduced, not corrected: ADR-0002 says a quirk is reproduced, and these are in the shipped v1.70 *instructions* rather than in a shipped number, so ADR-0023's "disbelieve one shipped value" machinery does not apply and should not be stretched to cover them.
- Only the slap bonus is built. The other two need a Casino and a Torture Chamber; their formulas are recorded as constants with the arithmetic in the doc comment, so building them later is transcription rather than re-derivation.

**Consequences.**
- **A variable that was not dead.** `IncreasedWorkRateDurationFromSlappingSeconds` (60) had been written up in `tile-work.md` and in Q159's reasoning as a variable nothing reads. It is read here, and it looked dead because it was looked for in the wrong mechanism: **a slap does two unrelated things** — Efficiency +10 points for 25 game seconds, from a hardcoded 100 ticks in the slap handler (Q159, CONFIRMED off the status bar); and Research +200% for 60 game seconds, gated on the same handler's `LastSlapTick` stamp at `creature+0x4BB`. Q159's conclusion is unchanged and correct, but there really is a minute-long slap effect, and dismissing the variable was wrong. **A shipped number with no reader is recorded as "no reader found", never as "no reader"** — the standing form.
- **The loader's floor at 1 runs upward as well as down.** `dk2tools kwd research` (new) dumps the column: the Skeleton's tabled `ResearchPerSecond` of 1 becomes 4 a game second — four times its record — and the Monk's and Dark Angel's 3 become 4. `SimulationTiming.LoaderRatePerTick` had justified leaving the floor's sign-flip case alone with "every one of the ten is either zero or at least four"; three creatures carry 1 or 3. The conclusion survives (no shipped field is negative in that range, so no sign flip) but the evidence for it was false and nothing had checked it for a month.
- **"Dark Angel " ships with a trailing space** in its name field, so a lookup by name silently returns null for it. Anything that keys creatures by name has the same bug waiting.
- A keeper with a Library and a researcher accumulates a number that nothing spends. That is a visible loose end and the intended shape: the tree is a separate decision, and building half a tree would be worse than none.
- **Eleven creatures can research and the Warlock is not the best of them** — the Wizard, a hero, is twice as fast. Any later balance work that assumes the Library is the Warlock's room should read the table first.
- The level curve is the steepest production curve in the game (100% to **700%** at level 10, identical to Manufacture), so a Warlock's level matters more here than anywhere else a creature works.
- `HashVersion` and the snapshot format both 35 → **36**, pin `0xD893704FB5D00309`.
- Rejected: building the tree at the same time. The accrual is read and testable; the tree needs the research-item table, the per-keeper unlock state and the UI's ordering, none of which has been read.

## ADR-0100 — A test that needs the game data must skip without it, not pass
**Date:** 2026-09-07 · **Status:** accepted · no `HashVersion` change

**Context.** Every test reading the shipped Dungeon Keeper 2 files opened with a variant of `string? install = Dk2Install.Find(); if (install is null) return;` — which makes the test **pass** when the install is absent rather than skipping, while fourteen file headers said *"Skips when no installation is configured (DK2_DIR), so CI stays green"*: true in its conclusion, false in its verb. Measured by hiding the install (`DK2_DIR=/nonexistent`) on 2026-09-07: **266 tests — 23% of the suite — needed the data and every one was reporting PASSED without it**, which was CI's steady state, since CI has no installation. The real-data suites are the only defence against a reading drifting away from the shipped files.

**Decision.** `tests/DK2.TestSupport` (a library, not a test project) provides:
- **`[InstallFact]` / `[InstallTheory]`** — `FactAttribute` / `TheoryAttribute` subclasses that set xUnit's `Skip` when no install is found, so the runner reports **Skipped**. xUnit here is 2.9.3, which has no `Assert.Skip`; setting `Skip` from the attribute's constructor needs no extra package.
- **`Dk2InstallGate.Path`** — the install root, non-null. A gated test's body uses it directly and contains no null check, because the early return is the bug being removed.
- **`DK2_REQUIRE_INSTALL=1`** — inverts the gate: a missing install becomes a **failure** rather than a skip, for a box or CI runner that is supposed to have the data, so quietly losing it cannot go green with two hundred skips.

Converted across all five affected suites — Behavior, Data, Assets, Convert and Presentation — and verified in three directions: install present, all pass and none skip; `DK2_DIR=/nonexistent`, 266 skipped and none failed; `DK2_REQUIRE_INSTALL=1` with no install, fails with none skipped.

**Consequences.**
- **`InstallGateTests` is what stops it returning.** The only way back to the old pattern is to look the install up inside a test, so the invariant is: *no test source outside `DK2.TestSupport` may mention `Dk2Install.`* A second test asserts the gate's own decision in all three states, so the guard cannot itself go vacuous.
- A green CI run now means less than it appeared to, and that is the point: it is honest about the tests it did not run. The skip count is documented in `ci.yml` with what a change in it would mean.
- **A missing data file inside a present install is now a failure.** Helpers that returned null when, say, `Creatures.kwd` was absent now `Assert.True` on its existence, because an install that has lost a file is a broken install, not a reason to skip. Nullable loader results are asserted rather than tolerated: `LoadCampaignMap` returning null used to end the test quietly and is now `Assert.NotNull`. Fourteen file headers corrected to say what the code does.
- Three traps, kept as the lesson: **a method-level sweep missed the transitive cases** — converting every test whose own body held the gate left 63 failures when the install was hidden, from tests gated through a helper (`Level1()`, `RealDatabase()`) that kept `[Fact]`; hiding the install and reading the failures is now the standing way to check this. **The guard flagged itself**, because its doc comment names the old pattern in prose, so doc comments are stripped before scanning. And **`Dk2Install` is a prefix of `Dk2InstallGate`**, so a bare substring search flagged all 39 files using the replacement correctly — the needle now carries its dot and is assembled at runtime. A guard that fires on the right answer is worse than no guard.
- ADR-0091…ADR-0099 were reserved for the M1 record-types branch in the `DungeonKeeperRemake-codex` worktree; this took 0100 so the two could not collide.
- Rejected: `Xunit.SkippableFact` (a package for something a five-line attribute does, and it would have to be added to five test projects); and leaving CI to fail loudly without the install (it would make the repository un-buildable for anyone who does not own the game, and "runs without the original" is a property the goal review keeps).

## ADR-0091 — M1 closes on readable records, byte accountability and an executable corpus audit
**Date:** 2026-09-07 · **Status:** accepted · amended 2026-09-08: this established record *consumption*, not parameter semantics — ADR-0092 replaces it for TRIGGERS, where several widths and names were false and many named bytes have no reader in DKII.EXE · Q220–Q221 · Research: `docs/research/kwd-record-fields.md`

**Context.** M1's table promised all 18 KWD/KLD record types. Nine editor/level types were already read, PLAYERS had landed without the table noticing, and THINGS and TRIGGERS were only split at their self-describing boundary. The remaining fixed layouts had been measured against the v1.70 install but had no typed readers, so two status tables had become false in different directions.

**Decision.** M1 is closed when each record-type id has a typed parsing surface, every newly decoded record accounts for its entire measured payload, and one executable audit re-runs that claim over the whole shipped corpus.
- CREATURE_SPELLS, SHOTS, EFFECTS and EFFECT_ELEMENTS are immutable editor definitions on `GameDatabase`.
- PLAYERS, THINGS, TRIGGERS and LEVEL are level-scoped loader results. Parsing them does not place anything or execute a trigger; that boundary remains M6.
- Each new typed record retains its exact source image, and `KwdRecordRoundTrip` supplies the same byte promise for parsers predating this ADR. `ToBytes()` is intentionally conservative: it preserves unknown fields and editor padding rather than manufacturing a canonical encoding DK2 never promised.
- THINGS has one typed class per tag 194–205. TRIGGERS exposes the common tail, the complete eight-byte union and type-aware parameter views. A semantic gap is named `Unknown_<offset>` instead of becoming an omitted byte.
- LEVEL's existing ticks-per-second datum is a property of the full parser; there is one source field, not a second timing parser.
- GLOBALS(0) is supported as the record-type discriminator in a LEVEL path row. The shipped corpus contains zero such rows and zero standalone type-0 chunks; that zero stays visible in the audit and Q220 carries the unknown custom-map form. A synthetic byte-round-trip covers the known row representation.
- `dk2tools kwd corpus` is the status source: on the reference install it reads 408 files, including 66 LEVEL, 6,318 THINGS and 9,161 TRIGGERS records, and byte-round-trips every record image from all 17 record types it encounters.

**Consequences.**
- Field order, widths and semantic labels were transcribed from OpenKeeper's GPLv3 reader under ADR-0003, then checked independently against shipped strides and full-corpus consumption. No external implementation was copied. **Consumption proves boundaries, not meaning** — the sentence ADR-0092 had to replace.
- DeadBody's 16-byte payload remains a **hypothesis** because no shipped file contains tag 202 (Q221).
- The milestone table can say M1 complete without implying that M6 behaviour exists. Future format changes have three guards: the measured-stride tests, install-independent byte round-trips, and the install-gated corpus audit. The audit owns the counts so the prose no longer has to be trusted on its own.
- `HashVersion` and snapshot `FormatVersion` unchanged by this ADR: it changes static-data readers only and adds no simulation state.

## ADR-0101 — A level's placed THINGS become world contents, and three transcribed field meanings were wrong
**Date:** 2026-09-08 · **Status:** accepted · Q222–Q224 · Research: `docs/research/placed-things.md`

**Context.** M1 taught the reader every KWD record type, including the ten THINGS tags, so a level's placed entities were readable and nothing read them. Campaign level 1 loaded as a tile grid and then had test furniture placed on it by hand — `scenarios/smoke/campaign_level1_load.yaml` proved that terrain ids resolve and nothing else — while the level's own four imps, its object and its two structures sat unused in `level1Things.kld`.

**Decision.** `ThingProjection.Place(world, database, things)` in `DK2.Simulation`, taking `IReadOnlyList<ThingRecord>` from `DK2.Data.Model`, so the simulation gains no dependency on file IO. It places creatures and objects, recognises rooms as already-in-the-grid, and **counts** everything it cannot place yet. Scenarios opt in with `setup.placed_things: true`, off by default because turning it on for an existing scenario changes what that scenario measures.

Three field meanings came from the external reader and were wrong:
1. **Coordinates are tile indices, not the simulation's subtile units.** The fields are 32-bit ints and the simulation carries positions at 4096 units to the tile, so reading them as subtile units type-checks, runs, and puts every entity in the level inside tile (0,0). Settled by the corpus in one line: coordinates run x 0..118 and y 0..102 against a largest map of 121×105, and not one of the 6279 records lands outside its own level's grid, where under the subtile reading every one would.
2. **`InitialHealth` is a percentage of the maximum, not a hit-point count.** Of 2891 placed creatures **2851 carry exactly 100**, the rest carry round percentages between 5 and 80, and nothing anywhere exceeds 100. The absolute reading is dangerous precisely because it is plausible — it would spawn every creature in the game at 100 health, which for a Goblin looks like a slightly weak Goblin rather than a defect.
3. **`RoomThing.RoomType` is a terrain id, and a ROOM record is not a placement.** Only four values occur — 14, 33, 34, 37 — the terrain ids of the Dungeon Heart and the three Hero Gates. Read as room ids, 14 would be the Graveyard and the other three would not exist, the room table stopping at 26. All 275 ROOM records agree with the terrain the MAP already holds under them, so the record annotates the grid rather than placing anything; projecting one as a placement would build every heart and hero gate in the campaign twice.

Three for three on the THINGS field names this projection had to rely on — the same result the `FillInHealth` episode produced (Q219), and the reason `codex/trigger-semantics` was sent to check the TRIGGERS parameter names against the binary rather than trusting the same source (ADR-0092).

**Consequences.**
- Two smaller readings: **a hero party is a fixed array of sixteen member slots**, an empty slot being creature id 0 — counting the empty ones is what first made the percentage claim look false, because an empty slot's `InitialHealth` is 0. And **health must be applied after the level**, since maximum health is the record's `Hp` scaled by the level curve; the wrong order leaves a level-10 creature at a level-1 maximum, which reads as a wounded creature rather than an ordering bug, so it is pinned by its own test.
- **1944 creatures and 788 objects** across the corpus project, and every one of the 66 levels projects without the database refusing an id — the sweep that would catch a creature or object id this reading got wrong, since both spawns refuse an unknown id rather than placing a nameless entity.
- **3272 records still have nowhere to go**: 1152 action points, 959 traps, 940 doors, 220 hero parties and one effect generator. The count is *returned* from the projection and reported as a scenario caveat, because a projection that silently ignores two thirds of a file looks identical to one that works. Q222–Q224 carry what each needs.
- **No floor on placed health**, a decision rather than an oversight: the clamp could never fire (smallest `Hp` 240, smallest percentage 5, curve only scales up), and the guarantee is asserted in a test instead.
- `dk2tools kwd things` keeps every count above re-checkable, and returns non-zero if a coordinate ever falls outside its level.
- Also corrected here: the scenario runner printed, on every tile-work run, *"What is not settled is fill-in, whose cost DK2's own compare makes one application and which nobody has watched (Q22)"*. **Q219 established that DK2 has no fill-in mechanism at all** — `FillInHealth` has no reader in the executable — and `TileWorkKind.FillIn` was deleted the same day. The caveat is dropped rather than reworded: there is nothing left unsettled about it.
- No `HashVersion` change (nothing added state to the world hash) and no `FormatVersion` change (nothing was added to a snapshot). ADR-0092…ADR-0099 remained reserved for the `codex/trigger-semantics` branch.

## ADR-0092 — Trigger parameter views follow DKII.EXE reads, not editor labels
**Date:** 2026-09-08 · **Status:** accepted · replaces ADR-0091's parameter-semantics sentence for TRIGGERS · sharpened by ADR-0102 (the byte named `Flag` is an opcode) · Q225–Q227 · Research: `docs/research/trigger-semantics.md`

**Context.** ADR-0091 made the 16-byte TRIGGERS structure load-bearing but left one unsafe sentence: it described parameter names and widths as externally transcribed and then checked by corpus consumption. Consumption proves boundaries, not meaning. DKII.EXE has context-specific world, player, creature and area dispatchers; each reads the type byte and only a subset of the eight-byte body.

**Decision.**
- `KwdTriggers.Parameters` exposes the offsets and widths DKII.EXE reads for each type. Signed comparison operands are signed in the view.
- A byte with a binary-established role keeps or gains its semantic name. A read whose role is not established is `Unknown_<offset>`, or retains an explicitly documented transcribed name.
- A byte not read on that type's complete dispatch path is `Unreached_<offset>`. It remains available through `Body` and round-trips: **`Unreached` is a reachability claim, not permission to discard the byte.**
- `dk2tools kwd trigger-types` is the reproducible scope audit, enumerating counts and level membership for every shipped tag/type combination. Types absent from the retail corpus remain visibly uncorroborated even where the executable contains a handler.
- The dispatcher and per-parameter evidence is research, not observed gameplay, so its maximum confidence is `StronglyInferred`.

**Consequences.**
- The reader no longer presents untouched editor residue as a gameplay input. Known false readings are removed: the shifted generic selectors, generic type 47's player/value interpretation, generic type 52's player label, action type 4's 32-bit objective id, action type 8's value, and action type 24's three-way interpretation of its second dword. Remaining external names are listed rather than silently promoted.
- M6 can design execution against a byte-accurate, provenance-labelled surface without this ADR deciding when or how triggers run. Trigger execution remains out of scope.
- `HashVersion` and snapshot `FormatVersion` unchanged by this ADR: static-data views and audit tooling only.

## ADR-0102 — Trigger flags and timers are level state, and the byte called `Flag` is an opcode
**Date:** 2026-09-08 · **Status:** accepted · amended 2026-09-08: an authored timer threshold is in **seconds**, not ticks (Q233 answered — the loader multiplies by variable 19 once, in place), and type 1's byte layout is corrected · Q228–Q231, Q233–Q234 · Research: `docs/research/trigger-state.md`

**Context.** ADR-0092 established every trigger parameter's offset, width and signedness from DKII.EXE but deliberately stopped short of execution. The parameters address state — flags, timers, action points — and the world had none of it. Flags and timers are **38% of all trigger traffic** (3,472 of 9,161 records), which makes them the first thing worth building and the largest single step toward "the level is winnable". The blocking question was scope, not mechanism: a per-player flag bank and a level-global one are the same code and different state, and the difference is invisible until a determinism hash diverges or one keeper's script quietly overwrites another's.

**Decision.** `TriggerState` on `WorldState`: **128 flag bytes** (`FlagCount`), **16 timer dwords** (`TimerCount`), one opaque special target. Hashed, snapshotted, and level-global.
- Level-global, from four readings: only two functions index either bank, and both are trigger dispatchers; every call site of the action dispatcher loads `this` from a global pointer; **the player-context dispatcher `0x004C8620` — invoked for one player, and holding that player as its own `this` — deliberately does not pass it**, loading the global instead before delegating, which is the one call site that would have revealed a per-player bank and says the opposite; and neither global is per-player, each written once or twice at construction. The scan walked every initialised block including `cseg`, and its positive control at `0x006F2590` returned the expected 226.
- **The byte named `Flag` is an operation and addressing mode, not a flag.** On action type 7 its bits select literal-versus-indirect operand (`0x02`), assign-versus-arithmetic (`0x08`) and add-versus-subtract (`0x10`); on generic types 1 and 2 the same idea sits in a different byte with a different bit. Those names were on Q226's list as "access known, name unverified" — the name was worse than unverified, it pointed at the wrong kind of thing.
- **A flag is a byte, so the 32-bit operand is truncated.** ADR-0092 recorded `Value:u32@04` correctly; what it could not say is that only the low eight bits reach a flag. The full dword reaches only flag id 128.
- **Add and subtract are signed; every read zero-extends.** A flag driven below zero reads back as 128..255, so a condition comparing it against 0 sees a large positive number. That is DK2's asymmetry and it is reproduced, not fixed.
- **A timer stores when it started, not how long is left.** Elapsed time is derived by subtraction on the condition side, so nothing needs decrementing once a tick — which also means this adds no per-tick work to the simulation.
- Not modelled: **flag id 128 is not a flag** — both dispatchers divert it to a separate 32-bit target with a companion byte and a virtual call, and what it is remains **UNKNOWN (Q229)**, so it is carried as an opaque value rather than given a plausible name (naming a mechanism from the shape of its handling is what made `FillInHealth` and creature flag bit 30 look right). And **an id above 128 is refused**, where the original would index past the flag bank into the timers; no shipped level does it, so this raises rather than reproducing an overrun nobody has watched (Q230).

**Amendment (2026-09-08) — an authored timer threshold is in seconds, and this ADR first said ticks.** Corrected by observation of the running game: a memory reader was attached to a live DKII-dx.EXE and two plays of campaign level 1 were captured at the tick rate, sampling the flag bank, the timer bank and the tick counter, then correlated against level 1's own trigger graph by predicting, for each type-2 condition, the tick at which the specific flag its subtree writes should change. Under a seconds reading the residual is a constant `+1` tick — the sampler's lag — in four of four measurements across two runs, two timers and two thresholds; under a ticks reading it is exactly `3 × threshold + 1` in all four, which is one missing multiply by variable 19, not noise. Twelve further conditions predicted flag writes that a ticks reading requires and that never happened. **`Value=600` is ten minutes, not 150 seconds.**
- What was retracted is an inference, not a disassembly: there is still no multiply in the trigger comparison and no reference to variable 19's global in `0x517400`–`0x519A00`. The scan that produced the wrong inference was sound over every block it covered and was filtered to a `0x2600`-byte window; `FUN_0049AC80` was already on a list of functions that reference variable 19 *and* call the clock, and it sits outside that window. **A scan proves nothing outside its range.**
- **Q233 answered the same day.** The conversion is at `0x0050C2DB`, in the level trigger initialiser `FUN_0050B5B0`: it walks the condition array at `+0x7308` and the action array at `+0x730C` with a 20-byte stride, and for condition types 2 and 34 and action types 16 and 23 it does `IMUL` by variable 19 on the dword at `record+4` and writes it back **in place**. Found by rerunning the failed scan with no range filter — 225 sites in 82 functions, positive control intact. So the `.kld` holds seconds, the loader scales once, and the comparison consumes ticks. **Nothing read from the binary was wrong; what was wrong was assuming a single unit for a value that passes through a transform.**
- **Q233 then confirmed by observation.** The condition array was read out of the running game with level 1 up: **356 conditions and 326 actions**, matching the `.kld` exactly, and **all 22 timer thresholds stored as the authored value times four** — id 10 holds 32 for an authored 8, id 41 holds 600 for 150, id 157 holds 840 for 210. The load-time multiply is no longer an inference. The seconds reading rests on **13 direct measurements** across four captures, every residual exactly `+1` tick and none for ticks. Two are qualitatively stronger than a timing match: **a chain** — condition 41 starts timer 5 and condition 153 on timer 5 writes flag 40, predicted at ticks 608 and 768, observed at 609 and 769, the second right only because the first was; and **a disagreement about which flag** — condition 10's branches are `flag0 == 0` and `flag1 == 1`, flag 0 changes during a run, and in two captures the ticks reading requires flag 1 to be written and it never was.
- **A claim in the research doc is withdrawn**, and the reason is a second byte on the wrong field: the "twelve predictions agreeing by silence, with every gate a `flag0 == 0` test" argument was read off **byte 03 of the flag condition, which is the indirect operand and is zero in 1374 of 1375 shipped records** — so every gate looked like flag 0 and none was really checked. The conclusion was right and the argument was not; the 300-second captures replaced it with direct measurements that need no gate analysis.
- **Type 1's byte layout is corrected in `KwdAdditionalRecords`**, from the handler at `0x0051845B`: byte 01 is the flag id (indexed into `+0x3F70`), byte 02's low bit selects the operand — set means the immediate at `+4`, clear means `flags[byte 03]` — and byte 03 is the indirect flag. Same correction already applied to type 2, missed because only type 2 was re-checked when that one was found. Pinned by a test written to fail under the old reading: byte 01 yields 91 distinct flag ids across the campaign, byte 03 yields two.
- **A live mechanism with a population of one.** Exactly one shipped record has byte 02's low bit clear — level 3, trigger 18: `flag 3 > flag 4`, repeating — and it is the same record with a non-zero byte 03, which is why the decode is not a coincidence. `TriggerConditionEvaluator` handles that indirect comparison and tests it against the shipped record.
- Also confirmed from the same captures: the clock counts ticks at 3.94/s with variable 19 read live as 4 (ADR-0089); a stamp in the timer bank is a tick count, caught five times across two runs, each holding exactly the tick it was set at; and both cached world pointers `0x006E5050` and `0x006E5054` hold `0x006F2A08`, so they are two caches of one pointer rather than two subobject pointers (Q231, closed). **Timing, from two 300-second runs:** steady state is 3.93 to 3.95 ticks/s (253 to 254 ms); the opening ~60 s of a fresh level runs slower (3.69) and everything after is flat; worst single interval in 300 s was 335 ms. The few ms above 250 track the host, so the simulation keeps 250 ms exactly and ADR-0089 stands on three independent measurements.
- **Three offsets are named** and one earlier note corrected: `+0x730C` is the **action** array specifically, not "the trigger array"; `+0x7308` is the condition array; `+0x731A` is the root trigger record's `RepeatCount`, copied from byte `+0xF` of the record the root id resolves to. The in-memory record is the 16-byte `.kld` payload verbatim plus four runtime bytes whose purpose is UNKNOWN.

**Consequences.**
- `TriggerState.HasElapsed(timerId, seconds, now)` takes **seconds** and multiplies by `SimulationTiming.TicksFromSeconds`. That is correct at the authored boundary and stays. When the `.kld` trigger loader is written the conversion must happen exactly once — either there, matching the original, or at the comparison, but not both. The original's choice has one visible consequence: because it rewrites the record in place, the scaled value is what a save game would contain.
- **The pinned determinism hash moved** because 128 flag bytes and 16 timer dwords enter it even when every slot is zero. That is the intended cost of putting them in the hash: an action writes a flag and a later condition reads it, so two worlds disagreeing about one diverge only when that condition is evaluated — late, and far from the cause. Moved `HashVersion` and snapshot `FormatVersion` 36 → **37** (pin at the time `0xD17C2E424DDE0AF2`); the current value lives in `src/DK2.Simulation/GameSimulation.cs` and the pin in `DeterminismTests`.
- Two later bumps sit on top of this decision and are recorded where the versions are: **38 is deliberately skipped**, reserved for the action-point branch in the codex worktree so the two cannot collide, and **39 corrects the timer bank from 256 slots to sixteen** — 240 fewer dwords in the hash — on two agreeing readings, that the next field on the owning object sits `0x40` bytes after the bank and that the condition path compares the id against `0x10` before indexing. `TriggerState.TimerCount` is the sixteen.
- `TriggerConditionEvaluator` is pure and evaluates one type-1 flag or ordinary type-2 timer record, preserving `NotEvaluated` for unsupported types and the UNKNOWN timer sentinel. It deliberately does not walk the tree or run actions. Q228 established the once-per-turn cadence; `trigger-tree.md` records the separately researched traversal semantics.
- Action points are now modelled (**ADR-0103**), and this ADR's reading of them was wrong: actions 16, 17, 27 and 31 do **not** write an `Available` byte, and the runtime object has no such field. The claim came from four parameter views whose second or third byte was transcribed with the same name; following the stores refutes it. Version 38, reserved above for that branch, was overtaken — integration took **40**.


## ADR-0103 — Action points are ordered world state, without an availability field
**Date:** 2026-09-08 · **Status:** accepted · corrects ADR-0102's availability consequence · Q222 answered, Q235–Q238 opened · Research: `docs/research/action-points.md`

**Context.** All 1,152 shipped action points were parsed and then discarded by the THINGS projection. ADR-0102 additionally stated that trigger actions 16, 17, 27 and 31 write an action-point `Available` byte — a conclusion drawn from a repeated transcribed parameter name rather than from following the stores.

**Decision.** `WorldState.ActionPoints` holds every point in THINGS-file order, because several original lookups walk the collection linearly. Each carries the low-word inclusive rectangle, action-point id, trigger id, next-waypoint id, the five flag bits the loader copies (`0x3D`), and the saved passage counter and wait timer. All of it is hashed and snapshotted.
- **No `Available` property.** None of the four actions writes the object: type 16 updates a separate four-slot world area bank, type 17 selects an area operation, type 27 builds a camera/event command from the rectangle centre, type 31 builds a path command. The runtime layout has no availability field.
- **`WaitDelay` is multiplied by the original tick rate once, at projection**, reproducing the loader at `0x00521221`, which `IMUL`s by variable 19 and stores only the low 16 bits — the truncation included. Third instance of the load-time-multiply idiom behind Q233.
- The **name** is not runtime state and is not hashed. **Trigger repeat count** is omitted until trigger records are projected, because the original derives it by resolving the record and inventing a default would be worse than leaving the dependency explicit.

**Consequences.**
- Level 1 projects 27 action points and leaves only its two hero parties unread; across 66 levels 1,152 enter the world and the unprojected count falls from 3,272 to 2,120.
- `HashVersion` and snapshot `FormatVersion` → **40**, stepping over the 38 this branch had reserved before master independently took 39. Pin `0x81D58B966898BEC8` → `0x6445940E0B0F23A9`.
- The loader flag mask is pinned by a test that can fail: the corpus authors bits `0x7D`, so bit 6 ships and is discarded, while bits 1 and 7 are never authored and rest on the disassembly alone. Every other assertion compared the constant against itself.
- **No action-point behaviour runs.** Flag meanings, timer and counter consumers, rectangle-entry cadence and waypoint traversal are Q235–Q238.

## ADR-0104 — Player gestures are translated outside Unity
**Date:** 2026-09-09 · **Status:** accepted

**Context.** `DungeonRunner` advanced the simulation with empty input, while
Unity-side gesture rules would evade the normal test suite and violate ADR-0036's
arithmetic-only adapter boundary.

**Decision.** `DK2.Presentation` owns `PlayerIntent`, `PlayerInputSnapshot` and a
stateful `PlayerInputTranslator`. The translator turns button edges into one
`InputFrame`: tag, untag, room-build and room-sell tools complete a row-major
rectangle on release; hand, slap and Create Imp tools act once on press. A hand
holding something drops first; otherwise a creature wins over an object on the
same tile. These are project interface choices, not claims about DK2.

Unity selects tools, projects a screen ray onto the ground plane, supplies
button edges, queues commands until the next whole simulation tick, and handles
camera arithmetic. Campaign THINGS use the existing projection so entity tools
have starting targets; saved-game entities remain unavailable because their
records are undecoded.

**Consequences.** All eight existing `PlayerCommand` types are reachable from
the Unity view without Unity types entering testable logic. Input changes no
simulation state shape, so `HashVersion` and snapshot `FormatVersion` do not move.

## ADR-0105 — The area walker, and five area conditions read off our own tile grid
**Date:** 2026-09-09 · **Status:** accepted · Q237 closed, Q246–Q248 opened · Research: `docs/research/trigger-walkers.md`, `docs/research/trigger-semantics.md`

**Context.** Three of DK2's four trigger walkers ran. The fourth belongs to the action point: 287 roots across the campaign, a five-instruction frame at `0x004B05E0`, and a dispatcher of its own at `0x004B0D70` owning seven condition types that all measure over the point's rectangle. Action points were already hashed world state (ADR-0103), so nothing new had to be modelled to walk them.

**Decision.** `TriggerExecutor` takes the world and the database, walks every action point that carries a root — after the party roots and before the players, the frame's own order — and evaluates the area context in `TriggerAreaConditions`.
- **The repeat argument is snapshotted at construction** from the root record's own byte, because DK2 snapshots it on the point at `+0x36` when the root is attached (`0x004B0C90`) rather than re-reading it each turn as the player frame does.
- **Five of the seven types are computed:** 40, 41, 42, 62 and 69. Types 39 and 70 count things in the rectangle through a filter we have not read and are declined, so they stay in the blocked census.
- **62 and 69 read our per-player tag bit** where DK2 reads a per-player task pool. The pool holds one entry per marked tile in two kinds and both conditions count both kinds, so the quantity is the same one.
- **No area action dispatcher exists**; the walker calls the world's directly, which the executor already did for every non-player context.

**Consequences.**
- `HashVersion` and snapshot `FormatVersion` → **44**. Pin `0x6E963365B44FD724` → `0x48D7950540013015`.
- Level 1 walks 15 more roots and its event set does not move: 2 agreeing, 46 DK2-only, 1 ours-only, unchanged. `docs/EXPERIMENTS.md` said the action-point walker would close that one false positive; it does not, and the corrected diagnosis is that both flags it needs are set by a human playing.
- Ownership rests on the tile's owner being what DK2 compares (Q247), and off-map rectangle tiles are skipped rather than read (Q248). Neither is reachable in the shipped campaign.
- Only the creature walker is left, and it needs creature state the executor does not have.

## ADR-0106 — Doors and traps are verbatim ordered world state without behaviour
**Date:** 2026-09-09 · **Status:** accepted · Q224 answered, Q249–Q253 opened · Research: `docs/research/doors-and-traps.md`

**Context.** All 940 doors and 959 traps in the shipped THINGS files parsed and round-tripped, then `ThingProjection` counted every one as `NotYetProjected` because `WorldState` had no storage for either type. Their runtime layout and behaviour were not read for this branch.

**Decision.** `WorldState.Doors` and `WorldState.Traps` hold every record in THINGS-file order. Projection copies every parsed field verbatim: no coordinate conversion, validation, flag mask, shot filtering or scaling. Every field is hashed and snapshotted.
- The three-byte door unknown is copied into world-owned storage rather than aliasing a parser buffer.
- All 940 door `TriggerId` values are zero. Doors therefore add no trigger walker; the existing five root sources are complete for the shipped corpus.
- Verbatim storage is conservative: it loses no information, but may hash a bit DK2 discards. Q250–Q253 keep that risk explicit for flags, shots and the four unknown fields.

**Consequences.**
- The shipped corpus projects 940 doors and 959 traps across 48 levels. Only 220 hero parties and one effect generator remain `NotYetProjected`.
- `HashVersion` and snapshot `FormatVersion` move 44 → **45**; pin `0x48D7950540013015` → `0x57E013D74DC65BAA`.
- **No behaviour is invented.** Doors do not open, close, lock or block movement. Traps do not arm, fire or consume shots. All such behaviour is UNKNOWN (Q249).

## ADR-0107 — An action returns what its dispatcher returns, whether or not we perform it
**Date:** 2026-09-09 · **Status:** accepted · Research: `docs/research/trigger-semantics.md`, `docs/research/trigger-walkers.md`

**Context.** The executor performs three action types and declined the rest, which is wrong at the walker: DK2 decides an action's return value from the jump table, not from the arm. The world dispatcher has one success exit and one `Unhandled Trigger When` exit; the player and creature dispatchers each own arms that succeed before delegating. So a type nobody has implemented still has a known, correct answer.

**Decision.** `RunAction` gives DK2's answer for every action type, performed or not.
- Player-owned and world-succeeding types return true and are counted in `TriggerRunResult.Partial` — the walk continues, the effect is missing, and the report says so.
- Types the reachable dispatcher chain refuses go to a new `TriggerRunResult.Unhandled`. That is not a gap of ours: the retail engine stops there too.
- `Blocked` therefore holds conditions only. Declining a condition is indistinguishable from it being false, and no dispatch table can supply the answer.

**Consequences.**
- `HashVersion` and snapshot `FormatVersion` move 45 → **46**; pin `0x57E013D74DC65BAA` → `0x52A068B601960E53`.
- **It buys no reach, and that is measured.** All 5,408 shipped action records are leaves, so no shipped walk descends past one. `dk2tools kwd oracle` is byte-identical before and after. What it does buy is the one-shot retire DK2 performs on a successful action, and a census that separates our gaps from DK2's.
- Level 1's turn-1 blocked census drops from 16 type groups to 10, all conditions; seven action groups move to `Partial`.
- The creature action dispatcher (`0x0049AC80`, base 15) was found while checking this and owns ten of the types the world refuses, type 15's 565 records among them. A type is not dead because the world has no arm for it.

## ADR-0108 — Hero parties and effect generators are verbatim ordered world state
**Date:** 2026-09-09 · **Status:** accepted · Q254–Q259 opened · Research: `docs/research/hero-parties-and-effect-generators.md`

**Context.** The remaining 221 parsed THINGS records — 220 hero parties from the 48-level campaign and one effect generator — were counted as `NotYetProjected` because `WorldState` had nowhere to keep them. In the full 66-level shipped sweep, parties occur on 35 levels.

**Decision.** `WorldState.HeroParties` and `WorldState.EffectGenerators` preserve THINGS-file order and every parsed field verbatim. Nested party members and both byte/list fields are copied into world-owned storage. Every value and list length is hashed and snapshotted.
- Party members are unspawned `GoodCreatureThing` templates, not placed creatures. Projection does not call `SpawnCreature` or increment its creature count.
- Effect ids remain variable-length. The fixed two-byte member tail and six-byte generator tail validate their lengths because snapshots encode neither length.
- `TriggerTree` continues to build its 24 party slots from the source `HeroPartyThing` records. Q256 records the temporary two-home representation.

**Consequences.**
- All parsed shipped THINGS records now project; `NotYetProjected` is zero across the corpus.
- `HashVersion` and snapshot `FormatVersion` → **49**. Written against 47, which ADR-0110 had taken by the time this merged; `hash-versions.md` records why neither 47 nor 48 is what shipped.
- **No behaviour is invented.** Parties do not spawn and generators do not generate. Both remain UNKNOWN (Q254–Q255).

## ADR-0109 — Eight player conditions are answered false, because the binary answers false
**Date:** 2026-09-09 · **Status:** accepted · Research: `docs/research/trigger-walkers.md`

**Context.** The blocked census is the work queue for trigger conditions, and 163 records in it were not work. The player condition dispatcher's jump table sends types 48, 53 and 56–61 to `0x004C850C` — which is not an arm that computes false but the dispatcher's own zero-return epilogue: `XOR EAX,EAX`, five pops, `RET 4`. Nothing is read; nothing can vary. Type 73 shows the difference, reaching the same instruction only by falling through a test of the player's `+0x134`.

**Decision.** `EvaluateCondition` returns `Unsatisfied` for those eight types in a player tree, where it returned `NotEvaluated`. They leave `TriggerRunResult.Blocked`.

**Consequences.**
- **No behaviour change and no hash change.** False and declined both stop the descent; the walk is identical. `HashVersion` stays 46.
- The stall frontier across the campaign falls from **777 distinct conditions to 616** (`dk2tools kwd oracle`), and `moved`/`after1` are unchanged, as predicted.
- Level 1's turn-1 blocked census loses types 53, 56, 58 and 60.
- Only in a player tree. The world dispatcher has no arm for these types at all, so a world-tree record of one stays blocked — which is what the test pins.

## ADR-0110 — A player tree knows which player it is, and type 30 is that player's gold
**Date:** 2026-09-09 · **Status:** accepted · Q260–Q263 opened · Research: `docs/research/trigger-semantics.md`

**Context.** `TriggerTree.PlayerRoots` was a list of trigger ids. DK2's frame walks a player's root with that player as the condition dispatcher's `this` (`0x004B9F10`), so a root without its owner cannot answer any of the 30 player-owned condition types — about 700 shipped records. Both production call sites had the `PlayerDef` in hand and dropped the id.

**Decision.** `TriggerPlayerRoot(PlayerId, TriggerId)` replaces the bare id, `TriggerRun` carries the player being walked, and condition type 30 is evaluated as that player's gold.
- **Type 30 is gold**, on two independent readings that agree. Its arm loads `[player+0x7E]` and compares (`0x004C841F`); the player frame rebuilds that dword by summing every Treasury and Dungeon Heart the player owns (`0x004B9F57`), which is the quantity `GoldStore.Total` already holds and `rooms-and-gold.md` established from the data side.
- **No one-turn lag.** The frame's sum runs after the tree walk, but it is a resync: `FUN_004BA420` writes `+0x7E` on every deposit and withdrawal, so the condition reads the live total.
- Types 31, 32 and 71 have the same minimal arm and are still declined — the offset each reads is unidentified (Q260–Q262).

**Consequences.**
- `HashVersion` and snapshot `FormatVersion` → **48**, and to 49 when ADR-0108 merged behind it.
- The campaign stall frontier falls **616 → 606**; no level's flags move, so every shipped gold test is false in a headless run.
- **A deviation, deliberately:** DK2 skips a Dungeon Heart whose `+0x9A` bit 0 is set and we do not (Q263), so a heart we count might be one DK2 does not.


## ADR-0111 — Player fields are read from DK2's own debug dumper, and type 32 is mana
**Date:** 2026-09-09 · **Status:** accepted · Q261, Q262 answered; Q264–Q266 opened · Research: `docs/research/player-object.md`

**Context.** Three player conditions — 31, 32 and 71 — have the same three-instruction arm: load one dword off the player, compare, return. The whole question is which field, and the offsets alone said nothing (Q260–Q262). `FUN_004BCF10` turned out to be a debug dumper that prints ~90 player fields, each with **the original's own name** in its format string.

**Decision.** `player-object.md` transcribes the map, and it is the reference for any future player offset. Condition type 32 evaluates as the walked player's mana.
- `+0x82` is `CurrentMana`, so type 32 is mana. `+0xEE6` is `NumberOfSlaps`, so type 71 is slaps.
- `+0x7E` prints as `Money`, which **confirms ADR-0110's gold reading by name** where that rested on the room types the frame sums.
- **Type 71 is not implemented.** This project counts no slaps per player, so its two shipped records stay blocked rather than being answered from a number we do not keep.

**Consequences.**
- `HashVersion` and snapshot `FormatVersion` → **50**; pin `0xDDC3EA20017640B6` → `0x98DE8BA2B3BDC7BF`.
- A name is not a mechanism. The dumper prints no member of the score block at `+0x142`–`+0x16E`, so type 31 stays open (Q260), and its `ResearchCompleted`/`ResearchPoint` labels disagree with a comment this repo wrote from the adder's arithmetic — recorded as Q265, not acted on, because an arithmetic path outranks a name.

### ADR-0111 amendment — all ten dumpers, not just the player's
**Date:** 2026-09-09. The technique repeated. 1,144 calls to the log `printf`
cluster into eleven functions: ten object dumpers and a profiler. All ten are
transcribed, indexed by `docs/research/debug-dumpers.md`, and they are the
reference for any runtime offset on those objects.

The creature's 199 fields and its job/needs object's 64 matter most: that is the
address range where `AGENTS.md` records Flame naming only 34% of symbols, and it
is what Goal 5 emphasises. The level-statistics dumper also answered Q260 — the
player's twelve score counters are its first twelve fields, and type 31's is
`GoldMined`. Nothing is implemented from a name alone.


## ADR-0112 — Gold mined is a keeper's counter, and it is credited to whoever swung
**Date:** 2026-09-09 · **Status:** accepted · Q260 closed · Research: `docs/research/player-object.md`

**Context.** Condition type 31 compares `[player+0x15A]`, which the level-statistics struct names `GoldMined` — 9 shipped records across 29 levels, the second-largest player-condition block. Nothing counted it. Both halves of the mechanism already existed: `TileWorkRules` computes the gold a swing frees, and `TileWorkSystem` already reported the amount.

**Decision.** `PlayerState.GoldMined` accumulates it, hashed and snapshotted, and condition type 31 compares it.
- **Credited to the miner's owner**, not to whoever ordered the work. DK2 adds to `[creature+0x2A]` at `0x004973CF`, and the two differ whenever a keeper orders work an allied imp performs.
- **Gold released, not gold banked.** The original credits before it asks whether the imp can carry any, so a full imp still adds to the total — which is the `uncollected` gold `TileWorkSystem` already reports.
- **No worker, no credit.** DK2's adder is inside the creature's mining routine, so work the harness queues with no swinger credits nobody.
- Only ever added to. Nothing spends it, exactly as `ResearchPoints` behaves.

**Consequences.**
- `HashVersion` and snapshot `FormatVersion` → **51**; pin `0x98DE8BA2B3BDC7BF` → `0x5C6A03ED5BD250BC`.
- **The first change today that moves the oracle.** Campaign-wide `moved` 54 → 55 and `after1` 9 → 10, stalls 606 → 599 — and the level that moved is **level 1**, the one with a live capture: 3 → 4 columns, 1 → 2 past turn 1, its blocked census down to four condition types.
- Unchecked: whether a gem seam's `GoldMinedFromGems` yield is credited the same way.

## ADR-0113 — Counting a keeper's creatures, and why the total excludes its workers
**Date:** 2026-09-09 · **Status:** accepted · Q268, Q269 opened · Research: `docs/research/trigger-semantics.md`, `docs/research/player-object.md`

**Context.** Condition type 19 is the largest unimplemented player-condition block in the corpus — **205 records across 29 levels**, more than gold, mana and gold-mined together. Its arm is `0x004C7B9D`, reached through the player dispatcher's byte table at `0x004C85E8` (base 19, so `DispatchMap.py`'s printed index is the true type minus 18).

**Decision.** Type 19 counts the creatures the walked keeper owns, by walking `WorldState.Creatures` and filtering on `OwnerPlayerId`.
- **`CreatureId` 0 does not mean "no filter" — it means the total, and the total excludes workers.** That path takes `TotalNumberOfOwnedThings[0]` and walks `ThingsOwnedList[0]` doing `DEC EAX` once per creature whose definition sets bit 0 of `+0x686` (`0x004C7BD9`). Bit 0 is `CreatureFlags.IsWorker`, and across the shipped roster **exactly Imp and Dwarf** set it — the keeper's worker and the heroes' — which is the roster a worker flag should select, so the bit-order source is confirmed by the data and not merely trusted.
- **A named creature id counts workers normally**, because that path reads a cached per-type total through `CPlayer::GetNumberOfCreaturesOfType` (`0x004BA7D0`, named by its own error string). Asking for imps returns imps. The asymmetry is DK2's, not ours.
- **Type 20 is declined, not answered.** Its arm `0x004C7C23` is the same walk plus a filter on `creature+0x28A` — a three-valued field this simulation has no equivalent for (Q269). Both its two shipped records compare with `>=`, so an over-count would fire them early; leaving them blocked is the honest answer.

**Consequences.**
- **The largest single movement the oracle has recorded.** Campaign-wide `stalls` **599 → 417**, `moved` **55 → 62**, `after1` **10 → 11**. Level 1's blocked census drops type 19 and gains nothing.
- No hashed state, so no `HashVersion` bump: the count is derived from creatures already hashed.
- Unverified: whether DK2 drops a creature from the owned list while a hand holds it (Q268). Ours does.

## ADR-0114 — Player room conditions count instances and sum their size
**Date:** 2026-09-09 · **Status:** accepted · Q270 opened · Research: `docs/research/room-conditions.md`

**Context.** Types 24 and 25 account for 109 shipped player conditions. Their
room-list mechanics are established, and the simulation already derives the
same owner/type room instances from the map.

**Decision.** Both conditions use the existing `PlayerCounter` and shared
comparison path. Type 25 counts owned room instances; type 24 sums their
`RoomInstance.TileCount`; live room captures later confirmed the original
`Size` is the slab count (`room-list.md`). A zero `RoomId` selects every room
type. The derived room list is built lazily once per `TriggerRun`.

Type 68 remains blocked pending evaluator wiring; ADR-0146 subsequently added
the `TotalCapacity` equivalent. Type 25's `Comparison == 0` availability-table
sub-mode also remains blocked: it is a different question, and none of the 85
shipped type-25 records uses it.

**Consequences.** No state was added, so hash and snapshot versions stay 51.
The rebased `kwd oracle 200` measurements are recorded in the commit.

**Amended 2026-09-09.** The per-`TriggerRun` cache rested on an unchecked claim — that no action this executor performs can alter terrain. It is true (only types 7, 8 and 14 are performed) but nothing would have noticed it becoming false, which is the shape of three long-lived defects in this repo. `TriggerExecutor.Performs` now names the set and two tests hold it: one pins the set so a fourth type is a failure here rather than a silence, and one digs a room between turns to prove the cache does not outlive one. The second was checked against a mutant that hoists the cache to the executor.
## ADR-0115 — Type 39 counts things in a rectangle, and two of its three selectors were misnamed
**Date:** 2026-09-09 · **Status:** accepted · Q271 opened · Research: `docs/research/trigger-semantics.md`

**Context.** Condition type 39 is the largest block in the corpus at **183 records**, and it was the top blocked type on every level sampled. Its arm is `0x004B0D9A` on the area dispatcher, which unpacks four selector bytes and delegates to `FUN_004B0620`; the predicate is `FUN_004B0A80`.

**Decision.** Type 39 counts the things standing in the action point's rectangle, filtered by owner and type, and compares the count. Built in `TriggerAreaConditions` beside the five tile counters that share the dispatcher.
- **Byte 03 is not a player id.** The arm reads it only to pick a category — `6` selects objects, every other value selects creatures — then overwrites the register. Of 183 records, 110 hold 0 and 70 hold 3, and **those two behave identically**; only the 3 records holding 6 differ. Renamed `ThingType`.
- **Byte 01 is the player**, matched in the predicate against a player field. Renamed `PlayerId`, which also puts the player at byte 01 like every other area condition. Its shipped values are the ordinary player ids — 3 (111), 1 (53), 0 (16), then 2, 4, 5 — where the old name `TargetId` explained none of that.
- **Byte 02 is the thing's own type id**, read as the creature's `+0x36F` or the object's `+0x12C` according to the category. Renamed `TypeId`. Zero means "any" on both it and the player, and both zero passes everything.
- A held object is not counted: DK2 walks each tile's thing list and a thing in a hand is not on one.

**Consequences.**
- Campaign-wide `stalls` **417 → 279**. `moved` and `after1` do not change, and the level-1 event set against played run 5 stays 2 / 46 / 2 — the frontier moves, the reachable script does not.
- No hashed state, so no `HashVersion` bump.
- **Type 70 keeps the old labels.** It has its own arm at `0x004B0DD7`, unread, so renaming its identical-looking bytes would be a guess.
- Unresolved: the predicate matches the player's `Number` (`+0x10FC`), not the `TagId` (`+0x04`) that pairs a player with its trigger root. Ours has one id for both (Q271).

## ADR-0116 — Type 47 asks who killed this keeper, and byte 00 is a player
**Date:** 2026-09-09 · **Status:** accepted · Research: `docs/research/trigger-semantics.md`, `docs/research/player-object.md`

**Context.** Condition type 47 was the largest remaining block, 72 records, and the top blocked type on every level sampled after ADR-0115. Its arm is `0x004C7B4B` on the player dispatcher.

**Decision.** Type 47 reads `PlayerState.KilledByPlayerId`, a new hashed byte for DK2's `player+0x132`, which its own debug dumper names `KilledByPlayerId`.
- **Byte 00 is a player, not a creature.** The reader called it `CreatureId`, corrected once already from an external list. The arm uses it only to compare against the killer's player `Number` (`0x004C7B86`), and its shipped values are 0 and 1, 3, 4, 5, 6, 7 — the player id range. Renamed `PlayerId`. That is the third misnamed selector found this way today, after type 39's two.
- **Answered whole, not compared.** The arm returns 0 on a living keeper, 1 when byte 00 is zero, and otherwise tests equality itself. It reads no `Comparison` and no `Value`, and the record carries neither, so routing it through the shared compare path would have thrown on a missing key.
- **Nothing sets the field.** No keeper can be defeated here — a fallen heart is not even claimable (Q207) — so every keeper reads as alive. That is DK2's answer for a living keeper too, so the condition is right today and will stay right when defeat arrives.
- Hashed rather than hardcoded false, because a field the simulation can be *given* is testable where a constant is not.

**Consequences.**
- `HashVersion` and snapshot `FormatVersion` → **52**; pin `0x5C6A03ED5BD250BC` → `0xB5C88C09424100AD`.
- Campaign-wide `stalls` **215 → 143**. `moved` and `after1` do not move, and could not: nothing kills a keeper, so all 72 records read false. This buys census honesty, not reach.
- We store the player id where DK2 stores the killer's object id and converts through its `Number`; the two agree only while `TagId` and `Number` do (Q271).
## ADR-0117 — Live captures compare by event set and stored-stamp alignment
**Date:** 2026-09-09 · **Status:** accepted · Experiment: `docs/experiments/level1-idle-001/`

**Context.** `trigger-diff` pairs exact turn numbers and is correct for two of
our runs. A live capture has a different, load-jittered origin; played sessions
also differ with human input. Pairing their rows or absolute turns manufactures
divergence. A probe started after level load also makes an event present in its
first sample an upper bound rather than a measured first turn.

**Decision.** `dk2tools capture-diff` first compares which flag and timer
columns ever become non-zero. It then selects the lowest-numbered timer stamped
on both sides, or an explicit `--anchor tN`, and subtracts each side's stored
stamp from subsequent first-event turns. It refuses timing alignment when no
shared timer exists. First-sample events remain visible but censored and do not
contribute a residual.

The reusable comparer lives beside `TriggerTurnCsvEmitter`; the CLI only renders
its result. `trigger-diff` is unchanged and its help identifies it as the exact
comparison for two of our runs.

**Consequences.** The gitignored level-1 idle capture gates a test when present:
both sides must contain exactly `t15`, `f1`, `f34`, `f83`, with no unique event,
and every measurable residual must be at most one tick. Its path and skip policy
live in `Dk2CaptureGate`. No simulation state changed, so no `HashVersion` bump.

## ADR-0118 — An event the other run stopped before is untested, not divergent
**Date:** 2026-09-10 · **Status:** accepted · Experiment: `docs/experiments/level20-idle-001/`

**Context.** ADR-0117 split events into three sets: both, capture only, ours
only. Two runs of the same level rarely cover the same span — a capture is
bounded by the owner's patience and our run by a turn count — so a one-sided set
mixes two unlike things: the other side watched and saw nothing, or the other
side had stopped. Level 20 idle made the difference visible. Our `f6` is due at
turn 2404 and the capture ended at 1218, so a run whose 150 columns were
identical at every tick both sides covered reported `event set differed`.

**Decision.** Once an anchor is chosen, map each one-sided event onto the other
side's clock through the two stored stamps. Those falling past that side's last
observed turn move to `OursBeyondCapture` / `CaptureBeyondOurs`, are printed as
untested, and do not count against `EventSetsMatch`. Events censored on a first
row are never reclassified: they began at or before it, so they are inside the
window by construction. Without an anchor nothing is reclassified, because
there is no clock to map onto.

**Consequences.** A verdict now distinguishes disagreement from absence of
observation, and a short capture can no longer manufacture a divergence — nor
hide one, since an event inside the window is still reported. The untested list
also names what a longer capture would buy, which is how the 11-minute Level 20
run that discriminates Q273 was chosen. No simulation state changed, so no
`HashVersion` bump.

## ADR-0119 — Compare a timer's stored stamps, not the turn the probe noticed them
**Date:** 2026-09-10 · **Status:** accepted · Experiment: `docs/experiments/level20-idle-002/`

**Context.** A live capture yields two unlike quantities. A flag is a boolean
seen only when the probe looks, so its turn is an observation. A timer slot
holds *the tick the game recorded when it started the timer*, so its value
survives being read late. ADR-0117 stored only each column's **first** non-zero
value, which threw the second kind away: Level 20's timer 1 restamps at 1724
inside the capture, and the comparer showed it as a censored ceiling event —
the run's one exact measurement, invisible.

That measurement is what answered Q273. DK2 stored **1724** where the probe
labelled the change **1725**, dating the probe's own lag at one tick against
the game's record, on a single event.

**Decision.** Record, per timer, the ordered sequence of distinct stored values,
and compare those sequences origin-relative. Truncate each side to what the
other run was still running to observe, strictly, since a sampled probe needs a
further read to show a stamp written on its last tick — ADR-0118's rule, which
a re-run against the earlier capture proved was needed here too. Report stamp
agreement separately from the sampled residual, and exit non-zero if either
fails. A sampled residual is annotated rather than silently corrected: the
mechanism behind the lag is not separated, so the raw number stays visible.

**Consequences.** The comparison now has an exact half and an approximate half,
and says which is which. `StampsAgree` is the strong claim; a `+1` on a flag is
agreement given the measured lag. No simulation state changed, so no
`HashVersion` bump.

## ADR-0120 — Condition type 29 is keeper-spell availability, tested strictly against 3
**Date:** 2026-09-10 · **Status:** accepted · Arm `0x004C83C8`

**Context.** Type 29 was the largest block in the stall census — **80 records
over 14 levels, 77 of them reached**. The plan asserted it needed "per-player
spell availability" without the arm having been read, so the shape, the state
tested and the meaning of id 0 were all guesses.

**Decision.** Read the arm. It takes byte 1 as `TargetId` and returns a bare
boolean: **zero walks the keeper's spell table** at `player+0x6E`/`+0x72`
looking for any entry whose availability is 3, **a named id looks that one up**
and compares the same way. It reads no `Comparison` and no `Value` — the record
carries neither — so like type 47 it is answered in `EvaluateCondition` and
never reaches `TriggerState.Compare`. Availability comes from `Availability`
kind 5, which `kwd-variables.md` had already identified as keeper spells from
its id range against `KeeperSpells.kwd`'s thirteen records.

**The state test is `== 3` exactly, not "non-zero".** That distinction is
load-bearing on shipped data: kind 5 holds **271 entries at 1** and one at 4,
and it separates a spell from a creature, where `PortalRules.IsUnlocked`
deliberately also accepts RESEARCHABLE (2).

**Consequences.** The campaign stall frontier falls **143 → 66**. No flag moves
that did not move before, so nothing in the two live captures changes and both
still match — the walk goes deeper without writing more. Availability is read
from level data rather than world state because nothing mutates it yet; action
type 38 carries an `Available` byte and will have to move it onto the player
when it is implemented. No simulation state changed, so no `HashVersion` bump.

## ADR-0121 — Rank stalls by the walker that owns them, and take the four arms that read whole
**Date:** 2026-09-10 · **Status:** accepted · `HashVersion` **53**

**Context.** The stall census reported a tag and a type and threw away the
context. That is the fact that picks the arm: each dispatcher tables its own
types first and delegates the rest, so type 46 in a player tree and type 46 in
the world tree are different code. Ranking without it had already produced one
wrong frontier (ADR-0119); reading arms without it risks reading the wrong one.

**Decision.** `BlockedTrigger` carries a `TriggerContext`, and every census
groups by it. Measured that way the 66 stalls are: 22 Player 26, 64 **World**
12, 70 **Area** 6, 28 Player 5, 2 Player 5, 52 Player 3, 27 Player 2, 71
Player 2, 20 Player 2, 73 Player 1, 46 Player 1, 23 Player 1.

Four of those read whole and are now built (`trigger-dispatch-tables.md`):

- **46 — DK2's own silent false.** Its world-table slot points at
  `0x00518941`, the unhandled path's tail *past* the printf, so the engine
  returns zero deliberately where the 57 types it truly refuses print first.
- **52 — creatures at or above a level.** `0x004C7E4E` walks the owned list
  comparing the creature's `+0x403` against byte 03, excluding workers by the
  `+0x686` bit type 19's total excludes them by. Byte 03 is a level, not the
  player id the previous reader named.
- **71 — the slap tally**, `player+0xEE6`.
- **73 — possession**, `player+0x134`: not possessing returns false *without*
  reading the comparison or the value.

**Two fields nothing writes, deliberately.** `SlapCount` and
`PossessedCreatureId` follow `KilledByPlayerId` (ADR-0116): zero is this
simulation's true state, not a stand-in, so the conditions read state instead of
answering a constant and are already right when the mechanism lands. Type 73
goes further and **declines** once possession is non-zero, because what it then
compares (`+0x488`) is unread — an honest gap beats a silent false.

**Consequences.** Frontier **66 → 59**; 1421 tests; both live captures still
match. Types 27 and 28 were read and *not* built: the base offsets prove them
door and trap availability, but all ten shipped records use the counting mode
whose partition bit is unidentified (Q281), so the half that was easy is inert.
Type 70 was read and not built for the same reason — its walk adds two filters
type 39's counter lacks (Q280). Q278–Q281 record what each one waits on.

## ADR-0122 — Read condition type 64 and decline it until parties spawn
**Date:** 2026-09-10 · **Status:** accepted · Arm `0x005187AD`

**Context.** Type 64 accounts for twelve of the campaign's 59 remaining stalls.
The plan said it waited on party spawning from record shape alone; the arm had
not established either the quantity or whether byte 01 really named a player.

**Decision.** The complete read is in `trigger-dispatch-tables.md`. Byte 01 is
`PartyId`. The arm compares authored occupied member slots minus active,
non-dying creatures carrying that party id. It combines the world creature list
for non-Good owners with the Good player's owned-creature list; the player array
is not indexed by the body byte.

Do not implement it yet. Action type 14 arms trigger subtrees but creates no
creatures, and `CreatureState` has no party identity. A template-only answer
would report the whole party missing immediately and may fire a trigger early.
Q283 binds the condition to the spawning work that supplies both facts.

**Consequences.** The stall frontier remains 59 by design. The parameter view
now says `PartyId` rather than `PlayerId`; no simulation state changed, so
`HashVersion` stays 53.

## ADR-0123 — Measure what a decline forecloses, and stop ranking the frontier by declines
**Date:** 2026-09-10 · **Status:** accepted · `TriggerReach`

> Numbered 0123 because 0122 had been handed to a concurrent worktree before
> this was written; it landed as the type 64 read above.

**Context.** This project has ranked the trigger frontier four times and been
wrong three times, each time by measuring a proxy: records shipped (ADR-0119
replaced it), declines reached (ADR-0119), declines by walker (ADR-0121). Each
proxy correlated with the thing wanted — *how much script does this unblock* —
and none of them was it.

**Decision.** Measure it. `TriggerReach.Gated` takes a run whose reach was
tracked and returns, per declined condition, **the records behind it that the
walk never reached because of it**: the full subtree under its child, minus
everything the run reached by another route. `dk2tools trigger-run` prints it
beside each census line and `kwd oracle` has a `gates` column.

**What it says.** Campaign-wide, **59 declines foreclose 153 records** — and the
ranking is not the one declines gave:

| type | declines | **gates** |
|---|---:|---:|
| 22 | 26 | **95** |
| 28 | 5 | **18** |
| 2 | 5 | **14** |
| 64 | 12 | **12** |
| 70 / 27 / 20 / 23 | 6 / 2 / 2 / 1 | 8 / 3 / 2 / 1 |

Type 28 outranks type 64 at a fifth of the declines, and **type 64 is the worst
entry in the table**: twelve declines foreclosing one record each.

**The larger consequence is that the frontier is nearly spent.** Level 4 is the
clean case: one decline, one record foreclosed, 103 records its static walk does
not reach. `kwd oracle`'s `could` legend asserted the opposite — that the gap
between `moved` and `could` is stalls — and that line is now corrected rather
than left standing.

**Corrected by ADR-0125.** This ADR set 153 against `kwd oracle`'s 4,252, which
is a *static* figure counting a different thing; the honest accounting is
1.6% of 9,161, and it is in ADR-0125.

So condition arms are no longer where reach comes from. The campaign has **289
creature roots**, the largest root class it has, and the creature walker is not
implemented at all; that is the next place to look, not another arm.

**Consequences.** No simulation state and no behaviour change: reach tracking is
off by default and `TrackingReachDoesNotChangeTheStateHash` pins that turning it
on is inert, the same contract `ObserveWith` has. No `HashVersion` bump.

## ADR-0124 — Build condition 70 for creatures; keep door and trap counts declined
**Date:** 2026-09-10 · **Status:** accepted · Q280/Q281

**Context.** Conditions 27, 28 and 70 were parked on three supposedly unknown
flags. DK2's own debug dumpers already name both type-70 bits `Possessed`. A
fresh arm read also disproved the premise that 27 and 28 share the trap bit.

**Decision.** Type 70 now asks whether its area contains a matching creature
named by any player's `PossessedCreatureId`. Its selector bytes are renamed to
player, subtype and thing category. Object targets still decline because object
possession is not represented; no shipped type-70 record selects objects.

Do not build 27 or 28 yet. Trap `+0x10C` bit 5 is strongly inferred
`UnderConstruction`, but trap construction is absent. Condition 27 instead
partitions on an unidentified door dword at `+0x109` (Q284). The per-type trap
helper counts matching `+0xF6` ids, with a Boulder-shot special case that also
counts live Boulder objects.

**Consequences.** The 66-map frontier moves from 59 stalls/153 gates to
**53/145**. No state was added, so `HashVersion` remains 53. Both stored live
captures retain exact event sets and timer stamps.

## ADR-0125 — Account for every trigger record a run never walks, and the frontier is 1.6% of it
**Date:** 2026-09-10 · **Status:** accepted · `dk2tools kwd unreached`

> Numbered 0125 because 0124 was handed to a concurrent worktree first.

**Context.** ADR-0123 measured what declines foreclose (153 records) and set it
against `kwd oracle`'s `reach`, concluding the frontier was 3.6% of unreached
script. **That comparison was wrong.** `reach` is a *static* walk that descends
only through condition types 1/2/34, so its gap is not what a run misses — the
two numbers measure different things. The conclusion happened to survive; the
arithmetic behind it did not, and the amended figure is below.

**Decision.** Put every record in exactly one bucket, from a real run: walked,
or one of five reasons it was not. `TriggerReach.Reachable` gives the static
closure under each root class, the buckets are ordered so the first to claim a
record keeps it, and `dk2tools kwd unreached [turns]` prints them per level.
Over 600 turns, all 66 levels, **9,161 records**:

| bucket | records | share |
|---|---:|---:|
| ran | 2,669 | 29% |
| **closed** — walked to, condition answered false | **3,832** | **42%** |
| **creature** — only under a creature root; no walker exists | **1,138** | 12% |
| **party** — only under a party subtree nothing arms (Q254) | **864** | 9% |
| orphan — reachable from no root at all | 513 | 5.6% |
| **declined** — the entire condition frontier | **145** | **1.6%** |

**Consequences.** Three things follow, and they reorder the roadmap.

**The frontier is spent.** 1.6%. Types 22, 23, 27, 28 and 64 are all read and
parked behind mechanisms; finishing every one of them cannot move reach
materially. Arms are no longer the work.

**An idle capture can only ever exercise 29% of the script.** The 3,832 closed
records sit behind conditions the walk reaches and answers honestly false,
because nothing in an idle world builds a room, fights, or issues a command.
That is a ceiling on M5's method, not a defect to fix, and it is why the
remaining gap needs mechanisms rather than dispatcher arms.

**The two structural gaps are named and sized.** The creature walker is 1,138
records — one missing walker, no new mechanism, the dispatcher chain already
documented in `trigger-walkers.md`. Action 14's creature creation is 864, and
it also unblocks condition 64 (Q283). Those are the next two pieces of work.

The 513 orphans are recorded, not explained: script in the shipped files that
no root of any class reaches. Whether DK2 reaches them by a route this project
has not found, or they are genuinely dead, is UNKNOWN (Q285).

## ADR-0126 — Most of the campaign's script is waiting for the player, and that is measured
**Date:** 2026-09-10 · **Status:** accepted · `dk2tools kwd unreached`

> Numbered 0126 because 0124 is held by a concurrent worktree.

**Context.** ADR-0125 found 3,824 records the walk reaches and answers false and
left it there. "Only a mechanism opens these" is not actionable until you know
*which*.

**Decision.** Attribute each unreached record to the nearest condition above it
that was reached and answered false (`TriggerReach.Closed`), grouped by type —
**over an hour, not 600 turns**. At 600 the biggest group is 501 level-time
gates not yet due; at 14,400 it is three. A short run reads as a blocked
simulation. The default is now 14,400: 3,145 ran, 3,356 closed.

**What closes them**, by the mechanism that would open each group:

| | types | records |
|---|---|---:|
| cascade — a flag or timer nothing upstream set | 1, 2 | 1,576 |
| **keeper activity — dig, claim, build a room** | 25, 24, 40, 41, 42 | **696** |
| creatures existing and moving | 19, 39 | 429 |
| dead in DK2 too — its own constant-false arm | 53 | 257 |
| combat, research, gold | 47, 29, 30, 31 | 279 |

**The keeper is the human one.** Types 40, 41 and 42 name the player they
measure, overwhelmingly player 3: 84 of 92, 47 of 69, 49 of 89. Those 696 wait
on somebody playing, not on a missing simulation, and a computer keeper does
not substitute — `DK2.AI.KeeperAI` is ours, not a reading of DK2's, so it would
inject divergence that could not be attributed.

**Consequences.** **Idle captures are exhausted.** Both match exactly, and this
says why that was achievable: an idle run only exercises the third of the script
not waiting for a player, and no mechanism work changes that.

M5's next step is therefore **an input channel** (ADR-0127), not more
simulation: `Tick(in InputFrame)` is half of one and nothing records DK2's own
input. Until that exists a played capture measures the player and an idle one a
third of the script — `our-side-of-a-capture-has-no-player`, now sized.

**Type 70 is the pattern in miniature** (ADR-0124): answering it moved eight
records from `declined` to `closed` and left `ran` at 3,145 exactly. An arm
answering false where it used to decline changes the bookkeeping, not the
reach. The split is now 149 declined, 3,356 closed.

Two caveats. The 1,576 cascade records are assumed downstream of other blocks,
not separately attributed, and some may instead wait on the special flag id 128
path (Q229). And 257 are dead in DK2 too, so the denominator is 8,904.

## ADR-0127 — Record the player's commands from DK2's task pool, not from its input
**Date:** 2026-09-10 · **Status:** accepted · `player-task-pool.md`

> Numbered 0127 because 0124 is held by a concurrent worktree.

**Context.** ADR-0126 measured that the largest identifiable block of unrun
script — 696 records — waits on **player 3, the human keeper**, digging,
claiming and building. No mechanism opens those. M5 therefore needs an input
channel: the same commands applied to DK2 and to us. `Tick(in InputFrame)` is
half of one; nothing records DK2's side.

**Decision.** Record **commands, not input.** Capturing keyboard and mouse
would mean reproducing DK2's camera and hit-testing exactly to turn a click
into a tile, and every error there would look like a simulation divergence.
DK2 already stores the result at the level we want: a per-player **task pool**,
one entry per marked tile, whose layout is in `player-task-pool.md`. Diffing it
between samples yields `tag`/`untag` at a coordinate and a tick.

`dk2watch.py --tags <out.csv>` writes that as `wall,tick,player,event,kind,x,y`
— a command log beside the existing state CSV rather than more columns in it,
because `capture-diff` parses that file by position.

**Rooms come from the same idea, not the same structure.** The player's room
list at `player+0x36` is already read for the gold resync; a room appearing in
it is a placement. That is the other half of the 696 (types 25 and 24 are 397
of them, terrain 40/41/42 the other 299) and is not built yet.

**Consequences.** The first capture is also the first test of the layout, which
has been read but never sampled — nothing is CONFIRMED here yet. The experiment
it enables is a **scripted** session ("dig these five tiles ten seconds apart,
build nothing"), because a small reproducible input keeps a divergence
attributable; free play does not, which is what
`our-side-of-a-capture-has-no-player` records.

Not covered: creature drops and spells. Neither appears in the 696, so neither
blocks this step.

## ADR-0128 — A captured task-pool log replays as input frames
**Date:** 2026-09-10 · **Status:** accepted · `InputLog`, `dk2tools replay`

**Context.** ADR-0127 found where DK2 keeps what the player asked for and made
the probe diff it. That produces a command log; nothing consumed one. ADR-0126
sized what a consumer is worth: 696 records wait on keeper activity, and an
idle capture cannot reach any of them.

**Decision.** `InputLog` reads a `--tags` log into `InputFrame`s keyed by DK2
turn, and `dk2tools replay <level> <tags.csv> <out.csv>` runs a level driven by
one, writing the probe's CSV shape so `capture-diff` compares a *played*
capture exactly as it already compares an idle one.

Three readings the log needs and the file does not carry, so all three are
arguments rather than inferences:

- **`--lag`, default 1.** A sampled column is one turn late (ADR-0119), so a
  row stamped 41 is a command issued on 40.
- **`--origin`, default 1.** Which DK2 turn our turn 1 is. A capture that
  attached mid-level lines up by setting it; commands dated before turn 1 are
  dropped and *counted*, so a wrong origin is visible rather than silent.
- **A tag and an untag of one tile in one sample cancel.** The pool splits
  tagged tiles into two lists by whether the terrain holds gold, so a tile
  crossing between them appears as both. Replaying the pair would unmark a
  tile the player still wants dug. The cancellation is safe because the
  alternative reading — a real untag and retag inside one sample — nets to no
  change in the pool and emits no rows at all.

**A replay runs the systems; an idle run does not.** `trigger-run` registers no
systems, which is why its output is purely the trigger tree's. That cannot
honour input at all — `TileTagSystem` is not there to read it — so `replay`
registers `SimulationSystems.Default()`.

**Measured, not assumed: that costs nothing.** With an empty log, level1 and
Level20 produce trigger output identical to the triggers-only run over 600
turns, every column. So the whole declared system list moves no flag and no
timer on an idle campaign level, and the two matched baselines survive the
change. It also says something about the idle captures: they matched partly
because nothing in them depended on the world changing.

**Consequences.** The replay reports `applied` and `rejected` tag counts from
the events the tag system already emits. That is the **first-capture check**:
the pool's layout is read but never sampled, so if its coordinates are not tile
coordinates, every tag lands off the map or on floor and the count says so —
rather than a divergence report that blames a rule (Q287).

Still missing on the recording side: rooms, creature drops and spells. Rooms
are the larger half of the 696 and are next.

## ADR-0129 — Record rooms from the player's room list, and refuse to invent their tiles
**Date:** 2026-09-10 · **Status:** accepted · `RoomLog`, `dk2tools replay --rooms`

**Context.** ADR-0126 sized keeper activity at 696 gated records; ADR-0128 built
the tag half. Rooms are the larger half, and nothing recorded them.

**Decision.** The probe walks each keeper's room list — `u16 [player+0x36]`, then
`u32 [0x006EE540 + idx*4]`, next at `u16 [room+0x08]`, null ends it — and writes
one row per change. `RoomLog` reads it back as `BuildRoomCommand`s and
`SellRoomCommand`s (`room-list.md`).

**The capture does not record the room's tile chain.** Later furniture research
established that `FirstSlab` heads a chain through each tile's `+0x04/+0x05`
link (`room-object.md`). The probe records only the room object's two list ends,
so replay remains exact **only when their bounds enclose exactly `Size` slabs**.
Anything else is counted and dropped.

**We do not fill the bounding box.** An L-shaped room has the bounds of the
rectangle that would contain it, so filling them puts tiles on the map DK2 never
had there, and every divergence after that is unattributable — the trap
`our-side-of-a-capture-has-no-player` names, in the other direction.

**Dropping is affordable because of what the conditions read.** Types 24, 25 and
68 read `TypeId`, `Size` and `TotalCapacity` and never a tile
(`room-conditions.md`), so for the measurement `capture-diff` runs, type, owner
and slab count is a complete recording.

**Three corrections came out of the same reading:**

- **The task pool's entry layout was four bytes low** throughout
  `debug-dumpers.md`: those offsets were paired against the dumper's cursor,
  which sits at `record+4`. Corrected, `Next` lands at `+0x1F` — the offset the
  pool's own walker reads, which the old reading contradicted. There are 500
  entries and each has twelve `Worker` words.
- **Marking a tile files under three kinds, not two.** `0x00512D20` pushes 11, 2
  and 1 from three arms. The probe now records all fifteen kinds and `InputLog`
  replays only 1 and 2, counting the rest, because what kind 11 is remains
  unread (Q288).
- **Rooms are not imp tasks.** No pool writer references the room handler table,
  across all 27 call sites of the adder. That agrees from the binary with what
  `imp-task-priority.md` §3 said from our side, so `RoomBuildSystem` building
  instantly is a deviation with nothing to model against rather than a gap.

**Consequences.** Both recording halves now exist and neither has been run
against a live game; the first capture tests the layouts before it tests any
rule. Q289 is the sharpest of those: if `FirstSlab`/`LastSlab` do not bound the
room, every placement drops and this ADR's method — exact or nothing — yields
nothing until a slab source is found.

## ADR-0130 — Two live captures, and most of what ADR-0128 assumed was wrong
**Date:** 2026-09-10 · **Status:** accepted · runs 9 and 10, level 1 · amends ADR-0128, ADR-0129

**Context.** ADR-0128 and ADR-0129 built both recorders against layouts read
from the binary and never sampled. The owner ran two captures. Both layouts
read; four of the readings around them did not survive.

**What held.** The pool's entry layout, the room-list walk, and the
coordinates: replaying run 9's marks applied 30 tags and had **none** rejected,
so they are tile coordinates and agree with our own map (Q287).

**What did not.**

- **The room walk was one player record out.** The base is `playerArray +
  (id−1)×4389`, since the pool formula's `playerId×4389 − 0x20` resolves to
  `player+0x1105`. Run 9 filed the human's own dungeon heart under player 2.
  The probe now carries the room's own `PlayerId` so the walk checks itself.
- **`FirstSlab`/`LastSlab` are list ends, not a minimum and a maximum.** Run
  10's settled lair runs (8,30) to (10,26), descending in y. Ordered they bound
  the room exactly — and every settled room agrees, which also **confirms
  `Size` is the slab count**, until now only strongly inferred.
- **But not during construction.** Imps build a room slab by slab, and for six
  samples `Size` ran *ahead* of what the bounds enclose — 3 slabs in a 2-slab
  box — which no bounding box can do (Q291). A replay must place against what
  it has already placed, not against the previous row, or a 15-slab lair
  arrives as one slab and six refusals.
- **The pool is a work pipeline, not a set of marks.** A tile occupies several
  kinds at once and migrates as an imp takes it up, so **a row is not an
  event**; entering the pool and leaving it are. Reading rows directly emitted
  untags the player never issued, which on replay stops our imps digging —
  exactly the failure the owner predicted before the first capture.

**Kinds 1 and 2 are rock and gold — CONFIRMED** (owner, 2026-09-10): four marks
placed as two rock and two gold arrived as exactly two of each.

**And a heuristic of mine, disproved on the spot.** I read a one-tile-per-sample
trickle as DK2's own work and a batch as the player, and said so. The owner had
placed those tiles individually, about 1.5 seconds apart. **Cadence carries no
information about who acted**; the kind does.

**Consequences.** A finished dig and a dropped mark both leave the pool, and
the kind transition between them is often not sampled — run 10's gold showed
none. The discriminator is the task's own **`Worker`** slots, now recorded on
every row: an imp that took the job sits in one. That closes the last known
hole in the tag recording without needing the runtime map, which ADR-0129 had
put on the critical path.

## ADR-0131 — A cancelled dig is told from a finished one by the claim, not the worker
**Date:** 2026-09-10 · **Status:** accepted · run 11, level 1 · amends ADR-0130

**Context.** ADR-0130 closed the last known hole in the tag recording with the
task's `Worker` count: an imp that took the job sits in a slot, so a removal
with a worker is a finished dig and one without is the player letting go. It
was reasoned from the field's name and never tested against play.

**It is wrong.** The owner cancelled a mark **with three imps already digging
it**, on request, and labelled it in advance. That removal carries the same
worker count as a completion. The column was added the same morning for exactly
this job and does not do it.

**Decision.** A dig's removal is a completion **iff a claim task follows on that
tile before the tile is marked again**. A dug tile is claimed and a cancelled
one stays rock, so the claim is evidence the tile changed, which is the thing
actually being asked. The bound matters: without it, re-marking a tile would
retrospectively turn an earlier cancellation into a completion.

**Against the labelled run**: sixteen commands, both cancellations kept, all
nine completions rejected, **no false untags**. Before this, every completed dig
replayed as the player changing their mind — which would stop our imps digging
tiles DK2 dug.

**Consequences.** One wrinkle stays open: a tile left the pool and returned
seven ticks later under a **different task id**, which is DK2 freeing and
re-allocating rather than the owner acting. It replays as an untag and an
immediate retag, so it is self-cancelling, but it is not understood.

**And the method note, which cost more than the bug.** Both discriminators —
the worker count here, and "a trickle is DK2's own work" in ADR-0130 — were
inferred from the shape of a capture and both were wrong. A capture records
what the game did and never who caused it. The owner labelling two cancellations
in advance settled in one run what two unlabelled runs could not, and it is now
the standing protocol: **ask for labelled play before inferring provenance**.

## ADR-0132 — Reinforcing needs the keeper's floor beside it, and that guard is inert today
**Date:** 2026-09-10 · **Status:** accepted · owner confirmation · `ImpTaskSystem`

**Context.** Our reinforce eligibility was tile-local — solid, penetrable,
unowned, below its maximum-health type — with no neighbour condition. The
owner: *"imps automatically reinforce walls, as soon as the floor tile is
claimed walls are eligible to be reinforced"*, and every reinforce task in run
11 sits on a wall touching a tile just claimed.

**Decision.** A wall is reinforceable only where one of its four sides faces
floor this keeper owns. The test goes where the job is chosen, not in
`TileWork.Applies`, which is handed one tile and no world.

**Four sides, not eight** — ours. A wall reinforces the face it presents to a
corridor and a diagonal neighbour presents none; targets are already chosen
from four neighbours, so eight here could only reject walls the worker reaches.
Nothing observed needs the diagonal.

**It changes no behaviour, and that was checked rather than assumed.** The
determinism pin did not move and no test failed. The reason: a wall is only
ever reached from an orthogonally adjacent tile a worker stands on, so either
that tile is the keeper's floor and the rule passes, or it is not and
**claiming outranks reinforcing**, so the floor becomes the keeper's first.
Ground that cannot be claimed — water, lava — cannot be stood on either.

**So it is a guard, not a fix**, and the code says so. It starts doing work the
moment anything lets a worker stand on ground it cannot claim: a bridge over
water, a rival's floor it may not take.

**Two fixtures had to be thrown away to learn this**, both states DK2 cannot
produce: Dirt Path with an owner stamped on it (Dirt Path is not `Ownable`),
and Claimed Path with no owner (claimed *means* owned). Each behaved
confusingly for that reason alone. A test standing on an impossible tile
measures nothing, and the negative half of this pair was deleted rather than
kept green.

## ADR-0133 — One application is a shift, not a division, and nothing is carried

**Context.** Run 15 read the running game's tile health word 20 times a second
while three tiles were dug, claimed and reinforced (`runtime-map.md`). That is
four work rates measured at once, against terrain whose deltas we already knew:

| variable | tabled | ours | DK2 |
|---|---|---|---|
| `MineGoldHealth` | −300 | −60 | **−60** |
| `DigOwnWallHealth` | −130 | −26 | **−26** |
| `ClaimTileHealth` | +825 | +165 | **+164** |
| `ReinforceWallHealth` | +50 | +10 | **+9** |

**Decision.** One application is `tabled × Efficiency-in-4096ths >> 14` — twelve
bits for the multiplier, two for the four ticks of a game second — as a single
**arithmetic shift**, which floors. Our `tabled ÷ 5` truncated toward zero and
is only equal when the value divides by twenty, which is why gold and digging
looked right for a fortnight while claiming and reinforcing did not.

**And nothing is carried.** `TileState.WorkRemainder` banked the fraction so the
per-second rate held exactly. Run 15 watched **54 consecutive reinforce steps of
9** where the tabled rate wants 9.6: DK2 drops it every time. The field is
deleted (`HashVersion` 54).

**The floor is not a rounding detail.** `DigEnemyWallHealth` −8 floors to −2,
which is −8 a game second exactly; truncating gave −1 and needed the
accumulator to reach the same place. Where they differ the game takes the floor.

**Efficiency moves to 4096ths.** `EfficiencyRules.Scale` and its bespoke
round-half-away-from-zero are gone: they existed to make 90 % pay 68 health a
swing from a whole percent, and `0xe65` lands at 67.47 and floors there by
itself. All three of the owner's gold measurements — 273, 309, 341 — still come
out, now from the arithmetic DK2 runs rather than one fitted to them. The
percents stay as the display value the status bar shows.

**What it costs.** Claiming a tile is five applications, not four; reinforcing
Rock is 55, not 50. Both are CONFIRMED tile by tile rather than fitted to a
stopwatch, which is how this number was wrong twice before (ADR-0024's 66, then
ADR-0089's 50 — see Q62).

**It does not close the replay gap, which was checked.** Runs 13–15 keep their
residuals almost exactly: digging is the delta that did not move, and the flags
that lag are dig-driven.

**Correction, same day.** This first said the remainder was Q296's approach time,
which we did not model. We do: `ImpTaskSystem.UpdateMoving` walks a worker to its
stance and only swings once `IsInWorkingPosition` passes. The claim came from a
grep that missed the call. What drives the 18–37 tick lead is unidentified.

## ADR-0134 — A marked dig continues through a wall's intermediate Rock

**Date:** 2026-09-10 · **Status:** accepted · Q215/Q297

**Context.** Run 15 saw one dug Reinforced Wall remain Rock for one tick and
another pass from Reinforced Wall to Dirt Path between samples. Our fixed
`DigOwnWall` order stopped when the wall became unowned Rock.

**Decision.** The three rate variants of a marked, non-gold dig are one
continuing task. Before every application, resolve Rock, own wall or enemy wall
from the tile's current terrain and owner. Preserve queue order: workers already
queued later in a tick observe changes made by earlier workers.

This follows DK2's worker handler, which selects one of the three rates before
its shared applier call. The applier carries no overflow and performs one
terrain conversion per call. It also has no max-health work-kind guard; caller
reachability keeps floor healing out (Q215, `tile-work.md`).

**Consequences.** Reinforced Wall still becomes Rock at 165. Two later rock
applications remove 100 then 65, so queue position alone produces both captured
timelines. `HashVersion` and snapshot `FormatVersion` move 54 → 55; no state
field changed, but a resumed dig job now evolves differently.
## ADR-0135 — A creature's state is DK2's state id, adopted one state at a time

**Date:** 2026-09-10 · **Status:** accepted · advances Q186, opens Q300 · Research: `docs/research/creature-state-table.md`

> Numbered 0135 because 0134 was taken by a concurrent branch, since merged.

**Context.** The 21-value `CreatureActivity` enum was never a decision — it grew
one activity at a time and is recorded only as a *fact* in plan §3 ("Ours has 21
activities"). DK2 has 281 states, and since 2026-09-06 the whole table has been
read out at CONFIRMED. The three things a state machine needs from it — a resume
slot, a transition gate, a category hook layer — cannot even be specified while
it is undecided which vocabulary is authoritative, because DK2's rule is written
in terms of state ids and there is no group tag for `MovingToJob`.

**Decision.**
1. **DK2's state id is the authoritative representation.** `CreatureActivity`
   becomes a retiring convenience, not the model.
2. **Adopted incrementally, not at once.** `CreatureState` gains a hashed
   `Dk2StateId` beside `Activity`; new work sets the id, and an activity retires
   when the states composing it have been read. The alternative — rewriting 106
   references across 61 test files in one commit — was rejected as unreviewable,
   and inventing our own transition rule was rejected outright: AGENTS.md forbids
   inventing behaviour that can be researched, and this one is researched.
3. **The table is data, transcribed, its tests the only guard.** `CreatureStateTable` carries
   the label string id, group tag, category and flag byte for all 281 states.
   Handler addresses stay in the note — nothing here calls them. Label *strings*
   stay out of `src/` per the note's §3: they are shipped-asset text, and the
   ids are enough. **The category and flag byte are typed**, so a row reads
   `Cat.Prison, Flag.Unread0 | Flag.SuppressNeedAnger` rather than five bare
   numbers; an untyped row is a memory dump transcribed into C#.
4. **An unread transition is not a refused one.** (Dying is admitted from almost anywhere — the passthrough inside group 3, an all-ones mask outside — but *not* from the Refused category, whose bit is never written.) `CreatureStateTransitions`
   answers **both** halves: all 78,680 ordered pairs resolve, 37,791 allowed and
   40,889 refused, measured and pinned. ADR-0109's precedent — return
   "unresolved" rather than invent a rule — was discharged by Q300 the same day.

**Consequences.**
- Nothing hashed changed **yet**: both new types are pure data and predicates,
  so `HashVersion` stays where it is and the pin is untouched. `Dk2StateId` takes
  **the next free** `HashVersion` and `FormatVersion`, not one reserved in
  advance: a reservation is worth nothing against a concurrent branch that reads
  the number out of `GameSimulation.cs`.
- The field is deliberately not in this commit, and now waits on one thing only
  — the pinned world, below.
- **The pin could not catch a `CreatureState` field at all** — the pinned world
  spawned no creatures, which made this a prerequisite rather than housekeeping.
  Done 2026-09-10, ahead of the field (Q206).
- Reading the 264 handlers has no shortcut: 259 serve exactly one state, and
  Flame names the behaviour of none. That is the size of the work this unblocks,
  not of this decision.

## ADR-0136 — Action type 22 writes one tile, and its coordinates are one-based

**Date:** 2026-09-10 · **Status:** accepted · Q301

**Context.** 160 `ALTER_TERRAIN_TYPE` records across 13 levels, and the fourth
most common action the world dispatcher actually performs. Its arm is
`0x00519425`, reached through the dispatcher's own tables rather than assumed:
type 22 maps to a real arm where types 15 and 3 map to the unhandled exit.

**Decision.** Perform it, and take three things from the arm.

**The authored coordinates are one-based.** The arm unpacks x and y from the
record's dword at `+0x04` and runs `DEC` on each before the tile lookup. This is
read from the instructions; the shipped corpus cannot confirm it, because its
coordinates run 5..110 and 5..62 and so never touch either edge. Getting it
wrong would move every scripted terrain change one tile up and left, which no
test of the write alone would catch, so a test states it.

**Two cases succeed and change nothing**: a coordinate off the map, and a tile
that already carries the requested terrain (`0x00519484`). Neither is an error.

**Ownership follows the terrain, not the record.** The record names a player and
the arm carries it, but a tile only takes an owner if its new terrain is
`Ownable` (Q95, ADR-0045). 41 of the 160 records write Claimed Path, which
*means* owned, and 94 write Dirt Path, which cannot be — writing the player onto
both would build tiles DK2 cannot produce.

**Room tiles are declined.** Past the guards the arm demolishes any room on the
tile through the global object table and notifies the map, and neither is read.
Performing the write without them would leave `RoomIndex` describing a room
whose tiles had moved, so a room tile on either side reports through
`Partial` instead — the executor's existing way of saying "descended, effect
missing" (Q301).

**Consequences.** `Performs` gains 22, and with it the obligation the test
`OnlyActionsThatCannotAlterTerrainArePerformed` was written to enforce: the
write clears `TriggerRun.Rooms`, so a room condition later in the same turn
cannot read the map as it was before. No state field changed and the
determinism pin does not move — the pinned world runs no triggers.

## ADR-0137 — Action type 1 creates a creature, and its coordinates are zero-based

**Date:** 2026-09-10 · **Status:** accepted · Q302–Q305

**Context.** 268 `CREATE_CREATURE` records across 13 levels, the largest action
type this executor could reach with every parameter already known. Its arm is
`0x00518B12`, read whole rather than sampled.

**Decision.** Create the creature: type, owner, tile, level and the health that
level carries. Three readings the arm settles, and one it does not.

**The coordinates are zero-based** — the arm makes a tile centre of each body
word without decrementing, where type 22's arm twelve slots along the same jump
table `DEC`s both (ADR-0136). Two arms, the same two words, different
conventions. The corpus cannot say which from the values, since type 1's
smallest is 2, but it can say it from where the creature lands: **none of the
268 spawns into solid terrain as written, and 67 would if decremented.** That is
the assertion, not the instruction read, because a test of the write alone would
never have caught it.

**`Level` is one-based and applied by repetition** — the arm calls the level-up
routine `Level - 1` times, so 0 and 1 are the same request and anything past 10
saturates there. Health follows from the level rather than from the record.

**Every record reports partial, and not because the creature is missing.** The
same arm copies the body's `Flag` byte onto the creature's own `Flags` byte at
`+0x46F`, and bit 0 of it is `DestroysRooms` — set by all 268, and a named DK2
capability this simulation has no reader for (Q302). Calling this performed
would overstate what was built, so the census keeps saying so.

**Not modelled, and each says why rather than guessing:** the collapsed arrival
`Flag` bit 7 asks for, which needs a collapse whose health value already
disagrees with DK2's (Q303); the `TagId` indirection the arm uses to turn the
player byte into an owner (Q305); and the Good-player spawn relocation (Q304).

**Consequences.** `Performs` gains 1. Creating a creature cannot alter terrain,
so the `TriggerRun.Rooms` cache needs no invalidation and
`OnlyActionsThatCannotAlterTerrainArePerformed` keeps its meaning. No state field
changed and the determinism pin does not move. No scenario is on a level that
ships a type 1 record, so nothing pinned moves either — which is also why this
gains no coverage until one is.

## ADR-0138 — Four gates on the collapsed window, not one

**Date:** 2026-09-11 · **Status:** accepted · Q303

**Context.** `CreatureDeathRules.HasDyingState` decided whether a creature that
runs out of health lies there rescuable from one thing: the definition's
`GenerateDeadBody`. That came from the owner (for the Imp) and from a **partial
quotation of OpenKeeper** — this repo recorded its `processHealthDepleted` as
testing that flag "and nothing else", when in fact only its *first* branch does
and three more follow (corrected below). Either way nobody had read DK2's own
answer; action type 1's arm led to `ModifyHealth` (`0x004B2CE0`), which has it.

**Decision.** Four gates, in DK2's order, between `0x004B2F2E` and
`0x004B2F96`. Any one of them sends the creature straight to dead; the corpse
still follows, gate 1 having passed.

1. the definition lacks `GenerateDeadBody`, flags bit 15 — **this was the
   whole rule, and it is the right first gate, not a wrong one**;
2. the creature is `Possessed`;
3. it stands in a **Prison** or a **Torture Chamber**, room `TypeId` 11 or 12;
4. it stands in **no room at all** and `DieInstantly` is true.

**3 and 4 are exclusive, and that is reproduced deliberately.** The room is
asked first, so a creature inside any *other* room is never asked about
`DieInstantly`: a Lord Of The Land killed in a Treasury lies there rescuable and
one killed in the corridor outside does not.

**`DieInstantly` becomes state.** `CreatureState.DieInstantly` mirrors `+0x3EA`
bit 11; action type 1 sets it from its `Flag` byte's bit 2 (ADR-0137). Its getter
`0x0048E110` answers true for creature types 21, 28 and 29 — King Reginald,
Stone Knight and Lord Of The Land — before it reads the bit, so those three
carry it whatever a script asked. A rule, not a lookup, so it is code.

**What the reference projects say** — two of the three missing gates were in
front of us, misquoted — is in `external-resources.md`, "Q303".

**What this changes today.** King Reginald and Lord Of The Land were rescuable
out of doors here and are not in DK2; Stone Knight was already caught by gate 1.
The Prison gate is wider than our `PrisonRules.IsImprisoned` bypass — DK2
keys on the tile, so a creature killed in a prison it is not held in is covered
too. Gate 2 answers from `PossessedCreatureId`, which nothing sets, so it is
inert and asserted anyway — the standing of condition type 73.

**Not taken.** DK2 keeps the rescue clock *in* the health field, writing
`CreatureDyingStateDurationSeconds × 4` = 240 on collapse (`0x004B2FA0`); we
keep `DyingTicks` beside a zeroed health. Same behaviour, different place.

**Consequences.** A new hashed and snapshotted bool: `HashVersion` 55 → 56,
snapshot `FormatVersion` 55 → 56, pin `0xAEC253948973DF35`.

## ADR-0139 — A prisoner is confined to the fenced interior, not to the room

**Date:** 2026-09-11 · **Status:** accepted · Q306, Q307

**Context.** `PrisonRules` let a prisoner stand anywhere in the room: deliveries
picked any free tile and the wander scan offered any orthogonal neighbour that
was part of the same room instance. That followed DK2's own scan, which tests
room membership — but the scan also tests a *nothing is standing here*
predicate, and the owner supplied what stands there: **a prison's fence begins at
the inner ring, so its outermost tiles carry no bars and are outside the cell**
(2026-09-11). Even a Skeleton raised from a dead prisoner leaves through the
gate rather than appearing outside.

**Decision.** Confine a prisoner to the room's **interior** — the tiles whose
four orthogonal neighbours are all part of the room. `FreeTileIn` offers only
those, and `TryWanderStep` steps only onto them.

**The substitution is the same one `Capacity` already makes.** DK2 counts the
tiles its furniture predicate accepts; we have no prison furniture, so the rim
stands in for it. `Capacity` has read `InteriorTileCount + 1` since ADR-0070 for
exactly that reason, and using a different stand-in for movement than for
capacity was the inconsistency.

**The interior becomes a set, not a count.** `RoomInstance.InteriorTileIndices`
replaces the stored `EdgeTileCount`, which is now derived from it. One pass over
the room, one source of truth, and a per-tile question that nothing could ask
before.

**A surprising consequence, asserted rather than hidden.** DK2's smallest prison
is 3x3, whose interior is a single tile — so on one of those a prisoner
cannot move at all. That follows from two owner observations that were never put
side by side: a 3x3 holds two, and a prisoner stays inside the fence.

**Capacity gains a second observed size.** `interior + 1` was confirmed only at
3x3, where it reads 2. The owner's status bar on a 5x5 reads **"Captives held:
6 / 10 … Maximum Skeletons: 10"**, and 10 is what the formula gives. No
other candidate fits both: `tiles - 1` gives 8 and 24, the interior alone gives
1 and 9. The theory row that predicted it is now an observation.

**Still open.** Whether a Skeleton is refused at that maximum is unobserved and
unbuilt (Q307), and so is the corpse a slapped prisoner leaves and where it
surfaces (Q306).

**Consequences.** No state field and no stored quantity changed — rooms are
derived from tiles each time — so `HashVersion` and the pin stand.

## ADR-0140 — A slain prisoner leaves a body on the rim; only the drain raises a Skeleton

**Date:** 2026-09-11 · **Status:** accepted · Q306, Q307

**Context.** `PrisonSystem` turned **every** prisoner whose health ran out into
the prison's `CreatedCreatureId`, on the manual's *"any creatures that perish in
Prison will rise as Skeletons"*. The owner slapped a captive Monk to death and
photographed the result: **a corpse, lying on the prison's rim outside the
fence**, with the live captives still inside it and no Skeleton. OpenKeeper
reaches the same rule independently — its skeleton branch requires the entity
not to be taking damage.

**Decision, in three parts.**

**A jailer may slap its own prisoner.** `CanSlap` refused, because a prisoner
belongs to the keeper it was captured from, so none of this was reachable. The
widening is exactly as far as the observation goes: your own creatures, plus the
enemies in your own prison.

**Only the drain raises.** A prisoner already out of health when the drain comes
round was killed by something else and is released instead. The test is the
health reading rather than a new "was damaged" flag, which is both simpler and
what the situation actually asks.

**The body goes to the rim**, the first free outermost tile of the prison it died
in — free meaning no creature on it. That is what puts it where an imp can
reach it without passing the gate, and it needs ADR-0139's tile set.

**The death itself is not booked here, and that is the point.** Releasing is
enough: `CreatureHealthSystem` skips prisoners and picks this one up on the next
game second, where standing on a Prison tile already means no rescuable window
(Q303, ADR-0138). The ordinary path then produces exactly the corpse observed,
which is a cross-check on ADR-0138 rather than a second mechanism.

**What replaced a test that pinned nothing.** `APrisonerIsNotSlappable` asserted
the opposite and cited no source — it had pinned whatever `CanSlap` happened
to do.

**Amended the same day.** A captive answers to its **jailer alone**, its own
keeper included — *"a keeper can't slap its own creature in someone else's
dungeon"* (owner). Prison is the only place this simulation puts a creature in
another's dungeon, so that is the whole reachable rule; reach in general is not.

**Left open.** The animation is presentation. The Skeleton cap **is** enforced —
at the maximum a withering prisoner dies to a corpse on the rim instead of rising
(owner) — but what it counts is unknown, so nothing enforces it yet (Q307).

**Consequences.** No state field and no stored quantity changed, so `HashVersion`
and the pin stand.

## ADR-0141 — Construction state backs door and trap trigger counts

**Date:** 2026-09-11 · **Status:** accepted · Q249–Q253, Q281, Q284 · Research: `docs/research/doors-and-traps.md`

**Context.** ADR-0106 retained authored doors and traps but declined conditions
27 and 28 because their runtime predicates were absent. Reading both complete
class vtables, their constructors, THINGS loaders and save ranges establishes
how the authored records seed those predicates.

**Decision.** Add hashed `UnderConstruction` state to doors and traps. A door
starts under construction exactly when authored `Flags` bit 1 is set. A trap
starts under construction exactly when authored `NumberOfShots` is zero; that
field is a Boolean construction marker in the loader, not a consumable count.
The verbatim authored fields remain for ADR-0106 compatibility.

Implement the counting modes of player conditions 27 and 28. Flag bit 3 counts
under-construction instances; bit 4 counts constructed instances; otherwise
zero `TargetId` counts all owned instances and a nonzero id filters by type.
Type 28's named Boulder Trap path also counts owned live Boulder objects.
Availability mode (`Comparison == 0`) remains declined because availability is
separate state and no shipped condition uses that mode.

**Door blocking is deliberately out of scope.** The binary read establishes
that constructed locked doors mark the navigation grid and that the update
toggles an open state from nearby creatures, but reproducing this touches
`DK2.Pathfinding`, imp task routing and every routed scenario. Neither trigger
condition needs it, and the open/close eligibility partitions are not fully
named, so it must arrive as its own measured mechanic rather than a side effect.

**Consequences.** Version 56 was taken by parallel work; `HashVersion` and
snapshot `FormatVersion` become **57**. The branch-base determinism literal is
re-pinned from a computed run. At 1,200 ticks the
campaign oracle moves `stalls / moved / after1` from **53 / 62 / 11** to
**46 / 62 / 11**; foreclosed gates fall from 145 to 124. Doors still do not
open, close or block movement; traps still do not fire.


## ADR-0142 — The prison drain is a relationship, not a flag

**Date:** 2026-09-11 · **Status:** accepted · Q266, Q302, Q308

**Context.** The owner: a campaign map can hold a creature in a prison **without
draining it**, so a script can release it when a keeper finds the place — "an
optional flag, on by default". The search began on the creature's `Flags` byte,
whose bits 5 and 6 are the only ones shipped data varies. It ended at the drain
itself, `FUN_0047B7C0`, read whole.

**Decision.** There is no flag. The drain runs every 16 ticks — four game
seconds, `variable 19 << 2`, which is where the original's coarser −12 comes
from — and takes health off only when all of these hold:

1. `HungerFill` scaled by level is non-zero (the Q173 gate, already built);
2. the creature is **in a room** at all, `+0x3B9` non-zero;
3. the room's owner and the creature's own owner **differ**, or they agree and
   that owner is **not the neutral player**.

So **a neutral creature in an unclaimed prison waits there indefinitely**, and
claiming the prison starts its clock. That is the campaign trick, and it needs no
per-creature switch. `PrisonRules.Withers` is the third gate in our terms.

**Two globals settle with it — Q266, open since 2026-09-08.** The drain
exempts `word[0x006F2548]`, an exemption that only makes sense for the player an
unclaimed room belongs to; the corpus places `NEUTRAL_CREATURE` records on prison
tiles, and the player frame returns early for the same id. **`0x006F2548` is the
neutral player and `0x006F254C` the hero one**, the latter already ours from the
party counter. Flame's map agrees; cited, not adopted.

**The flag hunt was not wasted, and its result is recorded rather than dropped.**
Shipped data uses four values of the `Flags` byte — `0x09` (the constructor's
own default), `0x29`, `0x49`, `0x69` — so bits 0 and 3 are universal and only
5 and 6 vary. Bit 6 is read **once in the whole binary** and sets state 76, whose
handler DK2 files in `KeeperState.cpp`; bit 5 twice, in a routine taking a room
and a creature. Neither is the drain (Q302).

**One caveat withdrawn the same day.** This ADR first called DK2's drain "wider"
than ours for keying on the room rather than on imprisonment. It is not:
`FUN_0047B7C0` is the **prison state's own** per-tick hook, so it runs for a
creature already in that state and the room lookup only chooses whose ownership
to compare. Nobody merely walking through drains — and nobody merely walking
through is stopped either, the confinement being the prisoner's and not the
prison's (owner). The gate is **inert today**: nothing here
imprisons a creature the map placed, so no neutral prisoner can arise. Built
anyway, because the rule it replaces said every prisoner that eats withers, and
that is wrong however few creatures reach it.

**Consequences.** No state field and no stored quantity changed; the pin stands.

## ADR-0143 — The Skeleton ceiling is per keeper, over that keeper's prisons

**Date:** 2026-09-11 · **Status:** accepted · Q307

**Context.** The owner: the ceiling is *"not global, not global per prisons on
the map, it is per keeper, per keeper's prisons"*, and asked for it against the
binary. The route in was `ModifyHealth`: when a health change is **exactly the
prison drain** and the creature's definition leaves a body, it hands the room
the creature is standing in a position and asks it to make a creature
(`0x004B3231`). If that returns zero it sets `DeadBodyOnDestroy` instead
(`0x004B323A`) — so the refusal and the corpse are the same branch.

**What the room does** — `FUN_004E30F0`, and it is two walks:

1. from the room's **owner**, walk that player's own room list (`player+0x36`),
   keep the rooms whose `TypeId` is 11, and **sum their
   `SafeCreatureCapacity`** (`0x004E3123`);
2. from the same owner, walk that player's **creature list**
   (`player+0x06`, `ThingsOwnedList[0]`) and count the ones whose type is the
   room definition's `CreatedCreatureId` (`0x004E315C`).

`count >= sum` refuses. **The owner's claim is confirmed exactly**, and the two
walks answer more than was asked: the ceiling is the sum over the keeper's
prisons, so a second prison raises it for both and selling one lowers it; and the
tally counts the keeper's Skeletons **anywhere on the map**, so one that walked
out of the cell it rose in still occupies the ceiling.

**Decision.** Build both walks — `PrisonRules.SkeletonCap` and
`SkeletonCount` — and have `PrisonSystem` route a refused prisoner into the
body-on-the-rim path ADR-0140 already built, which is what DK2's
`DeadBodyOnDestroy` amounts to here.

**Independent corroboration of the offsets.** `player+0x36` as the room list and
`player+0x06` as `ThingsOwnedList[0]` are DK2's own dumper names
(`player-object.md`), read months before this, and `room+0x4B` as
`SafeCreatureCapacity` likewise. Nothing here needed a new inference.

**A route that did not work, recorded so nobody repeats it.** The tooltip string
2586 carries the number, and its builder's argument descriptors use two different
vtables — which looked like two data sources and is not: both are string
**formatting-tag** classes, multi-byte and unicode. The display told us nothing
about where the value comes from.

**Consequences.** No state field and no stored quantity changed; the pin stands.

## ADR-0144 — Action type 17 is read, and deliberately not built

**Date:** 2026-09-11 · **Status:** accepted · Q237, Q309

**Context.** Type 17 was the next build candidate: 291 records across 40 levels
**including level 1**, so unlike types 1, 7 and 8 a level-1 replay would exercise
it and a capture could measure it. The arm at `0x00519257` was read end to end
first, to size the work. It turned out to size it at zero.

**What the arm does** — resolve the `ActionPointId`; **succeed and do nothing
if it does not resolve**; walk the point's rectangle inclusively; per slab set or
clear **one bit of the slab's flag word** according to body byte 1; then send one
rectangle-shaped notification. Full detail, including the multi-slab chain case,
in `trigger-dispatch-tables.md`.

**Decision.** Build nothing, and leave type 17 reported as `Partial`. Two
independent findings support it, either sufficient:

1. **Every reader found for that slab bit is display** — one overhead-map
   painter, in which a set bit suppresses the tile's colour. That painter tests
   the **fog** bit (slab `+0xB` bit `0x40`, `fog-of-war.md` §11) separately in
   the same expression, which rules fog out as this bit's meaning and
   corroborates the fog finding from a second direction for free.
2. **The notifications enter an optional sink** — one forwarder, 47 call
   sites across every subsystem, which returns success and does nothing when no
   handler object is installed. A channel the engine runs correctly without does
   not carry simulation state.

**What this buys even though no code changed.** Type 17's `Partial` is now a read
result rather than an absence of one, and 291 records stop being a to-do. The
rectangle corner **order** (`StartX +0x38`, `StartY +0x3A`, `EndX +0x3C`,
`EndY +0x3E`) moves from STRONGLY INFERRED to CONFIRMED, because the walk indexes
the slab array with one pair as the column and multiplies the other by the map
width. Q237's open half — *who walks a point's rectangle, and when* —
gains a named answer: this arm does, once, when the action runs.

**Rejected: modelling the bit as hashed tile state.** It would bump
`HashVersion` to store a field no rule reads, on a meaning nothing has pinned
— pinning a guess in the one place the project treats as settled.

**Left open.** What the bit *means* is UNKNOWN and is Q309. **"No simulation
reader found" is not "no simulation reader"**, and a mask that folded this bit
with neighbours would not appear in a scan for the constant.

**Consequences.** No state field, no stored quantity and no behaviour changed;
the pin stands.

## ADR-0145 — Q204's two flags were already named in files we had

**Date:** 2026-09-11 · **Status:** accepted · Q204

**Context.** Two flags gate DK2's job path and neither was identified: a
per-player bit in the dispatcher, and a per-creature-type bit in all three
choosers. Both were opened as Q204 in the sitting that reversed
`job-selection.md`, and the plan carried the first as the candidate for a
**fallen dungeon heart**, which would have made it the next feature's key.

**Decision.** Record both answers, build neither, and withdraw the defeat
hypothesis.

**`player+0x76` bit 2 is `CallToArmsOn`.** The player debug dumper reaches
`+0x76` once per bit; at `0x004BD31B` it shifts right by **2**, masks 1, and
prints `CallToArmsOn %d`. The shift is the evidence — the order of the names
already transcribed in `player-object.md` only corroborates it. The guard takes
the creature's **owner** (`+0x2A`, Q262's pointer), so the flag is the keeper's:
set, the dispatcher jumps past its whole `0..0x1C` table and admits jobs 10 and
16 alone. Job 10 sets state **194**, shipped label `leaving Dungeon`. **So a
Call To Arms suspends voluntary job-taking for that keeper's whole workforce.**

**`def+0x686 & 0x4000` is `IsHorny`.** The loader copies file `0xEFC` into
runtime `+0x686` unchanged, so `0x4000` is bit 14, which `CreatureEnums.cs` has
named `IsHorny` since it was written. Verified the ADR-0086 way rather than
trusted: the bit is set on **exactly one of the 46 shipped creatures**.

**Nothing is built, and the first cannot be.** This simulation has no Call To
Arms, so there is no state to drive that gate from; the omission is recorded at
`CreatureJobSystem` so it lands when Call To Arms does. The second is inert for
a different reason — Horny is never created here.

**What this cost, and the pattern it makes.** Both answers were already inside
this repository: one in a research doc transcribed for Q264, one in an enum
written months ago. **Nobody had put either beside the question.** That is the
second instance in one day — the first being the same `IsHorny` bit — so
the lesson is not about these two flags: **when a question names an offset or a
mask, search the repo for the offset before searching the binary.**

**Consequences.** No state field and no stored quantity changed; the pin stands.
Defeat loses its candidate flag and needs a new one (Q93, Q207).

## ADR-0146 — Auto furniture is reconciled from room shape

**Date:** 2026-09-11 · **Status:** accepted · Q89, Q270

**Context.** DK2 attraction needs room `TotalCapacity`, but that value counts
room-owned furniture rather than geometry. The simulation had no way to
distinguish such furniture from objects placed by creatures, the level, or a
keeper. Binary evidence for the implemented placement subset is recorded once
in `auto-placed-furniture.md`.

**Decision.** Add a hashed and snapshotted `ObjectState.AutoPlaced` bit. After
room construction each tick, derive the desired generic floor and wall furniture,
Temple Hand, and Hatchery coops from current room shapes, then reconcile only
objects bearing that bit. Object type choice uses DK2's coordinate-stable
selector, not the simulation RNG.

Room instances remain derived views. Storing a second furniture list on them
would duplicate world-object state and need invalidation whenever room tiles
change. Reconciliation preserves keeper-, creature-, and level-placed objects,
even at an identical tile and type.

**Boundary.** Wall placement requires both `Solid` and `AllowRoomWalls`; the binary's
extra grid test is its cached `Solid` classification. Sorted tiles stand in for
its linked-list phase. Object rotation, sub-tile offsets, and room-specific
decorative passes remain open. The five known capacity arms are exposed;
portal attraction is separate work.

**Consequences.** `HashVersion` and snapshot format become 58. Existing saves
are intentionally rejected; the determinism pin changes with the added bit.
## ADR-0147 — Attraction is a per-type budget, and the Lair is exempt

**Status.** Accepted 2026-09-14.

**Context.** `PortalRules.IsAttracted` answered attraction as a boolean with no
cap, so a keeper with one Temple received Dark Angels for ever. The owner saw
otherwise on 2026-09-11 — a 3x5 Temple produced none, a 5x5 produced exactly
**two**, and other types kept arriving throughout, so the portal was firing and
declining her. `binary-entrance.md` §4/§4.1 had the mechanism read and
unbuilt for want of room furniture, which ADR-0146 has since supplied.

**Decision.** A fifth gate, `AttractionBudget.Admits`, after ADR-0042's four.
For the creature's **first** attraction slot — the only one the original's
builder reads — sum each matching room's contribution and admit the type while
that sum **exceeds** the number of that type the keeper already holds.

| Room | Contributes | |
|---|---|---|
| Lair | never capped: the arm stores a flat 1.0 and jumps past the draw-down | CONFIRMED |
| Temple | **2** while it holds object 66, the Temple Hand, else 0 | CONFIRMED |
| Prison, Combat Pit | the room's slab count | STRONGLY INFERRED |
| every other | `AutoFurnitureRules.TryTotalCapacity` | CONFIRMED |

Whole numbers where the original uses 12-bit fixed point: every contribution is
shifted left 12 and every subtraction is exactly 1.0, so the comparison is the
same one and no rounding enters.

**Consequences.**

- **Goblins are unchanged**, because they are the Lair case. That is why level 1
  still matches and no scenario moved.
- Nine of the ten attracting types are capped where they were unbounded.
- No world state is added, so `HashVersion` is untouched.

**Assumptions, stated rather than hidden.**

- The Prison and Combat Pit arm also guards on `byte[slab+0x0C] == 0xFF`, a field
  this project has not read. If it excludes slabs, those two contribute fewer
  than the slab count. One live read of a prison settles it.
- The Lair arm has a second branch giving 3.0 when a creature byte `+0x771` is
  **22**. Unexplained, and harmless here: the draw-down is skipped either way, so
  1.0 and 3.0 both admit for ever.
- Slot 1 is never budgeted, only required to exist (`PortalRules.IsAttracted`).

## ADR-0148 — Two disposal tiles, and the prison decides at the door

**Status.** Accepted 2026-09-15.

**Context.** Owner play on 2026-09-13 recorded three divergences and built none
of them. Two are behavioural rules with no missing rooms behind them: disposal
has **two** unwalkable targets rather than one, and a prison refuses a delivery
**at the door** rather than before the imp sets off.

**Decision 1: `DisposalRules`, and the two targets are exclusive.** A portal's
hole takes anything that is not a worker; a dungeon heart's raised platform takes
a worker and nothing else. Both bypass the walkability refusal in
`HandSystem.Drop`, as the hole alone did (ADR-0084); neither bypasses ownership,
so a rival's heart is refused the way a rival's portal already was. A captive is
refused both, which is now CONFIRMED rather than the conservative fallback it was
chosen as.

**Decision 2: capacity is checked at delivery.** `PrisonRules.DeliveryTileIn`
gives a free cell where one exists and the nearest full prison's **rim** where
none does, so the imp sets off either way; `PrisonRules.AdmitsAt` decides at the
door, and a prison that filled during the walk turns the body away. The manual's
*"imps stop collecting prisoners"* describes a keeper with **no** prison, which
is still -1 and still nobody setting off.

**Consequences.**

- A keeper whose prisons are full now has imps that carry and are refused,
  leaving bodies on the rim. That is work where there was none, and it is what
  DK2 does.
- `HashVersion` is untouched: no world state is added, and no scenario moved.

**Assumptions, stated rather than hidden.**

- ~~Three assumptions about the heart~~ **withdrawn 2026-09-15**: the owner
  supplied screenshots of both halves (Q312, closed). It is the **single centre
  tile** of a heart that takes an imp, not the raised 3x3; the imp's **gold
  vanishes with it**; and a **goblin is refused** the heart outright, so the two
  disposal tiles really are exclusive. A dungeon heart is **always 5x5**.
- **Ours, not DK2's:** a body already lying at a prison that will not take it is
  not picked up again, or the imp would carry it round the room for ever. The
  end state is the owner's; the guard is ours.
- Which rim tile a refused body lands on, and how ties between eligible prisons
  break, remain UNKNOWN.

## ADR-0149 — Persistent Temple history follows a deterministic room anchor

**Status.** Accepted 2026-09-15.

**Context.** DK2 keeps a three-offering ring on each Temple room, but our
`RoomInstance` is rebuilt from tiles and deliberately carries no persistent
state (ADR-0027). Sacrifice therefore needs stable ownership without turning
all derived rooms into stored entities. DK2's identity across edits is UNKNOWN.

**Decision.** `WorldState` stores ordered `TempleSacrificeState` records keyed
by the lowest tile index of the derived Temple plus its owner. Reconciliation
runs after room build/sell and before creatures act; Hand intake also reconciles
before use. History follows the component only while that anchor and owner
remain a current Temple.

Thus extending without changing the lowest tile preserves history. On a split,
the component retaining the anchor inherits it. On a merge, the lowest-anchor
component survives and the others are discarded. Removing the anchor, adding a
new lower tile, selling the room, changing its type, or changing its owner resets
the affected history. These topology rules are ours, not claims about DK2.

**Consequences.** Histories are sorted by anchor, hashed, and snapshotted.
`HashVersion` and snapshot `FormatVersion` move 58 → 59; the pin becomes
`0x659AB04378E4C2FE`. `RoomInstance` remains wholly derived.

## ADR-0150 — A scenario states its timings in game seconds, and asserts them

**Status:** Accepted (2026-09-15)

**Context.** Twenty-five of the twenty-seven scenarios asserted nothing; they
were prose with an event log attached. Populating the expectation blocks
(ADR-0147's gate) found that **six of the twenty-seven were no longer
demonstrating their own question**, and four of the six had been disarmed by one
unrelated change: ADR-0089 moved a game second from twenty ticks to four, and
every scenario that had been tuned in ticks silently stopped reaching its
subject. A bird landed 350 seconds into a cell whose occupants die at 83. A dig
that needed two applications was given one, so three scenarios claimed nothing
and then had their builds refused `NotYourLand` — for months, with green tests.

**Decision.**

1. **Every scenario whose claim is an event carries an `expect` block**, and
   `EveryScenario_MeetsItsExpectations` is the gate. Twenty-three do; the other
   four say in the file why they cannot, rather than looking unfinished.
2. **A scenario's timings are stated in game seconds and derived into ticks in
   the comment beside them.** A bare tick number is a unit waiting to change
   under it.
3. **An experiment broken by a unit change is repaired to its authored intent**,
   not re-described. Moving the bird back to seventy game seconds is restoring
   the experiment; re-writing the prose to say the captives die first is
   abandoning it.
4. **Where the repair is not determined by the authored intent, the scenario is
   left recording its own regression instead.** A tick that meant seventy game
   seconds converts to one answer; "how much unclaimed ground is little enough"
   has none. `an_imp_drags_an_enemy_to_prison` (Q315) and
   `the_keeper_picks_up_an_imp_and_slaps_it` (Q317) now assert what they do
   today, with a `because` that names the question. Picking a new map or a new
   seam for them would be redesigning an experiment to make a check green, which
   is the failure mode the gate exists to prevent.

**Consequences.** The corpus is a regression corpus. A run report names its
claims and whether they held, and the harness exits non-zero when one fails, so
the CLI and the test gate agree. The cost is that four scenarios now carry a
WARNING paragraph saying they no longer answer their own title — which is the
honest state of them, and was true and unsaid before.

## ADR-0151 — Graveyard life-force belongs to a persistent room anchor

**Status.** Accepted 2026-09-16.

**Context.** The Graveyard room dump names one room field, the dword
`DecomposeValue`. Burial supplies the creature field of the same name; shipped
variable 25 requires 1000 and the room's `CreatedCreatureId` is Vampire. A
Goblin supplies 200, independently agreeing with the five-corpse account (Q165).
`RoomInstance` remains derived, so the tally needs persistent identity.

**Decision.** `WorldState` stores ordered `GraveyardState` records keyed by the
room's lowest tile index and owner, reconciled by ADR-0149's Temple policy.
Only successful burial adds life-force; death does not, because Horny and Jack
have value 200 but produce no body. At the threshold the room raises its
`CreatedCreatureId`, and `corpse_buried` reports `tally` plus `raised=Vampire
as=N`, matching the Prison's creation event shape.

Four unresolved edges use explicit project defaults: spawn on the last burial
tile (Q318), subtract 1000 rather than zeroing (Q319), at most one Vampire per
burial (Q320), and clear the tally once reconciliation observes the room absent
(Q321). These are not claims about DK2.

**Consequences.** Tallies are hashed and snapshotted. `HashVersion` and snapshot
`FormatVersion` move 59 → 60; the pin becomes `0xE44F66372F8963D9`.

## ADR-0152 — Creature trigger roots join the per-turn entity pass

**Status.** Accepted 2026-09-16.

**Context.** THINGS creatures carry a trigger root, but projection discarded it
and the executor walked only action points and players. The binary's creature
setter snapshots the root condition's repeat byte; its per-turn condition and
action dispatchers delegate creature → owner player → world. The separate
event-filter caller is not a tree walk and remains out of scope.

**Decision.** Project each creature's root and its initial repeat snapshot,
hash and snapshot both, then walk creature roots in THINGS order once per game
turn after action points. Q330 records that relative ordering as UNKNOWN. The
creature dispatcher evaluates health percentage (36), carried-gold percentage
(37), and level (49). Types 3–16, 50, 54, 55, 63 and 65 return DK2's constant
false on this path. The ten creature-owned action types report `Partial`: their
success return is known, but their effects are not implemented by guesswork.

**Consequences.** In the 14,400-turn shipped corpus, executed records rise
3144 → 3793 and the creature-only bucket falls 1138 → 504. The declined
condition frontier stays 128: the newly reached event arms are intentionally
false here, while their firing path remains future work. Level16 still reports
two world-type-64 stalls gating two records, but its 48 creature roots are now
walked. `HashVersion` and snapshot `FormatVersion` move 60 → 61; the pin becomes
`0x57435BC71CE1BBDC`.

## ADR-0153 — The melee damage formula is built as a rule nothing calls

**Status.** Accepted 2026-09-16.

**Context.** Q8 is now read end to end (`combat-damage.md`). The three helpers
the accessor uses are a fixed-point add, divide and multiply, not the clamps
`combat.md` §5.1 guessed; its three attacker arms are mutually exclusive and all
three flags were already named in our own `creature-object.md`; and there is no
resistance step anywhere between the accessor and the health routine. What the
simulation does not have is an attack state, target acquisition, or the cadence
of `combat-cadence.md`.

**Decision.** Implement §2–§4 as `MeleeDamageRules`, a pure static class with
tests, and **wire it to nothing**. Reproduce the original's two rounding
behaviours rather than tidying them: the damage is negated before the bonuses,
so every `/ 100` truncates toward zero, and a blow that rounds to zero never
reaches the health routine at all. Take the bearing as a raw angle rather than
reimplementing `atan2`. Mark the possession percentage `Hypothesis`, alone in
the class, because the scalar the original reads has no writer (Q337).

**Consequences.** 22 new tests, 5 of them install-gated and re-reading the three
shipped inputs so the synthetic fixture cannot drift from the install. No
simulation state, so `HashVersion` is unchanged at 61 and no scenario moves.
The Combat Pit's shipped 100 is an increment against a ceiling of 100, so a pit
fighter hits for **double** — the variable's name argues for the other reading
and is now pinned by a test. Combat still does not happen; this buys the attack
state a formula instead of a guess when someone writes it.

## ADR-0154 — A captive dropped on a Temple is sacrificed, not freed

**Status.** Accepted 2026-09-16.

**Context.** ADR-0064's release rule — *dropped anywhere that is not a prison
tile, a prisoner goes free* — is CONFIRMED by the owner, and ADR-0149's Temple
intake consumes any creature dropped on a Temple. The two collide on one tile,
and `HandSystem` resolved it by excluding captives from the intake, under a
comment saying the confirmed rule wins until asked. Q314 asked.

**Decision.** The Temple wins. **Owner, 2026-09-16: a wizard captive dropped on
a Temple was sacrificed** — *"the gods have accepted your sacrifice"*. The
`!PrisonRules.IsImprisoned` guard comes out of the creature-drop path, so the
intake is tested before the release, and ADR-0064's rule carries an explicit
Temple exception rather than a silent one.

**Consequences.** One guard removed, one test inverted —
`APrisonerDroppedOnATempleGoesFreeRatherThanBeingSacrificed` asserted the wrong
half for a day and is now
`APrisonerDroppedOnATempleIsSacrificedRatherThanFreed`. It had named its own
doubt in a comment, which is why it was findable the moment the answer arrived.
No hashed state changes and no scenario covers the path, so `HashVersion` stays
at 61 and the determinism pin does not move. A captive is an offering like any
other: it enters the Temple's three-entry recipe history under its creature type
and current level.

## ADR-0155 — The pathfinder's replacement target is known, and not chosen

**Status.** Accepted 2026-09-16 · amended the same day: the owner confirms creatures push each other (Q345), so one steering behaviour is now an observed divergence and not only a structural one.

**Context.** ADR-0012 shipped `GridPathfinder` as an admitted placeholder
because DK2's routing was unread. It is now read at the architectural level
(`navigation-and-steering.md`, Q5): the original routes over a **triangulated
navigation mesh**, reduces a corridor of gates to waypoints, and follows it with
a **steering system** of eight named behaviours. Ours is eight-connected A* over
tiles.

**Decision.** Record the divergence and change no code. Replacing a grid search
with a navmesh and a force-based mover is an architectural decision with a cost
far beyond this finding, and the owner makes it. `GridPathfinder` keeps its
`Dk2Behavior` confidence and its docstring gains a pointer to the research.

**Consequences.** The two observed facts ADR-0012 rests on survive, because they
were measured against the running game rather than inferred: eight-connectivity
with corner-cutting (Q28) and no terrain weighting (Q30). Any replacement must
still satisfy both. What does not survive is the interface's premise that a tile
route is what DK2 computes. No behaviour changes, so `HashVersion` stays at 61
and the determinism pin does not move. Q341–Q344 carry the unread algorithms.

## ADR-0156 — Separation is built as a rule and wired to nothing

**Status.** Accepted 2026-09-16.

**Context.** The owner confirms creatures push each other (Q345) and ours walk
through each other. DK2's force law is now read end to end
(`navigation-and-steering.md` §3.2): a gate on overlap, an octile distance, a
ratio against summed radii and three arms.

**Decision.** Build `SeparationRules` as pure arithmetic reproducing the
original's operations, and call it from nothing. Wiring needs two things this
project does not have: DK2's rule for combining five behaviour forces into
motion (Q343, unread), and a mover with a velocity to accept one. Inventing
either would put a guess underneath every creature's position, which is the
one place this project has agreed not to guess.

**Consequences.** Creatures still pass through each other, and that remains a
known, recorded divergence rather than a silent one. The arithmetic is
transcribed rather than reinterpreted — including an integer square root kept
because it stops at its first converged guess. The branch that would not
reconcile was answered by reading the caller instead (Q346, Q343): every force
is truncated to `max_force` before weighting, so the two arms agree downstream.
No
simulation state moves, so `HashVersion` stays at 61 and the determinism pin
does not move. Same shape as ADR-0153 for the damage formula, for the same
reason: the rule is knowable now, the integration is not.
