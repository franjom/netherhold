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
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, 2, Keeper]);
        AdvanceTo(db, world, tick);
        return world;
    }

    /// <summary>A 3x3 Prison of the keeper's, whose one interior tile gives a Prison-attracted type a weight of 1.0.</summary>
    private static IReadOnlyList<RoomInstance> PrisonRooms(GameDatabase db, WorldState world)
    {
        for (int y = 2; y <= 4; y++)
        {
            for (int x = 2; x <= 4; x++)
            {
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
            }
        }

        return RoomIndex.Build(world, db).Where(r => r.Definition.TypeId == RoomTypeId.Prison).ToList();
    }

    /// <summary>The world's clock moves only through a tick, so tick a simulation with no systems.</summary>
    private static void AdvanceTo(GameDatabase db, WorldState world, long tick)
    {
        var sim = new GameSimulation(db, world, seed: 0, systems: []);
        while (world.CurrentTick.Value < tick)
        {
            sim.Tick();
        }
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
            [Creature(5, (ushort)RoomTypeId.Prison), Creature(7, (ushort)RoomTypeId.Lair, 0x16)],
            PrisonRooms(Dk2TerrainFixture.Database(), world));

        Assert.Equal(
            [new EntranceInfoState(5, PortalChoiceRules.Fresh, 0), new EntranceInfoState(7, 0, 0)],
            world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void ATypeAlreadyThereAtTheStartIsNotBoosted_AndARowIsNeverRenoticed()
    {
        WorldState world = World(1);
        IReadOnlyList<RoomInstance> rooms = PrisonRooms(Dk2TerrainFixture.Database(), world);
        PortalChoiceRules.Notice(world, Keeper, [Creature(5, (ushort)RoomTypeId.Prison)], rooms);
        AdvanceTo(Dk2TerrainFixture.Database(), world, 500);
        PortalChoiceRules.Notice(world, Keeper, [Creature(5, (ushort)RoomTypeId.Prison)], rooms);

        Assert.Equal([new EntranceInfoState(5, 0, 0)], world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void APickTakesOneOffEveryBoost_AndTheOnlyWeightedTypeIsChosen()
    {
        WorldState world = World(2);
        CreatureDef goblin = Creature(7, (ushort)RoomTypeId.Lair, 0x16);
        world.Player(Keeper).EntranceInfo.Add(new EntranceInfoState(4, 4 * One, 2 * One));

        CreatureDef? chosen = PortalChoiceRules.Pick(world, Dk2TerrainFixture.Database(), new SimRandom(1), Keeper, [goblin], []);

        Assert.Same(goblin, chosen);
        Assert.Equal(
            [new EntranceInfoState(4, 3 * One, One), new EntranceInfoState(7, 0, 0)],
            world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void NoWeightedTypeMeansNoArrival_AndNothingDecays()
    {
        WorldState world = World(2);
        world.Player(Keeper).EntranceInfo.Add(new EntranceInfoState(9, 4 * One, 0));

        Assert.Null(PortalChoiceRules.Pick(world, Dk2TerrainFixture.Database(), new SimRandom(1), Keeper, [], []));
        Assert.Equal([new EntranceInfoState(9, 4 * One, 0)], world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void ATypeIsNoticedOnlyWhileItCarriesWeight_BinaryInferred()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(2);
        CreatureDef prisoner = Creature(5, (ushort)RoomTypeId.Prison);

        // No Prison yet: the weight is nothing, so 0x004DBD90 leaves the flag clear and the type unboosted.
        PortalChoiceRules.Notice(world, Keeper, [prisoner], []);
        Assert.Empty(world.Player(Keeper).EntranceInfo);

        // The room arrives: the type is noticed then, and boosted then.
        PortalChoiceRules.Notice(world, Keeper, [prisoner], PrisonRooms(db, world));
        Assert.Equal([new EntranceInfoState(5, PortalChoiceRules.Fresh, 0)], world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void ATypeWhoseRoomIsAlreadyFullyHeldIsNotNoticed_BinaryInferred()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(2);
        IReadOnlyList<RoomInstance> rooms = PrisonRooms(db, world);
        world.SpawnCreature(db, 7, new PlayerId(Keeper), new TilePos(8, 8));

        // The Prison's 1.0 is spent by the Goblin held, so the weight is zero.
        PortalChoiceRules.Notice(world, Keeper, [Creature(7, (ushort)RoomTypeId.Prison)], rooms);

        Assert.Empty(world.Player(Keeper).EntranceInfo);
    }

    /// <summary>The fixture's creature table with a unique Goblin (base type 7) added: the Goblin
    /// then has two definitions that map to it, and the Mistress (4) has one.</summary>
    private static GameDatabase DatabaseWithUniqueGoblin()
    {
        CreatureDef unique = Creature(40, (ushort)RoomTypeId.Prison) with
        {
            Flags = Dk2TerrainFixture.Database().CreatureById(7).Flags | CreatureFlags.IsUnique,
            UniqueBaseCreatureId = 7,
        };
        return Dk2TerrainFixture.Database(unique);
    }

    [Fact]
    public void ATypeWithAUniqueVariantLosesTwoPerPick_AndOneWithoutIsDockedOne_BinaryInferred()
    {
        WorldState world = World(2);
        CreatureDef vampire = Creature(8, (ushort)RoomTypeId.Lair, 26);
        // Type 7 has a unique definition of its own besides the Goblin; type 4 has none.
        world.Player(Keeper).EntranceInfo.Add(new EntranceInfoState(4, 5 * One, 4 * One));
        world.Player(Keeper).EntranceInfo.Add(new EntranceInfoState(7, 5 * One, 4 * One));

        Assert.Same(vampire, PortalChoiceRules.Pick(world, DatabaseWithUniqueGoblin(), new SimRandom(1), Keeper, [vampire], []));

        Assert.Equal(
            [
                new EntranceInfoState(4, 4 * One, 3 * One),
                new EntranceInfoState(7, 3 * One, 2 * One),
                new EntranceInfoState(8, 0, 0),
            ],
            world.Player(Keeper).EntranceInfo);
    }

    [Fact]
    public void TheDoubleDecayStopsAtZero_AndNeverGoesBelow_BinaryInferred()
    {
        WorldState world = World(2);
        CreatureDef vampire = Creature(8, (ushort)RoomTypeId.Lair, 26);
        world.Player(Keeper).EntranceInfo.Add(new EntranceInfoState(7, One, One));

        PortalChoiceRules.Pick(world, DatabaseWithUniqueGoblin(), new SimRandom(1), Keeper, [vampire], []);

        // 1.0 -> 0 on the first step; the second finds it zero and leaves it.
        Assert.Equal(new EntranceInfoState(7, 0, 0), world.Player(Keeper).EntranceInfo[0]);
    }

    /// <summary>With fewer than 46 definitions the indices past the count carry type 0 and land on
    /// an empty row (0x004B8640, 0x0050D450): the Imp's row loses only its own 1.0.</summary>
    [Fact]
    public void IndicesPastTheDefinitionCountDockNoRow_BinaryInferred()
    {
        WorldState world = World(2);
        CreatureDef vampire = Creature(8, (ushort)RoomTypeId.Lair, 26);
        world.Player(Keeper).EntranceInfo.Add(new EntranceInfoState(1, 5 * One, 4 * One));
        Assert.True(Dk2TerrainFixture.Database().Creatures.Count < PortalChoiceRules.RowCount);

        PortalChoiceRules.Pick(world, Dk2TerrainFixture.Database(), new SimRandom(1), Keeper, [vampire], []);

        Assert.Equal(new EntranceInfoState(1, 4 * One, 3 * One), world.Player(Keeper).EntranceInfo[0]);
    }

    /// <summary>A duplicate entry (a Unique definition's base, ADR-0783) takes its own share of the
    /// 100.0 and its own slot in the running total, so its type is drawn more often. Pick already
    /// did this; the test pins it for Rule 3's duplicates.</summary>
    [Fact]
    public void ADuplicateEntryTakesItsOwnShareOfTheDraw()
    {
        CreatureDef a = Creature(7, (ushort)RoomTypeId.Lair, 0x16);
        CreatureDef b = Creature(8, (ushort)RoomTypeId.Lair, 26);

        int ChosenA(IReadOnlyList<CreatureDef> admitted)
        {
            int count = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                WorldState world = World(1);
                if (ReferenceEquals(PortalChoiceRules.Pick(world, Dk2TerrainFixture.Database(), new SimRandom(seed), Keeper, admitted, []), a))
                {
                    count++;
                }
            }
            return count;
        }

        int single = ChosenA([a, b]);
        int duplicated = ChosenA([a, b, a]);

        Assert.InRange(single, 200, 250);          // 3.0 against 1.0 is three in four
        Assert.True(duplicated > single + 20, $"{duplicated} vs {single}");
    }
}
