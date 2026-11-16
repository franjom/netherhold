// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Behavior.Tests;

/// <summary>Other players' traps in the observation (economy and assault plan A1): shown as the world view draws them
/// (a visible tile, built, not hidden), never one's own, with the reach their finder looks (traps.md sections 5 and 6).
/// The traps are invented.</summary>
public class PlayerObservationTrapTests
{
    private const byte Turning = 1, Hidden = 2, OnTile = 3, Plain = 4;

    private static TrapDef Trap(byte id, uint flags, byte selector, int range) => new()
    {
        Name = $"trap {id}", TrapId = id, Health = 100, GoldCost = 0, ManufactureRequired = 0, ShotTypeId = 0,
        ManaUpkeepPerSecond = 0, Flags = flags, TriggerType = selector, RangeTiles = range,
    };

    [Fact]
    public void VisibleBuiltEnemyTrapsAreShownWithTheirFindersReach()
    {
        GameDatabase basic = PlayerObservationTests.Database();
        var database = new GameDatabase(basic.Terrain.Values, basic.Rooms.Values, basic.Creatures.Values, basic.Variables,
            basic.KeeperSpells.Values, basic.Objects.Values, traps:
            [
                Trap(Turning, TrapRules.TurnsToTarget, TrapRules.SightSelector, 5),
                Trap(Hidden, 0x20, TrapRules.SightSelector, 4),
                Trap(OnTile, 0, TrapRules.TileSelector, 3),
                Trap(Plain, 0, TrapRules.SightSelector, 6),
            ]);
        int width = PlayerObservationTests.Width, height = PlayerObservationTests.Height;
        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tiles[y * width + x] = new MapTile(PlayerObservationTests.DirtPath, x < PlayerObservationTests.Seen ? (byte)3 : (byte)4, 0, 0);
        WorldState world = WorldState.FromMap(database, new LevelMap(width, height, tiles), [3, 4], fogOfWarEnabled: true);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < PlayerObservationTests.Seen; x++)
                world.Tile(x, y).SetExploredBy(3);
        ThingProjection.Place(world, database,
        [
            new TrapThing(new byte[16]) { X = 0, Y = 0, TrapId = Turning, PlayerId = 4, NumberOfShots = 1 },
            new TrapThing(new byte[16]) { X = 1, Y = 0, TrapId = Hidden, PlayerId = 4, NumberOfShots = 1 },
            new TrapThing(new byte[16]) { X = 2, Y = 0, TrapId = OnTile, PlayerId = 4, NumberOfShots = 1 },
            new TrapThing(new byte[16]) { X = 3, Y = 0, TrapId = Plain, PlayerId = 4, NumberOfShots = 1 },
            new TrapThing(new byte[16]) { X = 1, Y = 2, TrapId = Turning, PlayerId = 3, NumberOfShots = 1 },   // the viewer's own
            new TrapThing(new byte[16]) { X = 6, Y = 1, TrapId = Turning, PlayerId = 4, NumberOfShots = 1 },   // on unexplored ground
        ]);

        PlayerObservation observation = new PlayerObservationBuilder(database, PlayerObservationTests.Viewer)
            .Capture(world, PlayerObservationTests.Stamp(world), []).Observation;

        Assert.Equal(
        [
            new PlayerVisibleTrap(new TilePos(0, 0), Turning, new PlayerId(4), 7),   // range plus the turning trap's two
            new PlayerVisibleTrap(new TilePos(2, 0), OnTile, new PlayerId(4), 0),    // fires at its own tile
            new PlayerVisibleTrap(new TilePos(3, 0), Plain, new PlayerId(4), 6),
        ], observation.EnemyTraps.OrderBy(t => t.Tile.X));

        // Once fired, the hidden one shows.
        world.Traps.Single(t => t.TrapId == Hidden).Hidden = false;
        Assert.Contains(new PlayerObservationBuilder(database, PlayerObservationTests.Viewer)
            .Capture(world, PlayerObservationTests.Stamp(world, 2), []).Observation.EnemyTraps, t => t.Type == Hidden);
    }
}
