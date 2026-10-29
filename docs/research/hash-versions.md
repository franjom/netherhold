# State-hash versions

`GameSimulation.HashVersion` numbers the shape of the simulation state that
`GameSimulation.StateHash()` folds, and `WorldSnapshot.FormatVersion` is kept
equal to it. `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` pins
one literal hash for a 500-tick run of `TestWorld.Simulation(seed: 0xDEADBEEF)`.

**The rule.** Change simulation state or hashing only with matching hash/save
version bumps and a new pin in the same commit. Unexplained pin failures block
shipping. Rationale: `docs/architecture/simulation-loop.md`.

**Composition versus behaviour.** A version bump changes the pin even when a
new field is omitted from hashing. State absent from its fixture needs separate
sensitivity tests (`PlayerMessageTests`); snapshot round trips cannot prove coverage.

## Versions
**330** - `0xAEA4A96E6A098C4A`. FormatVersion also 330. Behaviour only: the states a flight ends through hold a turn each, as the dispatcher `0x00476FC0` runs one handler a turn (ADR-0580): a withdraw's arrival enters 202, whose handler (`0x00461850`) returns 0 on the next turn; the alarm's arrival enters 251, whose handler dispatches the room on the next and leaves a worker in 252 while its action 79 plays (`0x00458680`; one play of art slot 29, 4 turns for the Imp) and anyone else idle; a responder's 254 counts from the turn after its arrival; and a creature a combat handler sets to 1 runs 1's handler on the next turn (`IdleHandlerWaits`). No fields added; the pin fixture moves by the version only. `PostFlightDwellTests`, `AlarmActionOnRealDataTests` and the updated sequences in `CombatFearTests` and `CombatNonFighterTests` cover it.

**329** - `0xAC8AC17E507DA2D0`. FormatVersion also 329. Hash algorithm only: `Hash64` mixes one value per multiply-rotate round and finishes with xxHash64's avalanche (ADR-0576). No hashed field, behavior or save layout changes. `Fnv1a64` retains stable asset/navigation fingerprints; saves and journals from 328 are refused. Verification: the throughput plan's Task 1 equivalence check.

**328** - `0x2B98D9C202582A74`. FormatVersion also 328. Behaviour only: a keeper's raw idle (state 1) runs the mood prelude and its arms, then hands over to 89, whose body (Call to Arms, lair, dropped job, happy chooser, fallback) runs on a later turn (`0x004773D0` -> `0x004778E0`; ADR-0574, Q672). A job's walk is saved with 89 as its caller, and an unhappy creature whose prelude takes nothing is charged no-work anger by 89's happy chooser; the angry chooser runs once a turn (`CreatureAngerSystem` marks it for the prelude); a Portal joiner makes its lair attempt as it leaves 193 (`0x00459D34`). No fields added; the pin fixture moves by the version only. `CreatureJobTests.AnUnhappyCreatureWithNothingToDoIsChargedByEightyNinesHappyChooser`, `AnAngryCreatureChoosesOnceInATurn_AndTakesNoJobInTheTurnItsAngryChooserChose`, `PortalCreatureActionTests.AJoinerSetsOffForALairInTheTickItLeaves193` and the 1 -> 89 sequences in `CreatureJobTests`, `CombatTests` and `LairAcquisitionTests` cover it.

**327** - `0x0A899428097E5E19`. FormatVersion also 327. Behaviour only: a zero Manufacture credit fails the category 5 update (`0x0047B190` returns 0), and the creature goes idle (ADR-0573). No fields added; the pin fixture moves by the version only. `DroppedToDoJobTests.ACreatureThatCannotManufactureIsDroppedInTheWorkshop_NeverEarns_AndIsIdledByEveryHook` and `ACreatureThatCannotResearchIsDroppedInTheLibraryAndStaysEarningNothing` cover it.

**326** - `0x7867DC185C8E72EE`. FormatVersion also 326. Behaviour only: the two route helpers' walks (states 6 and 7) end a running action (`0x00471890`, `0x00471A70` -> `0x00496D20`; ADR-0572, Q671), so a hold or a cast begun before the walk does not outlive it; state 8's approach keeps it. No fields added; the pin fixture moves by the version only. `CreatureStateMachineTests.ARouteHelpersWalkEndsTheRunningAction` and `TheCombatApproachKeepsTheRunningAction` cover it.

**325** - `0x55C440B501A59863`. FormatVersion also 325. Behaviour only: a door or trap order is pushed while the keeper's blueprints of the type outnumber its crates, queued orders no longer counted as crates (`0x005066E0`), and an authored door blueprint is queued at its own record (`0x00520F40`, ADR-0571). No fields added; the pin moves by the version only (the fixture has no blueprints).

**324** - `0x6F4F1D921054B690`. FormatVersion also 324. Behaviour only: creature roots run after their own update, area roots after creature work, and player roots after room ticks (ADR-0568). Same-turn ownership changes can start timers immediately. No fields added; the pin fixture moves by the version only. `TriggerSchedulingTests` covers visibility, the root order within a turn and the one-based turn every walk stamps.

**323** - `0x32086DAF699C9E45`. FormatVersion also 323. Behaviour only: waiting heroes start action 43 and preserve its hold across a failed standing job (ADR-0566). The existing ActionUntilTick carries it; no fields added. Clip timing retains the existing fresh-install surrogate boundary. HeroWalkTests.AWaitingHerosIdleActionOutlivesItsFailedJobDeadline covers the missing hold; the pin fixture moves by the version only.

**322** — `0x7A521062FC7B6D7A`. FormatVersion also 322. Behaviour only: every steal handler measures the holder from its position (`+0x1A/+0x1E`), at work or walking (`0x00469AF0` both arms, `0x0046AB50`, `0x0046AEA0`; ADR-0565); ours had used the stance point for a holder in its entry or work state. No new fields; the pin fixture moves by the version only. The Level 4 seed-1 3700 resync and Level 1 seeds 1-2 are byte-identical to hash 320. `TaskStealingTests.AHolderAtWorkIsMeasuredFromWhereItStands` and `AMinerAtWorkIsMeasuredFromWhereItStands` cover it.

**321** — `0x2B73025DDCF2DBEF`. FormatVersion also 321. Format only: the snapshot tags its random generator and carries `Dk2Lcg`'s seed as well as xoshiro's state (ADR-0564). No hashed field changes; the pin fixture moves by the version only. `SnapshotTests.RoundTrip_CarriesTheDk2Generator` and `Read_RefusesAGeneratorTagItDoesNotKnow` cover it.

**320** — `0xE8F0B969D072207C`. FormatVersion also 320. Behaviour only: state 26 asks its hands/store check and its pile check on the call that begins its next action, where the work clock begins one (`0x00465870`, `0x00496AA0`; ADR-0563); after the first strike that is the clip's wrap, 3 or 4 ticks later. No new fields; the pin fixture moves by the version only. `GoldCarryingTests.AMinerFilledByItsFirstStrike_LeavesWhenThatStrikesActionEnds` and `AMinerWhoseOverflowTakesItsPileOverTheLimit_OnALaterStrike_GivesTheSlotUpOnThatStrikesTick` cover it.

**319** — `0x1070AB7A291DA881`. FormatVersion also 319. Behaviour only: state 26 asks the hands test and the store chooser on its first update, before the strike timer, so a miner that reaches its seam full leaves on its first tick in 26 (`0x00465870`, ADR-0562). No new fields; the pin fixture moves by the version only. `GoldCarryingTests.AnImpThatReachesTheSeamFull_LeavesOnItsFirstTickInStateTwentySix` and `AnImpThatReachesTheSeamFullWithNowhereToStore_StrikesOnTheUsualTick` cover it.

