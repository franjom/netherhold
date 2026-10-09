// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>The job-room finders' messages 41 to 44 (<c>0x004F3560</c>, <c>0x004F1A20</c>,
/// <c>0x004F2B80</c>, <c>0x004D75F0</c>, <c>0x004E2500</c>, <c>0x004F6200</c>; ADR-0353).</summary>
public class JobRoomFinderMessageTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    /// <summary>Paved ground at (1..12, 1..8) the keeper owns, a treasury tile at (12, 8); rock beyond.</summary>
    private static WorldState World()
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(width: 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 8; y++)
            for (int x = 1; x <= 12; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        ref TileState treasury = ref world.Tile(12, 8);
        treasury.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        treasury.GoldHeld = 100_000;
        return world;
    }

    private static void Available(WorldState world, RoomTypeId room, int value = RoomNeedRules.Available)
    {
        List<AvailabilityState> rows = world.Player(Keeper).Availabilities;
        rows.RemoveAll(a => a.Kind == AvailabilityKind.Room && a.TypeId == (int)room);
        if (value != 1) rows.Add(new AvailabilityState(AvailabilityKind.Room, (int)room, value));
    }

    private static void Room(WorldState world, RoomTypeId type, int x0, int y0)
    {
        for (int y = y0; y < y0 + 3; y++)
            for (int x = x0; x < x0 + 3; x++)
                RoomRules.Place(Db, ref world.Tile(x, y), Db.Room(type), Keeper);
    }

    private sealed class Probe(Action<SimulationContext> act) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context) => act(context);
    }

    /// <summary>Lets <paramref name="ticks"/> pass, then asks the finder for a goblin at (2, 2).</summary>
    private static PlayerMessage? Seek(WorldState world, RoomTypeId wants, int ticks = 30, byte creatureType = GoblinId)
    {
        new GameSimulation(Db, world, 1, []).Run(ticks);
        CreatureState seeker = world.SpawnCreature(Db, creatureType, Keeper, new TilePos(2, 2));
        PlayerMessage? posted = null;
        new GameSimulation(Db, world, 1, [new Probe(c =>
            posted = RoomNeedRules.NoJobRoomFor(c, seeker, Db.CreatureById(seeker.CreatureTypeId), wants))]).Tick();
        return posted;
    }

    /// <summary>A furnished 3×3 Training Room at (5..7, 1..3), its two places taken.</summary>
    private static WorldState FullTraining(bool furnished = true)
    {
        WorldState world = World();
        Available(world, RoomTypeId.TrainingRoom);
        Room(world, RoomTypeId.TrainingRoom, 5, 1);
        if (furnished) Dk2TerrainFixture.FurnishRooms(Db, world);
        var hooks = new StateHookContext(world, Db);
        for (int i = 0; i < 2 && furnished; i++)
            Assert.True(CreatureStateMachine.TrySetState(hooks, world.SpawnCreature(Db, GoblinId, Keeper, new TilePos(6, 2)),
                JobRules.StateFor(JobType.Train)!.Value));
        return world;
    }

    [Fact]
    public void NoRoomOfTheTypePosts41OnlyAtAvailabilityThree()
    {
        WorldState world = World();
        Assert.Null(Seek(world, RoomTypeId.TrainingRoom));
        Available(world, RoomTypeId.TrainingRoom);
        Assert.Equal((PlayerMessageKind.RoomNeeded, (int)RoomTypeId.TrainingRoom),
            Seek(world, RoomTypeId.TrainingRoom) is PlayerMessage m ? (m.Kind, m.Data1) : default);
    }

    [Fact]
    public void TheLibraryFinderReadsTheLairsAvailabilityNotTheLibrarys()
    {
        WorldState world = World();
        Available(world, RoomTypeId.Library);
        Assert.Null(Seek(world, RoomTypeId.Library));
        Available(world, RoomTypeId.Lair);
        Available(world, RoomTypeId.Library, 1);
        Assert.Equal((int)RoomTypeId.Library, Seek(world, RoomTypeId.Library)?.Data1);
    }

    [Fact]
    public void AMistressWithNoTortureChamberPosts99AndAnyoneElse41()
    {
        WorldState world = World();
        Available(world, RoomTypeId.TortureChamber);
        PlayerMessage wanted = Seek(world, RoomTypeId.TortureChamber, creatureType: RoomNeedRules.MistressCreatureId)!.Value;
        Assert.Equal((PlayerMessageKind.RoomWanted, (int)RoomTypeId.TortureChamber, (uint)SimulationTiming.TicksFromSeconds(300)),
            (wanted.Kind, wanted.Data1, wanted.Timeout));
        Assert.Equal(PlayerMessageKind.RoomNeeded, Seek(world, RoomTypeId.TortureChamber)?.Kind);
    }

    [Fact]
    public void AReachedFullEquippedRoomPastFourSecondsPosts42()
    {
        WorldState world = FullTraining();
        Assert.Equal((PlayerMessageKind.RoomFull, (int)RoomTypeId.TrainingRoom),
            Seek(world, RoomTypeId.TrainingRoom) is PlayerMessage m ? (m.Kind, m.Data1) : default);
    }

    [Fact]
    public void AFullRoomWithinFourSecondsOfItsLastSizeChangeIsSilent()
    {
        WorldState world = FullTraining();
        Assert.Null(Seek(world, RoomTypeId.TrainingRoom, ticks: 4 * SimulationTiming.TicksPerSecond - 2));
    }

    [Fact]
    public void ATrainingRoomWithNoDummyPosts44NamingTheRoom()
    {
        WorldState world = FullTraining(furnished: false);
        PlayerMessage message = Seek(world, RoomTypeId.TrainingRoom)!.Value;
        RoomInstance room = RoomIndex.RoomAt(world, Db, 2 * world.Width + 6)!;
        Assert.Equal((PlayerMessageKind.RoomUnequipped, (int)RoomTypeId.TrainingRoom, SlabChain.FirstSlab(world, room)),
            (message.Kind, message.Data1, message.Data2));
        Assert.Equal((uint)SimulationTiming.TicksFromSeconds(240), message.Timeout);
    }

    [Theory]
    [InlineData(RoomTypeId.Temple)]
    [InlineData(RoomTypeId.CombatPit)]
    public void ATempleOrCombatPitHoldsOnePerRimTile(RoomTypeId type)
    {
        // Room_ComputeTotalCapacity's arm for types 13 and 16 counts the tiles Room_IsFloorSlot
        // rejects: a 3x3's eight rim tiles, with no +1.
        WorldState world = World();
        Room(world, type, 5, 1);
        Dk2TerrainFixture.FurnishRooms(Db, world);
        RoomInstance room = RoomIndex.RoomAt(world, Db, 2 * world.Width + 6)!;
        Assert.True(AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity));
        Assert.Equal(8, capacity);
    }

    [Fact]
    public void ATemplesA3IsNeverSetSoAFullTemplePosts44()
    {
        WorldState world = World();
        Available(world, RoomTypeId.Temple);
        for (int x = 5; x < 7; x++) RoomRules.Place(Db, ref world.Tile(x, 1), Db.Room(RoomTypeId.Temple), Keeper);
        RoomInstance temple = RoomIndex.RoomAt(world, Db, 1 * world.Width + 5)!;
        Assert.False(RoomNeedRules.IsEquipped(world, temple));
        for (int i = 0; i < 2; i++)
            world.SpawnCreature(Db, GoblinId, Keeper, new TilePos(5 + i, 1)).HeldPlace = new RoomPlace(1 * world.Width + 5 + i, RoomCapacityRules.Units);
        Assert.Equal(PlayerMessageKind.RoomUnequipped, Seek(world, RoomTypeId.Temple)?.Kind);
    }

    [Fact]
    public void AFullWorkshopIsReportedOnlyWhileAnOrderIsQueued()
    {
        // A Workshop with no Anvil, Bucket or Bench has no place and +0xA3 clear: 44.
        WorldState world = World();
        Available(world, RoomTypeId.Workshop);
        Room(world, RoomTypeId.Workshop, 5, 1);
        Assert.Null(Seek(world, RoomTypeId.Workshop));
        world.Player(Keeper).ManufactureQueue.Add(new ManufactureOrder(ManufactureRules.TrapKind, 1, 100));
        Assert.Equal(PlayerMessageKind.RoomUnequipped, Seek(world, RoomTypeId.Workshop)?.Kind);
    }

    [Fact]
    public void AFullLibraryWithAShelfPosts42OnlyWithSomethingToResearch()
    {
        WorldState world = World();
        Available(world, RoomTypeId.Lair);
        Room(world, RoomTypeId.Library, 5, 1);
        world.Player(Keeper).ResearchTarget = 0;
        Assert.Null(Seek(world, RoomTypeId.Library));
        world.Player(Keeper).ResearchTarget = 1;
        Assert.Equal(PlayerMessageKind.RoomFull, Seek(world, RoomTypeId.Library)?.Kind);
    }

    [Fact]
    public void ALibraryWithEveryShelfFullPosts45NamingTheRoom()
    {
        WorldState world = World();
        Available(world, RoomTypeId.Lair);
        Room(world, RoomTypeId.Library, 5, 1);
        world.Player(Keeper).ResearchTarget = 1;
        int middle = 2 * world.Width + 6;
        for (int i = 0; i < 2; i++) world.SpawnObject(Db, ResearchTreeRules.BookObjectTypeId, Keeper, middle);
        PlayerMessage message = Seek(world, RoomTypeId.Library)!.Value;
        RoomInstance room = RoomIndex.RoomAt(world, Db, middle)!;
        Assert.Equal((PlayerMessageKind.RoomStorageFull, (int)RoomTypeId.Library, SlabChain.FirstSlab(world, room), (uint)SimulationTiming.TicksFromSeconds(120)),
            (message.Kind, message.Data1, message.Data2, message.Timeout));
    }

    [Fact]
    public void WhileAJackpotPartyIsOnACasinoWithoutOneIsNotReported()
    {
        WorldState world = World();
        Available(world, RoomTypeId.Casino);
        Room(world, RoomTypeId.Casino, 5, 1);
        Assert.Equal(PlayerMessageKind.RoomFull, Seek(world, RoomTypeId.Casino)?.Kind);
        world.Player(Keeper).CasinoParties = 1;
        Assert.Null(Seek(world, RoomTypeId.Casino));
    }

    [Fact]
    public void TheSpectatorsPitSearchNeverAsksForAPit()
    {
        WorldState world = World();
        Available(world, RoomTypeId.CombatPit);
        CreatureState seeker = world.SpawnCreature(Db, GoblinId, Keeper, new TilePos(2, 2));
        PlayerMessage? posted = null;
        new GameSimulation(Db, world, 1, [new Probe(c =>
            posted = RoomNeedRules.NoSpectatorPitFor(c, seeker, Db.CreatureById(GoblinId)))]).Tick();
        Assert.Null(posted);
    }

    [Fact]
    public void TheFightersPitSearchAsksForAPitAndReportsOneWithNoInterior()
    {
        WorldState world = World();
        Available(world, RoomTypeId.CombatPit);
        CreatureState seeker = world.SpawnCreature(Db, GoblinId, Keeper, new TilePos(2, 2));
        PlayerMessage? Ask()
        {
            PlayerMessage? posted = null;
            new GameSimulation(Db, world, 1, [new Probe(c =>
                posted = RoomNeedRules.NoFightingPitFor(c, seeker, Db.CreatureById(GoblinId)))]).Tick();
            return posted;
        }

        Assert.Equal(PlayerMessageKind.RoomNeeded, Ask()?.Kind);
        for (int x = 5; x < 7; x++) RoomRules.Place(Db, ref world.Tile(x, 1), Db.Room(RoomTypeId.CombatPit), Keeper);
        new GameSimulation(Db, world, 1, []).Run(30);
        Assert.Equal(PlayerMessageKind.RoomFull, Ask()?.Kind);
    }

    [Fact]
    public void AFightersPitWhoseFloorSlotsAreAllStoodOnFailsTheDropTestAndPosts42()
    {
        // 0x004D8430 passes while fewer creatures stand on the floor slots than there are slots: a
        // 3x3 has one, so one creature on it fails the pit; the pit has an interior, +0xA3, so 42.
        WorldState world = World();
        Available(world, RoomTypeId.CombatPit);
        Room(world, RoomTypeId.CombatPit, 5, 1);
        CreatureState seeker = world.SpawnCreature(Db, GoblinId, Keeper, new TilePos(2, 2));
        new GameSimulation(Db, world, 1, []).Run(30);
        PlayerMessage? Ask()
        {
            PlayerMessage? posted = null;
            new GameSimulation(Db, world, 1, [new Probe(c =>
                posted = RoomNeedRules.NoFightingPitFor(c, seeker, Db.CreatureById(GoblinId)))]).Tick();
            return posted;
        }

        Assert.Null(Ask());
        world.SpawnCreature(Db, GoblinId, Keeper, new TilePos(6, 2));
        Assert.Equal(PlayerMessageKind.RoomFull, Ask()?.Kind);
    }

    /// <summary>0x004D6ED0 sets +0xA3 when a lever spot exists; ours takes that as any interior tile.</summary>
    [Fact]
    public void ACasinoCountsAsEquippedWithAnInteriorTile_InferredFromItsLeverSpot()
    {
        WorldState world = World();
        Room(world, RoomTypeId.Casino, 5, 1);
        Assert.True(RoomNeedRules.IsEquipped(world, RoomIndex.RoomAt(world, Db, 2 * world.Width + 6)!));

        WorldState strip = World();
        for (int x = 5; x < 8; x++) RoomRules.Place(Db, ref strip.Tile(x, 1), Db.Room(RoomTypeId.Casino), Keeper);
        Assert.False(RoomNeedRules.IsEquipped(strip, RoomIndex.RoomAt(strip, Db, 1 * strip.Width + 6)!));
    }

    [Fact]
    public void ARoomWithAPlaceBehindRockPosts43AndOneWithAReachablePlacePostsNothing()
    {
        WorldState world = World();
        Available(world, RoomTypeId.TrainingRoom);
        Room(world, RoomTypeId.TrainingRoom, 16, 1);
        Dk2TerrainFixture.FurnishRooms(Db, world);
        Assert.Equal(PlayerMessageKind.RoomUnreachable, Seek(world, RoomTypeId.TrainingRoom)?.Kind);

        WorldState open = World();
        Available(open, RoomTypeId.TrainingRoom);
        Room(open, RoomTypeId.TrainingRoom, 5, 1);
        Dk2TerrainFixture.FurnishRooms(Db, open);
        Assert.Null(Seek(open, RoomTypeId.TrainingRoom));
    }

    [Fact]
    public void AnExistingWorkshopRaisesNothingBeyond41HereForItsUnreadByte()
    {
        WorldState world = World();
        Available(world, RoomTypeId.Workshop);
        Room(world, RoomTypeId.Workshop, 16, 1);
        Assert.Null(Seek(world, RoomTypeId.Workshop));
    }
}
