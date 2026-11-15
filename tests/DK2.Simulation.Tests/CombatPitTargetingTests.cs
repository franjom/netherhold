// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class CombatPitTargetingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = Keeper + 1;
    private const byte SeekerType = 34;

    private static (GameDatabase Database, WorldState World) Open()
    {
        GameDatabase database = Dk2TerrainFixture.Database(Dk2TerrainFixture.Goblin() with
        {
            CreatureId = SeekerType,
            Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.IsFearless,
            DistanceCanSee = new Fp32(8 * Fp32.One),
            DistanceCanHear = new Fp32(8 * Fp32.One),
            Spells = [],
        });
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(20, 16),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        for (int x = 1; x < world.Width - 1; x++) world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (database, world);
    }

    private static void Force(CreatureState me, int id)
    {
        me.AttackAbnormalTargets = true;
        me.AttackingId = id;
        me.AttackingKind = FightTargetKind.Creature;
    }

    private sealed class Sight(bool blocked) : ISightBlocker
    {
        public bool Blocks(int x, int y) => blocked;
    }

    private sealed class SelectOnce(CreatureState me, ISightBlocker? sight = null) : ISimulationSystem
    {
        public string Name => "pit target selection";
        public bool Selected { get; private set; }
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            var hearts = new HeartSites();
            hearts.Refresh(context.World, context.Database);
            Selected = CombatTargetRules.SelectAVictim(context,
                sight ?? new WorldSight(context.Navigation, context.World, context.Database),
                hearts, new RoomCache(), me, context.Database.CreatureById(me.CreatureTypeId), []);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMissingForcedTagFindsTheNearestAttackableCreatureEvenWhenItIsAnAlly(bool hearingOnly)
    {
        var (database, world) = Open();
        CreatureState me = world.SpawnArrived(database, SeekerType, Keeper, new TilePos(5, 5));
        CreatureState ally = world.SpawnArrived(database, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(7, 5));
        world.SpawnArrived(database, Dk2TerrainFixture.KnightCreatureId, Rival, new TilePos(8, 5));
        Force(me, int.MaxValue);
        var select = new SelectOnce(me, hearingOnly ? new Sight(true) : null);

        new GameSimulation(database, world, 42, [select]).Tick();

        Assert.True(select.Selected);
        Assert.False(me.AttackAbnormalTargets);
        Assert.Equal(ally.Id, me.FightTargetId);
    }

    [Fact]
    public void AnImpDoesNotHearAnOwnCreatureBeyondItsOneTileSightInTheMissingTagScan()
    {
        var (database, world) = Open();
        CreatureState me = world.SpawnArrived(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(5, 5));
        world.SpawnArrived(database, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(7, 5));
        Force(me, int.MaxValue);
        var select = new SelectOnce(me);

        new GameSimulation(database, world, 42, [select]).Tick();

        Assert.False(select.Selected);
        Assert.False(me.AttackAbnormalTargets);
    }

    [Fact]
    public void AMissingForcedDoorTagAlsoUsesTheNearestAttackableCreatureScan()
    {
        var (database, world) = Open();
        CreatureState me = world.SpawnArrived(database, SeekerType, Keeper, new TilePos(5, 5));
        CreatureState ally = world.SpawnArrived(database, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(7, 5));
        Force(me, int.MaxValue);
        me.AttackingKind = FightTargetKind.Door;
        var select = new SelectOnce(me);

        new GameSimulation(database, world, 42, [select]).Tick();

        Assert.True(select.Selected);
        Assert.False(me.AttackAbnormalTargets);
        Assert.Equal(ally.Id, me.FightTargetId);
    }

    [Fact]
    public void AnUnreachableForcedCreatureFallsThroughToARevealedTrap()
    {
        var (database, world) = Open();
        CreatureState me = world.SpawnArrived(database, SeekerType, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnArrived(database, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 5));
        for (int y = 1; y < world.Height - 1; y++) world.Tile(9, y).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(7, 5).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(7, 5).OwnerPlayerId = Rival;
        ThingProjection.Place(world, database, [new TrapThing(new byte[16])
            { X = 7, Y = 5, TrapId = Dk2TerrainFixture.AlarmTrapId, PlayerId = Rival, NumberOfShots = 1 }]);
        world.Traps[^1].Hidden = false;
        Force(me, target.Id);
        var select = new SelectOnce(me);

        new GameSimulation(database, world, 42, [select]).Tick();

        Assert.True(select.Selected);
        Assert.Equal(FightTargetKind.Trap, me.FightTargetKind);
        Assert.Equal(world.Traps[^1].Id, me.FightTargetId);
    }

    [Fact]
    public void AForcedWorkerStillAllowsTheHeartScanUsingTheUnboundedRunningDistance()
    {
        var (database, world) = Open();
        for (int y = 3; y <= 7; y++)
        for (int x = 8; x <= 12; x++)
            RoomRules.Place(database, ref world.Tile(x, y), database.Room(RoomTypeId.DungeonHeart), Rival);
        CreatureState me = world.SpawnArrived(database, SeekerType, Keeper, new TilePos(5, 5));
        CreatureState worker = world.SpawnArrived(database, Dk2TerrainFixture.ImpCreatureId, Rival, new TilePos(6, 5));
        Force(me, worker.Id);
        var select = new SelectOnce(me);

        new GameSimulation(database, world, 42, [select]).Tick();

        Assert.True(select.Selected);
        Assert.Equal(FightTargetKind.Heart, me.FightTargetKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFriendlySpellOutranksAForcedCreatureAndAnUnreachableAllyRefusesTheWholeAcquisition(bool unreachable)
    {
        var (database, world) = Open();
        CreatureState priest = world.SpawnArrived(database, Dk2TerrainFixture.PriestCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnArrived(database, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 6));
        CreatureState ally = world.SpawnArrived(database, Dk2TerrainFixture.KnightCreatureId, Keeper, new TilePos(7, 5));
        ally.Health = 100;
        StatePlacement.Place(ally, 199);
        if (unreachable)
            for (int y = 1; y < world.Height - 1; y++) world.Tile(6, y).TerrainId = Dk2TerrainFixture.Rock;
        Force(priest, target.Id);
        var select = new SelectOnce(priest, new Sight(false));

        new GameSimulation(database, world, 42, [select]).Tick();

        Assert.Equal(!unreachable, select.Selected);
        if (!unreachable)
        {
            Assert.Equal(ally.Id, priest.FightTargetId);
            Assert.Equal(CombatRules.AttackMode.SpellAtAlly, priest.AttackType);
        }
        else Assert.Equal(CombatRules.NoTarget, priest.FightTargetId);
    }
}
