// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>
/// <see cref="ThingProjection"/> against the fixture. The same assertions run
/// against the shipped campaign in
/// <c>DK2.Behavior.Tests.PlacedThingsOnRealDataTests</c>, which is what stops a
/// transcribed fixture from agreeing with a wrong reading.
/// </summary>
public class ThingProjectionTests
{
    private static GameDatabase Database() => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase database) =>
        WorldState.FromMap(
            database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);

    private static KeeperCreatureThing Keeper(
        int x, int y, byte creatureId, byte playerId, byte level = 1, int health = 100) =>
        new(new byte[28])
        {
            X = x, Y = y, CreatureId = creatureId, PlayerId = playerId,
            Level = level, InitialHealth = health,
        };

    [Fact]
    public void AThingsCoordinatesAreTileIndices_NotTheSimulationsSubtileUnits()
    {
        // The trap this file exists to keep shut. A THING's X and Y are 32-bit
        // ints and the simulation carries positions at Fp32.One (4096) units to
        // the tile, so reading them as subtile units type-checks, runs, and puts
        // every creature in the level inside tile (0,0).
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database, [Keeper(5, 7, Dk2TerrainFixture.ImpCreatureId, 3)]);

        CreatureState imp = Assert.Single(world.Creatures);
        Assert.Equal(new TilePos(5, 7), imp.Tile);
        Assert.Equal(CreatureState.CentreOf(5), imp.PosX);
        Assert.Equal(CreatureState.CentreOf(7), imp.PosY);
    }

    [Fact]
    public void InitialHealthIsAPercentageOfTheMaximum_NotAHitPointCount()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        CreatureDef imp = database.CreatureById(Dk2TerrainFixture.ImpCreatureId);

        ThingProjection.Place(world, database,
        [
            Keeper(4, 4, Dk2TerrainFixture.ImpCreatureId, 3, health: 100),
            Keeper(5, 4, Dk2TerrainFixture.ImpCreatureId, 3, health: 50),
            Keeper(6, 4, Dk2TerrainFixture.ImpCreatureId, 3, health: 5),
        ]);

        int max = CreatureLevelRules.MaxHealth(database, imp, 1);
        Assert.Equal([max, max / 2, max * 5 / 100],
                     world.Creatures.Select(c => c.Health).ToArray());

        // And the reading it is not: an absolute count would put all three at
        // 100, 50 and 5 hit points out of the Imp's several hundred.
        Assert.NotEqual(100, world.Creatures[0].Health);
    }

    [Fact]
    public void HealthIsScaledByTheCreaturesOwnLevel_NotByLevelOne()
    {
        // Ordering, not arithmetic. Maximum health is the record's Hp scaled by
        // the level curve, so a projection that writes health before it writes
        // the level leaves a level-10 creature standing at a level-1 maximum -
        // which looks like a wounded creature rather than like a bug.
        GameDatabase database = Database();
        WorldState world = World(database);
        CreatureDef imp = database.CreatureById(Dk2TerrainFixture.ImpCreatureId);

        ThingProjection.Place(world, database,
            [Keeper(4, 4, Dk2TerrainFixture.ImpCreatureId, 3, level: 10, health: 100)]);

        CreatureState placed = Assert.Single(world.Creatures);
        Assert.Equal(10, placed.Level);
        Assert.Equal(CreatureLevelRules.MaxHealth(database, imp, 10), placed.Health);
        Assert.True(placed.Health > CreatureLevelRules.MaxHealth(database, imp, 1));
    }

    [Fact]
    public void TheSideOfANeutralOrGoodCreatureComesFromWhichRecordItIs()
    {
        // Neither record carries a player field, because it does not need one.
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database,
        [
            new NeutralCreatureThing(new byte[24])
            { X = 4, Y = 4, CreatureId = 7, Level = 1, InitialHealth = 100 },
            new GoodCreatureThing(new byte[32])
            { X = 5, Y = 4, CreatureId = 7, Level = 1, InitialHealth = 100, Unknown_001D = new byte[2] },
        ]);

        Assert.Equal([PlayerIds.Neutral, PlayerIds.Good],
                     world.Creatures.Select(c => c.OwnerPlayerId).ToArray());
    }

    [Fact]
    public void AnObjectCarriesItsMoneyAmountAsGold()
    {
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database,
        [
            new ObjectThing(new byte[24])
            {
                X = 6, Y = 8, ObjectId = Dk2TerrainFixture.GoblinBedObjectId,
                PlayerId = PlayerIds.FirstKeeper, MoneyAmount = 750,
                Unknown_0008 = new byte[4],
            },
        ]);

        ObjectState placed = Assert.Single(world.Objects);
        Assert.Equal((8 * Dk2TerrainFixture.Map().Width) + 6, placed.TileIndex);
        Assert.Equal(PlayerIds.FirstKeeper, placed.OwnerPlayerId);
        Assert.Equal(750, placed.GoldHeld);
    }

    [Fact]
    public void ARoomRecordIsCountedAndNotPlaced_BecauseTheGridAlreadyHasIt()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        byte before = world.Tile(4, 4).TerrainId;

        ThingProjectionResult result = ThingProjection.Place(world, database,
        [
            new RoomThing(new byte[20])
            { X = 4, Y = 4, RoomType = Dk2TerrainFixture.TreasuryTerrain, PlayerId = 3, InitialHealth = 100 },
        ]);

        Assert.Equal(1, result.RoomsAlreadyInTheGrid);
        Assert.Equal(0, result.Placed);
        Assert.Equal(before, world.Tile(4, 4).TerrainId);
    }

    [Fact]
    public void DoorsAndTrapsAreProjected_NotCountedAsMissing()
    {
        // Door and trap counts leave the remaining-gap count honest: a caller
        // can distinguish projected state from a silently ignored record.
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjectionResult result = ThingProjection.Place(world, database,
        [
            new DoorThing(new byte[20]) { X = 4, Y = 4, DoorId = 1, Unknown_0011 = new byte[3] },
            new TrapThing(new byte[16]) { X = 5, Y = 4, TrapId = 1 },
            new ActionPointThing(new byte[56]) { StartX = 6, StartY = 4, ActionPointId = 1, Name = "" },
        ]);

        Assert.Equal(0, result.NotYetProjected);
        Assert.Equal(3, result.Placed);
        Assert.Equal(1, result.Doors);
        Assert.Equal(1, result.Traps);
        Assert.Empty(world.Creatures);
        Assert.Empty(world.Objects);
        Assert.Single(world.ActionPoints);
        Assert.Single(world.Doors);
        Assert.Single(world.Traps);
    }

    [Fact]
    public void ADoorAndTrapCarryEveryParsedFieldVerbatimInFileOrder()
    {
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjectionResult result = ThingProjection.Place(world, database,
        [
            new DoorThing(new byte[20])
            {
                X = -11, Y = 12, Unknown_0008 = unchecked((int)0x89ABCDEF),
                TriggerId = 65432, DoorId = 201, PlayerId = 202, Flags = 0xE7,
                Unknown_0011 = new byte[] { 0xA1, 0xB2, 0xC3 },
            },
            new DoorThing(new byte[20])
            {
                X = 21, Y = 22, DoorId = 2, Unknown_0011 = new byte[3],
            },
            new TrapThing(new byte[16])
            {
                X = -31, Y = 32, Unknown_0008 = unchecked((int)0xFEDCBA98),
                NumberOfShots = 203, TrapId = 204, PlayerId = 205,
                Unknown_000F = 0xD4,
            },
            new TrapThing(new byte[16]) { X = 41, Y = 42, TrapId = 4 },
        ]);

        Assert.Equal(2, result.Doors);
        Assert.Equal(2, result.Traps);
        Assert.Equal(new[] { -11, 21 }, world.Doors.Select(d => d.X).ToArray());
        DoorState door = world.Doors[0];
        Assert.Equal(12, door.Y);
        Assert.Equal(unchecked((int)0x89ABCDEF), door.Unknown_0008);
        Assert.Equal((ushort)65432, door.TriggerId);
        Assert.Equal((byte)201, door.DoorId);
        Assert.Equal((byte)202, door.PlayerId);
        Assert.Equal((byte)0xE7, door.Flags);
        Assert.Equal(new byte[] { 0xA1, 0xB2, 0xC3 }, door.Unknown_0011.ToArray());

        Assert.Equal(new[] { -31, 41 }, world.Traps.Select(t => t.X).ToArray());
        TrapState trap = world.Traps[0];
        Assert.Equal(32, trap.Y);
        Assert.Equal(unchecked((int)0xFEDCBA98), trap.Unknown_0008);
        Assert.Equal((byte)203, trap.NumberOfShots);
        Assert.Equal((byte)204, trap.TrapId);
        Assert.Equal((byte)205, trap.PlayerId);
        Assert.Equal((byte)0xD4, trap.Unknown_000F);
    }

    [Fact]
    public void DoorsAndTrapsParticipateInTheWorldHash()
    {
        GameDatabase database = Database();
        WorldState empty = World(database);
        WorldState populated = World(database);
        ThingProjection.Place(populated, database,
        [
            new DoorThing(new byte[20])
            { X = 1, Y = 2, DoorId = 3, Flags = 4, Unknown_0011 = new byte[3] },
            new TrapThing(new byte[16])
            { X = 5, Y = 6, TrapId = 7, NumberOfShots = 8 },
        ]);

        Hash64 emptyHash = Hash64.Start();
        empty.HashInto(ref emptyHash);
        Hash64 populatedHash = Hash64.Start();
        populated.HashInto(ref populatedHash);

        Assert.NotEqual(emptyHash.Value, populatedHash.Value);
    }

    [Fact]
    public void EveryDoorAndTrapFieldParticipatesInItsStateHash()
    {
        ulong[] doorHashes =
        [
            DoorHash(new DoorState { Unknown_0011 = new byte[3] }),
            DoorHash(new DoorState { X = 1, Unknown_0011 = new byte[3] }),
            DoorHash(new DoorState { Y = 1, Unknown_0011 = new byte[3] }),
            DoorHash(new DoorState { Unknown_0008 = 1, Unknown_0011 = new byte[3] }),
            DoorHash(new DoorState { TriggerId = 1, Unknown_0011 = new byte[3] }),
            DoorHash(new DoorState { DoorId = 1, Unknown_0011 = new byte[3] }),
            DoorHash(new DoorState { PlayerId = 1, Unknown_0011 = new byte[3] }),
            DoorHash(new DoorState { Flags = 1, Unknown_0011 = new byte[3] }),
            DoorHash(new DoorState { Unknown_0011 = new byte[] { 1, 0, 0 } }),
            DoorHash(new DoorState { Unknown_0011 = new byte[] { 0, 1, 0 } }),
            DoorHash(new DoorState { Unknown_0011 = new byte[] { 0, 0, 1 } }),
        ];
        Assert.Equal(doorHashes.Length, doorHashes.Distinct().Count());

        ulong[] trapHashes =
        [
            TrapHash(new TrapState()),
            TrapHash(new TrapState { X = 1 }),
            TrapHash(new TrapState { Y = 1 }),
            TrapHash(new TrapState { Unknown_0008 = 1 }),
            TrapHash(new TrapState { NumberOfShots = 1 }),
            TrapHash(new TrapState { TrapId = 1 }),
            TrapHash(new TrapState { PlayerId = 1 }),
            TrapHash(new TrapState { Unknown_000F = 1 }),
        ];
        Assert.Equal(trapHashes.Length, trapHashes.Distinct().Count());

        static ulong DoorHash(DoorState state)
        {
            Hash64 hash = Hash64.Start();
            state.HashInto(ref hash);
            return hash.Value;
        }

        static ulong TrapHash(TrapState state)
        {
            Hash64 hash = Hash64.Start();
            state.HashInto(ref hash);
            return hash.Value;
        }
    }

    [Fact]
    public void AnActionPointCarriesTheRuntimeFieldsTheBinaryLoads()
    {
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjectionResult result = ThingProjection.Place(world, database,
        [
            new ActionPointThing(new byte[56])
            {
                StartX = 3, StartY = 4, EndX = 8, EndY = 9,
                WaitDelay = 19, Flags = 0x7F, TriggerId = 321,
                ActionPointId = 7, NextWaypointId = 11, Name = "fixture",
            },
        ]);

        ActionPointState point = Assert.Single(world.ActionPoints);
        Assert.Equal(1, result.ActionPoints);
        Assert.Equal((byte)7, point.Id);
        Assert.Equal((ushort)3, point.StartX);
        Assert.Equal((ushort)4, point.StartY);
        Assert.Equal((ushort)8, point.EndX);
        Assert.Equal((ushort)9, point.EndY);
        Assert.Equal((ushort)321, point.TriggerId);
        Assert.Equal((byte)11, point.NextWaypointId);

        // Bits 0,2,3,4,5 are copied. Source bits 1 and 6 are not, and no one
        // of these bits is reinterpreted as an invented Available field.
        Assert.Equal(ActionPointState.LoadedFlagMask, point.Flags);
        Assert.Equal((ushort)0, point.PassageCount);
        Assert.Equal((ushort)(19 * SimulationTiming.OriginalTicksPerGameSecond),
                     point.WaitTimerTicks);
    }

    [Fact]
    public void ActionPointRuntimeFieldsParticipateInTheWorldHash()
    {
        GameDatabase database = Database();
        WorldState one = World(database);
        WorldState two = World(database);
        var thing = new ActionPointThing(new byte[56])
        {
            StartX = 3, StartY = 4, EndX = 8, EndY = 9,
            WaitDelay = 2, Flags = 0x10, TriggerId = 1,
            ActionPointId = 7, NextWaypointId = 0, Name = "fixture",
        };
        ThingProjection.Place(one, database, [thing]);
        ThingProjection.Place(two, database, [thing]);

        Hash64 before = Hash64.Start();
        one.HashInto(ref before);
        two.ActionPoints[0].WaitTimerTicks--;
        Hash64 after = Hash64.Start();
        two.HashInto(ref after);

        Assert.NotEqual(before.Value, after.Value);
    }

    [Fact]
    public void PlacementOrderIsTheFilesOrder_BecauseUpdateOrderIsBehaviour()
    {
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database,
        [
            Keeper(7, 4, Dk2TerrainFixture.ImpCreatureId, 3),
            Keeper(4, 4, Dk2TerrainFixture.ImpCreatureId, 3),
            Keeper(6, 4, Dk2TerrainFixture.ImpCreatureId, 3),
        ]);

        Assert.Equal([7, 4, 6], world.Creatures.Select(c => c.Tile.X).ToArray());
    }
}
