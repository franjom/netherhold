// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary><see cref="PortalChoiceRules"/>, read from <c>0x004DBF90</c> and <c>0x004DBD90</c>.
/// The names carry the doubt: the rules are STRONGLY INFERRED from the binary, and only the
/// Level 2 recording's arrival order checks them against play.</summary>
public class PortalChoiceTests
{
    private const int One = 0x1000;
    private const byte Keeper = 3;

    private static CreatureDef Creature(byte id, ushort roomId, byte lairObject = 0) =>
        Dk2TerrainFixture.Database().CreatureById(7) with
        {
            CreatureId = id, LairObjectId = lairObject,
            Attractions = [new CreatureAttraction(1, roomId, 0), new CreatureAttraction(0, 0, 0)],
        };

    private static WorldState World(long tick)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, 2, Keeper]);
        AdvanceTo(db, world, tick);
        return world;
    }

    /// <summary>The world's clock moves only through a tick, so tick a simulation with no systems.</summary>
    private static void AdvanceTo(GameDatabase db, WorldState world, long tick)
    {
        var sim = new GameSimulation(db, world, seed: 0, systems: []);
        while (world.CurrentTick.Value < tick) sim.Tick();
    }

    [Theory]
    [InlineData(PortalChoiceRules.Fresh, 0, 200)]  // a fresh boost is x200, not x10
    [InlineData(9 * One, 0, 9)]
    [InlineData(One / 2, 0, 1)]                    // under 1.0 counts as 1.0
    [InlineData(0, 2 * One, 1)]                    // a block of 2.0 halves: 2.0 / 2
    public void TheBoostMultipliesAndTheBlockDivides_BinaryInferred(int boost, int block, int timesOne)
    {
        int weighted = PortalChoiceRules.Weighted(2 * One, new EntranceInfoState(5, boost, block));
        Assert.Equal(timesOne * (block == 0 ? 2 * One : One), weighted);
    }

    [Fact]
    public void AFreshBlockZeroesTheWeight_BinaryInferred() =>
        Assert.Equal(0, PortalChoiceRules.Weighted(3 * One, new EntranceInfoState(5, PortalChoiceRules.Fresh, PortalChoiceRules.Fresh)));

    [Fact]
    public void ALairTypeWeighsOne_AndTheGoblinsLairThree_WhateverIsHeld()
    {
        WorldState world = World(10);
        Assert.Equal(3 * One, PortalChoiceRules.BaseWeight(world, Creature(7, (ushort)RoomTypeId.Lair, 0x16), [], Keeper));
        Assert.Equal(One, PortalChoiceRules.BaseWeight(world, Creature(8, (ushort)RoomTypeId.Lair, 26), [], Keeper));
    }

    [Fact]
    public void ATypeNoticedAfterTickOneIsBoosted_UnlessItIsLairAttracted_BinaryInferred()
    {
        WorldState world = World(2);
        PortalChoiceRules.Notice(world, Keeper,
            [Creature(5, (ushort)RoomTypeId.Library), Creature(7, (ushort)RoomTypeId.Lair, 0x16)]);

        Assert.Equal(
            [new EntranceInfoState(5, PortalChoiceRules.Fresh, 0), new EntranceInfoState(7, 0, 0)],
            world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void ATypeAlreadyThereAtTheStartIsNotBoosted_AndARowIsNeverRenoticed()
    {
        WorldState world = World(1);
        PortalChoiceRules.Notice(world, Keeper, [Creature(5, (ushort)RoomTypeId.Library)]);
        AdvanceTo(Dk2TerrainFixture.Database(), world, 500);
        PortalChoiceRules.Notice(world, Keeper, [Creature(5, (ushort)RoomTypeId.Library)]);

        Assert.Equal([new EntranceInfoState(5, 0, 0)], world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void APickTakesOneOffEveryBoost_AndTheOnlyWeightedTypeIsChosen()
    {
        WorldState world = World(2);
        CreatureDef goblin = Creature(7, (ushort)RoomTypeId.Lair, 0x16);
        world.Player(Keeper).EntranceInfo.Add(new EntranceInfoState(5, 4 * One, 2 * One));

        CreatureDef? chosen = PortalChoiceRules.Pick(world, new SimRandom(1), Keeper, [goblin], []);

        Assert.Same(goblin, chosen);
        Assert.Equal(
            [new EntranceInfoState(5, 3 * One, One), new EntranceInfoState(7, 0, 0)],
            world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void NoWeightedTypeMeansNoArrival_AndNothingDecays()
    {
        WorldState world = World(2);
        world.Player(Keeper).EntranceInfo.Add(new EntranceInfoState(9, 4 * One, 0));

        Assert.Null(PortalChoiceRules.Pick(world, new SimRandom(1), Keeper, [], []));
        Assert.Equal([new EntranceInfoState(9, 4 * One, 0)], world.Player(Keeper).EntranceInfo);
    }
}
