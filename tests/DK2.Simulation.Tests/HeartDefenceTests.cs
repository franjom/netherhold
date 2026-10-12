// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Dungeon Heart's responder dispatch (<c>0x004D9690</c>, ADR-0407): an attackable enemy
/// within <c>DungeonHeartReportingDistanceTiles</c> of the heart sends the owner's creatures within
/// <c>DungeonHeartCreatureGatheringDistanceTiles</c>, workers included, running to it in state 6
/// resuming 254, on every fourth turn.</summary>
public class HeartDefenceTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const int TravelToPoint = 6;
    private const int ReportingDanger = 254;

    [Fact]
    public void AnEnemyImpNearTheHeartSendsTheOwnersImpRunningToIt_OnTheMeasuredPhase()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState defender = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 14);
        CreatureState intruder = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, 11, 8);

        long sent = FirstSent(db, world, defender, 8);

        // Room 1100's phase: DK2 turns 0 mod 4, which ours runs on ticks 3 mod 4 (turn = tick + 1).
        Assert.Equal(3, sent);
        Assert.Equal((6, 254), (defender.Dk2StateId, defender.UtilityNewState));
        Assert.True(defender.Running);
        Assert.Equal(intruder.Tile, defender.Destination);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(22)]
    public void AWorkingImpStopsItsTaskWhenAnEnemyEntersTheHeartReportingRadius(int workingState)
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState defender = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 14);
        StatePlacement.Place(defender, workingState);
        Assert.Equal(CreatureActivity.Working, defender.Activity);
        Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, 11, 8);

        Assert.Equal(3, FirstSent(db, world, defender, 8));
        Assert.Equal(CreatureActivity.Reporting, defender.Activity);
        Assert.True(defender.Running);
    }

    [Fact]
    public void AnEnemyOutsideTheReportingRadiusSendsNobody()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState defender = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 14);
        // Five tiles off the heart's position word (11,11), outside its four.
        Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, 11, 6);

        Assert.Equal(-1, FirstSent(db, world, defender, 8));
    }

    [Fact]
    public void ACreatureBeyondTheGatheringRadiusOrSlappedOrHurtStaysPut()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState far = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 18);
        CreatureState slapped = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 14);
        StatePlacement.Place(slapped, 77);
        CreatureState hurt = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 10, 14);
        hurt.Health = 1;
        CreatureState near = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 14);
        Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, 11, 8);

        Assert.Equal(3, FirstSent(db, world, near, 8));
        Assert.All([far, slapped, hurt], c => Assert.NotEqual(ReportingDanger, c.UtilityNewState));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The tick on which the creature is first walking to 254, or -1.</summary>
    private static long FirstSent(GameDatabase db, WorldState world, CreatureState creature, int ticks)
    {
        GameSimulation sim = new(db, world, 1, [new HeartDefenceSystem()]);
        for (int i = 0; i < ticks; i++)
        {
            long tick = world.CurrentTick.Value;
            sim.Run(1);
            if (creature.Dk2StateId == TravelToPoint && creature.UtilityNewState == ReportingDanger)
                return tick;
        }
        return -1;
    }

    /// <summary>An open 20 × 20 map with a 3 × 3 heart of the keeper's at (10,10); its position word
    /// is one tile past the centre, (11,11).</summary>
    private static (GameDatabase, WorldState) World()
    {
        var radii = new GlobalVariables();
        radii.Misc[VariableTypeId.DungeonHeartReportingDistanceTiles] = new MiscVariable(VariableTypeId.DungeonHeartReportingDistanceTiles, 4, 0, 0);
        radii.Misc[VariableTypeId.DungeonHeartCreatureGatheringDistanceTiles] = new MiscVariable(VariableTypeId.DungeonHeartCreatureGatheringDistanceTiles, 7, 0, 0);
        GameDatabase db = Dk2TerrainFixture.Database().ForLevel(radii);
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
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                ref TileState tile = ref world.Tile(new TilePos(10 + dx, 10 + dy));
                tile.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
                tile.OwnerPlayerId = Keeper;
            }
        }
        return (db, world);
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));
}
