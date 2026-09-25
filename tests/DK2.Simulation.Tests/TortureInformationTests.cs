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