**318** — `0x13D94E8C07770596`. FormatVersion also 318. Behaviour only: the fear accumulator returns for a stunned creature (209, 255, 256, 257; `0x0048F350`) before its handover, so an attacked creature's `TempFear` piles up through a stun and it radiates nothing (ADR-0559). No new fields; the pin fixture stuns no creature and moves by the version only. `CombatFearTests.AStunnedCreatureNeitherHandsOverNorRadiates_ItsIntakePilesUp` covers it.

**317** — `0x8718652BBA69C08B`. FormatVersion also 317. `PlayerState.ManufactureRingTail` (the ring's tail `+0x802`) and `ManufactureSlotZeroCost` (the cost word at ring slot 0): the adder pays at slot 0's cost, not the head's (`0x00506B50`, Q618, ADR-0557); the panel's percent divides by it. Enqueues go through `ManufactureRules.Enqueue`. The pin fixture makes no crate. `CrateTests.TheNextCrateIsPaidAtSlotZerosCostNotTheHeadsOwn_Q618`, `SnapshotTests.TheManufactureRingsTailAndSlotZeroCostChangeTheHashAndSurviveRestore` and `BottomPanelIndicatorsTests` cover it.

**316** — `0x962BD3492ED4F056`. FormatVersion also 316. The combat states' handlers run as a step after the needs routine, so a creature the scan engages runs its fight state in that update (ADR-0555); the scan no longer posts the battle notice; never-used melee and spell stamps are `CreatureState.NeverUsed` (−1), which also moves the pin fixture's creatures' hashed stamps. `CombatAttackerScanTests`, `CombatNonFighterTests`, `CombatFearTests` and `CombatTests` cover it.

**315** — `0x6D517264B5EC57B9`. FormatVersion also 315. Behaviour only: one creature's whole update before the next one's, newest first (`CreatureUpdateSystem`, ADR-0554): the fight, the needs routine, the anger ladder and angry chooser, the state handlers, the mover, then a worker's handler; an imp sent walking during its update steps from the next; payday's clock runs before the creatures and sets a credited creature off whatever its retry stamp; the manufacture loss moves into the player frame. No new fields; the pin fixture's run is unchanged and the pin moves by the version only. `CreatureUpdateSystemTests`, `PaydayTests.TheClockSetsACreditedCreatureOffAtOnce_WhateverTheNeedsRoutinesRetryStamp` and `ImpWalkEndTests` cover it.

**314** — `0xABEF4268245B9904`. FormatVersion also 314. Behaviour only: a Bile Demon's idle shot takes its first update the turn it is made, as a list-0 shot does in `0x004B7830` (ADR-0551). No new fields; the pin fixture has no Bile Demon. `RefusedStateTests` covers it.

**313** — `0xC84A9341D87683EF`. FormatVersion also 313. The F5 branch (311, 312: ADR-0548, ADR-0549) merged with 310: the composition of both, with the branch's own `GameMode` byte dropped for 310's enum. `VerdictRules.End` now takes the database so each ended player's statistics are sampled. No other change.

**312** — `0xECE45FDBEDAA605E`. FormatVersion also 312. `PlayerState.Finale` (the win sequence's player fields: `+0x76` bits `0x40`, `0x800`, `0x40000`, `0x80000`, `0x400000`, the landing point `+0xB29..+0xB31`, the gem tile and id `+0xB35..+0xB3D`, FireworksTime `+0x111D`, the camera glide hold and state 233's pickup tick and flags), `WorldState.LocalPlayerId` (world `+0x264`) and `CreatureState.ReaperHidden` (`+0xF3` bit 0) (ADR-0549, `campaign-win-sequence.md`). The pin fixture ends no game; `FinaleTests` cover sensitivity and snapshot restore.

**311** — `0x5D42400187C9E3C2`. FormatVersion also 311 (branch only). `PlayerState.Statistics`: the level-statistics counters DK2 keeps at `player+0x142`..`+0x185` that were not already fields, the level-end sample of `0x004BE630` and the counters frozen at the verdict (ADR-0548, `level-statistics.md`). `WorldState.GameMode` (DK2's `0x0075AD4B`, a byte here; master's enum replaced it in 313) and `WorldState.Carry`, the profile writes specials 129 and 140 make during play (`CampaignCarry`); special 140 stores an own creature and kills it bodyless (`0x004A3FC0`). The pin fixture ends no game and activates no special; `LevelStatisticsTests` and `SpecialSystemTests` cover sensitivity, snapshot restore, the end sample, both arms and the transfer arrival.
**310** — `0x07AD9F97E72E7FD3`. FormatVersion also 310. New fields: `WorldState.GameMode` and `ActivePlayerOrder` are hashed and saved, and `PlayerState.AlliedWith` (already hashed) is now saved (ADR-0546, ADR-0547). Behaviour: the level's id-66 alliances apply at load, imps refuse an ally's floor, verdicts carry to allies in skirmish and multiplayer, the heart tail walks the active list, the last-keeper award is skipped in the campaign, and the time limit counts down before the trigger walk. The pin fixture has no alliance records and no fallen heart; `LevelAllianceTests`, `VerdictRulesTests` and `DungeonHeartTests` cover the rest.

**309** — `0x0528E18272904DCD`. FormatVersion also 309. Behavior only: a withdrawal's step 5 spends the away-from-target stage's draws when a live creature or trap is staged, and each caller stages what DK2 leaves in `+0x502` — the NonFighter arm its candidate hero, `SelectAVictim` nothing; the local tries no longer require a walkable tile (ADR-0545). No new fields; the pin fixture has no withdrawal.

**308** — `0xC26E46D419A4702E`. FormatVersion also 308. Behavior only: a withdrawal's failed room pick (no rooms, or a drawn bridge) ends it with no further draw, and the pick is `0x004C63D0`'s list index (ADR-0544). No new fields; the pin fixture has no withdrawal. `CombatFearTests`' roomless and bridge tests fail on 307; its three-unroutable-room test pins that step 5 is still reached.

**307** — `0xC5540B4DA9053FF3`. FormatVersion also 307. Behaviour only: a creature with a bed goes to it only when tired; a wound alone no longer sends it, because the wound branch's Sleep job routes only on the latch `+0x58` (ADR-0543). No new fields. `CreatureNeedsTests.AWoundedGoblinWithABedWaitsUntilItIsTired` covers it.

**306** — `0xD4E1B134A3DB5F84`. FormatVersion also 306. Behavior only: hero updates and both party-link scans run newest first (ADR-0542). No new fields; the pin fixture has no active hero pass. Initial party destinations, follower numbering and removal have separate regressions.

**305** — `0xD5067604AB8DBDF9`. FormatVersion305 retains per-tile player-room-list insertion positions and the next world insertion counter (ADR-0541). Construction, capture, absorption and split replay maintain order; the combat room cache uses it. Focused tests check hash sensitivity, snapshot restoration and subsequent insertion.

**304** — `0x050D6062F2D6C047`. FormatVersion also 304. Global Secret Door concealment, per-keeper entry order and retained holders, and the creature's selected Secret Door marker enter state (ADR-0536). The pin fixture has no doors; focused hash sensitivity and snapshot checks cover the additions.

**303** — `0x391985F81BB2BA42`. FormatVersion also 303. Owner-directed withdrawal of the portal-crystal correction (ADR-0527): remove derived footprints, swept movement guards, target projection and predictive contacts; restore pre-correction steering. Portal rendering, hole navigation and Heart protection remain. No new snapshot fields. Packet outcomes are [remeasured against hash 302](../experiments/2026-10-25-packet-replay-hash303/README.md).

**302** — `0x2A9B65A5C2262075`. FormatVersion also 302. Scripted speech retains type 21 when SpeakOnly is zero; first Portal types retain 13/14 and request the level override line. `PlayerState.IntroducedCreatureTypes` and `CreatureState.StatusFlowerUntilTurn` are hashed/saved (ADR-0535). The fixture has no introductions but hashes the new empty lists and zero deadlines; sensitivity and restore tests cover nonzero state.

**301** — `0x6DC0EE1B1BAD1B68`. FormatVersion301: retain runtime store-pile headings (furniture-placement.md §7). All Hand gold drops use the landing merge with optional fixed-point input, hashed with the command (gold-on-the-floor.md; keeper-spell-create-gold.md §3). Current packet measurements: [hash-302 experiment](../experiments/2026-10-24-packet-replay-hash302/README.md).

**300** — `0x2F1568CD9D248B1B`. FormatVersion stays 297. Behaviour only: each portal crystal blocks the convex hull of its `Portal_Gem` primitive below body height instead of that primitive's full bounding box, padded by the mover's radius with bevelled corners (ADR-0527 amendment 2). No new fields.

**299** — `0xC9315DE4157C529E`. FormatVersion stays 297. Behaviour only: every live store's turn draws the sparkle `0x004F3A00` on the world stream before its sweep — `random(100)` per Treasury and unruined Heart, and below 30 one more per tile holding gold, below 10 requesting effect 34 at the pile (ADR-0534); `PickUpStoreGoldCommand` lifts a store tile's whole gold into the hand as a neutral Gold Pile 1 (`0x004C0CB0`). Moves every replay rig: each level's Heart now draws every turn. No new fields.

**298** — `0x3E2A6741A425B7F1`. FormatVersion stays 297. Behaviour only: the keeper's own dig mark on a goldless wall ends reinforce and repair work there (a rival's tag and a gold wall do not) — the pool raise of kind 1 (`0x00507BB0`) removes the tile's kind-6/16 entries and refuses new ones, and states 22 and 24 drop their task once its action ends (`0x004663C4`); the wall keeps its health (ADR-0533, Q621). No new fields.

