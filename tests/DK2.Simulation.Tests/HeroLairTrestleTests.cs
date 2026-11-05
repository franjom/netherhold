// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The level start's sweep over Hero Lair ground (<c>0x0050C4B5</c>, ADR-0673): every interior tile of terrain 35
/// with no object 0x71 beside it draws <c>random(4)</c>; on a zero the object is made and its heading drawn,
/// <c>random(0x800)</c>. Paired runs compare the world seed with and without the terrain.</summary>
public class HeroLairTrestleTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Lair = HeroLairRules.HeroLairTerrainId;

    /// <summary>The shipped data's terrain 35 and object 0x71, as copies of the fixture's dirt path and a bed.</summary>
    private static GameDatabase Db()
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        TerrainDef lair = fixture.TerrainById(Dk2TerrainFixture.DirtPath) with { TerrainId = Lair, Name = "Hero Lair" };
        ObjectDef trestle = fixture.ObjectById(Dk2TerrainFixture.GoblinBedObjectId)! with
        {
            ObjectId = HeroLairRules.TrestleObjectId, Name = "Hero Armoury Trestle",
        };
        return new GameDatabase(fixture.Terrain.Values.Append(lair), fixture.Rooms.Values, fixture.Creatures.Values,
            fixture.Variables, objects: fixture.Objects.Values.Append(trestle));
    }

    /// <summary>A lair patch of <paramref name="width"/> by <paramref name="height"/> tiles at (8, 2) on the 16 by 12 map.</summary>
    private static WorldState World(GameDatabase db, int width, int height)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, Keeper]);
        for (int y = 2; y < 2 + height; y++)
            for (int x = 8; x < 8 + width; x++)
                world.Tile(x, y).TerrainId = Lair;
        return world;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, uint seed) =>
        new(db, world, new SimRandom(new Dk2Lcg(seed)), []);

    private static uint Seed(GameSimulation sim) => ((Dk2Lcg)sim.Rng.Algorithm).Seed;

    /// <summary>The seed after the given number of <c>random(4)</c> draws.</summary>
    private static uint After(uint seed, int draws)
    {
        var lcg = new Dk2Lcg(seed);
        for (int i = 0; i < draws; i++) lcg.NextInt(4);
        return lcg.Seed;
    }

    /// <summary>The first seed whose first <c>random(4)</c> is zero, or is not.</summary>
    private static uint SeedWhereFirstDrawIsZero(bool zero)
    {
        for (uint seed = 1; ; seed++)
            if ((new Dk2Lcg(seed).NextInt(4) == 0) == zero) return seed;
    }

    private static List<ObjectState> Trestles(WorldState world) =>
        [.. world.Objects.Where(o => o.ObjectTypeId == HeroLairRules.TrestleObjectId)];

    [Fact]
    public void WithoutLairGroundTheLevelStartDrawsNothing()
    {
        GameDatabase db = Db();
        GameSimulation sim = Simulation(db, World(db, 0, 0), 9);
        Assert.Equal(new Dk2Lcg(9).Seed, Seed(sim));
        Assert.Empty(Trestles(sim.World));
    }

    [Fact]
    public void ARingOfLairWithoutAnInteriorDrawsNothing()
    {
        GameDatabase db = Db();
        GameSimulation sim = Simulation(db, World(db, 2, 5), 9);
        Assert.Equal(new Dk2Lcg(9).Seed, Seed(sim));
    }

    [Fact]
    public void AnInteriorTileDrawsOnceAndALoneNonZeroDrawMakesNothing()
    {
        GameDatabase db = Db();
        uint seed = SeedWhereFirstDrawIsZero(zero: false);
        GameSimulation sim = Simulation(db, World(db, 3, 3), seed);
        Assert.Equal(After(seed, 1), Seed(sim));
        Assert.Empty(Trestles(sim.World));
    }

    [Fact]
    public void AZeroDrawMakesTheObjectAndDrawsItsHeadingFromTheSameStream()
    {
        GameDatabase db = Db();
        uint seed = SeedWhereFirstDrawIsZero(zero: true);
        GameSimulation sim = Simulation(db, World(db, 3, 3), seed);

        var lcg = new Dk2Lcg(seed);
        lcg.NextInt(4);
        int heading = lcg.NextInt(0x800);
        ObjectState trestle = Assert.Single(Trestles(sim.World));
        Assert.Equal((3 * sim.World.Width) + 9, trestle.TileIndex);
        Assert.Equal(heading & 0x7FF, trestle.Heading);
        Assert.Equal(PlayerIds.Good, trestle.OwnerPlayerId);
        Assert.Equal(lcg.Seed, Seed(sim));
    }

    [Fact]
    public void AnObjectBesideTheTileStopsItsDraw()
    {
        GameDatabase db = Db();
        uint seed = SeedWhereFirstDrawIsZero(zero: true);
        WorldState world = World(db, 3, 3);
        world.SpawnObject(db, HeroLairRules.TrestleObjectId, new PlayerId(PlayerIds.Good), (2 * world.Width) + 8); // the patch's corner
        GameSimulation sim = Simulation(db, world, seed);
        Assert.Equal(new Dk2Lcg(seed).Seed, Seed(sim));
        Assert.Single(Trestles(world));
    }

    [Fact]
    public void AnObjectTwoTilesAwayDoesNotStopTheDraw()
    {
        GameDatabase db = Db();
        uint seed = SeedWhereFirstDrawIsZero(zero: false);
        WorldState world = World(db, 3, 3);
        world.SpawnObject(db, HeroLairRules.TrestleObjectId, new PlayerId(PlayerIds.Good), (2 * world.Width) + 12);
        GameSimulation sim = Simulation(db, world, seed);
        Assert.Equal(After(seed, 1), Seed(sim));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATrestleMadeEarlierInTheSweepStopsTheNeighbourBehindIt(bool thirdDrawIsZero)
    {
        // A 5 by 3 patch has interior tiles (9, 3), (10, 3) and (11, 3) in scan order. A zero on the first makes an
        // object (and draws its heading) that stops the second without a draw; the third is two tiles away and draws.
        GameDatabase db = Db();
        uint seed = 1;
        while (!FirstAndThirdDraws(seed, thirdDrawIsZero)) seed++;
        GameSimulation sim = Simulation(db, World(db, 5, 3), seed);

        var lcg = new Dk2Lcg(seed);
        lcg.NextInt(4);
        lcg.NextInt(0x800);
        lcg.NextInt(4);
        if (thirdDrawIsZero) lcg.NextInt(0x800);
        Assert.Equal(lcg.Seed, Seed(sim));
        Assert.Equal(thirdDrawIsZero ? [(3 * sim.World.Width) + 9, (3 * sim.World.Width) + 11] : [(3 * sim.World.Width) + 9],
            Trestles(sim.World).Select(t => t.TileIndex));
    }

    private static bool FirstAndThirdDraws(uint seed, bool thirdIsZero)
    {
        var lcg = new Dk2Lcg(seed);
        if (lcg.NextInt(4) != 0) return false;
        lcg.NextInt(0x800);
        return (lcg.NextInt(4) == 0) == thirdIsZero;
    }

    [Fact]
    public void WithoutTheObjectDefinitionTheTileStillDrawsButMakesNothingAndDrawsNoHeading()
    {
        // Object_Create (0x004B7A90) refuses an unknown type after the sweep's first draw, so the heading is not drawn.
        GameDatabase fixture = Dk2TerrainFixture.Database();
        TerrainDef lair = fixture.TerrainById(Dk2TerrainFixture.DirtPath) with { TerrainId = Lair, Name = "Hero Lair" };
        var db = new GameDatabase(fixture.Terrain.Values.Append(lair), fixture.Rooms.Values, fixture.Creatures.Values,
            fixture.Variables, objects: fixture.Objects.Values);
        uint seed = SeedWhereFirstDrawIsZero(zero: true);
        GameSimulation sim = Simulation(db, World(db, 3, 3), seed);
        Assert.Equal(After(seed, 1), Seed(sim));
        Assert.Empty(Trestles(sim.World));
    }

    private static void PaintHatchery(GameDatabase db, WorldState world)
    {
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 5; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Hatchery), Keeper);
    }

    [Fact]
    public void TheSweepFollowsTheHatcheryCoopsOfTheSameLevelStart()
    {
        // The coops' draws (ADR-0672, 0x0050BC9D) come first, the sweep (0x0050C4B5) after. Count the coops on a world
        // without lair ground, then pick a seed whose first draw is not zero (a sweep run first would make nothing)
        // and whose draw after the coops' is.
        GameDatabase db = Db();
        WorldState bare = World(db, 0, 0);
        PaintHatchery(db, bare);
        int coops = Simulation(db, bare, 1).World.Objects.Count(o => o.ObjectTypeId == HatcheryRules.CoopObjectId);
        Assert.True(coops > 0);

        uint seed = 1;
        while (new Dk2Lcg(seed).NextInt(4) == 0 || !SweepZeroAfter(seed, coops)) seed++;
        WorldState world = World(db, 3, 3);
        PaintHatchery(db, world);
        GameSimulation sim = Simulation(db, world, seed);

        Assert.Equal(coops, world.Objects.Count(o => o.ObjectTypeId == HatcheryRules.CoopObjectId));
        ObjectState trestle = Assert.Single(Trestles(world));
        var lcg = new Dk2Lcg(seed);
        for (int i = 0; i <= coops; i++) lcg.NextInt(4);
        Assert.Equal(lcg.NextInt(0x800) & 0x7FF, trestle.Heading);
        Assert.Equal(lcg.Seed, Seed(sim));
    }

    private static bool SweepZeroAfter(uint seed, int earlierDraws)
    {
        var lcg = new Dk2Lcg(seed);
        for (int i = 0; i < earlierDraws; i++) lcg.NextInt(4);
        return lcg.NextInt(4) == 0;
    }
}
