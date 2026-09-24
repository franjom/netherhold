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
        int x, int y, byte creatureId, byte playerId, byte level = 1, int health = 100,
        ushort triggerId = 0) =>
        new(new byte[28])
        {
            X = x, Y = y, CreatureId = creatureId, PlayerId = playerId,
            Level = level, InitialHealth = health, TriggerId = triggerId,
        };

    [Fact]
    public void APlacedCreatureKeepsItsTriggerRoot()
    {
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database,
            [Keeper(5, 7, Dk2TerrainFixture.ImpCreatureId, 3, triggerId: 42)]);

        Assert.Equal(42, Assert.Single(world.Creatures).TriggerId);
    }

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
            { X = 5, Y = 4, CreatureId = 7, Level = 1, InitialHealth = 100 },
        ]);

        Assert.Equal([PlayerIds.Neutral, PlayerIds.Good],
                     world.Creatures.Select(c => c.OwnerPlayerId).ToArray());
    }

    [Fact]
    public void APlacedCreatureTakesTheRecordFlagBitsTheLoaderCopies()
    {
        // 0x00520391 on: a good creature's eight bits, a neutral or keeper creature's all but
        // bits 1 and 2 (ADR-0201).
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database,
        [
            new NeutralCreatureThing(new byte[24])
            { X = 4, Y = 4, CreatureId = 7, Level = 1, InitialHealth = 100, Flags = 0xFF },
            new GoodCreatureThing(new byte[32])
            { X = 5, Y = 4, CreatureId = 7, Level = 1, InitialHealth = 100, Flags = 0xFF },
            new KeeperCreatureThing(new byte[28])
            { X = 6, Y = 4, CreatureId = 7, Level = 1, InitialHealth = 100, Flags = 0x26, PlayerId = PlayerIds.FirstKeeper },
        ]);

        Assert.Equal([(byte)0xF9, (byte)0xFF, (byte)0x20], world.Creatures.Select(c => c.Flags).ToArray());
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

    /// <summary>
    /// The snapshot writes a door's trailing unknown with no length and reads back a
    /// fixed three, so a door holding any other number would desync the stream and
    /// corrupt every record after it — silently, because `BinaryReader` cannot tell.
    /// Nothing shipped can produce one; the type refuses to hold one anyway.
    /// </summary>
    [Fact]
    public void ADoorRefusesATrailingUnknownThatIsNotThreeBytes()
    {
        Assert.Equal(3, DoorState.Unknown0011Length);

        ArgumentException error = Assert.Throws<ArgumentException>(() => new DoorState
        {
            X = 1, Y = 2, Unknown_0008 = 3, TriggerId = 0,
            DoorId = 4, PlayerId = 5, Flags = 6,
            Unknown_0011 = new byte[] { 7, 8 },
        });

        Assert.Contains("3 bytes, not 2", error.Message);
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
        Assert.True(door.UnderConstruction);
        Assert.Equal(new byte[] { 0xA1, 0xB2, 0xC3 }, door.Unknown_0011.ToArray());

        Assert.Equal(new[] { -31, 41 }, world.Traps.Select(t => t.X).ToArray());
        TrapState trap = world.Traps[0];
        Assert.Equal(32, trap.Y);
        Assert.Equal(unchecked((int)0xFEDCBA98), trap.Unknown_0008);
        Assert.Equal((byte)203, trap.NumberOfShots);
        Assert.Equal((byte)204, trap.TrapId);
        Assert.Equal((byte)205, trap.PlayerId);
        Assert.Equal((byte)0xD4, trap.Unknown_000F);
        Assert.False(trap.UnderConstruction);
        Assert.True(world.Traps[1].UnderConstruction);
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
            DoorHash(new DoorState { UnderConstruction = true, Unknown_0011 = new byte[3] }),
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
            TrapHash(new TrapState { UnderConstruction = true }),
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
    public void HeroPartiesAndEffectGeneratorsAreProjectedVerbatimWithoutSpawningMembers()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        var member = new GoodCreatureThing(new byte[32])
        {
            X = -1, Y = 2, Z = -3, GoldHeld = 65432, Level = 201, Flags = 0xE1,
            ObjectiveTargetActionPointId = -4, InitialHealth = -5, TriggerId = 65000,
            ObjectiveTargetPlayerId = 202, Objective = 203, CreatureId = 204,
            WanderRadius = 0xA1, PartyId = 0xB2, Flags2 = 0xC3,
        };

        ThingProjectionResult result = ThingProjection.Place(world, database,
        [
            new HeroPartyThing(new byte[555])
            {
                Name = "second", TriggerId = 60000, PartyId = 222,
                Unknown_0023 = -23, Unknown_0027 = -27, Members = [member],
            },
            new HeroPartyThing(new byte[555])
            {
                Name = "first", PartyId = 1, Members = [],
            },
            new EffectGeneratorThing(new byte[36])
            {
                X = -8, Y = 9, Unknown_0008 = -10, Unknown_000C = 11,
                Unknown_0010 = 60001, Unknown_0012 = 60002,
                EffectIds = [60003, 4, 60005], Frequency = 206, GeneratorId = 207,
                Unknown_001E = new byte[] { 1, 2, 3, 4, 5, 6 },
            },
        ]);

        Assert.Equal(2, result.HeroParties);
        Assert.Equal(1, result.EffectGenerators);
        Assert.Equal(0, result.NotYetProjected);
        Assert.Empty(world.Creatures);
        Assert.Equal(new[] { "second", "first" }, world.HeroParties.Select(p => p.Name));

        HeroPartyState party = world.HeroParties[0];
        Assert.Equal(((ushort)60000, (byte)222, -23, -27),
            (party.TriggerId, party.PartyId, party.Unknown_0023, party.Unknown_0027));
        HeroPartyMemberState projected = Assert.Single(party.Members);
        Assert.Equal((-1, 2, -3, (ushort)65432, (byte)201, (byte)0xE1),
            (projected.X, projected.Y, projected.Z, projected.GoldHeld, projected.Level, projected.Flags));
        Assert.Equal((-4, -5, (ushort)65000, (byte)202, (byte)203, (byte)204, (byte)0xC3),
            (projected.ObjectiveTargetActionPointId, projected.InitialHealth, projected.TriggerId,
             projected.ObjectiveTargetPlayerId, projected.Objective, projected.CreatureId, projected.Flags2));
        Assert.Equal(0xA1, projected.WanderRadius); Assert.Equal(0xB2, projected.PartyId);

        EffectGeneratorState generator = Assert.Single(world.EffectGenerators);
        Assert.Equal((-8, 9, -10, 11, (ushort)60001, (ushort)60002, (byte)206, (byte)207),
            (generator.X, generator.Y, generator.Unknown_0008, generator.Unknown_000C,
             generator.Unknown_0010, generator.Unknown_0012, generator.Frequency, generator.GeneratorId));
        Assert.Equal(new ushort[] { 60003, 4, 60005 }, generator.EffectIds);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, generator.Unknown_001E.ToArray());
    }

    [Fact]
    public void FixedEffectGeneratorTailRejectsWrongLengths()
    {
        Assert.Equal(6, EffectGeneratorState.Unknown001ELength);
        Assert.Throws<ArgumentException>(() => new EffectGeneratorState
        {
            EffectIds = [], Unknown_001E = new byte[] { 1, 2, 3, 4, 5 },
        });
    }

    [Fact]
    public void EveryHeroPartyAndEffectGeneratorFieldParticipatesInTheHash()
    {
        HeroPartyMemberState Member(Action<HeroPartyMemberStateBuilder>? change = null)
        {
            var b = new HeroPartyMemberStateBuilder(); change?.Invoke(b); return b.Build();
        }
        ulong[] memberHashes =
        [
            MemberHash(Member()), MemberHash(Member(b => b.X = 1)), MemberHash(Member(b => b.Y = 1)),
            MemberHash(Member(b => b.Z = 1)), MemberHash(Member(b => b.GoldHeld = 1)),
            MemberHash(Member(b => b.Level = 1)), MemberHash(Member(b => b.Flags = 1)),
            MemberHash(Member(b => b.ObjectiveTargetActionPointId = 1)),
            MemberHash(Member(b => b.InitialHealth = 1)), MemberHash(Member(b => b.TriggerId = 1)),
            MemberHash(Member(b => b.ObjectiveTargetPlayerId = 1)),
            MemberHash(Member(b => b.Objective = 1)), MemberHash(Member(b => b.CreatureId = 1)),
            MemberHash(Member(b => b.Unknown = [1, 0])), MemberHash(Member(b => b.Unknown = [0, 1])),
            MemberHash(Member(b => b.Flags2 = 1)),
        ];
        Assert.Equal(memberHashes.Length, memberHashes.Distinct().Count());

        ulong[] partyHashes =
        [
            PartyHash(new HeroPartyState { Name = "", Members = [] }),
            PartyHash(new HeroPartyState { Name = "x", Members = [] }),
            PartyHash(new HeroPartyState { Name = "", TriggerId = 1, Members = [] }),
            PartyHash(new HeroPartyState { Name = "", PartyId = 1, Members = [] }),
            PartyHash(new HeroPartyState { Name = "", Unknown_0023 = 1, Members = [] }),
            PartyHash(new HeroPartyState { Name = "", Unknown_0027 = 1, Members = [] }),
            PartyHash(new HeroPartyState { Name = "", Members = [Member()] }),
        ];
        Assert.Equal(partyHashes.Length, partyHashes.Distinct().Count());

        ulong[] generatorHashes =
        [
            GeneratorHash(), GeneratorHash(x: 1), GeneratorHash(y: 1), GeneratorHash(u8: 1),
            GeneratorHash(uc: 1), GeneratorHash(u10: 1), GeneratorHash(u12: 1),
            GeneratorHash(ids: [1]), GeneratorHash(frequency: 1), GeneratorHash(id: 1),
            GeneratorHash(tail: [1, 0, 0, 0, 0, 0]),
            GeneratorHash(tail: [0, 0, 0, 0, 0, 1]),
        ];
        Assert.Equal(generatorHashes.Length, generatorHashes.Distinct().Count());

        static ulong MemberHash(HeroPartyMemberState state) { Hash64 h = Hash64.Start(); state.HashInto(ref h); return h.Value; }
        static ulong PartyHash(HeroPartyState state) { Hash64 h = Hash64.Start(); state.HashInto(ref h); return h.Value; }
        static ulong GeneratorHash(int x = 0, int y = 0, int u8 = 0, int uc = 0,
            ushort u10 = 0, ushort u12 = 0, ushort[]? ids = null, byte frequency = 0,
            byte id = 0, byte[]? tail = null)
        {
            var state = new EffectGeneratorState
            {
                X = x, Y = y, Unknown_0008 = u8, Unknown_000C = uc,
                Unknown_0010 = u10, Unknown_0012 = u12, EffectIds = ids ?? [],
                Frequency = frequency, GeneratorId = id, Unknown_001E = tail ?? new byte[6],
            };
            Hash64 h = Hash64.Start(); state.HashInto(ref h); return h.Value;
        }
    }

    private sealed class HeroPartyMemberStateBuilder
    {
        public int X, Y, Z, ObjectiveTargetActionPointId, InitialHealth;
        public ushort GoldHeld, TriggerId;
        public byte Level, Flags, ObjectiveTargetPlayerId, Objective, CreatureId, Flags2;
        public byte[] Unknown = new byte[2];
        public HeroPartyMemberState Build() => new()
        {
            X = X, Y = Y, Z = Z, GoldHeld = GoldHeld, Level = Level, Flags = Flags,
            ObjectiveTargetActionPointId = ObjectiveTargetActionPointId,
            InitialHealth = InitialHealth, TriggerId = TriggerId,
            ObjectiveTargetPlayerId = ObjectiveTargetPlayerId, Objective = Objective,
            CreatureId = CreatureId, WanderRadius = Unknown[0], PartyId = Unknown[1], Flags2 = Flags2,
        };
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
