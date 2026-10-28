// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Allies end games together in skirmish and multiplayer only (<c>0x004BE630</c>,
/// win-and-defeat.md §7, ADR-0547). Players are activated 0..6, so the active list runs 6, 5, 4, 3.</summary>
public sealed class VerdictRulesTests
{
    private const byte A = 3, B = 4, C = 5, D = 6;
    private static readonly GameDatabase Database = Dk2TerrainFixture.Database();

    private static WorldState World(GameMode mode, params (byte, byte)[] pacts)
    {
        WorldState world = WorldState.FromMap(Database, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, A, B, C, D], gameMode: mode);
        foreach ((byte one, byte other) in pacts) world.SetAllied(one, other, allied: true);
        return world;
    }

    private static byte[] Statuses(WorldState world) =>
        [world.Player(A).Status, world.Player(B).Status, world.Player(C).Status, world.Player(D).Status];

    [Fact]
    public void InTheCampaignAVerdictTouchesNobodyElse()
    {
        WorldState world = World(GameMode.Campaign, (B, C), (B, D), (C, D));

        VerdictRules.End(world, Database, A, PlayerStatus.Lost);
        VerdictRules.End(world, Database, B, PlayerStatus.Won);

        Assert.Equal([PlayerStatus.Lost, PlayerStatus.Won, PlayerStatus.Playing, PlayerStatus.Playing], Statuses(world));
    }

    /// <summary>A's win ends C, then B (0x004BEFE1). B's loss is the one that finds only A and its
    /// ally D left, and makes D win too (0x004BF172): the ally's win comes through the recursion.</summary>
    [Theory]
    [InlineData(GameMode.Skirmish)]
    [InlineData(GameMode.Multiplayer)]
    public void AWinnerEndsEveryKeeperThatIsNotItsMutualAlly_AndTheLastLossMakesTheAllyWin(GameMode mode)
    {
        WorldState world = World(mode, (A, D));

        VerdictRules.End(world, Database, A, PlayerStatus.Won);

        Assert.Equal([PlayerStatus.Won, PlayerStatus.Lost, PlayerStatus.Lost, PlayerStatus.Won], Statuses(world));
    }

    [Fact]
    public void NonAlliesOfTheWinnerLoseEvenWhenAlliedToEachOther()
    {
        WorldState world = World(GameMode.Skirmish, (A, D), (B, C));

        VerdictRules.End(world, Database, A, PlayerStatus.Won);

        // C's loss leaves D, B and the winner A: B is no ally of A, the pivot, so nobody else wins;
        // then B's loss leaves A and D, and D wins.
        Assert.Equal([PlayerStatus.Won, PlayerStatus.Lost, PlayerStatus.Lost, PlayerStatus.Won], Statuses(world));
    }

    [Fact]
    public void WhenEveryoneLeftIsAnAllyOfTheLastOneMet_TheyAllWin()
    {
        WorldState world = World(GameMode.Skirmish, (B, C), (B, D), (C, D));

        VerdictRules.End(world, Database, A, PlayerStatus.Conquered);

        Assert.Equal([PlayerStatus.Conquered, PlayerStatus.Won, PlayerStatus.Won, PlayerStatus.Won], Statuses(world));
    }

    [Fact]
    public void WhenOneLeftIsNoAllyOfTheLastOneMet_NobodyWins()
    {
        WorldState world = World(GameMode.Skirmish, (B, C));

        VerdictRules.End(world, Database, A, PlayerStatus.Lost);

        Assert.Equal([PlayerStatus.Lost, PlayerStatus.Playing, PlayerStatus.Playing, PlayerStatus.Playing], Statuses(world));
    }

    /// <summary>The pivot is the last keeper met, B. C and D are each B's ally but not each other's:
    /// D comes first in the list and wins with B, and D's win makes C, no ally of D's, lose.</summary>
    [Fact]
    public void TwoAlliesOfThePivotThatAreNotAlliesOfEachOther_TheFirstWinsAndTheOtherLoses()
    {
        WorldState world = World(GameMode.Skirmish, (B, C), (B, D));

        VerdictRules.End(world, Database, A, PlayerStatus.Lost);

        Assert.Equal([PlayerStatus.Lost, PlayerStatus.Won, PlayerStatus.Lost, PlayerStatus.Won], Statuses(world));
    }

    [Fact]
    public void AnAllianceMustBeMutual()
    {
        WorldState world = World(GameMode.Skirmish, (B, C), (B, D), (C, D));
        world.Player(D).AlliedWith = 0;

        VerdictRules.End(world, Database, A, PlayerStatus.Lost);

        Assert.Equal([PlayerStatus.Lost, PlayerStatus.Playing, PlayerStatus.Playing, PlayerStatus.Playing], Statuses(world));
    }

    /// <summary><c>0x0050A67A</c>: when the limit runs out every keeper loses, and with bit 3 set the
    /// losses carry no survivor's win (<c>0x00509740</c>).</summary>
    [Theory]
    [InlineData(GameMode.Campaign)]
    [InlineData(GameMode.Skirmish)]
    public void ARunOutTimeLimitEndsEveryKeeperOnlyOutsideTheCampaign(GameMode mode)
    {
        WorldState world = World(mode, (B, C), (B, D), (C, D));
        world.Triggers.LevelTimeLimit = 1;
        var sim = new GameSimulation(Dk2TerrainFixture.Database(), world, 1, []);

        sim.Tick();

        byte expected = mode == GameMode.Campaign ? PlayerStatus.Playing : PlayerStatus.Lost;
        Assert.True(world.Triggers.LevelTimeUp);
        Assert.Equal([expected, expected, expected, expected], Statuses(world));
    }
}
