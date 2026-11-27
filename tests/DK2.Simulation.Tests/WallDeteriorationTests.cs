// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Random wall deterioration (<c>0x004BB6D0</c>, ADR-0085). Shipped data switches it off, so
/// these tests turn variables 145 and 146 on to exercise the branch.</summary>
public class WallDeteriorationTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int FloorColumn = 8;
    private const int StartHealth = 500;

    private static GameDatabase Db(int picks, int damage)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var variables = new GlobalVariables();
        foreach (KeyValuePair<VariableTypeId, MiscVariable> entry in basis.Variables.Misc)
        {
            variables.Misc[entry.Key] = entry.Value;
        }

        variables.Misc[VariableTypeId.NumberOfRandomWallDeteriorationsPerSecond] =
            new MiscVariable(VariableTypeId.NumberOfRandomWallDeteriorationsPerSecond, picks, 0, 0);
        variables.Misc[VariableTypeId.WallDeteriorationDamage] = new MiscVariable(VariableTypeId.WallDeteriorationDamage, damage, 0, 0);
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values, variables);
    }

    /// <summary>The keeper's own reinforced walls everywhere inside the border, with one column of its
    /// claimed floor down the middle: only the walls either side of it touch its floor.</summary>
    private static WorldState World(GameDatabase db)
    {
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = x == FloorColumn ? Dk2TerrainFixture.ClaimedPath : Dk2TerrainFixture.ReinforcedWall;
            tile.OwnerPlayerId = Keeper;
            tile.Health = StartHealth; // below MaxHealth, which Tile_ApplyWork would clamp back to
        }
        }

        return world;
    }

    [Fact]
    public void EachPickDamagesOnlyAnOwnWallBesideOwnFloor()
    {
        GameDatabase db = Db(picks: 255, damage: -10);
        WorldState world = World(db);
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 7, [new DungeonHeartSystem(frames: false)]);
        sim.ObserveWith(sink);

        sim.Run(1);

        SimEvent[] hits = [.. sink.Events.Where(e => e.Kind == "wall_deteriorated")];
        Assert.NotEmpty(hits);
        Assert.All(hits, e => Assert.Matches($@"at=\(({FloorColumn - 1}|{FloorColumn + 1}),\d+\) delta=-10", e.Detail));
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
        {
            int count = hits.Count(e => e.Detail.Contains($"at=({x},{y}) "));
            if (x == FloorColumn)
                {
                    continue;
                }

                Assert.Equal(StartHealth - (10 * count), world.Tile(x, y).Health);
        }
        }
    }

    [Fact]
    public void ZeroPicksDrawsNothing()
    {
        // Variable 145 at 0 (the shipped value) returns before the first draw.
        GameDatabase db = Db(picks: 0, damage: -10);
        WorldState world = World(db);
        var simulation = new GameSimulation(db, world, 7, [new DungeonHeartSystem(frames: false)]);
        WorldState control = World(Db(picks: 0, damage: 0));
        var reference = new GameSimulation(Db(picks: 0, damage: 0), control, 7, [new DungeonHeartSystem(frames: false)]);

        simulation.Run(3);
        reference.Run(3);

        Assert.Equal(reference.Rng.SaveState(), simulation.Rng.SaveState());
    }
}
