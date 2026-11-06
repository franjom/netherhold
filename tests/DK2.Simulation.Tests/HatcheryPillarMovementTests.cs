// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class HatcheryPillarMovementTests
{
    [Theory]
    [InlineData(1, 1, true, false, false, false, true, 3)]
    [InlineData(3, 1, true, false, false, false, true, 3)]
    [InlineData(1, 3, true, false, false, false, true, 3)]
    [InlineData(3, 3, true, false, false, false, true, 3)]
    [InlineData(1, 1, false, false, false, false, false, 3)]
    [InlineData(1, 1, true, true, false, false, false, 3)]
    [InlineData(1, 1, true, true, true, false, true, 3)]
    [InlineData(1, 1, true, true, false, true, true, 3)]
    [InlineData(1, 1, true, false, false, false, false, 2)]
    [InlineData(3, 1, true, false, false, false, false, 4)]
    public void RoomPointSelectionRespectsVirtualPillarsAndTheirSuppression(
        int cornerX, int cornerY, bool obstacle, bool otherPillar, bool held, bool carried, bool blocked, int roomWidth)
    {
        GameDatabase original = Dk2TerrainFixture.Database();
        ObjectDef template = original.ObjectById(Navigation.WorldSteering.ArchesObjectId)!;
        ObjectDef pillar = template with
        {
            ObjectId = HatcheryRules.PillarObjectId,
            Flags = obstacle ? ObjectFlags.Obstacle | ObjectFlags.Pillar : ObjectFlags.Pillar,
        };
        ObjectDef substitute = template with { ObjectId = 200, Flags = ObjectFlags.Pillar };
        var db = new GameDatabase(original.Terrain.Values, original.Rooms.Values, original.Creatures.Values,
            original.Variables, objects: original.Objects.Values.Append(pillar).Append(substitute));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= roomWidth; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Hatchery), PlayerIds.FirstKeeper);
        // Only the requested corner can be selected; otherwise the bird stays on its current slab.
        world.SpawnObject(db, Navigation.WorldSteering.DungeonHeartObstacleObjectId,
            PlayerIds.FirstKeeper, cornerY * world.Width + (4 - cornerX));
        world.SpawnObject(db, Navigation.WorldSteering.DungeonHeartObstacleObjectId,
            PlayerIds.FirstKeeper, 2 * world.Width + 2);
        if (otherPillar)
        {
            ObjectState existing = world.SpawnObject(db, substitute.ObjectId,
                PlayerIds.FirstKeeper, cornerY * world.Width + cornerX);
            existing.InHand = held;
            if (carried) existing.CarriedByCreatureId = 17;
        }
        ObjectState bird = world.SpawnObject(db, HatcheryRules.ChickenObjectId,
            PlayerIds.FirstKeeper, cornerY * world.Width + 2);
        ChickenMovementRules.SetProcess(bird, ChickenProcess.Hatchery);
        const uint seed = 1;
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), [new ChickenMovementSystem()]);
        var random = new Dk2Lcg(((Dk2Lcg)sim.Rng.Algorithm).Seed);
        random.NextInt(4);
        random.NextInt(2560);
        random.NextInt(2560);

        sim.Tick();

        Assert.Equal(blocked ? 2 : cornerX, bird.MoveDestinationX >> 12);
        Assert.Equal(cornerY, bird.MoveDestinationY >> 12);
        Assert.Equal((byte)ChickenProcess.Moving, bird.ProcessState);
        Assert.Equal(random.Seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == HatcheryRules.PillarObjectId);
    }
}
