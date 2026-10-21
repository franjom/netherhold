// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Room_BorderEffectTick (<c>0x004E7CD0</c>): a room's cursor runs its contour by the
/// table at <c>0x006A22F8</c>, one tile's length every 4096 / 800 ticks, and a Flame Round Room is
/// asked for at it on DK2's odd turns.</summary>
public class RoomBorderFlameTests
{
    private const byte Keeper = 4;

    private static WorldState World(GameDatabase db, params (int X, int Y)[] tiles)
    {
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        foreach ((int x, int y) in tiles)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Casino), Keeper);
        return world;
    }

    /// <summary>Every edge crossing the cursor makes from its start, as (x, y, orientation).</summary>
    private static List<(int, int, int)> Crossings(GameDatabase db, WorldState world, int count)
    {
        RoomInstance room = RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.Casino);
        RoomBorderCursor cursor = RoomBorderRules.Start(world, room);
        var seen = new List<(int, int, int)>();
        while (seen.Count < count)
        {
            RoomBorderCursor next = RoomBorderRules.Advance(cursor, world, room);
            if ((next.X, next.Y, next.Orientation) != (cursor.X, cursor.Y, cursor.Orientation))
                seen.Add((next.X, next.Y, next.Orientation));
            cursor = next;
        }
        return seen;
    }

    [Fact]
    public void TheCursorRunsClockwiseRoundASquareRoomOneEdgeAtATime()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db, (1, 1), (2, 1), (1, 2), (2, 2));

        Assert.Equal(
        [
            (1, 1, 0), (2, 1, 0), (2, 1, 1), (2, 2, 1), (2, 2, 2), (1, 2, 2), (1, 2, 3), (1, 1, 3),
            (1, 1, 0),
        ], Crossings(db, world, 9));
    }

    [Fact]
    public void AtAnInnerCornerTheCursorStepsDiagonallyOntoTheNextEdge()
    {
        // An L: (1,1) over (1,2), (2,2). Running south down (1,1)'s east edge, the third probe
        // finds (2,1) outside the room, so the cursor moves to (2,2) and runs east along its north edge.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db, (1, 1), (1, 2), (2, 2));

        Assert.Equal([(1, 1, 0), (1, 1, 1), (2, 2, 0), (2, 2, 1)], Crossings(db, world, 4));
    }

    [Fact]
    public void AFlameIsAskedForOnOddTurnsAtTheClampedEdgePointInTheOwnersName()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db, (1, 1), (2, 1), (1, 2), (2, 2));
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, seed: 1, [new RoomBorderFlameSystem()]);
        sim.ObserveWith(sink);

        sim.Run(2);

        // Tick 0 is DK2's turn 1. The new cursor starts on (1,1)'s west edge running north, phase
        // 800: the point is 0x200 in from the west and 0xFFF - 800 down the edge.
        ViewEffectRequest flame = Assert.Single(sink.Events, e => e.EffectRequest.HasValue).EffectRequest!.Value;
        Assert.Equal(RoomBorderRules.FlameEffectId, flame.EffectId.Value);
        Assert.Equal(Keeper, flame.Owner.Value);
        Assert.Equal(new FpPosition3(new(0x1000 | 0x200), new(0x1000 | (0xFFF - 800)), new(Fp32.One)), flame.Position);
        Assert.Equal(default, flame.Heading);
        RoomBorderCursor cursor = Assert.Single(world.RoomBorderCursors);
        Assert.Equal(1600, cursor.Phase);
    }

    [Fact]
    public void ACursorOutlivesASnapshotAndGoesWithItsRoom()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db, (1, 1), (2, 1), (1, 2), (2, 2));
        var sim = new GameSimulation(db, world, seed: 1, [new RoomBorderFlameSystem()]);
        sim.Run(7);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new RoomBorderFlameSystem()]);
        Assert.Equal(world.RoomBorderCursors, copy.World.RoomBorderCursors);
        Assert.Equal(sim.StateHash(), copy.StateHash());

        foreach ((int x, int y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        sim.Run(1);
        Assert.Empty(world.RoomBorderCursors);
    }
}
