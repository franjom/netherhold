// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The level's VARIABLES id-66 records ally pairs of players at load (<c>0x0051DC90</c>,
/// alliances.md §2, ADR-0546).</summary>
public sealed class LevelAllianceTests
{
    private const byte Keeper = PlayerIds.FirstKeeper, Rival = Keeper + 1;

    private static WorldState Load(params AllianceEntry[] records)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        database.Variables.Alliances.AddRange(records);
        return WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
    }

    [Fact]
    public void TheShippedPatternAlliesNeutralWithEveryone_AndNobodyElse()
    {
        WorldState world = Load(new(2, 1), new(3, 2), new(4, 2));

        Assert.True(world.AreAllied(PlayerIds.Neutral, PlayerIds.Good));
        Assert.True(world.AreAllied(Keeper, PlayerIds.Neutral));
        Assert.True(world.AreAllied(PlayerIds.Neutral, Rival));
        Assert.False(world.AreAllied(Keeper, Rival));
        Assert.False(world.AreAllied(Keeper, PlayerIds.Good));
    }

    [Fact]
    public void ARecordNamingAPlayerTheLevelLacks_OrOutsideTheArray_IsSkipped()
    {
        WorldState world = Load(new(Keeper, 6), new(Keeper, 9), new(0, Keeper));

        for (int i = 0; i < world.PlayerCount; i++)
            Assert.Equal(0, world.PlayerAtIndex(i).AlliedWith);
    }

    [Fact]
    public void AnAllianceSurvivesASaveAndLoad()
    {
        WorldState world = Load(new AllianceEntry(Keeper, Rival));
        var original = new GameSimulation(Dk2TerrainFixture.Database(), world, 1, Dk2TerrainFixture.DefaultSystems());

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), Dk2TerrainFixture.Database());

        Assert.True(restored.World.AreAllied(Keeper, Rival));
        Assert.Equal(original.StateHash(), restored.StateHash());
    }
}
