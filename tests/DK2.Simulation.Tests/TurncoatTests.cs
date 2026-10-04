// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class TurncoatTests
{
    private const byte Keeper = 3, Rival = 4, SpellId = 7;
    private const int EngagingState = 199;

    private static (GameDatabase Db, WorldState World, CreatureState Target) Setup(CreatureFlags additionalFlags = 0)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var spell = new KeeperSpellDef
        {
            Name = "Turncoat", KeeperSpellId = SpellId, ManaCost = 20000, ManaUpkeepPerSecond = 0,
            ShotTypeId = 31, ShotData1 = 25, BonusShotTypeId = 31, BonusShotData1 = 40,
            CooldownRaw = 245760, LocationSelector = 1, TargetSelector = 2,
        };
        ShotDef shot = new(new byte[239])
        {
            Name = "Turncoat", MeshResource = basis.Shots[4].MeshResource,
            Light = basis.Shots[4].Light, SoundCategory = "", ShotId = 31,
            Speed = 2048, Radius = 819, Health = 1, Flags = 6,
            ShotProcessFlags = 4, CollideType = 2, ProcessType = 13, AttackCategory = 9,
        };
        var creatures = basis.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.GoblinCreatureId
            ? c with { Flags = c.Flags | additionalFlags } : c);
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, creatures,
            basis.Variables, [spell], objects: basis.Objects.Values, shots: basis.Shots.Values.Append(shot));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        var at = new TilePos(10, 10);
        world.Tile(at).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(at).OwnerPlayerId = Keeper;
        world.Player(Keeper).Mana = 45000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(5, SpellId, 3));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, at);
        return (db, world, target);
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 160)]
    public void CastUsesTheEnemyCreaturePathAndPaysOnce_StronglyInferredFromBinary(bool upgraded, ushort ticks)
    {
        var (db, world, target) = Setup();
        world.SetUpgraded(Keeper, 5, SpellId, upgraded);
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Run(7);
        var cast = new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id);
        sim.Tick(InputFrame.Of(cast));
        Assert.Equal(25000, world.Player(Keeper).Mana);
        Assert.Equal(20000, world.Player(Keeper).OneShotManaSpent);
        Assert.Equal(Rival, target.OwnerPlayerId);
        Assert.Equal(ticks, target.TurncoatTicks);
        Assert.Equal(Keeper, target.TurncoatPlayerId);
        Assert.Equal((7L, 7L), (world.Player(Keeper).Availabilities[0].LastCastTick,
            world.Player(Keeper).Availabilities[0].LastCheckedTick));
    }

    [Fact]
    public void ExpiryRestoresSelectionSideButKeepsTheTagAndCurrentFight_AcrossSnapshot()
    {
        var (db, world, target) = Setup();
        CreatureState keeper = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(11, 10));
        target.TurncoatTicks = 3;
        target.TurncoatPlayerId = Keeper;
        target.Dk2StateId = EngagingState;
        target.FightTargetKind = FightTargetKind.Creature;
        target.FightTargetId = keeper.Id;
        target.FightTargetPlayerId = Keeper;
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem()]);
        sim.Run(1);
        Assert.False(TurncoatRules.AreCombatEnemies(world, target, keeper));
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new CreatureStatusSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        sim.Run(2);
        restored.Run(2);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal((ushort)0, target.TurncoatTicks);
        Assert.Equal(Keeper, target.TurncoatPlayerId);
        Assert.True(TurncoatRules.AreCombatEnemies(world, target, keeper));
        Assert.Equal(EngagingState, target.Dk2StateId);
        Assert.Equal(keeper.Id, target.FightTargetId);
        Assert.Equal(Rival, target.OwnerPlayerId);
    }

    [Fact]
    public void AWorkingTargetKeepsItsJobRoomClaimPartyAndOwnerRoster()
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = JobRules.StateFor(JobType.Train)!.Value;
        target.CurrentJob = JobType.Train;
        target.HeldPlace = new RoomPlace(210, 1);
        target.PartyId = 7;
        int[] order = world.OwnedCreatures(Rival).Select(c => c.Id).ToArray();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        Assert.Equal((ushort)100, target.TurncoatTicks);
        Assert.Equal(JobRules.StateFor(JobType.Train), target.Dk2StateId);
        Assert.Equal(JobType.Train, target.CurrentJob);
        Assert.Equal(new RoomPlace(210, 1), target.HeldPlace);
        Assert.Equal(7, target.PartyId);
        Assert.Equal(order, world.OwnedCreatures(Rival).Select(c => c.Id));
        Assert.Empty(world.OwnedCreatures(Keeper));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(16384, 0)]
    [InlineData(16385, 4)]
    [InlineData(-1, 65532)]
    public void DurationStoreNarrowsToAWordAndRecastsOverwriteIt(int seconds, ushort expected)
    {
        var (db, world, target) = Setup();
        target.TurncoatTicks = 9;
        target.TurncoatPlayerId = Rival;
        var sim = new GameSimulation(db, world, 1, [new Probe(context =>
        {
            ShotState shot = ShotRules.Spawn(world, db.Shots[31], Keeper, target.PosX, target.PosY,
                seconds, 0, 0, FightTargetKind.Creature, target.Id, Rival, -1, target.PosX, target.PosY);
            TurncoatRules.Strike(context, shot, target, db.CreatureById(target.CreatureTypeId));
        })]);
        sim.Run(1);
        Assert.Equal(expected, target.TurncoatTicks);
        Assert.Equal(Keeper, target.TurncoatPlayerId);
    }

    [Fact]
    public void EnemyShotPredicateAcceptsAnActiveTurncoatOfItsOwnActualOwner()
    {
        var (db, world, target) = Setup();
        target.TurncoatTicks = 1;
        target.TurncoatPlayerId = Keeper;
        ShotState shot = ShotRules.Spawn(world, db.Shots[31], Rival, target.PosX, target.PosY,
            25, 0, 0, FightTargetKind.None, -1, Rival, -1, target.PosX, target.PosY);
        Assert.Same(target, ShotRules.FindHit(world, db, shot, db.Shots[31]));
        target.TurncoatTicks = 0;
        Assert.Null(ShotRules.FindHit(world, db, shot, db.Shots[31]));
    }

    [Fact]
    public void ImmunityRefusesTheEffectAfterTheCastHasPaid()
    {
        var (db, world, target) = Setup(CreatureFlags.IsImmuneToTurncoat);
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        Assert.Equal(25000, world.Player(Keeper).Mana);
        Assert.Equal((ushort)0, target.TurncoatTicks);
        Assert.Equal((ushort)0, target.TurncoatPlayerId);
    }

    [Fact]
    public void HornySkipsTheWholeTimedUpdate()
    {
        var (db, world, target) = Setup(CreatureFlags.IsHorny);
        target.TurncoatTicks = 1;
        target.TurncoatPlayerId = Keeper;
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem()]);
        sim.Run(3);
        Assert.Equal((ushort)1, target.TurncoatTicks);
    }

    [Fact]
    public void BothCombatSidesAndAlliancesAreResolvedThroughTheTimer()
    {
        var (db, world, target) = Setup();
        CreatureState keeper = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(11, 10));
        keeper.TurncoatTicks = 1;
        keeper.TurncoatPlayerId = Rival;
        Assert.False(TurncoatRules.AreCombatEnemies(world, target, keeper));
        target.TurncoatTicks = 1;
        target.TurncoatPlayerId = Keeper;
        Assert.True(TurncoatRules.AreCombatEnemies(world, target, keeper));
        keeper.TurncoatPlayerId = Keeper;
        Assert.False(TurncoatRules.AreCombatEnemies(world, target, keeper));
        keeper.TurncoatPlayerId = Rival;
        world.SetAllied(Keeper, Rival, true);
        Assert.False(TurncoatRules.AreCombatEnemies(world, target, keeper));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void CombatSelectionUsesTheSideAfterThisTurnsCountdown(ushort ticks, bool choosesKeeper)
    {
        var (db, world, target) = Setup();
        CreatureState rival = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        CreatureState keeper = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(12, 10));
        target.TurncoatTicks = ticks;
        target.TurncoatPlayerId = Keeper;
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem(), new CombatSystem()]);
        sim.Run(1);
        Assert.Equal((choosesKeeper ? keeper : rival).Id, target.FightTargetId);
        Assert.Equal(Rival, target.OwnerPlayerId);
    }

    [Fact]
    public void ACombatCastLeavesTheFightThroughItsStateHook()
    {
        var (db, world, target) = Setup();
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(11, 10));
        target.Dk2StateId = EngagingState;
        target.FightTargetKind = FightTargetKind.Creature;
        target.FightTargetId = enemy.Id;
        Assert.True(CombatRules.Register(enemy, target.Id, ranged: false));
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        Assert.Equal(CreatureStateMachine.Idle, target.Dk2StateId);
        Assert.Equal(FightTargetKind.None, target.FightTargetKind);
        Assert.DoesNotContain(target.Id, enemy.EngagedMelee);
    }

    [Fact]
    public void AWalkResumingCombatStopsAfterTheAcceptedIdleRequest()
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = 6;
        target.StateBeforeUtilityState = EngagingState;
        target.UtilityNewState = EngagingState;
        var sim = new GameSimulation(db, world, 1, [new Probe(context =>
        {
            context.Navigation.Refresh();
            Assert.True(context.Navigation.TryRoute(target, new TilePos(14, 10), 0));
            ShotState shot = ShotRules.Spawn(world, db.Shots[31], Keeper, target.PosX, target.PosY,
                25, 0, 0, FightTargetKind.Creature, target.Id, Rival, -1, target.PosX, target.PosY);
            TurncoatRules.Strike(context, shot, target, db.CreatureById(target.CreatureTypeId));
        })]);
        sim.Run(1);
        Assert.Equal(CreatureStateMachine.Idle, target.Dk2StateId);
        Assert.False(target.IsRouted);
    }

    [Fact]
    public void SuccessfulDeathEntryClearsBothWords()
    {
        var (db, world, target) = Setup();
        target.TurncoatTicks = 2;
        target.TurncoatPlayerId = Keeper;
        var sim = new GameSimulation(db, world, 1, [new Probe(context => CreatureDeathRules.Collapse(context.Hooks, target))]);
        sim.Run(1);
        Assert.Equal(CreatureDeathRules.FallingState, target.Dk2StateId);
        Assert.Equal((ushort)0, target.TurncoatTicks);
        Assert.Equal((ushort)0, target.TurncoatPlayerId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EachTurncoatWordChangesTheStateHash(bool timer)
    {
        var (db, world, target) = Setup();
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        if (timer) target.TurncoatTicks = 1;
        else target.TurncoatPlayerId = Keeper;
        Assert.NotEqual(before, sim.StateHash());
    }

    [Fact]
    public void ActualOwnerCannotPickUpAnActiveTurncoat_ExceptTheOwnedPitArm()
    {
        var (db, world, target) = Setup();
        Assert.True(HandRules.HasPickupPermission(world, db, target, Rival));
        target.TurncoatTicks = 1;
        target.TurncoatPlayerId = Keeper;
        Assert.False(HandRules.HasPickupPermission(world, db, target, Rival));
        target.CombatPitFighter = true;
        world.Tile(target.Tile).OwnerPlayerId = Rival;
        Assert.True(HandRules.HasPickupPermission(world, db, target, Rival));
        target.CombatPitFighter = false;
        target.TurncoatTicks = 0;
        Assert.True(HandRules.HasPickupPermission(world, db, target, Rival));
    }

    private sealed class Probe(Action<SimulationContext> action) : ISimulationSystem
    {
        public string Name => "turncoat_probe";
        public void Execute(SimulationContext context) => action(context);
    }
}
