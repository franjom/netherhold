// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class TremorCreatureTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte CreatureType = Dk2TerrainFixture.WarlockCreatureId;
    private const int TravelState = 6;

    private static (GameDatabase Db, WorldState World, CreatureState Creature) Setup(CreatureFlags extraFlags = default)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var knockdown = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 26, FramesPerSecond = 30 };
        var getUp = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 51, FramesPerSecond = 30 };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == CreatureType
                ? c with { KnockdownAnimation = knockdown, GetUpAnimation = getUp, Flags = c.Flags | extraFlags } : c),
            basis.Variables, objects: basis.Objects.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        var at = new TilePos(10, 10);
        world.Tile(at).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(at).OwnerPlayerId = Keeper;
        CreatureState creature = world.SpawnCreature(db, CreatureType, Keeper, at);
        Dk2TerrainFixture.SetKnownIdentities(world);
        creature.Dk2StateId = CreatureStateMachine.Idle;
        return (db, world, creature);
    }

    [Fact]
    public void RecoveryUsesTheActionPollBoundariesOfBothCapturedClips_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        creature.Dk2StateId = 210;
        creature.StunTicks = CombatRules.TicksOf(db.CreatureById(CreatureType).StunDuration);
        int stun = creature.StunTicks;
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem(), new TremorCreatureSystem()]);
        sim.Tick();
        Assert.Equal(263, creature.Dk2StateId);
        Assert.Equal(5, creature.ActionUntilTick);
        Assert.False(StatusEffectRules.IsStunned(creature));
        sim.Run(4);
        Assert.Equal(263, creature.Dk2StateId);
        sim.Tick();
        Assert.Equal(264, creature.Dk2StateId);
        Assert.Equal(13, creature.ActionUntilTick);
        sim.Run(7);
        Assert.Equal(264, creature.Dk2StateId);
        sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
        Assert.Equal(stun, creature.StunTicks);
    }

    [Fact]
    public void DelayIsConsumedOnceAfterKnockdownCompletes_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        creature.Dk2StateId = 210;
        creature.ShouldDelayGetUp = true;
        var sim = new GameSimulation(db, world, 1, [new TremorCreatureSystem()]);
        sim.Run(5);
        Assert.True(creature.ShouldDelayGetUp);
        sim.Tick();
        Assert.Equal(263, creature.Dk2StateId);
        Assert.False(creature.ShouldDelayGetUp);
        sim.Tick();
        Assert.Equal(264, creature.Dk2StateId);
        Assert.Equal(14, creature.ActionUntilTick);
    }

    [Theory]
    [InlineData(210)]
    [InlineData(263)]
    [InlineData(264)]
    public void ARecoveryStateWithoutFleeingConsumesNoReactionDraw_StronglyInferredFromBinary(int state)
    {
        var (db, world, creature) = Setup();
        creature.Dk2StateId = state;
        new GameSimulation(db, world, new SimRandom(new NoDraws()), [new Probe(context =>
            Assert.True(TremorRules.Respond(context, creature, creature.Tile, new Fp32(3 * Fp32.One))))]).Tick();
        Assert.Equal(state, creature.Dk2StateId);
    }

    [Fact]
    public void TravelIsRejectedBeforeTheFleeingKnockdownDraw_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        creature.Dk2StateId = 6;
        creature.UtilityNewState = CreatureStateMachine.Idle;
        creature.Fleeing = true;
        new GameSimulation(db, world, new SimRandom(new NoDraws()), [new Probe(context =>
            Assert.False(TremorRules.Respond(context, creature, creature.Tile, new Fp32(3 * Fp32.One))))]).Tick();
        Assert.True(creature.Fleeing);
    }

    [Fact]
    public void UnsignedSquaredDistanceRetainsItsHighBit_StronglyInferredFromBinary()
    {
        Assert.Equal(46340, IntegerMath.SquareRoot(0x80000000u));
        Assert.Equal(65535, IntegerMath.SquareRoot(uint.MaxValue));
        Assert.Equal(0, IntegerMath.SquareRoot(0u));
    }

    [Fact]
    public void FleeingAndTheRecoveryDelaySurviveASnapshotAndAffectItsHash_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        creature.Dk2StateId = 263;
        creature.Fleeing = true;
        creature.ShouldDelayGetUp = true;
        creature.ActionUntilTick = 5;
        ISimulationSystem[] systems = [new TremorCreatureSystem()];
        var sim = new GameSimulation(db, world, 1, systems);
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, systems);
        CreatureState restored = Assert.Single(copy.World.Creatures);
        Assert.True(restored.Fleeing);
        Assert.True(restored.ShouldDelayGetUp);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        restored.Fleeing = false;
        Assert.NotEqual(sim.StateHash(), copy.StateHash());
        restored.Fleeing = true;
        restored.ShouldDelayGetUp = false;
        Assert.NotEqual(sim.StateHash(), copy.StateHash());
        restored.ShouldDelayGetUp = true;
        sim.Run(15);
        copy.Run(15);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    private sealed class NoDraws : IRandomAlgorithm
    {
        public int NextInt(int exclusiveMax) => throw new InvalidOperationException("Unexpected RNG draw.");
        public void HashInto(ref Hash64 hash) { }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    public void FleeingKnockdownClearsTheFlagBeforeEnteringRecovery_StronglyInferredFromBinary(int draw)
    {
        var (db, world, creature) = Setup();
        creature.Fleeing = true;
        var draws = new Draws((100, draw));
        new GameSimulation(db, world, new SimRandom(draws), [new Probe(context =>
            Assert.True(TremorRules.Respond(context, creature, creature.Tile, new Fp32(3 * Fp32.One))))]).Tick();
        Assert.Equal(TremorRules.KnockdownState, creature.Dk2StateId);
        Assert.False(creature.Fleeing);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void FiveIsOutsideTheKnockdownChanceAndUsesTheOwnedFloorDraws_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        creature.Fleeing = true;
        var draws = new Draws((100, 5), (5, 2), (5, 2));
        new GameSimulation(db, world, new SimRandom(draws), [new Probe(context =>
            Assert.True(TremorRules.Respond(context, creature, creature.Tile, new Fp32(3 * Fp32.One))))]).Tick();
        Assert.Equal(TravelState, creature.Dk2StateId);
        Assert.True(creature.Fleeing);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void RecoveryPreservesTheRouteAndHoldsItUntilAWalkingState_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        var destination = new TilePos(12, 10);
        for (int x = 10; x <= 12; x++)
        {
            world.Tile(new TilePos(x, 10)).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(new TilePos(x, 10)).OwnerPlayerId = Keeper;
        }
        var sim = new GameSimulation(db, world, 1,
            [new Probe(context =>
            {
                if (context.Tick.Value != 0) return;
                context.Navigation.Refresh();
                Assert.True(context.Navigation.TryRoute(creature, destination, DK2.Simulation.Navigation.MoveOptions.Loose));
                creature.Dk2StateId = TremorRules.KnockdownState;
            }), new TremorCreatureSystem(), new CreatureMovementSystem()]);
        int xBefore = creature.PosX, yBefore = creature.PosY;
        sim.Run(14);
        Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
        Assert.True(creature.TremorRoutePaused);
        Assert.True(creature.Body.PathTotalCount > 0);
        Assert.Equal((xBefore, yBefore), (creature.PosX, creature.PosY));
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new TremorCreatureSystem(), new CreatureMovementSystem()]);
        Assert.True(Assert.Single(restored.World.Creatures).TremorRoutePaused);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        sim.Tick();
        Assert.Equal((xBefore, yBefore), (creature.PosX, creature.PosY));
        creature.Dk2StateId = TravelState;
        creature.UtilityNewState = CreatureStateMachine.Idle;
        sim.Tick();
        Assert.False(creature.TremorRoutePaused);
        Assert.NotEqual((xBefore, yBefore), (creature.PosX, creature.PosY));
    }

    private sealed class Draws(params (int Bound, int Value)[] draws) : IRandomAlgorithm
    {
        private int _next;
        public int Remaining => draws.Length - _next;
        public int NextInt(int exclusiveMax)
        {
            Assert.True(_next < draws.Length, "Unexpected RNG draw.");
            var draw = draws[_next++];
            Assert.Equal(draw.Bound, exclusiveMax);
            return draw.Value;
        }
        public void HashInto(ref Hash64 hash) => hash.Add(_next);
    }

    [Fact]
    public void OwnedSearchScansItsExtraRowButRetriesOnlyTheFiveByFiveSquare_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        world.Tile(creature.Tile).OwnerPlayerId = PlayerIds.Neutral;
        var sixthRow = new TilePos(8, 13);
        world.Tile(sixthRow).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(sixthRow).OwnerPlayerId = Keeper;
        var draws = new Draws(Enumerable.Repeat((5, 0), 50).ToArray());
        new GameSimulation(db, world, new SimRandom(draws), [new Probe(context =>
            Assert.False(TremorRules.Respond(context, creature, creature.Tile, new Fp32(3 * Fp32.One))))]).Tick();
        Assert.Equal(0, draws.Remaining);
        Assert.False(creature.Fleeing);
        Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
    }

    [Fact]
    public void AwayRetriesDrawXThenYAndRetainTheOriginalFractions_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        for (int y = 8; y <= 12; y++)
            for (int x = 8; x <= 12; x++)
            {
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
                world.Tile(new TilePos(x, y)).OwnerPlayerId = PlayerIds.Neutral;
            }
        creature.PosX += 123;
        creature.PosY += 321;
        int targetX = creature.PosX + Fp32.One, targetY = creature.PosY - Fp32.One;
        var draws = new Draws((2, 0), (2, 1), (4, 1), (4, 1));
        new GameSimulation(db, world, new SimRandom(draws), [new Probe(context =>
            Assert.True(TremorRules.Respond(context, creature, creature.Tile, new Fp32(3 * Fp32.One))))]).Tick();
        Assert.Equal((targetX << 4, targetY << 4), (creature.Body.PathTargetPos.X, creature.Body.PathTargetPos.Y));
        Assert.Equal(TravelState, creature.Dk2StateId);
        Assert.True(creature.Fleeing);
        Assert.Equal(0, draws.Remaining);
    }

    private sealed class Probe(Action<SimulationContext> action) : ISimulationSystem
    {
        public string Name => "tremor-test-probe";
        public void Execute(SimulationContext context) => action(context);
    }

    [Fact]
    public void AChosenHungerNeedBypassesTheIdleFleeingClear_StronglyInferredFromBinary()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 10; y++)
            for (int x = 1; x <= 10; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        for (int y = 6; y < 9; y++)
            for (int x = 6; x < 9; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Hatchery), Keeper);
        var food = new TilePos(7, 7);
        world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, world.TileIndex(food));
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(2, 2));
        creature.Dk2StateId = CreatureStateMachine.Idle;
        creature.HungerNeed = 1;
        creature.Fleeing = true;
        new GameSimulation(db, world, 42,
            [new TremorCreatureSystem(), new CreatureEatSystem(), new CreatureJobSystem()]).Tick();
        Assert.Equal(7, creature.Dk2StateId);
        Assert.Equal(92, creature.UtilityNewState);
        Assert.True(creature.Fleeing);
    }

    [Fact]
    public void OrdinaryIdleSelectionClearsFleeing_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        creature.Fleeing = true;
        var sim = new GameSimulation(db, world, 42, [new CreatureJobSystem()]);
        sim.Tick();
        Assert.False(creature.Fleeing);
        Assert.Equal(CreatureJobSystem.IdleChooserState, creature.Dk2StateId);
        sim.Tick();
        Assert.Equal(CreatureJobSystem.DoingNothingState, creature.Dk2StateId);
    }

    [Theory]
    [InlineData(JobType.Leave)]
    [InlineData(JobType.Rebel)]
    public void AcceptedAngryJobBypassesIdleFleeingClear_StronglyInferredFromBinary(JobType job)
    {
        var (db, world, creature) = MoodSetup(job, JobType.None);
        var sim = new GameSimulation(db, world, 42, [new CreatureJobSystem(), new CreatureAngerSystem()]);
        sim.Tick();
        Assert.True(creature.Fleeing);
        if (job == JobType.Rebel)
        {
            Assert.Equal(CreatureStateMachine.Idle, creature.Dk2StateId);
            Assert.Equal(RebellionRules.DefectDelayTicks, creature.RebelCountdownTicks);
            sim.Tick();
            Assert.Equal(RebellionRules.DefectDelayTicks - 1, creature.RebelCountdownTicks);
        }
    }

    [Fact]
    public void FailedAngryThenAcceptedUnhappyJobBypassesIdleFleeingClear_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.None, JobType.Leave);
        new GameSimulation(db, world, 42, [new CreatureJobSystem(), new CreatureAngerSystem()]).Tick();
        Assert.True(creature.Fleeing);
        Assert.Equal(194, creature.Dk2StateId);
    }

    /// <summary>An unhappy creature whose table is Sulk, with a bed of its own in a Lair, takes the
    /// job and is put in its state, 0x55 (<see cref="JobRules.StateFor"/>), here on the bed tile.</summary>
    [Fact]
    public void AnUnhappyCreatureWithABedTakesSulk_AndEntersItsState()
    {
        var (db, world, creature) = MoodSetup(JobType.None, JobType.Sulk);
        foreach (TilePos at in new[] { new TilePos(2, 2), new TilePos(3, 2), new TilePos(2, 3), new TilePos(3, 3) })
            RoomRules.Place(db, ref world.Tile(at), db.Room(RoomTypeId.Lair), Keeper);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, creature));

        var sim = new GameSimulation(db, world, 42, [new CreatureJobSystem(), new CreatureAngerSystem()]);
        for (int i = 0; i < 40 && creature.Dk2StateId != JobRules.StateFor(JobType.Sulk); i++) sim.Tick();

        Assert.Equal(JobRules.StateFor(JobType.Sulk), creature.Dk2StateId);
    }

    [Fact]
    public void BothMoodSelectorsRefusingRunOnlyIdleHandler_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.None, JobType.None);
        new GameSimulation(db, world, 42, [new CreatureJobSystem(), new CreatureAngerSystem()]).Tick();
        Assert.False(creature.Fleeing);
        Assert.Equal(89, creature.Dk2StateId);
    }

    [Fact]
    public void AcceptedLeaveDoesNotRunItsNewHandlerInTheSelectionTurn_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.Leave, JobType.None);
        RoomRules.Place(db, ref world.Tile(creature.Tile), db.Room(RoomTypeId.Portal), Keeper);
        new GameSimulation(db, world, 42, [new CreatureJobSystem(), new CreatureAngerSystem()]).Tick();
        Assert.Same(creature, world.CreatureById(creature.Id));
        Assert.Equal(LeavingRules.ChoosingState, creature.Dk2StateId);
        Assert.True(creature.Fleeing);
    }

    private static (GameDatabase Db, WorldState World, CreatureState Creature) MoodSetup(JobType angry, JobType unhappy)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        byte type = Dk2TerrainFixture.GoblinCreatureId;
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == type
                ? c with
                {
                    AngryJobs = angry == JobType.None ? [] : [new(angry, 0, 0, 100)],
                    UnhappyJobs = unhappy == JobType.None ? [] : [new(unhappy, 0, 0, 100)]
                } : c), basis.Variables, objects: basis.Objects.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < 15; y++)
            for (int x = 1; x < 15; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        CreatureState creature = world.SpawnCreature(db, type, Keeper, new TilePos(2, 2));
        creature.Dk2StateId = CreatureStateMachine.Idle;
        creature.Fleeing = true;
        creature.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        new GameSimulation(db, world, 42, []).Run(3);
        return (db, world, creature);
    }

    [Fact]
    public void UnhappyAcceptanceBypassesIdleWithoutAnAngryRoll_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.Rebel, JobType.Leave);
        creature.Anger.Lair = CreatureAngerRules.UnhappyAt(db.CreatureById(creature.CreatureTypeId));
        var draws = new Draws((100, 0));
        new GameSimulation(db, world, new SimRandom(draws), [new CreatureJobSystem(), new CreatureAngerSystem()]).Tick();
        Assert.True(creature.Fleeing);
        Assert.Equal(194, creature.Dk2StateId);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void RefusedAngryJobFallsThroughToUnhappy_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.Sulk, JobType.Leave);
        var draws = new Draws((100, 0), (100, 0));
        new GameSimulation(db, world, new SimRandom(draws), [new CreatureJobSystem(), new CreatureAngerSystem()]).Tick();
        Assert.True(creature.Fleeing);
        Assert.Equal(194, creature.Dk2StateId);
        Assert.Equal(0, creature.LastAngryChoiceTick);
        Assert.Equal(3, creature.LastUnhappyChoiceTick);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void ClosedAngryGateStillTriesUnhappy_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.Rebel, JobType.Leave);
        creature.LastAngryChoiceTick = 1;
        var draws = new Draws((100, 0));
        new GameSimulation(db, world, new SimRandom(draws), [new CreatureJobSystem(), new CreatureAngerSystem()]).Tick();
        Assert.True(creature.Fleeing);
        Assert.Equal(194, creature.Dk2StateId);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void CallToArmsSkipsMoodSelectorsBeforeIdle_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.Rebel, JobType.Leave);
        creature.HeedingCallToArms = true;
        var draws = new Draws();
        new GameSimulation(db, world, new SimRandom(draws), [new CreatureJobSystem(), new CreatureAngerSystem()]).Tick();
        Assert.False(creature.Fleeing);
        Assert.Equal(CreatureJobSystem.IdleChooserState, creature.Dk2StateId);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void MoodPreludeDoesNotRepeatAndItsMarkerResetsNextTurn_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.None, JobType.None);
        var draws = new Draws((100, 0), (100, 0), (100, 0), (100, 0));
        var sim = new GameSimulation(db, world, new SimRandom(draws), [new CreatureJobSystem(), new CreatureAngerSystem()]);
        sim.Tick();
        Assert.Equal(2, draws.Remaining);
        creature.Dk2StateId = CreatureStateMachine.Idle;
        creature.Fleeing = true;
        sim.Tick();
        Assert.Equal(0, draws.Remaining);
        Assert.False(creature.Fleeing);
    }

    [Theory]
    [InlineData(CreatureMood.Angry, 5)]
    [InlineData(CreatureMood.Unhappy, 3)]
    public void State89RunsHappyAfterItsMoodPreludeRefuses_StronglyInferredFromBinary(CreatureMood mood, int rolls)
    {
        var (db, world, creature) = MoodSetup(JobType.None, JobType.None);
        CreatureDef definition = db.CreatureById(creature.CreatureTypeId);
        if (mood == CreatureMood.Unhappy) creature.Anger.Lair = CreatureAngerRules.UnhappyAt(definition);
        var draws = new Draws(Enumerable.Repeat((100, 0), rolls).ToArray());
        var sim = new GameSimulation(db, world, new SimRandom(draws), [new CreatureJobSystem(), new CreatureAngerSystem()]);
        sim.Tick();
        Assert.Equal(CreatureJobSystem.IdleChooserState, creature.Dk2StateId);
        int before = creature.Anger.Work;
        sim.Tick();
        Assert.Equal(CreatureJobSystem.DoingNothingState, creature.Dk2StateId);
        Assert.Equal(before + SimulationTiming.LoaderRatePerTick(definition.AngerNoWork), creature.Anger.Work);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void AcceptedMoodPreludeBypassesState89Handler_StronglyInferredFromBinary()
    {
        var (db, world, creature) = MoodSetup(JobType.Rebel, JobType.None);
        creature.Dk2StateId = CreatureJobSystem.IdleChooserState;
        creature.Fleeing = false;
        var draws = new Draws((100, 0));
        new GameSimulation(db, world, new SimRandom(draws), [new CreatureJobSystem(), new CreatureAngerSystem()]).Tick();
        Assert.True(creature.RebelActive);
        Assert.Equal(RebellionRules.DefectDelayTicks, creature.RebelCountdownTicks);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void HeroIdleSelectionClearsFleeingBeforeTheStandingJob_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        creature.OwnerPlayerId = PlayerIds.Good;
        creature.Fleeing = true;
        new GameSimulation(db, world, 42, [new HeroSystem()]).Tick();
        Assert.Equal(HeroJobRules.StandingJobState, creature.Dk2StateId);
        Assert.False(creature.Fleeing);
    }

    [Fact]
    public void WorkerIdleSelectionClearsFleeingBeforeTaskSelection_StronglyInferredFromBinary()
    {
        var (db, world, creature) = Setup();
        CreatureState worker = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, creature.Tile);
        worker.Dk2StateId = CreatureStateMachine.Idle;
        worker.Fleeing = true;
        new GameSimulation(db, world, 42, [new ImpTaskSystem()]).Tick();
        Assert.Equal(10, worker.Dk2StateId);
        Assert.False(worker.Fleeing);
    }

    [Theory]
    [InlineData(true, 238)]
    public void SpecialRoleIdleSelectsItsOwnUnbuiltHandlerWithoutKeeperJobs_StronglyInferredFromBinary(bool horny, int destination)
    {
        var (db, world, creature) = Setup(horny ? CreatureFlags.IsHorny : default);
        if (!horny) creature.OwnerPlayerId = PlayerIds.Neutral;
        creature.Fleeing = true;
        var random = new SimRandom(42);
        var initialRandomState = random.SaveState();
        ISimulationSystem[] systems = [new CreatureJobSystem(), new CreatureAngerSystem()];
        var sim = new GameSimulation(db, world, random, systems);
        sim.Tick();
        Assert.False(creature.Fleeing);
        Assert.Equal(destination, creature.Dk2StateId);
        Assert.Equal(CreatureActivity.UnbuiltHandler, creature.Activity);
        if (!horny) Assert.Equal(CreatureJobSystem.NothingToDoTicks, creature.StateDeadlineTick);
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, systems);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        sim.Run(8);
        copy.Run(8);
        Assert.Equal(destination, creature.Dk2StateId);
        Assert.Equal(JobType.None, creature.CurrentJob);
        Assert.Equal(0, creature.LastHappyChoiceTick);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal(initialRandomState, random.SaveState());
    }
}