**297** — `0xC05DD2EF3BEE644C`. FormatVersion also 297. `TriggerState.SpecialInfoPlayerId` (action 46) and `MaximumSlaps` (player action 50, init -1); `CreatureState.HealthSourcePlayerId` (`+0x36`, stamped at each health-change call site) and `DeathUncounted` (`+0x3E6` bit 0x40, nothing sets it yet); `PlayerState.HeroesDestroyed` (`+0x14A`), credited in `CreatureHealthSystem.Die`/`CreditHeroKill` (ADR-0532). The level-end pass `0x004BE9E6`, the My Pet Dungeon score, `+0x146` and the `0x19E`/`0x173` tallies are not built.

**296** — `0x9182B5916A8366B9`. Behaviour only (295 on the step6 branch; renumbered when merged after ADR-0530's 295): a keeper's Imp dropped on one of its own Heart's eight platform tiles lands there instead of being refused; only the centre disposes of it, now crediting Create Imp's mana cost as state 71 does, and never on a ruined Heart (ADR-0526 amendment, owner 2026-10-23). No new fields.

**295** — `0x4E209623A4874882`. Behaviour only: a room tile is refused where `0x0044F800` finds a trap or door of any owner, or another player's living non-Imp creature; a dragged span tests only its seeds (ADR-0530, Q501). No new fields.

**294** — `0xA9B05FB68CF7011B`. Four saved tutorial-highlight rectangles, ids and signed tick timers (ADR-0528), plus predictive crystal contacts replacing oversized steering circles (ADR-0527 amendment). The pinned fixture has no active highlights or portal route; separate tests cover both behaviors.

**293** — `0xAA764D602CDE17DC`. Behaviour only: swept portal-crystal bounds, body clearance, target projection and steering contacts (ADR-0527). Bounds are derived from room tiles; no new saved fields.

**292** — `0x3CA3464C77E0C2AD`. Behaviour only: a hand drop obeys DK2's drop gate `0x0040D8F0` (alliance, solid and door tiles, the Heart, Portal, Prison, Temple, Torture and Combat Pit arms; ADR-0526). No new fields.

**291** — `0x4F7FEF46308B6B76`. The rooms' border cursors (room `+0x81..+0x89`: tile, orientation, phase), hashed and saved after the marked-creature slots: each room with HasFlame and an owner runs one round its contour and asks for a Flame Round Room on odd turns (ADR-0524). The pin's fixture has no rooms, so the change is the version and an empty list.

**290** — `0xE6FC955DAC6ECF7F`. The world's six marked-creature slots (world `+0x72BD`, creature id and value), hashed and saved after the Tremor slots: trigger action 42 fills them and a creature's removal frees its slot (ADR-0523). The pin's fixture marks nobody, so the change is the version and six empty slots; `PortalGemOnRealDataTests` round-trips Level 1's marked Lord.

**289** — `0xBD66BD442BC1F4D0`. Player `LastPaydayTick` and native-width creature name/blood indices, hashed and saved (ADR-0295 amendment, ADR-0521); master's 283–288 merged under it. Numbered 275, then 283, on classic-look/step6.

**288** — `0x9FB00F71132E91E9`. Native wet/blood footprint counters, saved and hashed; see [creature-footprints.md](creature-footprints.md).

**287** — `0xC428FD4390FADF96`. Behaviour only: Tier B's tile chain tries the tile's loose objects after its gold (`0x00469580`'s third step, ADR-0519). No new fields.

**286** — `0xBD6FBFFB91F1AADF`. Behaviour only: speed no longer scales with level; the body's cap is the definition's speed (`0x004887D0`, ADR-0518). No new fields.

**285** — `0xE44D70CE6838C66C`. Behaviour only: an alarm goes only to a Guard Room (`0x00475336`, ADR-0517). No new fields.

**284** — `0xA1D6204A54297315`. Behaviour only: walkers and dig turners keep a gone task, as states 6 and 13 do (ADR-0516). No new fields.

**283** — `0x07E0196E6BEA9DCA`. `CreatureState.ActionCount`, hashed and saved after `ActionHandlerDone`; idle actions play their clip and installs draw their coin (ADR-0515).

**282** — `0xA98DDD15FD7EB37B`. `CreatureState.ActionArtSlot`, `ActionAnimationPhase`, `ActionAnimationRate`, `ActionAnimationFlags` and `ActionHandlerDone`, hashed and saved after `StateEnteredTick`: the dig and mining actions end on the stored clip (ADR-0514).

**281** — `0x781C971C1E408A00`. Behaviour only: route requests and stops zero the body's velocity (`0x004D0F60`, `0x0048ECE0`; ADR-0513). No new fields.

**280** — `0xA9BE08812B0B9989`. Eight verified Imp corrections integrated under ADR-0512; see [imp-fidelity-integration.md](imp-fidelity-integration.md). `CreatureState.PosZ` and `CreationVelocityZ` are saved and hashed. Versions 275–279 were isolated experiments; their pins do not describe this composition.

**274** — `0x34A682FB8867CE5B`. Steering axes use resulting velocity after clamping (ADR-0509); no new fields. Versions 270–273 belong to preserved Imp experiments.

**269** — `0xFA0172F155ECAA5C`. Native-byte Heart routing and the earlier small core guard (ADR-0505); no new fields. Versions 266–268 belong to earlier Imp experiments; ADR-0512 records their integrated and provisional parts.

**265** — `0x10A9C84139F8DF10`. Behaviour only: creature neighbours come from the native tile rectangle before combined-radius contact checks; the torturer's victim route retains native arrival flags (ADR-0500). No new saved field.

**264** — `0xC53FC1CC3EABDE59`. Behaviour only: a portal admits a creature whose room entry asks size 0 with none of the room owned, so a Lair creature comes through before the first Lair (`0x004DC810`, ADR-0499). Snapshot format is 264.

**263** — `0x688ADFB86353DF0E`. `CreatureState.GemCarrier`, DK2's creature `+0x92` bit 2, hashed and saved after the standing job: trigger action 20 hands a creature a Portal Gem and sets it (ADR-0495). Snapshot format is 263. The pin's fixture has no carrier, so the change is the version alone; `PortalGemOnRealDataTests` round-trips a set flag.

**262** — `0x6A54498299CB6FDD`. `PlayerState.CameraPathEndTick`, saved after PortalsClosed: action 31 starts a scripted camera path and condition 72 waits for its end (ADR-0496).

**261** — `0xFD8573748CE724A2`. A level's VARIABLES id 77 records set creature definition fields (ADR-0491): Level 1's Lord of the Land Threat 125 and MeleeDamage 200, Level 4's Threat 150; no new saved field. The fixture has no level variables, so only the version moves the pin.

**260** — `0xBC2CC79AE5E4E213`. Exploration is saved and hashed alongside master's navigation stamps.
See `fog-reveal-sources.md`. Snapshot format is260.

**259** — `0xC8D3AFFC7D3F1C08`. A hero party's members stand on their spawn tile's corner, the tile times 4096, as `0x00519F90` places them, not its centre; a 3x1 gate's step-off keeps its tile centre (ADR-0490); no new saved field. The fixture raises no party, so only the version moves the pin.

**258** — `0xD08AD6BA391493C1`. A creature's spell shot leaves from its slot's origin turned by its heading (`0x00498989`), not its centre (ADR-0489); no new saved field. The fixture casts nothing, so only the version moves the pin.

**257** — `0x11DCC450617C3D56`. A creature getting up (257) may look for a fight: the may-look gate tests the state flag's bit 6, which 257 lacks, and the freeze timer, where ours refused every stunned and frozen activity (ADR-0488); no new saved field.

**256** — `0x766C3AB62E635E07`. `WorldState.NavStamps`, hashed and saved after the doors: navigation bytes that outlive their writer until the tile's terrain changes, a door's 0 left on water or lava by `0x004A66F0(0)` (Q600) and the Heart builder's 3 on its centre (`0x004DB700`, Q601); `WorldSnapshot.FormatVersion` 256. The fixture has no doors and no Heart record, so only the version moves the pin.

**255** — `0xE09E9405FB971F17`. The navigation mesh's locate cache (the original's
`DAT_006E5AC0`, sixteen cells) is state, saved and hashed in place of the locate
hint, and the whole-map build takes the original's reset diagonal, corner order,
split slots and constraint insertion (ADR-0486); `WorldSnapshot.FormatVersion`
255. The fixture routes nothing, so only the version moves the pin.
**254** — `0x14705036ECD12242`. The hero and neutral players' creatures run no needs, need-anger or mood choosers, the dispatcher's owner test (ADR-0485, Q599); no new saved field.

**253** — `0xEA82DB139335F65D`. The approach is state 8, a walk resuming 199, instead of a flag on 199; the engagement re-enters 199 only outside the Combat category read through a walk (ADR-0483). `Approaching` is derived from the state and left the snapshot: `WorldSnapshot.FormatVersion` 253. `CombatSystem` skips corpses (Q598).

**252** — `0x7326CEF9676EA896`. A creature walking to its fight target (our `Approaching`, DK2's state 8) earns no combat experience; ours paid it as if in state 199 (ADR-0366 amendment); no new saved field.

**251** — `0x3DFD1B0E0D7517C9`. The store sweep runs when DK2's turn, our tick plus one, is a multiple of 16, one tick earlier than under 250 (ADR-0285 amendment, Q596); no new saved field.

**250** — `0xBD323E55AFE88884`. The stun (255, 256) counts from the turn after it is set, as 255's Age does, so a hand drop lies one turn longer than under 249, as Level 1's capture measured; 255's first call asks for the Creature Dazed Effect (ADR-0480); no new saved field.

**249** — `0x8C0EAA543500440F`. The room ticks run after the creatures and before the player frame, as `0x004EC450` does: the Casino's, the Portal's and room health move there, and the Library, Training Room, Guard Room and Workshop step `0x004E8C60` angers a room's place-holders when both alignments meet in it (ADR-0479); no new saved field.

**248** — `0xC51BD03052ECA96A`. The Heart's opening sequence is timed in game turns (DK2's turn 1 is our tick 0), so the ordinary frame's wait ends one tick earlier than under 247; no new saved field.

**247** — `0x9B410CB31AADB8BD`. The Dungeon Heart room's ordinary frame (regeneration, ruin) waits until nine seconds after its opening sequence starts, as `0x004D9A60` does (ADR-0475); no new saved field. The view effects of the sequence are neither saved nor hashed.

**246** — `0xD6C4B6C05FD11458`. The flee run and the hero reluctance count only Guard Rooms near the
attacker (`0x00460B50`); no new saved field. ADR-0473.

**245** — `0xF345C33D7F7605C3`. Worker takes need a route (ADR-0471) and a reachable dig side, object or body (ADR-0472); no new saved field.

**244** — `0x2B86A02DFCAA09DE`. A creature's `ClaimActionTile`, the tile a possessed worker's
claim strikes each tick, and a work order's multiplier and strike flags are saved and hashed
(ADR-0469); the gem rule (ADR-0470) and the possessed object arms (ADR-0468) add no field.

**243** — `0x768B6A51253B3081`. The navigation mesh is simulation state: a
refresh edits the standing mesh around each changed tile (the original's
partial rebuild, `0x004CE770`) instead of building a new one, so the mesh
depends on the order of edits and the hash folds in every triangle, point
and free list; a save carries them and the locate hint too (ADR-0461).
**242** — `0xC2A0D662C94D7F10`. Sleep entry aligns ordinary creatures to
their bed and uses its heading; Mistress entry places her after the bed's
door animation starts; departure selection rejects stale wander routes
(ADR-0460). No new stored field.

**241** — `0xD4E74C513D204899`. A tunneller's walks keep their destination within
its own tile (`TryRouteToStance`), so a worker off its stand tile's centre walks
there and digs instead of re-planning the walk every period. No new persisted
field; the version distinguishes the changed resumed behaviour. ADR-0459.

**240** — `0x8FBB003CBA25CFD2`. Ownership-loss completion releases unsupported
walls using the native neighbour/corner rules; live Heart platforms are
excluded from movement and routing, with ruin reopening them (ADR-0451/0452).
No new persisted field; the version distinguishes changed resumed behaviour.

**239** — `0xBE0B8CC626E76E53`. Lair drops enter state 79, select a bed in
the landing room and release an old bed before the jittered walk to state 80.
No new saved field. ADR-0450.

**238** — `0xE834B736C858816C`. Claim, enemy-floor attack and room conversion
retain a centre destination for same-tile approaches. Visual effect requests
do not enter saved or hashed state. ADR-0449.

**237** — `0xBC650457E0557295`. Route-less fight states integrate contact;
in-range fighters face their target and hold melee through the authored
animation-completion poll. No new saved field. ADR-0447.

**236** — `0xC3C0A8A82912396E`. Combines possession followers/inputs with failed hero
jobs running the native idle wrapper;
same-tile wander and training routes retain their destination waypoint.
ADR-0444–0446.

**235** — `0xF586FE56CCF63F2F`. The possession/arms branch: followers (leader, route deadline, count, shared melee target; ADR-0445) and inputs (disguise, sniper mode, hypnosis hold, old owner, mark and attack counts on creatures, the sniper bit on shots; ADR-0446) are saved and hashed. Classic's unmerged idle fixes used `0x7033026B2672013F`; 236 separates the combined snapshot.

**234** — `0x9EE6056B64A1FD38`. The claim priority refuses a tile holding a closed, completed door (`0x0046AB50`'s door gate); no new saved field. ADR-0443.

**233** — `0x538823E3806E3E01`. Combines master's Receive Imps with saved/hashed
room-build waves, shared ambient rat updates and original Portal heading.
Includes classic versions 226–230. ADR-0442. Branch 96fa1942 reused 232
with pin `0x745E1A08B1224DFA`; 233 separates the merged snapshot layout.

**232** — `0xE5DB51390ABD6056`. Special 124, Receive Imps, creates ten level-5 Imps round the Heart; no new saved field. ADR-0441.

**231** — `0xB8FA72F6DE9C12A1`. A room on `Room_ComputeTotalCapacity`'s default arm (Guard Room, Hatchery, Treasury, ...) holds its tile count; no new saved field. ADR-0440; versions 226–230 were used by the parallel classic-look branch.

**230** — `0x0117CFA8C106044C`. Alarm retreat routes to the random room-chain
point instead of the fixed centre; Portal, Heart and bridge tiles are never
floor slots for that selector (combat-alarm.md §2).

**229** — `0xF2A7DEB8B43F2FC3`. Registered attackers are acquired before the
fight-style scan; Imps bypass its distance cap while retaining visibility and
route checks (combat-acquisition.md §6).

**228** — `0x315162363F8047CA`. Heart combat targets resolve the three Arch
Pillars at their builder positions instead of the outer room ring;
acquisition, approach, melee and shots use the same point (combat-acquisition.md §9).

**227** — `0xCBBDEF9956A975B1`. Live Heart cores reject movement segments and
route targets resolve to their edge; ruins release the structure mask and obstacles. ADR-0439.

**226** — `0xB369A6E155352AB0`. Supported animation lifecycles retain training actions, object animation stamps and physical poses, and the Prison door opening stamp. ADR-0438.

**225** — `0x2B74D4ED96BA549B`. Imps turn toward the task tile before digging, mining, reinforcing or repairing its wall, holding the entry state until aligned. ADR-0435.

**224** — `0x236F0479C5BE1A9E`. Owned Hatchery chickens run their movement/reaction loop, with saved and hashed heading, age, reaction state, movement block and room membership. ADR-0434.

**223** — `0x4A7D0294815B3F49`. Hero followers retain sub-tile destination walks after reaching their trailing tile. ADR-0433.

**222** — `0xD423AF7C779275F4`. Hatchery meals hold state 94 until action 26's first animation cycle completes, then use their captured remaining-food decision to resume the chicken search or idle. The captured decision is hashed and saved. ADR-0432.

**221** — `0xFB311154736B9A1B`. Waiting heroes walk to their chosen sub-tile points instead of receiving an already-finished route. No new saved field. ADR-0430.

**220** — `0xA2446806EE35E55E`. Combat updates creatures newest first, matching the original head-linked list. No new saved field. ADR-0429.

**219** — `0x83702433083CA7D9`. Hatchery eggs use the normal non-pillar slab path and two sub-tile draws; hatching and presentation retain coordinates. Existing saved position fields, no new field. ADR-0428.

**218** — `0x6ECAB92D5B5D6E64`. An outmatched fleeing Scout runs to raise the alarm; the near-room gate uses the attacker's tile and respects Call to Arms. No new saved field. ADR-0425.

**217** — `0xACDC3C26931DA73F`. Ordinary obstacle queries preserve tile-head order and retained set/toggle flags; grounded water speed is halved in entity units. No new saved field. ADR-0423 and ADR-0424.

**216** — `0x4C65FAEF24CB1622`. Torture attachment uses actual device positions and wall facing; wheel/chair obstacles participate in continuous steering. No new saved field. ADR-0420.

**215** — `0x867E1B17FFDAB65D`. Level-placed flag 0x40 creatures land into cells/devices; involuntary torture and state 76 prevent attackability; DoubleQuad interiors carry nav byte 4, with the Prison delivery option; wall torture companions are simulation devices; wall machines use their working-position inset (Q581); ruined-at-load free casts reject corner rock/outside-map points. No new saved field. ADR-0419.

**214** — `0xF18C419AE1575DF8`. A corpse is laid only on a floor slot with a tombstone, objects 31–34 (`0x004537E0` in `0x004E6380` and `0x004E6490`); the Graveyard's crypt (object 75, `0x004DD5C0`) placed on a rebuild, refusing its cell as a slot and raising the Vampire; a Graveyard rebuilt in play takes in the dead bodies on its tiles not yet its owner's (`0x004DD750`), through the new saved `CreatureState.BodyOwnerPlayerId` (the dead body's `+0x28`, neutral at death), which the corpse selections also require neutral; the test fixture's Graveyard takes its shipped flags and ObjectIds; no new field. ADR-0418 amendment.
**213** — `0x973803F63EBFD481`. A delivered corpse lies on a Graveyard floor slot (new `CreatureState.GraveyardSlot`, the dead body's `+0x132`, saved) until its lifetime ends and it is buried; a Graveyard's place counts its lying corpses; the pick-up takes the first Graveyard in room order with a free slot; a freed room lets its corpses go. ADR-0418.
**212** — `0x1CE851EB002A6064`. A collapsed own creature with no bed is carried to the nearest Lair with space and makes its bed there (`0x0046D810` result 4, `0x0046E610`, state 45 and 79); a bed lost during the carry re-routes to such a Lair; the room point `0x004E8430` is shared; no new field. ADR-0417.
**211** — `0x8800AAF8F8F81A4F`. A researcher's Library is the Library finder's (`0x004E2500`): a shelf draw per reachable Library on every search, the nearest by first slab, one with no holder of the other alignment first; no new field. ADR-0416.
**210** — `0x0D827AF91A745AD2`. A Casino's party moves with the room: kept as it grows, the joined room's kept in a merge, ended in every piece when a tile leaves (`Room_Construct`, `0x004ED440`); no new field. ADR-0415.
**209** — `0xE29E7F167923B105`. The corpse selections' Graveyard messages (41 once a level through the new `PlayerState.GraveyardWantedPosted`, player `+0xF4E`, saved; 42 with no place), a corpse taken only from its keeper's tile and only to a Graveyard with a floor slot. ADR-0414.
**208** — `0xDF2D50DC47317F6E`. The job-room finders' remaining messages (Workshop behind its queue, the Library's shelf test and 45, the Casino's party branch, the Mistress's 99, both Combat Pit finders) and Temple and Combat Pit capacity (their rim, `0x004E3ED0`); no new field. ADR-0413.
**207** — `0xEC031DFFFBBCFE6B`. The rescue selector's room messages (`0x0046D810`: 41/43/42 for the Prison or a bedless body's Lair) and the Prison's `Activation` (`+0x59`): on at a Prison's first tile and at map load (`0x004E2B30`), kept with the room as it grows or is sold, and required by the carry's Prison search; a casualty is taken only from a tile its keeper owns; no new field. ADR-0412.
**206** — `0x817A546857D0D020`. A Dungeon Heart's update (`0x004D9690`) sends its owner's creatures, workers included, running to an attackable enemy within `DungeonHeartReportingDistanceTiles` of it every fourth turn; no new field. The pinned world carries neither radius, so the value moved on composition. ADR-0407.
**205** — `0xA3E9B6DDE3F06795`. A room's place-holders are kicked by a neighbouring tile's terrain change (`0x004E44E0`, the latest claimant only), a new owner (slot `+0x0D`) and the room being freed (slot `+0x05`), and a Torture Chamber's victims through 136; new fields `TileState.RoomKickedTick`, `CreatureState.HeldPlaceSerial` and `WorldState.NextPlaceClaim`. ADR-0406.
**204** — `0x5C3A1B89341D720F`. The keeper's slap step reads a worker's last task kind (`+0x98`) rather than a held reinforce target, and a repair records kind 16, not 6 (`0x0046AEA0`); no new field. ADR-0405.
**203** — `0xAEBBF589AE314BD2`. The slap's state moves 67, 77 and 120 and their reaction (`slap-states.md`): a slap whose move is refused does nothing more; the reaction plays action 19 and goes back to its room's state; no new field. ADR-0401. Built on 202; 201 was this work's number while it waited on a side branch and is unused.
**202** — `0x6967DA71375E1109`. The room return states 62, 70 and 154 through `RoomEvictRules`, and the research cheer 60 on a shelved item (`room-return-states.md`); no new field. ADR-0402. (201 unused: see 203.)

**200** — `0x587802C58EC02B4B`. Category 0 states 5, 71, 86, 113, 126 and 135 (`refused-states.md`): every neutral creature idles in 5, the Heart's drop 71, the tunneller's 86, the evicted holders' 113 and 126 through `RoomEvictRules`, the willing torturee's 135, the gold cheer 211; the Bile Demon's idle shot; the Temple's offering only in its pool; no new field. ADR-0400.

**199** — `0xF759489159B4EB3E`. The Workshop's states 63–65 (`workshop-states.md`): the machine mode, state `+0xB9` (creature `+0x307`), and the Library's target point renamed to the shared `StateTarget`. ADR-0404.

**198** — `0x6B9E7F7D302E3BF3`. The Temple's states 101–107 (`temple-states.md`): creature `HeldPlaceStamp` (ours, the held room's size stamp at the claim, for 107), and every job's walk to a room ends at DK2's room point `0x004E4C10` rather than the room's centre. ADR-0403.

**197** — `0x7E3268744B92B012`. The Library's states 53–59 (`library-states.md`): the researcher's target point (state `+0xA9`/`+0xAD`), the watched creature (`+0xA7`), and the Casino's wait position renamed to the shared `WaitPos`. ADR-0397.

**196** — `0xC44B4149D9B1725D`. The Casino's states 145–153, the jackpot, its party and its winner's 152 and 155–157 (`casino.md`): creature `+0x2FF`, state `+0xAC`/`+0xB4`/`+0xB8`, the drunk timer `+0x4CE`, the winner's `+0x3F2`/`+0x3F4`, room `+0x41`/`+0x45`/`+0x3D` on the Casino's lowest tile and player `+0xF4A`. ADR-0396.

**195** — `0x95D304A3DA5B37B4`. A trainee at the Training Room's level cap goes from state 108 through 110 to idle (`0x00485310`, `0x00485580`); no new field. ADR-0393.

**194** — `0x17E9192AEC50EC93`. An imp's gold fetch walks to the first neutral pile's point, state 27 runs when that walk ends (the pile need not still be there) and takes only the piles strictly within `0x555` (`0x00466850`, `0x0046B6D0`). No new field. ADR-0389.

**193** — `0xC5E69C4191F2E3A6`. Only a keeper's own workers hold, crowd or lose its tasks; the fight approach walks to a target on its own tile (one-point route, `0x004CF490`); no new field. ADR-0388.

**192** — `0x40EF9AF6D79DA685`. Imps repair their keeper's damaged wall (kind 16, `0x00453E30`), and a work application truncates the tabled rate over four before flooring it times the work rate (`0x0051CBA0`); no new field. ADR-0387.

**191** — `0xC5322686209DAEB0`. An imp task walk runs the walk wrapper's ability scan and casts Imp Haste from level 4 (`0x004651A0`); work experience is paid once per work hit, not per tick of work. No new field. ADR-0385, ADR-0386.

**190** — `0xEE1C266107F94AAF`. The binary sweep's fourth fixes, the imp's: the claim on the way before the walk-over pick-up, from a worker job only, and the pick-up from the imp's own tile only; the ring scan's own tile unfiltered; a failed walk to a free reinforce side deletes the wall's task; a crate's blueprint before the shared latch; state 27's take stopping at the pile that fills the hands. No new field. ADR-0378 to ADR-0382.

**189** — `0xFE655F271EBBEB12`. Trigger action 18 makes or breaks an alliance (`0x00519376`), where it was left unperformed; no new field. ADR-0377.

**188** — `0xCAAC539B930BB381`. The binary sweep's third fixes: the Graveyard's level-scaled burial, capacity gate, zeroed tally, random Vampire tile and tally lost on a sale; Vampire reincarnation; trigger conditions 20 and 21; slaps counted for the slapped creature's owner; flag 128 as the level score. No new field. ADR-0370 to ADR-0375.

**187** — `0xDCB551C8DB3F790C`. The binary sweep's second fixes: non-ownable ground to the neutral player, trigger action 22's sale and room build, no victory at a collapse, the forced creature target's spell, combat experience, shots newest first, the victory pause by clip, the Flanker's heading and the turn to the body; three effect-generator fields DK2 never reads leave the hash. No new field. ADR-0362 to ADR-0369.

**186** — `0x540CF5C4A50095C3`. The binary sweep's first fixes: the carrier's store tile and walk point, a draw per new store pile, the sweep's destroy and Treasury gate, the claim search's fall-through, the mana clamp, the portal tally on death, carried bodies' clocks, the heart's start, and the trigger root order; new field `CreatureState.PortalEntranceTile`. ADR-0355 to ADR-0361.

**185** — `0xD840FA98E806236E`. No imp sets off for an enemy while every Prison is full; a body refused at the door is put down on the room point 0x004E8430 draws from the world stream; no new field. ADR-0354.

**184** — `0xB22ED6D42A6AD975`. Tile work posts 12 to the owner of a Solid, Ownable tile another player's work brought down and 34 to the owner of a room a Good creature's blow left standing; no new field. ADR-0353 stage 6.

**183** — `0x683FC33F7E712118`. The Training, Temple, Torture, Casino, Library and Workshop finders post 41, 43, 42 or the new 44 (its subtable) when they find no room; no new field. ADR-0353 stage 5.

**182** — `0xDC5ABB62C9123E6F`. The claim 0x004E5A70 posts 42 when no other room of the type has a place the creature can reach, and for a Prison past its limit; a 42 for a type off its subtable keeps its terminator record a sweep; no new field. ADR-0353 stage 5.

**181** — `0x402E9BC4C3C96C2A`. The room transfer and the defeat handover post 35 to the old owner and 32 or 33 to the new, with the 32 to 35 subtables; no new field. ADR-0353 stage 4.

**180** — `0x68CC6F319486D231`. `MintWageCommand`: the keeper's wage spent from the stores in shares and put in its hand as a pile; no new field. ADR-0353 stage 2.

**179** — `0x2D8E29670FC89364`. The anger wrapper posts 17 and 18 on an upward band change and the sweep frees one whose creature left the band; no new field. ADR-0353 stage 1.

**178** — `0x030F88C2BFE9ADEB`. A Sight of Evil cast (spell 1) is accepted at a place: permission, payment, one-shot mana and both clocks, with no shot made; no new field. The pinned world casts nothing, so the value moved on the version alone. ADR-0352.

**177** — `0x3C779015EE115C66`. The mixed-class tile chain link on creatures, objects, traps, doors and shots with its world counter, and action 15's point, pitch and range on the creature; the possessed action command (action 26) is built. Scenario event/world outputs are unchanged; see `keeper-spell-possession.md`, Possessed actions.

**176** — `0x207022305EFDD86A`. A creature's height and previous height, written by possessed movement (action 25); the move command is built. All 42 scenarios keep their event/world outputs and change hash composition only; see `keeper-spell-possession.md`. ADR-0350.

**175** — `0x6F9C6A3B4D22F803`. Creature possession/influence flags and influence deadline; entry/exit and stat modifiers are built. All42 scenarios change hash composition only; see `keeper-spell-possession.md`. ADR-0350.

**174** — `0x8213FCEC0D1B3EA0`. Player OneShotReaper-used flag survives removal and snapshots; variant creation and its countdown are built. Every scenario hash changes composition; see `keeper-spell-reaper.md`. ADR-0350.

**173** — `0x901D3950F7F8504D`. Restore constructor combat-bit defaults; the summon callback explicitly clears them. Authored Horny keeps fight acquisition. Every scenario's hashes change composition; see `keeper-spell-reaper.md`. ADR-0350.

**172** — `0x7C8753E946D1DCA4`. Reaper arrival, grace deadline, action, fade and combat-control state, plus the timer/variant words for the shared creation arm. Every scenario hash changes composition; see `keeper-spell-reaper.md` for stage-0 comparisons. ADR-0350.

**171** — `0xE109C983710B8617`. Creature Turncoat countdown and retained combat-side tag, both unsigned words; actual ownership stays unchanged. Every scenario hash changes composition; behavioral comparisons against stage 0 are in `keeper-spell-turncoat.md`. ADR-0350.

**170** — `0x08B3B3184DD609AE`. Eight world Tremor records and creature `Fleeing`, `ShouldDelayGetUp` and retained-route pause; the fixed records and false flags change hash composition even without a cast. See ADR-0350 and `keeper-spell-tremor.md`.

**169** — `0x1D8367ACE6D6D7CB`. Master's 168 merged into the room-health branch: both shapes, a room tile's `RoomHealthLost` and its `KeptManaGain` (ADR-0336, ADR-0334). **167** — `0x5127FACEAEC8EA6B`. A room's health, DK2's `room+0x35`, held as each room tile's `RoomHealthLost` (ADR-0336); no behaviour moves, as an untouched room's deficit is zero. **168** — `0x958573FFD0B603A6`. The m7/close merge (its 163–165 were taken first): an object's health and position and the Boulder's roll, speed, wear and signs (ADR-0333); a creature's heart player (`+0x2F5`) and death-shot flag (ADR-0337); the alarm's route word (ADR-0332) and area damage's 32-bit arithmetic change behaviour, not shape. **166** — `0x32B476C1ECE5CC30`. A hatchery's `ChickensGenerated`, `room+0x51`, on its lowest tile; a coop no longer stocks a hatchery, and the Hatchery finder posts 41–43 (ADR-0338; 163–165 were taken first). **165** — `0x3112DA3F61990DB7`. A tile's `KeptManaGain`, the floor's mana a room built over it keeps paying (ADR-0337); a Claimed Vault's `MaxManaGain` raises its owner's gain cap. **162** — `0x323B5E88EF7AFB82`. A keeper's trap no-mana clock, player `+0xAA` (ADR-0326; 159 before master took 160); trap upkeep, trap and door area damage and trap collision change behaviour, not shape. **161** — `0x1F7722DF305E58E1`. A Guard Post's room, trap `+0x113` (ADR-0330); it was 158 before master took 160. No scenario's event log or world changes (measured on 1aa98db). **160** — `0xA846A4DFE0257674`. A room tile's `RoomSizeChangedTick`, DK2's `room+0x9F` stamped at every rebuild; room messages 41–43 are retained (ADR-0327). **156** — `0xA8639EEF2B7A2350`. A trap's runtime words (health, heading, recharge and recovery deadlines, charge, Trigger count, hidden, threat and its clock, the Trigger's countdown and creature, the sight cache), a creature's tile-chain link and its counter (ADR-0321). **155** — `0x72C7236DC18B69AF`. A creature's Casino fearless countdown, flag and `FearlessUntil`, its `TortureVoluntary`, and a keeper's `NumberOfManufacturingCreatures` (ADR-0322). **153** — `0x1149D4B4FA9DC991`. A tile's `RoomRuined`, the ruin trigger action 32 writes on a Hero Gate (ADR-0320). **152** — `0xE459726930E9CA06`. A door's id, health and recovery deadline, a trap's id, both id counters, a creature's forced-target kind and `RebelActive`; blueprints name ids (ADR-0318). **149** — `0x62BD4E4816875CDD`. A keeper's `PortalsClosed` (DK2's `PortalsOpen`, player `+0x76` bit 10), written by action 37; a portal's clock restarts at every due roll (ADR-0314). **148** — `0xCF0197A5CA7ACEB4`. Training and Casino enter hooks refuse before their claims (ADR-0313); no new field: the countdown seed moves after their tests. No scenario's series and no rig log changes on efa3157. **147** — `0x75745303F30520A9`. A creature's held room place (ADR-0309). **146** — `0x31889C236E089472`. A tile's dig task that outlives its wall (ADR-0305). **145** — `0xC3DB6013EF7EF967`. The Angry flourish follows the art type (ADR-0301, Q188); no new field. **144** — `0xAFA4D2781BF3CBA8`. A creature's chicken timer (ADR-0302). **143** — `0x6A93081534CC88BD`. The Casino big-winner countdown the walk-over pick-up reads (ADR-0300). **142** — `0x5C3E476976F79986`. The payday clock armed by a keeper's first non-worker (ADR-0295). **141** — `0x1B3AF57D6F23BE5E`. A tile's mark order, and the gold an imp keeps in its hands (ADR-0288). **140** — `0x25381F6E02610FEC`. A shot's forced-collision word (ADR-0287). **151** — `0x2F51C47030E5B38B`. The Combat category's enter and leave hooks on every route in and out, and a fight's re-selection kept inside 199 (ADR-0319); no new field. **149** — `0x62BD4E4816875CDD`. A keeper's `PortalsClosed` (DK2's `PortalsOpen`, player `+0x76` bit 10), written by action 37; a portal's clock restarts at every due roll (ADR-0314). **148** — `0xCF0197A5CA7ACEB4`. Training and Casino enter hooks refuse before their claims (ADR-0313); no new field: the countdown seed moves after their tests. No scenario's series and no rig log changes on efa3157. **147** — `0x75745303F30520A9`. A creature's held room place (ADR-0309). **146** — `0x31889C236E089472`. A tile's dig task that outlives its wall (ADR-0305). **145** — `0xC3DB6013EF7EF967`. The Angry flourish follows the art type (ADR-0301, Q188); no new field. **144** — `0xAFA4D2781BF3CBA8`. A creature's chicken timer (ADR-0302). **143** — `0x6A93081534CC88BD`. The Casino big-winner countdown the walk-over pick-up reads (ADR-0300). **142** — `0x5C3E476976F79986`. The payday clock armed by a keeper's first non-worker (ADR-0295). **141** — `0x1B3AF57D6F23BE5E`. A tile's mark order, and the gold an imp keeps in its hands (ADR-0288). **140** — `0x25381F6E02610FEC`. A shot's forced-collision word (ADR-0287).
**139** — `0xF3B644CAA2F6FE61`. A worker's last task tile, and the job choice of ADR-0282.
**138** — `0xB858D2093C84A232`. The Call to Arms, its flag's process states and the heeding bit (ADR-0279).
**137** — `0x5113A130122CFD15`. The tick a creature was spawned or last put down (ADR-0278).
**136** — `0x4C67CCBC9B5CA5C4`. Mana-pressure clocks, removal stamp and corpse bit (ADR-0277).
**135** — `0x4289BE0A7A244163`. Stored worker idle flag (ADR-0274).
**134** — `0xBBEF0554E88AFB16`. Creature acquisition order, including transfers (ADR-0273).
**133** — `0x584EDA75014EEB18`. A keeper's portal entrance rows: boost and sack block (ADR-0272).
**132** — `0xFFF1AFE1DFB6A231`. A miner's standing slot and a floor pile's cell (ADR-0264).
**131** — `0xC0AF4F951938F85C`. Worker pickup flags on task exit and prison delivery (ADR-0263).

