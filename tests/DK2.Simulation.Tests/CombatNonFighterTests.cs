// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The NonFighter arm and the alarm — docs/research/combat-alarm.md, ADR-0166: a worker
/// fights anything within a tile of its heart, ignores enemies that are not heroes elsewhere,
/// withdraws from a hero away from its rooms, and near them counts heads and either keeps
/// scanning, raises the alarm at its nearest room, or withdraws. The room's creatures answer
/// the alarm until their Threat covers the deficit, then stand down after thirty ticks.</summary>
public class CombatNonFighterTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWorkingImpAndAnEngagedHeroReactInNewestFirstOrder(bool impSpawnedLast)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.LairTerrain, 13, 9);
        CreatureState first = Spawn(db, world,
            impSpawnedLast ? Dk2TerrainFixture.KnightCreatureId : Dk2TerrainFixture.ImpCreatureId,
            impSpawnedLast ? PlayerIds.Good : Keeper, 12, 12);
        CreatureState second = Spawn(db, world,
            impSpawnedLast ? Dk2TerrainFixture.ImpCreatureId : Dk2TerrainFixture.KnightCreatureId,
            impSpawnedLast ? Keeper : PlayerIds.Good, 12, 12);
        CreatureState imp = impSpawnedLast ? second : first;
        CreatureState hero = impSpawnedLast ? first : second;
        hero.PosX = imp.PosX + 1300;
        StatePlacement.Place(imp, 16);
        imp.FightProcessCounter = 3;
        imp.LastFightScanTileIndex = (imp.Tile.Y * world.Width) + imp.Tile.X;
        StatePlacement.Place(hero, 199);
        hero.FightTargetKind = FightTargetKind.Creature;
        hero.FightTargetId = imp.Id;
        hero.AttackType = CombatRules.AttackMode.MeleeAtCreature;
        hero.AttackRangeSquared = CombatRules.MeleeReachSquared(db.CreatureById(hero.CreatureTypeId), db.CreatureById(imp.CreatureTypeId));
        Assert.True(CombatRules.Register(imp, hero.Id, ranged: false));
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Tick();

        // The scan engages the hero and, in the same update, the dispatcher runs 199's handler, whose
        // NonFighter counter sends the Imp into flight (0x00476FC0 after 0x0045E9F0; ADR-0555): the
        // native capture shows an Imp going from a walk or work state straight to 203.
        Assert.Equal(CreatureActivity.Fleeing, imp.Activity);
        Assert.Equal(500, imp.Health);
        int engaged = sink.Events.ToList().FindIndex(e => e.Kind == "engaged" && e.Detail.Contains($"creature={imp.Id} ") && e.Detail.Contains($"target={hero.Id} "));
        int fled = sink.Events.ToList().FindIndex(e => e.Kind == "flees" && e.Detail.Contains($"creature={imp.Id} "));
        int swing = sink.Events.ToList().FindIndex(e => e.Kind == "swing" && e.Detail.Contains($"creature={hero.Id} "));
        Assert.True(swing >= 0);
        Assert.True(engaged >= 0 && fled > engaged);
        Assert.Equal(impSpawnedLast, fled < swing);
        Assert.True(hero.SwingInProgress);
        Assert.Equal(0, imp.BackOffTimer);
    }

    /// <summary>Near its heart the arm takes any hostile within a tile, so the Imp engages;
    /// once the Goblin engages, a non-Imp is among its attackers and the counter
    /// (<c>0x0045EC10</c>, ADR-0347) sends it into flight with no back-off. Before ADR-0347 this test
    /// asserted a lasting fight.</summary>
    [Fact]
    public void AnImpNearItsHeartTakesAnythingWithinATile_AndFleesFromANonImpBeforeItSwings()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.DungeonHeartTerrain, 12, 12);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 14, 12);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, imp);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        int engaged = sink.Events.ToList().FindIndex(e => e.Kind == "engaged" && e.Detail.Contains($"creature={imp.Id} ") && e.Detail.Contains($"target={goblin.Id} "));
        int struck = sink.Events.ToList().FindIndex(e => e.Kind == "swing" && e.Detail.Contains($"creature={goblin.Id} ") && e.Detail.Contains($"target={imp.Id} "));
        int fled = sink.Events.ToList().FindIndex(e => e.Kind == "flees" && e.Detail.Contains($"creature={imp.Id} "));
        Assert.True(engaged >= 0 && fled > engaged, $"engaged {engaged}, fled {fled}");
        Assert.True(struck < 0 || fled < struck, $"fled {fled}, attacker swing {struck}");
        Assert.EndsWith("backoff=0", sink.Events[fled].Detail);
    }

    [Theory]
    [InlineData(12, 12, 37, false, true)]
    [InlineData(12, 12, 120, false, false)]
    [InlineData(17, 18, 37, false, false)]
    [InlineData(18, 17, 37, false, true)]
    [InlineData(12, 12, 37, true, false)]
    public void AFleeingImpRaisesTheAlarmWhenOutmatched_UsingTheAttackersTileForTheRoomBoundary(
        int impX, int attackerX, int alliedThreat, bool callToArms, bool raisesAlarm)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.GuardRoomTerrain, 10, 10);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, impX, 12);
        CreatureState attacker = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, attackerX, 12);
        world.Player(Keeper).CallToArms = new(callToArms, 0, 0, 0, 0);
        if (alliedThreat == 120)
            Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, impX, 13);
        StatePlacement.Place(imp, 203);
        imp.FightTargetKind = FightTargetKind.Creature;
        imp.FightTargetId = attacker.Id;
        imp.BackOffTimer = 0;
        CombatRules.Register(attacker, imp.Id, ranged: false);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(1);

        Assert.Equal(raisesAlarm, sink.Events.Any(e => e.Kind == "raises_alarm"
            && e.Detail.Contains($"creature={imp.Id} ")));
        if (raisesAlarm)
        {
            Assert.Equal(CreatureActivity.RaisingAlarm, imp.Activity);
            Assert.Equal((6, 251), (imp.Dk2StateId, imp.UtilityNewState));
            Assert.True(imp.Running);
            Assert.True(imp.HasRoute);
            Assert.Equal((attacker.PosX, attacker.PosY, 54), (imp.AlarmX, imp.AlarmY, imp.AlarmDeficit));
            Assert.Equal(FightTargetKind.None, imp.FightTargetKind);
            Assert.DoesNotContain(imp.Id, attacker.EngagedMelee);
        }
        else if (attackerX == 18)
        {
            Assert.Equal(CreatureActivity.Withdrawing, imp.Activity);
        }
    }

    /// <summary><c>0x00460B50</c> walks the owner's rooms and skips any whose type byte <c>+0x18</c>
    /// is not 9: a Lair beside the fight is no Guard Room, so the outmatched Imp withdraws, as
    /// Level 3's Imps do in DK2's capture (72 of 76 flights end in state 202).</summary>
    [Fact]
    public void AnOutmatchedImpBesideARoomThatIsNotAGuardRoomWithdraws()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.LairTerrain, 10, 10);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState attacker = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        StatePlacement.Place(imp, 203);
        imp.FightTargetKind = FightTargetKind.Creature;
        imp.FightTargetId = attacker.Id;
        imp.BackOffTimer = 0;
        CombatRules.Register(attacker, imp.Id, ranged: false);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(1);

        Assert.DoesNotContain(sink.Events, e => e.Kind is "raises_alarm" or "disengaged" && e.Detail.Contains($"creature={imp.Id} "));
        Assert.Equal(CreatureActivity.Withdrawing, imp.Activity);
    }

    [Fact]
    public void AnImpAwayFromItsHeartIgnoresAnEnemyThatIsNotAHero()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, imp);
        DelayAcquisition(world, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(8);
        Assert.NotEqual(CreatureActivity.Fighting, imp.Activity);
        Assert.DoesNotContain(sink.Events, e => e.Detail.Contains("creature=" + imp.Id) && e.Kind is "engaged" or "withdraws" or "raises_alarm");
    }

    [Fact]
    public void AnImpAwayFromItsHeartAcquiresAnEnemyImp_AndAnImpAttackerDoesNotForceItsTargetToFlee()
    {
        // The escape byte (Q390): a candidate whose base creature type is the Imp's is judged by the
        // shared template wherever the worker stands — so enemy Imps engage each other anywhere. The
        // counter's NonFighter rule (0x0045EC10) forces flight only for an attacker that is NOT an
        // Imp, so two Imps fight; before ADR-0347 ours had the test the other way round.
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState rival = Touching(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, imp);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        Assert.Contains(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains("creature=" + imp.Id) && e.Detail.Contains("target=" + rival.Id));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "flees" && e.Detail.Contains("fear=0 "));
    }

    /// <summary><c>0x0045EC80</c> with <c>0x0045EC10</c>: a <c>NonFighter</c> whose attackers include
    /// a live creature that is not an Imp is over threat at once, with no fear at all; one attacked
    /// only by an Imp is not.</summary>
    [Theory]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId, true)]
    [InlineData(Dk2TerrainFixture.ImpCreatureId, false)]
    public void ANonFighterIsOverThreatAtOnceOnlyWhenAnAttackerIsNotAnImp(byte attackerType, bool overThreat)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState attacker = Spawn(db, world, attackerType, Rival, 13, 12);
        imp.EngagedMelee[2] = attacker.Id;
        imp.CurrentFear = 0;

        Assert.Equal(overThreat, FearRules.OverThreat(world, db, imp, db.CreatureById(imp.CreatureTypeId)));
        Assert.Equal(overThreat ? 3 : 0, imp.OverThreatCounter);
    }

    /// <summary>The approach state's flight (<c>0x00470E30</c>) zeroes the back-off only for an
    /// <c>AlwaysFlee</c> definition; the fight state's rule (no target, a NonFighter, a faster
    /// target) is its own. A Goblin, not AlwaysFlee, with no target and fear just over its
    /// threshold (q = 1) keeps 90 ticks from the approach, where the fight state zeroes it.</summary>
    [Fact]
    public void TheApproachStatesFlightZeroesTheBackOffOnlyForAnAlwaysFleeCreature()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 6, 6);
        CreatureDef goblinDef = db.CreatureById(goblin.CreatureTypeId);
        Assert.False(goblinDef.Flags.HasFlag(CreatureFlags.AlwaysFlee));
        Assert.True(db.CreatureById(imp.CreatureTypeId).Flags.HasFlag(CreatureFlags.AlwaysFlee));
        goblin.FightTargetKind = FightTargetKind.None;
        goblin.CurrentFear = (ushort)(FearRules.FearThreshold(db, goblinDef, goblin.Level) + 1);
        imp.FightTargetKind = FightTargetKind.None;
        imp.CurrentFear = (ushort)(FearRules.FearThreshold(db, db.CreatureById(imp.CreatureTypeId), imp.Level) + 1);
        int min = db.Variables[VariableTypeId.BackOffMinimumDurationSeconds];
        int max = db.Variables[VariableTypeId.BackOffMaximumDurationSeconds];
        int expected = min + ((max - min) * 3);
        Assert.NotEqual(0, expected);
        byte fromApproach = 255, fromFight = 255, impFromApproach = 255;
        new GameSimulation(db, world, 1, [new Probe(context =>
        {
            FearRules.EnterFlee(context, goblin, goblinDef, fromApproach: true);
            fromApproach = goblin.BackOffTimer;
            FearRules.EnterFlee(context, goblin, goblinDef);
            fromFight = goblin.BackOffTimer;
            FearRules.EnterFlee(context, imp, db.CreatureById(imp.CreatureTypeId), fromApproach: true);
            impFromApproach = imp.BackOffTimer;
        })]).Tick();

        Assert.Equal(expected, fromApproach);
        Assert.Equal(0, fromFight);
        Assert.Equal(0, impFromApproach);
    }

    private sealed class Probe(Action<SimulationContext> body) : ISimulationSystem
    {
        public string Name => "probe";
        public void Execute(SimulationContext context) => body(context);
    }

    [Fact]
    public void AnImpThatSeesAHeroAwayFromItsRoomsWithdraws()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // No Guard Room near the hero, but a room to withdraw to: with none at all the
        // withdrawal finds no destination (0x004611D0's failed room pick).
        PlaceRoom(world, Keeper, Dk2TerrainFixture.TreasuryTerrain, 15, 16);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.PosX = imp.PosX + 3700; // under a tile: inside an Imp's forced sight
        knight.PosY = imp.PosY;
        DelayAcquisition(world, knight);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        Assert.Contains(sink.Events, e => e.Kind == "withdraws" && e.Detail.Contains("creature=" + imp.Id));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains("creature=" + imp.Id));
    }

    [Fact]
    public void AnImpNearItsRoomsThatOutnumbersTheHeroKeepsScanning()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.GuardRoomTerrain, 13, 9);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.PosX = imp.PosX + 3700;
        knight.PosY = imp.PosY;
        Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 15, 12); // Threat 120 on our side
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        Assert.DoesNotContain(sink.Events, e => e.Detail.Contains("creature=" + imp.Id) && e.Kind is "engaged" or "withdraws" or "raises_alarm");
        Assert.Equal(CreatureActivity.Idle, imp.Activity);
    }

    [Fact]
    public void AnOutnumberedImpNearItsRoomsRaisesTheAlarm_AndTheRoomAnswersUntilTheDeficitIsCovered()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // The room six tiles from the hero: within the Imp's reporting distance, beyond the
        // Goblins' own sight and hearing, so none of them acquires the Knight before the alarm.
        PlaceRoom(world, Keeper, Dk2TerrainFixture.GuardRoomTerrain, 13, 5);
        int roomTile = (5 * world.Width) + 13;
        CreatureState first = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 5);
        CreatureState second = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 14, 5);
        CreatureState third = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 13, 4);
        foreach (CreatureState g in new[] { first, second, third })
        {
            g.CurrentJob = JobType.Sulk;
            StatePlacement.As(g, CreatureActivity.AtWork);
            g.JobTileIndex = roomTile;
        }
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.PosX = imp.PosX + 3700;
        knight.PosY = imp.PosY;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        // The Knight's 37 against the Imp's 2 at three quarters: a deficit of (37 − 1) × 3 / 2 = 54.
        sim.Run(5);
        var alarm = Assert.Single(sink.Events, e => e.Kind == "raises_alarm");
        Assert.Contains("deficit=54", alarm.Detail);
        Assert.Equal(CreatureActivity.RaisingAlarm, imp.Activity);
        Assert.True(imp.Running);

        // Two Goblins of 37 cover 54; the third stays at work.
        sim.Run(40);
        var raised = Assert.Single(sink.Events, e => e.Kind == "alarm_raised");
        Assert.Contains("sent=2", raised.Detail);
        int reporting = new[] { first, second, third }.Count(g => g.Activity is CreatureActivity.Reporting or CreatureActivity.Fighting);
        Assert.Equal(2, reporting);
        Assert.Equal(CreatureActivity.AtWork, third.Activity);
        Assert.NotEqual(CreatureActivity.RaisingAlarm, imp.Activity);
    }

    [Fact]
    public void TheAlarmWalksInSixTowardsTwoFiftyOne_AndTheRespondersTowardsTwoFiftyFour()
    {
        // ADR-0208: the Scout enters 199 and walks in 6 resuming 251, then stands down through
        // 1 and 252; a responder walks from where it was in 6 resuming 254.
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.GuardRoomTerrain, 13, 5);
        int roomTile = (5 * world.Width) + 13;
        CreatureState[] goblins =
        [
            Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 5),
            Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 14, 5),
            Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 13, 4),
        ];
        foreach (CreatureState g in goblins)
        {
            g.CurrentJob = JobType.Sulk;
            StatePlacement.As(g, CreatureActivity.AtWork);
            g.JobTileIndex = roomTile;
        }
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.PosX = imp.PosX + 3700;
        knight.PosY = imp.PosY;
        DelayAcquisition(world, knight);
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        var responders = new HashSet<int>();
        var seen = StateTrace.Of(sim, imp, 45, observeTick: () =>
        {
            foreach (CreatureState g in goblins)
                if (g.StateBeforeUtilityState == JobRules.StateFor(JobType.Sulk)
                    && (g.UtilityNewState == 254 || g.Dk2StateId is 254 or 199)) responders.Add(g.Id);
        });

        Assert.Equal([(6, 199, 251), (CreatureStateMachine.Idle, 199, 1)], seen);
        Assert.Equal(2, responders.Count);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void TheRaisingImpRoutesWithTheDoorBit_AndItsRespondersWithout()
    {
        // Q517: 0x0047542A and 0x0047583F push 0x80 for type id 1, the Imp, and 0 for anyone else.
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.GuardRoomTerrain, 13, 5);
        int roomTile = (5 * world.Width) + 13;
        CreatureState[] goblins =
        [
            Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 5),
            Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 14, 5),
        ];
        foreach (CreatureState g in goblins)
        {
            g.CurrentJob = JobType.Sulk;
            StatePlacement.As(g, CreatureActivity.AtWork);
            g.JobTileIndex = roomTile;
        }
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.PosX = imp.PosX + 3700;
        knight.PosY = imp.PosY;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        Assert.Equal(CreatureActivity.RaisingAlarm, imp.Activity);
        Assert.Equal(MoveOptions.Loose, imp.RouteOptions);

        for (int i = 0; i < 40 && !sink.Events.Any(e => e.Kind == "alarm_raised"); i++) sim.Run(1);
        Assert.Single(sink.Events, e => e.Kind == "alarm_raised");
        Assert.All(goblins, g => Assert.Equal(CreatureActivity.Reporting, g.Activity));
        Assert.All(goblins, g => Assert.Equal(MoveOptions.Plain, g.RouteOptions));
    }

    [Fact]
    public void AResponderStandsDownThirtyTicksAfterArriving()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        StatePlacement.As(goblin, CreatureActivity.Reporting);
        goblin.Running = true;
        GameSimulation sim = Combat(db, world);

        sim.Run(FearRules.ReportTicks);
        Assert.Equal(CreatureActivity.Reporting, goblin.Activity);
        sim.Run(1);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.False(goblin.Running);
    }

    [Fact]
    public void TheAlarmSurvivesASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        imp.AlarmX = 5000;
        imp.AlarmY = 6000;
        imp.AlarmDeficit = 54;
        imp.AlarmRoomTile = 7;
        imp.ReportTicks = 3;
        GameSimulation original = Combat(db, world);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new CombatSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        CreatureState copy = restored.World.CreatureById(imp.Id)!;
        Assert.Equal((5000, 6000, 54, 7, 3), (copy.AlarmX, copy.AlarmY, copy.AlarmDeficit, copy.AlarmRoomTile, copy.ReportTicks));
    }

    // ------------------------------------------------------------------ helpers

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId == Dk2TerrainFixture.Rock || tile.TerrainId == Dk2TerrainFixture.Gold
                    || tile.TerrainId == Dk2TerrainFixture.Gems)
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
            }
        }
        return (db, world);
    }

    /// <summary>A three-by-three room of the terrain centred on the tile, owned by the keeper.</summary>
    private static void PlaceRoom(WorldState world, byte owner, byte terrain, int cx, int cy)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                ref TileState tile = ref world.Tile(new TilePos(cx + dx, cy + dy));
                tile.TerrainId = terrain;
                tile.OwnerPlayerId = owner;
            }
        }
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    private static CreatureState Touching(GameDatabase db, WorldState world, byte type, byte owner, CreatureState other)
    {
        CreatureState c = world.SpawnCreature(db, type, owner, other.Tile);
        c.PosX = other.PosX + 1300;
        c.PosY = other.PosY;
        return c;
    }

    private static GameSimulation Combat(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CombatSystem()]);

    // Keep the candidate unregistered while testing the ordinary NonFighter arm.
    private static void DelayAcquisition(WorldState world, CreatureState candidate)
    {
        candidate.LastFightScanTileIndex = candidate.Tile.Y * world.Width + candidate.Tile.X;
        candidate.FightProcessCounter = 0;
    }
}
