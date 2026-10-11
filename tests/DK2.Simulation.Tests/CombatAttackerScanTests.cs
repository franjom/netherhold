// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class CombatAttackerScanTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GiantType = 35;

    [Theory]
    [InlineData(16, 3, false)]
    [InlineData(16, 5, false)]
    [InlineData(6, 3, false)]
    [InlineData(6, 5, false)]
    [InlineData(16, 5, true)]
    [InlineData(6, 5, true)]
    public void ARegisteredAttackerInterruptsImpWorkBeyondItsOrdinaryDecisionRadius(int state, int distance, bool ranged)
    {
        var (db, world) = OpenWorld();
        CreatureState giant = Spawn(db, world, GiantType, PlayerIds.Good, 8 + distance, 8);
        CreatureState imp = WorkingImp(db, world, state);
        Assert.True(CombatRules.Register(imp, giant.Id, ranged));

        Combat(db, world).Tick();

        Assert.Equal(FightTargetKind.Creature, imp.FightTargetKind);
        Assert.Equal(giant.Id, imp.FightTargetId);
        Assert.True(CombatRules.InCombat(imp));
        Assert.NotEqual(CreatureActivity.Working, imp.Activity);
    }

    [Fact]
    public void AnUnregisteredDistantHeroDoesNotInterruptTheOrdinaryImpScan()
    {
        var (db, world) = OpenWorld();
        Spawn(db, world, GiantType, PlayerIds.Good, 13, 8);
        CreatureState imp = WorkingImp(db, world, 16);

        Combat(db, world).Tick();

        Assert.Equal(FightTargetKind.None, imp.FightTargetKind);
        Assert.Equal(CreatureActivity.Working, imp.Activity);
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.Rock)]
    [InlineData(Dk2TerrainFixture.Lava)]
    public void ARegisteredAttackerAcrossAnObstructedRouteIsRejected(byte barrierTerrain)
    {
        var (db, world) = OpenWorld();
        for (int y = 1; y < world.Height - 1; y++)
            world.Tile(new TilePos(10, y)).TerrainId = barrierTerrain;
        CreatureState giant = Spawn(db, world, GiantType, PlayerIds.Good, 13, 8);
        CreatureState imp = WorkingImp(db, world, 16);
        Assert.True(CombatRules.Register(imp, giant.Id, ranged: false));

        Combat(db, world).Tick();

        Assert.Equal(FightTargetKind.None, imp.FightTargetKind);
        Assert.Equal(CreatureActivity.Working, imp.Activity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADeadOrMissingRegisteredAttackerIsIgnored(bool dead)
    {
        var (db, world) = OpenWorld();
        CreatureState giant = Spawn(db, world, GiantType, PlayerIds.Good, 13, 8);
        CreatureState imp = WorkingImp(db, world, 16);
        if (dead) giant.Health = 0;
        imp.EngagedMelee[0] = dead ? giant.Id : 9999;

        Combat(db, world).Tick();

        Assert.Equal(FightTargetKind.None, imp.FightTargetKind);
        Assert.Equal(CreatureActivity.Working, imp.Activity);
    }

    [Fact]
    public void EquallyDistantRegisteredAttackersUseSlotOrder()
    {
        var (db, world) = OpenWorld();
        CreatureState firstSpawned = Spawn(db, world, GiantType, PlayerIds.Good, 13, 8);
        CreatureState secondSpawned = Spawn(db, world, GiantType, PlayerIds.Good, 13, 8);
        CreatureState imp = WorkingImp(db, world, 16);
        Assert.True(CombatRules.Register(imp, secondSpawned.Id, ranged: false));
        Assert.True(CombatRules.Register(imp, firstSpawned.Id, ranged: false));

        Combat(db, world).Tick();

        Assert.Equal(secondSpawned.Id, imp.FightTargetId);
    }

    [Fact]
    public void RegisteredAttackersAreInterleavedMeleeThenRangedPerSlot()
    {
        var (db, world) = OpenWorld();
        CreatureState melee = Spawn(db, world, GiantType, PlayerIds.Good, 13, 8);
        CreatureState ranged = Spawn(db, world, GiantType, PlayerIds.Good, 13, 8);
        CreatureState imp = WorkingImp(db, world, 16);
        imp.EngagedMelee[1] = melee.Id;
        imp.EngagedRanged[0] = ranged.Id;

        Combat(db, world).Tick();

        Assert.Equal(ranged.Id, imp.FightTargetId);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(6)]
    public void ARegisteredGiantMakesTheImpFleeBeforeReachingMeleeRange(int state)
    {
        var (db, world) = OpenWorld();
        CreatureState giant = Spawn(db, world, GiantType, PlayerIds.Good, 13, 8);
        CreatureState imp = WorkingImp(db, world, state);
        Assert.True(CombatRules.Register(imp, giant.Id, ranged: false));
        GameSimulation simulation = Combat(db, world);

        simulation.Tick();
        simulation.Tick();

        Assert.Equal(CreatureActivity.Fleeing, imp.Activity);
        Assert.False(giant.SwingInProgress);
        Assert.Equal(500, imp.Health);
    }

    private static CreatureState WorkingImp(GameDatabase db, WorldState world, int state)
    {
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 8, 8);
        imp.UtilityNewState = 15;
        StatePlacement.Place(imp, state);
        imp.FightProcessCounter = 3;
        imp.LastFightScanTileIndex = imp.Tile.Y * world.Width + imp.Tile.X;
        return imp;
    }

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Knight() with
        {
            CreatureId = GiantType,
            Name = "Giant",
            Threat = 160,
        });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    private static GameSimulation Combat(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CombatSystem()]);
}
