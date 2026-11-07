// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class ChickenBirthTests
{
    private static readonly PlayerId Keeper = new(PlayerIds.FirstKeeper);
    private sealed class RecordingRandom : IRandomAlgorithm
    {
        public List<int> Bounds { get; } = [];
        public int NextInt(int exclusiveMax) { Bounds.Add(exclusiveMax); return Bounds.Count == 1 ? 393 : 777; }
        public void HashInto(ref Hash64 hash) => hash.Add(Bounds.Count);
    }

    private static GameSimulation Start(SimRandom rng, bool animations = false)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        if (animations)
        {
            var art = new CreatureAnimationTiming[9];
            art[0] = new() { ResourceType = 6, FrameCount = 45, FramesPerSecond = 30 };
            art[5] = new() { ResourceType = 6, FrameCount = 99, FramesPerSecond = 1 };
            db = new GameDatabase(db.Terrain.Values, db.Rooms.Values, db.Creatures.Values, db.Variables,
                objects: db.Objects.Values.Select(o => o.ObjectId == HatcheryRules.EggObjectId ? o with { ArtSlots = art } : o),
                keeperSpells: db.KeeperSpells.Values);
        }
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [Keeper.Value]);
        return new GameSimulation(db, world, rng, animations ? [new HatcherySystem(lays: false)] : []);
    }

    private static ObjectState Egg(GameSimulation sim) => sim.World.SpawnObject(sim.Database,
        HatcheryRules.EggObjectId, Keeper, sim.World.TileIndex(new TilePos(2, 2)));

    [Fact]
    public void LiveChickenCreationConsumesTheWorldSizeDraw()
    {
        var random = new RecordingRandom();
        GameSimulation sim = Start(new SimRandom(random));
        ObjectState chicken = sim.World.SpawnObject(sim.Database, HatcheryRules.ChickenObjectId, Keeper, 0);
        Assert.Equal([2048], random.Bounds);
        Assert.Equal(new Fp32(4489), chicken.ChickenScale);
    }

    [Theory]
    [InlineData(HatcheryRules.EggObjectId)]
    [InlineData(HatcheryRules.CoopObjectId)]
    public void OtherObjectsDoNotConsumeAChickenSizeDraw(byte type)
    {
        var random = new RecordingRandom();
        GameSimulation sim = Start(new SimRandom(random));
        sim.World.SpawnObject(sim.Database, type, Keeper, 0);
        Assert.Empty(random.Bounds);
    }

    [Fact]
    public void OffHatcheryBirthDrawsSizeBeforeHeading()
    {
        var random = new RecordingRandom();
        GameSimulation sim = Start(new SimRandom(random));
        ObjectState chicken = HatcheryRules.Hatch(sim.World, sim.Database, Egg(sim));
        Assert.Equal([2048, 2048], random.Bounds);
        Assert.Equal(777, chicken.Heading);
        Assert.Equal(new Fp32(4489), chicken.ChickenScale);
    }

    [Fact]
    public void StoppingALaunchedEggKeepsItsExistingAnimationStamp()
    {
        GameSimulation sim = Start(new SimRandom(new Dk2Lcg(7)), animations: true);
        sim.Run(10);
        ObjectState egg = Egg(sim);
        egg.ProcessState = EggLaunchRules.LaunchProcess;
        egg.ProcessAgeTicks = 20;
        egg.ProcessDeadline = 9;
        sim.Tick();
        Assert.Equal(32, egg.ProcessState);
        Assert.Equal(0, egg.ProcessAgeTicks);
        Assert.Equal(9, egg.ProcessDeadline);
    }

    [Fact]
    public void HatchPollingUsesTheArtStampAndKeepsTheOneTurnEndArt()
    {
        GameSimulation sim = Start(new SimRandom(new Dk2Lcg(7)), animations: true);
        sim.Run(10);
        ObjectState egg = Egg(sim);
        egg.ProcessState = 32;
        egg.ProcessAgeTicks = 5;
        egg.ProcessDeadline = 3;
        sim.Tick();
        Assert.Contains(egg, sim.World.Objects);
        Assert.Equal(1, egg.ProcessCount);
        sim.Tick();
        Assert.DoesNotContain(egg, sim.World.Objects);
        Assert.Single(sim.World.Objects, o => o.ObjectTypeId == HatcheryRules.ChickenObjectId);
    }

    [Fact]
    public void OwnedHatcheryBirthHasTheSizeDrawAndNoHeadingDraw()
    {
        var random = new RecordingRandom();
        GameSimulation sim = Start(new SimRandom(random));
        ref TileState tile = ref sim.World.Tile(2, 2);
        RoomRules.Place(sim.Database, ref tile, sim.Database.Room(RoomTypeId.Hatchery), Keeper);
        ObjectState chicken = HatcheryRules.Hatch(sim.World, sim.Database, Egg(sim));
        Assert.Equal([2048], random.Bounds);
        Assert.Equal(0, chicken.Heading);
        Assert.Equal((byte)ChickenProcess.Hatchery, chicken.ProcessState);
    }

    [Fact]
    public void AnOlderProcessCounterDoesNotCompleteANewerAnimation()
    {
        GameSimulation sim = Start(new SimRandom(new Dk2Lcg(7)), animations: true);
        sim.Run(10);
        ObjectState egg = Egg(sim);
        egg.ProcessState = 32;
        egg.ProcessAgeTicks = 100;
        egg.ProcessDeadline = 5;
        sim.Tick();
        Assert.Equal(0, egg.ProcessCount);
        Assert.Contains(egg, sim.World.Objects);
    }

    [Fact]
    public void RetainedSizeChangesTheHashAndSurvivesSaveAndCopy()
    {
        GameSimulation sim = Start(new SimRandom(new Dk2Lcg(2308691482)));
        ObjectState chicken = sim.World.SpawnObject(sim.Database, HatcheryRules.ChickenObjectId, Keeper, 0);
        Assert.Equal(new Fp32(4489), chicken.ChickenScale);
        ulong before = sim.StateHash();
        chicken.ChickenScale = new Fp32(4490);
        Assert.NotEqual(before, sim.StateHash());
        GameSimulation loaded = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, []);
        Assert.Equal(chicken.ChickenScale, Assert.Single(loaded.World.Objects).ChickenScale);
        Assert.Equal(chicken.ChickenScale, Assert.Single(WorldState.CopyOf(sim.World).Objects).ChickenScale);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        sim.World.SpawnObject(sim.Database, HatcheryRules.ChickenObjectId, Keeper, 0);
        loaded.World.SpawnObject(loaded.Database, HatcheryRules.ChickenObjectId, Keeper, 0);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }
}
