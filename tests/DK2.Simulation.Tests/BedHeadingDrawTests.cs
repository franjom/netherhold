// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class BedHeadingDrawTests
{
    private const uint CapturedSeed = 1515096229;

    [Theory]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId)]
    [InlineData(Dk2TerrainFixture.VampireCreatureId)]
    public void RuntimeBedCreationTakesOneHeadingDraw(byte creatureType)
    {
        var (context, creature, rng) = Setup(creatureType);

        ObjectState bed = LairRules.PlaceBed(context, creature, creature.Tile);

        // Native turn1617's bed1456: heading2005 from this preseed.
        Assert.Equal(2005, bed.Heading);
        Assert.Equal(1962582464U, rng.Seed);
        Assert.Equal(creature.Id, bed.CreatureId);
        Assert.Equal(creature.OwnerPlayerId, bed.OwnerPlayerId);
        Assert.Equal(context.World.TileIndex(creature.Tile), bed.TileIndex);
    }

    [Fact]
    public void AnObjectCreationFailureTakesNoHeadingDraw()
    {
        CreatureDef invalid = Dk2TerrainFixture.Goblin() with { CreatureId = 240, LairObjectId = 255 };
        var (context, creature, rng) = Setup(240, invalid);
        Assert.Null(context.Database.ObjectById(255));

        Assert.Throws<KeyNotFoundException>(() => LairRules.PlaceBed(context, creature, creature.Tile));

        Assert.Equal(CapturedSeed, rng.Seed);
    }

    [Fact]
    public void SyntheticBedPlacementKeepsItsExplicitSetupAdapter()
    {
        var (context, creature, rng) = Setup(Dk2TerrainFixture.GoblinCreatureId);

        ObjectState bed = LairRules.PlaceBed(context.World, context.Database, creature,
            context.World.TileIndex(creature.Tile));

        Assert.Equal(0, bed.Heading);
        Assert.Equal(CapturedSeed, rng.Seed);
    }

    private sealed class CaptureContext : ISimulationSystem
    {
        public string Name => "BedHeadingTest";
        public SimulationContext? Context { get; private set; }
        public void Execute(SimulationContext context) => Context = context;
    }

    private static (SimulationContext, CreatureState, Dk2Lcg) Setup(byte creatureType, CreatureDef? extra = null)
    {
        GameDatabase db = extra is null ? Dk2TerrainFixture.Database() : Dk2TerrainFixture.Database(extra);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        CreatureState creature = world.SpawnCreature(db, creatureType, PlayerIds.FirstKeeper, new TilePos(2, 2));
        var rng = new Dk2Lcg(CapturedSeed);
        var capture = new CaptureContext();
        new GameSimulation(db, world, new SimRandom(rng), [capture]).Tick();
        rng.Reseed(CapturedSeed);
        return (capture.Context!, creature, rng);
    }
}
