// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class HatcheryObjectAnimationTests
{
    [Fact]
    public void FullHatcheryStartsCoopAndLaunchesItsHiddenEggAfterFourSeconds()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, 3]);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Hatchery), 3);
        RoomInstance room = RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.Hatchery);
        HatcheryRules.PlaceCoops(world, db, room);
        foreach (int tile in room.TileIndices) world.SpawnObject(db, 9, 3, tile);
        ObjectState egg = Assert.IsType<ObjectState>(HatcheryRules.TryLayEgg(world, db, room, new SimRandom(7)));
        Assert.Equal((byte)24, egg.ProcessState);
        Assert.Contains(world.Objects, o => o.ObjectTypeId == 35 && o.ProcessState == 29);
        var simulation = new GameSimulation(db, world, 7, [new HatcherySystem()]);
        for (int tick = 0; tick < 15; tick++) simulation.Tick();
        Assert.Equal(0, egg.PhysicalVelocityX);
        Assert.Equal(0, egg.PhysicalVelocityY);
        simulation.Tick();
        Assert.Equal(0x3000 - 16 * 240 * 256, egg.PhysicalVelocityZ);
        Assert.InRange(Math.Abs(egg.PhysicalVelocityX) + Math.Abs(egg.PhysicalVelocityY), 0x6000, 0x9000);
    }

    [Fact]
    public void EggInstallsHatchEndThenBecomesChickenOnTheFollowingTurn()
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        CreatureAnimationTiming[] art = new CreatureAnimationTiming[9];
        art[0] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 45, FramesPerSecond = 30 };
        art[5] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 15, FramesPerSecond = 30 };
        var database = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values, fixture.Creatures.Values,
            fixture.Variables, objects: fixture.Objects.Values.Select(o => o.ObjectId == HatcheryRules.EggObjectId ? o with { ArtSlots = art } : o));
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [0, 1, 3]);
        ObjectState egg = world.SpawnObject(database, HatcheryRules.EggObjectId, 3, 40);
        egg.ProcessState = 32;
        var simulation = new GameSimulation(database, world, 7, [new HatcherySystem()]);
        var events = new ListEventSink();
        simulation.ObserveWith(events);
        for (int tick = 0; tick < 6; tick++) simulation.Tick();
        Assert.Equal((byte)0, egg.ProcessCount);
        Assert.Contains(egg, world.Objects);
        simulation.Tick();
        Assert.Equal((byte)1, egg.ProcessCount);
        Assert.Contains(egg, world.Objects);
        Assert.Equal(new GameTick(6).Value, egg.ProcessDeadline);
        simulation.Tick();
        Assert.DoesNotContain(egg, world.Objects);
        Assert.Contains(world.Objects, thing => thing.ObjectTypeId == HatcheryRules.ChickenObjectId && thing.TileIndex == 40);
        Assert.Contains(events.Events, e => e.Kind == "chicken_hatched" && e.Detail.Contains("animation=complete"));
    }
}
