// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The hated-company step of the Library, Training Room, Guard Room and Workshop handlers
/// (<c>0x004E8C60</c>, ADR-0479).</summary>
public sealed class HatedCompanyTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte CalmKnightId = 30;

    // A 3x3 Guard Room at (5..7, 3..5) on a 20x20 map; its lowest tile, the phase, is 3 x 20 + 5.
    private const int Phase = (3 * 20) + 5;

    // The first due turn makes (turn + 65) % 32 zero: turn 31, which is tick 30.
    private const int FirstDueTick = HatedCompanySystem.PeriodTurns - (Phase % HatedCompanySystem.PeriodTurns) - 1;

    private static (GameDatabase, WorldState) World(RoomTypeId type = RoomTypeId.GuardRoom)
    {
        CreatureDef calm = Dk2TerrainFixture.Knight() with
        {
            Name = "Calm Knight",
            CreatureId = CalmKnightId,
            Flags = Dk2TerrainFixture.Knight().Flags | CreatureFlags.DoesntGetAngryWithEnemies,
        };
        GameDatabase db = Dk2TerrainFixture.Database(extraCreatures: [calm]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 3; y <= 5; y++)
            for (int x = 5; x <= 7; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
        return (db, world);
    }

    private static CreatureState Holder(GameDatabase db, WorldState world, byte creatureId)
    {
        CreatureState c = world.SpawnCreature(db, creatureId, Keeper, new TilePos(6, 4));
        c.HeldPlace = new RoomPlace((4 * world.Width) + 6, RoomCapacityRules.Units);
        return c;
    }

    private static void Run(GameDatabase db, WorldState world, int ticks) =>
        new GameSimulation(db, world, seed: 42, [new HatedCompanySystem()]).Run(ticks);

    [Fact]
    public void ARoomHoldingBothAlignmentsAngersEveryHolderByEightSecondsOnItsTurn()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = Holder(db, world, Dk2TerrainFixture.GoblinCreatureId);
        CreatureState knight = Holder(db, world, Dk2TerrainFixture.KnightCreatureId);

        Run(db, world, FirstDueTick);
        Assert.Equal(0, goblin.Anger[AngerBucket.Treatment]);

        Run(db, world, 1);
        Assert.Equal(150 * HatedCompanySystem.SecondsPerApplication, goblin.Anger[AngerBucket.Treatment]);
        Assert.Equal(150 * HatedCompanySystem.SecondsPerApplication, knight.Anger[AngerBucket.Treatment]);

        Run(db, world, HatedCompanySystem.PeriodTurns);
        Assert.Equal(2 * 150 * HatedCompanySystem.SecondsPerApplication, goblin.Anger[AngerBucket.Treatment]);
    }

    [Theory]
    [InlineData(RoomTypeId.Library)]
    [InlineData(RoomTypeId.TrainingRoom)]
    [InlineData(RoomTypeId.Workshop)]
    public void TheLibraryTrainingRoomAndWorkshopRunTheStepToo(RoomTypeId type)
    {
        (GameDatabase db, WorldState world) = World(type);
        CreatureState goblin = Holder(db, world, Dk2TerrainFixture.GoblinCreatureId);
        Holder(db, world, Dk2TerrainFixture.KnightCreatureId);

        Run(db, world, FirstDueTick + 1);

        Assert.Equal(150 * HatedCompanySystem.SecondsPerApplication, goblin.Anger[AngerBucket.Treatment]);
    }

    [Fact]
    public void ARoomOfOneAlignmentAngersNobody()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState first = Holder(db, world, Dk2TerrainFixture.GoblinCreatureId);
        CreatureState second = Holder(db, world, Dk2TerrainFixture.GoblinCreatureId);

        Run(db, world, FirstDueTick + 1);

        Assert.Equal(0, first.Anger[AngerBucket.Treatment]);
        Assert.Equal(0, second.Anger[AngerBucket.Treatment]);
    }

    [Fact]
    public void AHolderThatDoesNotGetAngryWithEnemiesMakesTheRoomMixedButTakesNothing()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = Holder(db, world, Dk2TerrainFixture.GoblinCreatureId);
        CreatureState calm = Holder(db, world, CalmKnightId);

        Run(db, world, FirstDueTick + 1);

        Assert.Equal(150 * HatedCompanySystem.SecondsPerApplication, goblin.Anger[AngerBucket.Treatment]);
        Assert.Equal(0, calm.Anger[AngerBucket.Treatment]);
    }

    [Fact]
    public void ALairIsNotOneOfTheFourRooms()
    {
        (GameDatabase db, WorldState world) = World(RoomTypeId.Lair);
        CreatureState goblin = Holder(db, world, Dk2TerrainFixture.GoblinCreatureId);
        Holder(db, world, Dk2TerrainFixture.KnightCreatureId);

        Run(db, world, FirstDueTick + 1);

        Assert.Equal(0, goblin.Anger[AngerBucket.Treatment]);
    }
}
