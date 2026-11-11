// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
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
    public void HeartDispatch_004D9A1A_RetainsTheIntrudersPointWithinTheRespondersTile_004CF490()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState defender = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 12);
        CreatureState intruder = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, 11, 12);
        intruder.PosX += 0x400;

        Assert.Equal(3, FirstSent(db, world, defender, 8));

        // The native route keeps a destination even with no triangle gates.
        Assert.True(defender.HasRoute);
        Assert.Equal((intruder.PosX << 4, intruder.PosY << 4),
            (defender.Body.PathTargetPos.X, defender.Body.PathTargetPos.Y));
    }

    [Fact]
    public void HeartDispatch_0047189E_RejectsASolidDestinationEvenOnTheRespondersTile()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState defender = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 12);
        Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, 11, 12);
        world.Tile(11, 12).TerrainId = Dk2TerrainFixture.Rock;
        defender.ReportTicks = 17;

        Assert.Equal(-1, FirstSent(db, world, defender, 8));
        Assert.Equal(17, defender.ReportTicks);
        Assert.False(defender.HasRoute);
    }

    [Fact]
    public void HeartDispatch_004D99C9_AWorkerThatCannotReachTheIntruderKeepsItsOwnWalk()
    {
        // 0x004D0E60 routes into a local buffer: a refused responder's body is not touched, so a
        // worker walking to its job walks on. Level 11A: Morgana's imps crossing her bridge kept
        // losing that walk every fourth turn to Drako's diggers behind her wall (Q563).
        (GameDatabase db, WorldState world) = World();
        CreatureState walker = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 14);
        // Inside the reporting radius of (11,11), sealed in by rock on all eight sides.
        CreatureState intruder = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Rival, 13, 8);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0) world.Tile(13 + dx, 8 + dy).TerrainId = Dk2TerrainFixture.Rock;
        Assert.True(new WorldSteering(db, world).TryRouteTo(
            walker, CreatureState.CentreOf(16), CreatureState.CentreOf(16), MoveOptions.Loose));
        Fixed16Vector goal = walker.Body.PathTargetPos;
        int distance = walker.Body.PathTotalDist;

        Assert.Equal(-1, FirstSent(db, world, walker, 8));
        Assert.True(walker.HasRoute);
        Assert.Equal(goal, walker.Body.PathTargetPos);
        Assert.Equal(distance, walker.Body.PathTotalDist);
        Assert.NotEqual(intruder.Tile, walker.Destination);
    }

    [Theory]
    [InlineData(29, 30, 254)]
    [InlineData(30, 31, 1)]
    [InlineData(int.MaxValue, int.MinValue, 1)]
    [InlineData(-2, -1, 1)]
    [InlineData(-1, 0, 254)]
    public void Reporting_004759A6_RetainsTheCounterAndUsesAnUnsignedTimeout(int before, int after, int state)
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState defender = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 11, 12);
        StatePlacement.Place(defender, 254);
        defender.ReportTicks = before;
        defender.Running = true;

        new GameSimulation(db, world, 1, [new DK2.Simulation.Systems.CombatSystem()]).Tick();

        Assert.Equal(after, defender.ReportTicks);
        Assert.Equal(state, defender.Dk2StateId);
        Assert.Equal(state == 254, defender.Running);
    }

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
