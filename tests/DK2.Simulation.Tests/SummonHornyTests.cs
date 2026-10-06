// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public sealed class SummonHornyTests
{
    private const byte Keeper = 3, HornyType = 201, SpellId = 5;

    internal static (GameDatabase Db, WorldState World, TilePos At) Setup()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        CreatureDef horny = basis.CreatureById(Dk2TerrainFixture.GoblinCreatureId) with
        {
            Name = "Horned Reaper", CreatureId = HornyType,
            Flags = CreatureFlags.IsHorny, Hp = 500,
            ReaperSummonAnimation = new CreatureAnimationTiming
            {
                ResourceType = 6, Flags = (CreatureAnimationFlags)513,
                FrameCount = 121, FramesPerSecond = 30,
            },
        };
        var spell = new KeeperSpellDef
        {
            Name = "Summon Horny", KeeperSpellId = SpellId, ManaCost = 100000,
            ManaUpkeepPerSecond = 2000, ShotTypeId = 46, ShotData1 = 0,
            LocationSelector = 1, TargetSelector = 4,
        };
        ShotDef shot = new(new byte[239])
        {
            Name = "Create Reaper Shot", ShotId = 46,
            MeshResource = basis.Shots[4].MeshResource, Light = basis.Shots[4].Light,
            SoundCategory = "", Speed = 2048, Radius = 819, Health = 1,
            Flags = 4, ShotProcessFlags = 128, ProcessType = 33,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Append(horny), basis.Variables,
            basis.KeeperSpells.Values.Where(s => s.KeeperSpellId != SpellId).Append(spell),
            objects: basis.Objects.Values,
            shots: basis.Shots.Values.Where(s => s.ShotId != 46).Append(shot));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        var at = new TilePos(10, 10);
        world.Tile(at).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(at).OwnerPlayerId = Keeper;
        world.Player(Keeper).Mana = 200000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, SpellId, 3));
        return (db, world, at);
    }

    [Fact]
    public void CastCreatesTheDataSelectedLevelTenReaperForTheCaster_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, SpellId,
            CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y))));
        CreatureState reaper = Assert.Single(world.Creatures);
        Assert.Equal(HornyType, reaper.CreatureTypeId);
        Assert.Equal(Keeper, reaper.OwnerPlayerId);
        Assert.Equal(10, reaper.Level);
        Assert.Equal(235, reaper.Dk2StateId);
        Assert.Equal(100000, world.Player(Keeper).Mana);
        Assert.Equal(100000, world.Player(Keeper).OneShotManaSpent);
        Assert.Equal(201u, reaper.HornyTick);
        Assert.Equal(0, reaper.ReaperTicks);
        Assert.False(reaper.IsOneShotReaper);
        Assert.False(reaper.ReaperCanAcquireFight);
        Assert.False(reaper.ReaperIsAttackable);
    }

    [Fact]
    public void AuthoredReaperCanAcquireFightBeforeAnySummonGreeting_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        Assert.True(CombatRules.MayLookForAFight(world, db, reaper, force: false));
        Assert.True(reaper.ReaperCanAcquireFight);
        Assert.True(reaper.ReaperIsAttackable);
        reaper.ReaperCanAcquireFight = false;
        Assert.False(CombatRules.MayLookForAFight(world, db, reaper, force: false));
        Assert.True(CombatRules.MayLookForAFight(world, db, reaper, force: true));
    }

    [Fact]
    public void UpkeepStartsStrictlyAfterTheFiftySecondGrace_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = 238;
        reaper.HornyTick = 201;
        var sim = new GameSimulation(db, world, 1);
        sim.Run(200);
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db).Sum());
        sim.Tick();
        Assert.Equal(2000, ManaRules.UpkeepPerSecondByPlayerIndex(world, db).Sum());
    }

    [Theory]
    [InlineData(121, 30, 0, 0, 17)]
    [InlineData(8, 4, 0, 0, 8)]
    [InlineData(8, 4, 2, 0, 10)]
    [InlineData(8, 4, 0, 3, 8)]
    [InlineData(8, 4, 2, 3, 10)]
    [InlineData(1, 30, 2, 0, 3)]
    [InlineData(1, 30, 2, 100, 16)]
    [InlineData(65535, 1, 0, 0, 1)]
    public void PlainActionEndsAtTheFirstObservedCycle_ConfirmedFromBinary(
        ushort frames, ushort fps, byte startFrames, byte endFrames, int expectedTicks)
    {
        var timing = new CreatureAnimationTiming
        {
            ResourceType = 6, FrameCount = frames, FramesPerSecond = fps,
            Flags = CreatureAnimationFlags.Present
                | (startFrames == 0 ? 0 : CreatureAnimationFlags.StartSegment)
                | (endFrames == 0 ? 0 : CreatureAnimationFlags.EndSegment),
            StartSegmentFrames = startFrames, EndSegmentFrames = endFrames,
        };
        Assert.Equal(expectedTicks, AnimationTimingRules.FirstCycleHoldTicks(timing));
    }

    [Fact]
    public void ArrivalWaitsForTheShippedFirstCycleBeforeEnablingCombat_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        var cast = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        cast.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, SpellId,
            CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y))));
        CreatureState reaper = Assert.Single(world.Creatures);
        var sim = new GameSimulation(db, world, 1, [new ReaperSystem()]);
        sim.Tick();
        Assert.Equal(236, reaper.Dk2StateId);
        Assert.False(reaper.ReaperCanAcquireFight);
        sim.Run(8);
        Assert.Equal((236, byte.MaxValue), (reaper.Dk2StateId, reaper.ReaperAlpha));
        sim.Tick();
        Assert.Equal(237, reaper.Dk2StateId);
        sim.Tick();
        Assert.Equal(46, reaper.ReaperActionId);
        sim.Run(16);
        Assert.Equal(237, reaper.Dk2StateId);
        sim.Tick();
        Assert.Equal(238, reaper.Dk2StateId);
        Assert.True(reaper.ReaperCanAcquireFight);
        Assert.True(reaper.ReaperIsAttackable);
        Assert.Equal(0, reaper.ReaperActionId);
    }

    [Theory]
    [InlineData(1999, true)]
    [InlineData(2000, false)]
    public void RemainingManaBelowOneMorePaymentTriggersDeparture_ConfirmedFromBinary(int mana, bool leaves)
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = 238;
        reaper.ReaperCanAcquireFight = reaper.ReaperIsAttackable = true;
        world.Player(Keeper).Mana = mana;
        var sim = new GameSimulation(db, world, 1, [new CallbackSystem(context =>
            Assert.Equal(leaves, ReaperRules.AfterPayment(context, world.Player(Keeper))))]);
        sim.Tick();
        Assert.Equal(leaves ? 242 : 238, reaper.Dk2StateId);
        Assert.Equal(!leaves, reaper.ReaperCanAcquireFight);
        Assert.Equal(!leaves, reaper.ReaperIsAttackable);
    }

    [Fact]
    public void DepartureFadesAtAgeTwentyFourAndRemovesAtThirtyOne_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = 238;
        var departure = new GameSimulation(db, world, 1, [new CallbackSystem(context => ReaperRules.Depart(context, reaper))]);
        departure.Tick();
        var sim = new GameSimulation(db, world, 1, [new ReaperSystem(), new ReaperRemovalSystem()]);
        sim.Run(23);
        Assert.Equal(byte.MaxValue, reaper.ReaperAlpha);
        Assert.Single(world.Creatures);
        sim.Tick();
        Assert.Equal(223, reaper.ReaperAlpha);
        sim.Run(7);
        Assert.Empty(world.Creatures);
        Assert.Equal(0, reaper.Health);
    }

    [Theory]
    [InlineData(235)]
    [InlineData(237)]
    [InlineData(242)]
    public void ArrivalAndDepartureStayExcludedFromUpkeep_ConfirmedFromBinary(int state)
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = state;
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db).Sum());
    }

    [Theory]
    [InlineData(235)]
    [InlineData(236)]
    [InlineData(237)]
    [InlineData(238)]
    [InlineData(242)]
    public void OwnedReaperRefusesAnotherCastEvenWhileArrivingOrLeaving_ConfirmedFromBinary(int state)
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = state;
        var sim = new GameSimulation(db, world, 1, [new CallbackSystem(context =>
            Assert.Equal(KeeperSpellRefusal.ExistingReaper, KeeperSpellRules.CanCastAtPlace(context,
                new CastSpellAtPlaceCommand(Keeper, SpellId, CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y)))))]);
        sim.Tick();
        Assert.Equal(200000, world.Player(Keeper).Mana);
    }

    [Fact]
    public void OneShotAndEndedPlayerAreExcludedFromNormalHornyUpkeep_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = 238;
        reaper.IsOneShotReaper = true;
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db).Sum());
        reaper.IsOneShotReaper = false;
        world.Player(Keeper).Status = PlayerStatus.Won;
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db).Sum());
    }

    [Fact]
    public void ArrivalRemainsUnattackableEvenWhenThePhysicalBitIsSet_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = 236;
        reaper.ReaperCanAcquireFight = reaper.ReaperIsAttackable = true;
        Assert.False(CombatRules.IsAttackable(reaper));
        Assert.False(CombatRules.MayLookForAFight(world, db, reaper, force: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(19)]
    public void SnapshotContinuesArrivalAndItsActionClock(int turns)
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = 235;
        reaper.HornyTick = 201;
        var sim = new GameSimulation(db, world, 1, [new ReaperSystem(), new ReaperRemovalSystem()]);
        sim.Run(turns);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new ReaperSystem(), new ReaperRemovalSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        sim.Run(30);
        restored.Run(30);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(238, Assert.Single(restored.World.Creatures).Dk2StateId);
    }

    [Fact]
    public void SnapshotContinuesDepartureWithoutRestartingTheFade()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = 242;
        var sim = new GameSimulation(db, world, 1, [new ReaperSystem(), new ReaperRemovalSystem()]);
        sim.Run(25);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new ReaperSystem(), new ReaperRemovalSystem()]);
        sim.Run(6);
        restored.Run(6);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Empty(restored.World.Creatures);
    }

    [Theory]
    [InlineData(nameof(CreatureState.ReaperTicks))]
    [InlineData(nameof(CreatureState.HornyTick))]
    [InlineData(nameof(CreatureState.IsOneShotReaper))]
    [InlineData(nameof(CreatureState.ReaperComplete))]
    [InlineData(nameof(CreatureState.ReaperAlpha))]
    [InlineData(nameof(CreatureState.ReaperActionId))]
    [InlineData(nameof(CreatureState.ReaperStateAgeTicks))]
    [InlineData(nameof(CreatureState.ReaperCanAcquireFight))]
    [InlineData(nameof(CreatureState.ReaperIsAttackable))]
    public void EveryReaperLifecycleFieldAffectsTheStateHash(string fieldName)
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        var sim = new GameSimulation(db, world, 1);
        ulong before = sim.StateHash();
        switch (fieldName)
        {
            case nameof(CreatureState.ReaperTicks): reaper.ReaperTicks = 1; break;
            case nameof(CreatureState.HornyTick): reaper.HornyTick = 1; break;
            case nameof(CreatureState.IsOneShotReaper): reaper.IsOneShotReaper = true; break;
            case nameof(CreatureState.ReaperComplete): reaper.ReaperComplete = true; break;
            case nameof(CreatureState.ReaperAlpha): reaper.ReaperAlpha = 1; break;
            case nameof(CreatureState.ReaperActionId): reaper.ReaperActionId = 46; break;
            case nameof(CreatureState.ReaperStateAgeTicks): reaper.ReaperStateAgeTicks = 1; break;
            case nameof(CreatureState.ReaperCanAcquireFight): reaper.ReaperCanAcquireFight = false; break;
            case nameof(CreatureState.ReaperIsAttackable): reaper.ReaperIsAttackable = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(fieldName));
        }
        Assert.NotEqual(before, sim.StateHash());
        Assert.Equal(sim.StateHash(), WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db).StateHash());
    }

    [Theory]
    [InlineData(447, 448)]
    [InlineData(448, 448)]
    public void CreationRespectsTheCreaturePoolCapacityAfterChargingTheCast_ConfirmedFromBinary(int existing, int expected)
    {
        var (db, world, at) = Setup();
        for (int i = 0; i < existing; i++)
            world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, at);
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, SpellId,
            CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y))));
        Assert.Equal(expected, world.Creatures.Count);
        Assert.Equal(existing < 448 ? 1 : 0, world.Creatures.Count(c => c.CreatureTypeId == HornyType));
        Assert.Equal(100000, world.Player(Keeper).Mana);
    }

    [Fact]
    public void CategoryHealthRequestDoesNotDrainTheImmuneReaper_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, HornyType, Keeper, at);
        reaper.Dk2StateId = 238;
        var sim = new GameSimulation(db, world, 1, [new ReaperSystem()]);
        sim.Run(400);
        Assert.Equal(500, reaper.Health);
    }

    private sealed class CallbackSystem(Action<SimulationContext> execute) : ISimulationSystem
    {
        public string Name => "reaper-test";
        public void Execute(SimulationContext context) => execute(context);
    }
}
