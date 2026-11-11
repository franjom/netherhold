// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>What a keeper learns when an enemy dies under torture (<c>0x004F2370</c>, ADR-0230,
/// <c>torture-information.md</c>).</summary>
public sealed class TortureInformationTests
{
    private const byte Keeper = 4;
    private const byte Rival = 5;

    private static WorldState World(GameDatabase db) =>
        WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);

    [Theory]
    [InlineData(JobClass.Artisan, new[] { 3, 7, 10, 11 })]
    [InlineData(JobClass.Thinker, new[] { 4, 9, 10, 11 })]
    [InlineData(JobClass.Fighter, new[] { 5, 8, 10, 11 })]
    [InlineData(JobClass.Scout, new[] { 10, 11 })]
    public void TheVictimsJobClassNamesTheCandidates_ThenTen_ThenTheReveal(JobClass job, int[] expected)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        CreatureDef victim = Dk2TerrainFixture.Goblin() with { JobClass = job };

        Assert.Equal(expected, TortureInformationRules.Candidates(world, victim, Rival, Keeper));
    }

    [Fact]
    public void AHeroGivesOnlyTheReveal()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);

        Assert.Equal([TortureInformationRules.Reveal], TortureInformationRules.Candidates(world, Dk2TerrainFixture.Goblin(), PlayerIds.Good, Keeper));
    }

    /// <summary>Each draw is one of the candidates, and a reward of 3–10 is kept: drawn again and
    /// again, a Fighter's victims give 5, 8 and 10 once each and then only the reveal.</summary>
    [Fact]
    public void EachRewardIsGivenOnce_ThenOnlyTheRevealIsLeft()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        CreatureDef fighter = Dk2TerrainFixture.Goblin() with { JobClass = JobClass.Fighter };
        var rng = new SimRandom(3);

        var drawn = new List<int>();
        for (int i = 0; i < 40; i++) drawn.Add(TortureInformationRules.Give(world, rng, fighter, Rival, Keeper));

        Assert.Equal(1, drawn.Count(r => r == 5));
        Assert.Equal(1, drawn.Count(r => r == 8));
        Assert.Equal(1, drawn.Count(r => r == 10));
        Assert.All(drawn, r => Assert.Contains(r, new[] { 5, 8, 10, TortureInformationRules.Reveal }));
        Assert.Equal([TortureInformationRules.Reveal], TortureInformationRules.Candidates(world, fighter, Rival, Keeper));
        Assert.False(TortureInformationRules.Given(world, TortureInformationRules.Reveal, Keeper, Rival));
    }

    /// <summary>A reward once given about a keeper is not a candidate again for that keeper, and
    /// still is about another.</summary>
    [Fact]
    public void AGivenRewardDropsOut_PerPairOfKeepers()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        CreatureDef fighter = Dk2TerrainFixture.Goblin() with { JobClass = JobClass.Fighter };
        world.Player(Keeper).InformationGiven = (1UL << (((5 - 3) * 8) + Rival)) | (1UL << (((10 - 3) * 8) + Rival));

        Assert.Equal([8, 11], TortureInformationRules.Candidates(world, fighter, Rival, Keeper));
        Assert.True(TortureInformationRules.Given(world, 5, Keeper, Rival));
        Assert.False(TortureInformationRules.Given(world, 5, Keeper, 6));
        Assert.True(TortureInformationRules.Given(world, 5, Keeper, Keeper));   // about oneself, always

        // The reveal is never kept: its flag would be bit (11 - 3) * 8, which a 64-bit shift
        // wraps onto reward 3's.
        world.Player(Keeper).InformationGiven |= 1UL << (((3 - 3) * 8) + Rival);
        Assert.False(TortureInformationRules.Given(world, TortureInformationRules.Reveal, Keeper, Rival));
    }
}

/// <summary>Reward 11, the reveal, and the messages every reward posts (<c>0x004F2370</c>).</summary>
public sealed class TortureRevealTests
{
    private const byte Keeper = 4;
    private const byte Rival = 5;