**130** — `0x1E3B2DF848B4A2F9`. Constructor worker flags and pickup gates (ADR-0262).

**129** — `0x1E64CAC720660CBC`. Per-player casualty-priority mask (ADR-0261).

**128** — `0xDBFD17A3B1351515`. Keeper-spell last-cast/last-checked clocks and
one-shot mana expenditure (ADR-0260). 127 was reserved for the doors/traps
branch, which changed no hashed state (ADR-0251), so it was never used.

**126** — `0x1C559815D4AB992B`, the research tree: a keeper's target, each availability row's `Upgraded`, a book's research kind and type (ADR-0247). **125** — `0x5D8E842FA7697068`. Persist the state-entry turn used by the
state-199 message prologue; ADR-0246.

**124** — `0x00C706A5C1D92221`, retained player messages and their next id (ADR-0245). **123** — `0x14316E84B1FDB0F7`, each room tile's place in its slab chain and the next one to hand out (ADR-0240). **122** — `0xED95A480A9DB92C5`, `LevelTimeLimit` and `SpecialInfoNextRoomTypeId` (ADR-0235). **121** — `0x954849F97CFC0296`, a door's orientation (ADR-0233). **120** — `0x88F728EFC351D95D`, what a keeper has learned from torture (ADR-0230). **119** — `0xE5005E1E7800B920`, a Prison's door (ADR-0229). **118** — `0x89637C687FBCC88D`, a spectator's remembered position and a Combat Pit's fight stamp (ADR-0227). **117** — `0xFA805D7B0DCA5668`, OldFightPos and OldFightTick (ADR-0225). **116** — `0xAACD3B15D9CFDCFF`, an idle worker's wander stamp and action timer (ADR-0224). **115** — `0x9D966A6EC7AC034A`, a tunnelling worker's plan (ADR-0222). **114** — `0x7AE4DE645C0A63A1`, a creature's origin tile, where a thief escapes to (ADR-0219). **113** — `0xFBE9C5B2CC6DE5EC`, `SkipNextPayment` joins `GetPaid` (ADR-0217). **112** — `0x2EF320B63DEDBBD1`, a hero's standing job and its party walk (ADR-0216). **111** — `0x92F1E00A712726B0`, the job a creature was dropped on (ADR-0215). **110** — `0x2B65E45AF14CB7A7`, a keeper's blueprint list (ADR-0214). **109** — `0x1754FA244591A3A6`, a Combat Pit fighter, its forced target and its state's turn (ADR-0213). **108** — `0xEE02B16BC2932E35`, a guard's facing, deadline and lost turn (ADR-0212). **107** — `0x01B0EBC839B5A5B4`, the activity is derived from the state and leaves the hash and the save (ADR-0211). **106** — `0x52719F6004D8BDBB`, every creature holds a state from its constructor on: 1 where it had held none, so the pinned world's two creatures moved it (ADR-0209). **105** — `0xDBC19E3981F90A36`, the Workshop's points, list and crates (ADR-0203). **104** — `0xFB68B3CB26783D05`, pay's cadence and the drop grace (ADR-0202). **103** — `0x1A5F99A4C9E39974`, freeing comrades (ADR-0201). **102** — `0x91DAFF09144DBA4F`, needs take a creature off its job (ADR-0198). **101** — `0x4543E72F3F20FE22`, a hero party raised by action 14 (ADR-0197); **100** — `0xFB318DBAE28A937F`, The Mistress's Torture job (ADR-0196).
**99** — `0xD079580BE0144072`, a prisoner walks each step in 116 (ADR-0194); **98** — `0xD686048703B6ED8D`, a captive on a torture device (ADR-0193); **97** — `0x99249045AA085298`, the countdown hooks (ADR-0192); **96** — `0xA2D20DF5116200CB`, a captive on DK2's state ids (ADR-0191).
**95** — `0xA22834D9084DFA9E`, the lever on a furniture slot (ADR-0190); **94** — `0x71F6772A33262059`, paid then to the Casino (ADR-0189).
**93** — `0xD783D2057BC97FE4`, the Casino's lever (ADR-0188); **92** — `0x669BBBA8B0751A77`, a creature's purse and the Casino (ADR-0187); **91** — `0xA5F9A90F3ED7133A`, work effects by category (ADR-0186); **90** — `0xFD1E03A96BC65015`, the dispatcher's category rule (ADR-0185); **89** — `0x584E9B5AC2195900`, an imp's bodies (ADR-0184).
**88** — `0x89B5AB8D13B66793`, objects by their flags (ADR-0183); **87** — `0x1B90606BEF13CDC6`, an imp's gold (ADR-0182); **86** — `0xDEB6A8B84200ED21`, its tile work (ADR-0181).
**85** — `0xE0EDFEC6BD215E6C`, collecting wages (ADR-0180); **84** — `0xDB5AAE21ABF713BF`, the anger wipes (ADR-0179); **83** — `0xDFC977B66E524362`, the lair and sleep (ADR-0178); **82** — `0xF1F260238F3D713D`, a job's walk and work (ADR-0177).
**81** — `0xE54D6E7BACCB5868`. Eating on DK2's state ids (ADR-0176). From 81 on, traces pin the chains.