    // Two of the rival's rooms, A made before B: DK2's list holds B first.
    private static readonly TilePos[] RoomA = [new(2, 2), new(3, 2), new(2, 3), new(3, 3)];
    private static readonly TilePos[] RoomB = [new(8, 2), new(9, 2), new(8, 3), new(9, 3)];

    private static (GameDatabase Db, WorldState World) Setup(bool fog = true)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival], fogOfWarEnabled: fog);
        Place(db, world, RoomA, 1);
        Place(db, world, RoomB, 2);
        return (db, world);
    }

    private static void Place(GameDatabase db, WorldState world, TilePos[] tiles, long position)
    {
        foreach (TilePos at in tiles)
        {
            RoomRules.Place(db, ref world.Tile(at.X, at.Y), db.Room(RoomTypeId.Treasury), Rival);
            world.Tile(at.X, at.Y).RoomListPosition = new RoomListPosition(position);
            world.Tile(at.X, at.Y).ClearExploredBy(Keeper);
        }
    }

    private static bool Explored(WorldState world, TilePos[] tiles) =>
        tiles.All(t => world.Tile(t.X, t.Y).IsExploredBy(Keeper));

    [Fact]
    public void TheNewestUnexploredRoomIsExploredWhole()
    {
        (GameDatabase db, WorldState world) = Setup();

        Assert.Equal(RevealResult.Room, TortureInformationRules.RevealTo(world, db, Rival, Keeper));

        Assert.True(Explored(world, RoomB));
        Assert.DoesNotContain(RoomA, t => world.Tile(t.X, t.Y).IsExploredBy(Keeper));
    }

    [Fact]
    public void ARoomWhoseFirstSlabIsExploredIsPassedOver()
    {
        (GameDatabase db, WorldState world) = Setup();
        RoomInstance b = RoomLists.Of(world, db, Rival)[0];
        world.TileAtIndex(SlabChain.FirstSlab(world, b)).SetExploredBy(Keeper);

        Assert.Equal(RevealResult.Room, TortureInformationRules.RevealTo(world, db, Rival, Keeper));

        Assert.True(Explored(world, RoomA));
    }

    [Fact]
    public void WithEveryRoomExploredTheFirstCreatureOnUnexploredGroundIsTagged()
    {
        (GameDatabase db, WorldState world) = Setup();
        foreach (TilePos t in RoomA.Concat(RoomB)) world.Tile(t.X, t.Y).SetExploredBy(Keeper);
        CreatureState seen = world.SpawnCreature(db, 7, Rival, new TilePos(12, 6));
        world.Tile(12, 6).SetExploredBy(Keeper);
        CreatureState hidden = world.SpawnCreature(db, 7, Rival, new TilePos(14, 6));
        world.Tile(14, 6).ClearExploredBy(Keeper);

        Assert.Equal(RevealResult.Creature, TortureInformationRules.RevealTo(world, db, Rival, Keeper));

        Assert.Equal(TortureInformationRules.SightPasses, hidden.TortureSightPasses);
        Assert.Equal(Keeper, hidden.TortureSightPlayerId);
        Assert.Equal(0, seen.TortureSightPasses);
    }

    [Fact]
    public void WithFogOffNothingIsRevealed()
    {
        (GameDatabase db, WorldState world) = Setup(fog: false);

        Assert.Equal(RevealResult.None, TortureInformationRules.RevealTo(world, db, Rival, Keeper));
        Assert.False(Explored(world, RoomB));
    }

    [Fact]
    public void EveryRewardPostsNinetySeven_ThenFiftyNineForTheReveal()
    {
        (GameDatabase db, WorldState world) = Setup();

        (int reward, _) = TortureInformationRules.Award(world, db, new SimRandom(1), Dk2TerrainFixture.Goblin(), PlayerIds.Good, Keeper);

        Assert.Equal(TortureInformationRules.Reveal, reward);
        Assert.Equal([PlayerMessageKind.InformationLearned, PlayerMessageKind.InformationReveal],
            world.Messages.Entries.Where(m => m.PlayerId == Keeper).Select(m => m.Kind));
        Assert.All(world.Messages.Entries, m => Assert.Equal((uint)SimulationTiming.TicksFromSeconds(30), m.Timeout));
    }

    [Fact]
    public void ARowRewardPostsNinetySix()
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureDef scout = Dk2TerrainFixture.Goblin() with { JobClass = JobClass.Scout };

        // A Scout's candidates are 10 and 11; draw until 10 comes.
        var rng = new SimRandom(1);
        int reward = 0;
        for (int i = 0; i < 50 && reward != 10; i++)
            reward = TortureInformationRules.Award(world, db, rng, scout, Rival, Keeper).Reward;

        Assert.Equal(10, reward);
        Assert.Contains(world.Messages.Entries, m => m.PlayerId == Keeper && m.Kind == PlayerMessageKind.InformationUnlocked);
    }

    [Fact]
    public void ATagNamingTheCreaturesOwnKeeperSpendsNothing_AndSpentPassesStop()
    {
        (GameDatabase db, WorldState world) = Setup();
        for (int y = 4; y <= 8; y++)
            for (int x = 10; x <= 14; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        CreatureState own = world.SpawnCreature(db, 7, Rival, new TilePos(11, 6));
        own.TortureSightPasses = 3;
        own.TortureSightPlayerId = Rival;
        CreatureState spent = world.SpawnCreature(db, 7, Rival, new TilePos(11, 5));
        spent.TortureSightPasses = 1;
        spent.TortureSightPlayerId = Keeper;
        spent.TortureSightTileIndex = world.TileIndex(spent.Tile);
        var sim = new GameSimulation(db, world, seed: 1, [new Systems.FogOfWarSystem()]);

        (own.PosX, own.PosY) = (CreatureState.CentreOf(12), CreatureState.CentreOf(6));
        (spent.PosX, spent.PosY) = (CreatureState.CentreOf(12), CreatureState.CentreOf(5));
        sim.Tick();
        Assert.Equal(3, own.TortureSightPasses);
        Assert.Equal(0, spent.TortureSightPasses);

        for (int x = 10; x <= 14; x++) world.Tile(x, 4).ClearExploredBy(Keeper);
        (spent.PosX, spent.PosY) = (CreatureState.CentreOf(13), CreatureState.CentreOf(5));
        sim.Tick();
        Assert.False(world.Tile(14, 4).IsExploredBy(Keeper));
    }

    /// <summary><c>0x0048CC90</c>: a tagged creature spends a pass on each tile it moves onto and
    /// explores the 3 x 3 round it for the chamber's keeper.</summary>
    [Fact]
    public void ATaggedCreatureExploresRoundItselfForTheTorturerAsItMoves()
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureState spy = world.SpawnCreature(db, 7, Rival, new TilePos(11, 6));
        spy.TortureSightPasses = TortureInformationRules.SightPasses;
        spy.TortureSightPlayerId = Keeper;
        spy.TortureSightTileIndex = world.TileIndex(spy.Tile);
        for (int y = 4; y <= 8; y++)
            for (int x = 10; x <= 14; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).ClearExploredBy(Keeper);
            }
        var sim = new GameSimulation(db, world, seed: 1, [new Systems.FogOfWarSystem()]);

        sim.Tick();
        Assert.Equal(TortureInformationRules.SightPasses, spy.TortureSightPasses);
        Assert.False(world.Tile(13, 6).IsExploredBy(Keeper));

        (spy.PosX, spy.PosY) = (CreatureState.CentreOf(12), CreatureState.CentreOf(6));
        sim.Tick();

        Assert.Equal(TortureInformationRules.SightPasses - 1, spy.TortureSightPasses);
        Assert.True(world.Tile(13, 6).IsExploredBy(Keeper));
        Assert.True(world.Tile(13, 7).IsExploredBy(Keeper));
        Assert.False(world.Tile(14, 6).IsExploredBy(Keeper));
    }
}