**80** — `0xF53826B464578C5B`. The resume pair stops carrying a sentinel
(ADR-0175). `StateBeforeUtilityState` and `UtilityNewState` default to **0**, as
DK2's state-object constructor writes, not to -1; the resume slot is never unset
while a creature travels, so the transition rule may read it unconditionally.
A composition bump: both fields hold their default for the whole pinned run.

**79** — `0x6ACE9686C2A3E64E`. A bed is a hospital (ADR-0173). Waking needs
both an elapsed `TimeSleep` and full level-scaled health, as DK2's state 83
does. **The pinned world exercises none of it** — nothing in it sleeps
wounded — so the literal moved on the version alone. The behaviour is pinned
by two tests instead, both verified to fail with the predicate flipped off.

**78** — `0x00C68551383ADF89`. Rebellion (ADR-0172). Angry job 16 walks out
through its own keeper's nearest Portal, and with none reachable waits
`RebelCountdownTicks` and changes owner to the hero side, carrying
`JobType.KillPlayer` and `RebelAgainstPlayerId`. Two new hashed fields.

**77** — `0x2EC7D308858908C2`. The two Q402 divergences the hook read found (ADR-0171):
a cell bites `4 × PrisonModifyCreatureHealthPerSecond` every four game seconds instead of
one second's worth each second, and adds `ModifyAngerInCompanyOfHatedCreaturesPerSecond`
with it; anger cools in all seven states of categories 6, 9 and 10 rather than only asleep
in one's own bed. `CreatureState.PrisonWitherTicks` carries the per-creature phase.

**76** — `0xFD44E939D714F3F1`. The utility-state pair beside it — DK2's own
`StateBeforeUtilityState` (`+0x40`) and `UtilityNewState` (`+0xDF`). The second is the
resume slot the transition rule reads in place of the current state while the category
is 2, and it is where the thirteen travelling `CreatureActivity` entries land when they
retire together. Nothing writes either; composition only.

[Versions 9–75](hash-versions-early.md) retain the earlier pins and their reasons.
