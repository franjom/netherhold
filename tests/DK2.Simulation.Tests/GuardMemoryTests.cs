// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class GuardMemoryTests
{
    private const byte Keeper = 4;

    private static (GameDatabase Db, WorldState World, CreatureState Guard, GameSimulation Sim, ListEventSink Events) Setup(bool small = false)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = Keeper;
        }
        PaintGuard(db, world, small);
        RoomRules.Place(db, ref world.Tile(1, 1), db.Room(RoomTypeId.Lair), Keeper);
        RoomRules.Place(db, ref world.Tile(2, 6), db.Room(RoomTypeId.TrainingRoom), Keeper);
        RoomRules.Place(db, ref world.Tile(3, 6), db.Room(RoomTypeId.TrainingRoom), Keeper);
        Dk2TerrainFixture.Furnish(db, world, RoomTypeId.TrainingRoom, new TilePos(2, 6), Keeper);
        world.Tile(2, 3).GoldHeld = 10_000;
        CreatureState guard = world.SpawnCreature(db, 7, Keeper, new TilePos(6, 4));
        LairRules.PlaceBed(world, db, guard, world.TileIndex(new TilePos(1, 1)));
        guard.DroppedToDoJob = JobType.Guard;
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, 42, [new HandSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(events);
        sim.Run(2);
        Assert.Equal(JobType.Guard, guard.CurrentJob);
        events.Clear();
        return (db, world, guard, sim, events);
    }

    private static void PaintGuard(GameDatabase db, WorldState world, bool small = false)
    {
        for (int y = small ? 4 : 3; y <= (small ? 4 : 5); y++)
        for (int x = small ? 6 : 5; x <= (small ? 6 : 7); x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.GuardRoom), Keeper);
    }

    private static void Interrupt(CreatureState guard, JobType dropped = JobType.None)
    {
        guard.StopMoving();
        guard.ClearJob();
        guard.DroppedToDoJob = dropped;
        StatePlacement.Place(guard, 89);
    }

    [Fact]
    public void RememberedGuardRunsBeforeFreshHappySelection()
    {
        (GameDatabase _, WorldState _, CreatureState? guard, GameSimulation? sim, ListEventSink? events) = Setup();
        Interrupt(guard);
        sim.Tick();
        Assert.Equal(JobType.Guard, guard.CurrentJob);
        Assert.Contains(events.Events, e => e.Kind == "job_taken" && e.Detail.Contains("how=remembered"));
        Assert.DoesNotContain(events.Events, e => e.Kind == "job_taken" && e.Detail.Contains("how=rolled"));
    }

    [Fact]
    public void DroppedJobStillPrecedesRememberedGuard()
    {
        (GameDatabase _, WorldState _, CreatureState? guard, GameSimulation? sim, ListEventSink? events) = Setup();
        Interrupt(guard, JobType.Train);
        sim.Tick();
        Assert.Equal(JobType.Train, guard.CurrentJob);
        Assert.Contains(events.Events, e => e.Kind == "job_taken" && e.Detail.Contains("how=dropped"));
    }

    [Fact]
    public void SavedGuardRoomWinsOverNearerRoom()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation? sim, ListEventSink _) = Setup();
        for (int y = 3; y <= 5; y++)
        for (int x = 10; x <= 12; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.GuardRoom), Keeper);
        guard.PosX = CreatureState.CentreOf(11);
        guard.PosY = CreatureState.CentreOf(4);
        Interrupt(guard, JobType.Guard);
        sim.Tick();
        RoomInstance target = RoomIndex.RoomAt(world, db, guard.JobTileIndex)!;
        Assert.Contains(world.TileIndex(new TilePos(6, 4)), target.TileIndices);
    }

    [Fact]
    public void FailedRememberedAttemptDoesNotForgetTheJob()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation? sim, ListEventSink? events) = Setup();
        for (int y = 3; y <= 5; y++)
        for (int x = 5; x <= 7; x++) world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        Interrupt(guard);
        sim.Tick();
        Assert.DoesNotContain(events.Events, e => e.Kind == "job_taken" && e.Detail.Contains("how=remembered"));
        PaintGuard(db, world);
        Interrupt(guard);
        sim.Tick();
        Assert.Equal(JobType.Guard, guard.CurrentJob);
        Assert.Contains(events.Events, e => e.Kind == "job_taken" && e.Detail.Contains("how=remembered"));
    }

    [Fact]
    public void RememberedAssignmentConsumesCapacityWhileGuardIsAwayIncludingItsOwnRetry()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup(small: true);
        Interrupt(guard);
        RoomInstance room = RoomIndex.RoomAt(world, db, world.TileIndex(new TilePos(6, 4)))!;
        Assert.False(RoomCapacityRules.HasRoom(world, db, room, guard.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PickingUpClearsRememberedJobAndRoom(bool stored)
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup();
        Assert.Equal(JobType.Guard, guard.ReturnToJob);
        var hand = new GameSimulation(db, world, 42, [new HandSystem { StoredCommandPickups = stored }]);
        hand.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, guard.Id)));
        Assert.Equal(JobType.None, guard.ReturnToJob);
        Assert.Equal(RoomReference.None, guard.ReturnRoom);
    }

    [Fact]
    public void StoredCommandClearsMemoryEvenWhenTheHandStateRefuses()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup();
        guard.InHand = true;
        var hand = new GameSimulation(db, world, 42, [new HandSystem { StoredCommandPickups = true }]);
        hand.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, guard.Id)));
        Assert.Equal(JobType.None, guard.ReturnToJob);
        Assert.Equal(RoomReference.None, guard.ReturnRoom);
    }

    [Fact]
    public void InteractivePermissionRefusalDoesNotClearMemory()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup();
        RoomReference room = guard.ReturnRoom;
        guard.OwnerPlayerId = PlayerIds.Neutral;
        var hand = new GameSimulation(db, world, 42, [new HandSystem()]);
        hand.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, guard.Id)));
        Assert.Equal(JobType.Guard, guard.ReturnToJob);
        Assert.Equal(room, guard.ReturnRoom);
    }

    [Fact]
    public void SnapshotRetainsMemoryAndItsContinuation()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation? sim, ListEventSink _) = Setup();
        Interrupt(guard);
        using var bytes = new MemoryStream(WorldSnapshot.ToBytes(sim));
        GameSimulation restored = WorldSnapshot.Read(bytes, db, [new HandSystem(), new CreatureJobSystem()]);
        CreatureState copy = restored.World.CreatureById(guard.Id)!;
        Assert.Equal(guard.ReturnToJob, copy.ReturnToJob);
        Assert.Equal(guard.ReturnRoom, copy.ReturnRoom);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        for (int i = 0; i < 12; i++)
        {
            sim.Tick();
            restored.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
        }
        Assert.Equal(JobType.Guard, copy.CurrentJob);
    }

    [Fact]
    public void BothRememberedFieldsParticipateInHash()
    {
        (GameDatabase _, WorldState _, CreatureState? guard, GameSimulation? sim, ListEventSink _) = Setup();
        ulong remembered = sim.StateHash();
        guard.ReturnToJob = JobType.None;
        Assert.NotEqual(remembered, sim.StateHash());
        guard.ReturnToJob = JobType.Guard;
        guard.ReturnRoom = RoomReference.None;
        Assert.NotEqual(remembered, sim.StateHash());
    }

    [Fact]
    public void FullSavedRoomFallsBackToAnotherRoom()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation? sim, ListEventSink _) = Setup();
        for (int y = 3; y <= 5; y++)
        for (int x = 10; x <= 12; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.GuardRoom), Keeper);
        Interrupt(guard, JobType.Guard);
        int tile = world.TileIndex(new TilePos(6, 4));
        for (int i = 0; i < 8; i++)
            world.SpawnCreature(db, 7, Keeper, new TilePos(6, 4)).HeldPlace = new RoomPlace(tile, 1);
        sim.Tick();
        RoomInstance target = RoomIndex.RoomAt(world, db, guard.JobTileIndex)!;
        Assert.Contains(world.TileIndex(new TilePos(11, 4)), target.TileIndices);
        Assert.True(guard.ReturnRoom.RefersTo(world, target));
    }

    [Fact]
    public void TrackedRoomReferenceSurvivesRemovalOfItsOriginalAnchor()
    {
        (GameDatabase? db, WorldState? world, CreatureState _, GameSimulation _, ListEventSink _) = Setup();
        for (int y = 3; y <= 5; y++)
        for (int x = 5; x <= 7; x++) world.Tile(x, y).RoomListPosition = new RoomListPosition(100);
        RoomInstance room = RoomIndex.RoomAt(world, db, world.TileIndex(new TilePos(6, 4)))!;
        var reference = RoomReference.Capture(world, room);
        world.Tile(reference.Tile.X, reference.Tile.Y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        Assert.NotNull(reference.Resolve(world, db, Keeper, RoomTypeId.GuardRoom));
    }

    [Fact]
    public void TrackedRoomReplacementAtTheSameTileDoesNotReuseAnOldReference()
    {
        (GameDatabase? db, WorldState? world, CreatureState _, GameSimulation _, ListEventSink _) = Setup();
        RoomInstance room = RoomIndex.RoomAt(world, db, world.TileIndex(new TilePos(6, 4)))!;
        foreach (int tile in room.TileIndices) world.TileAtIndex(tile).RoomListPosition = new RoomListPosition(100);
        var reference = RoomReference.Capture(world, room);
        foreach (int tile in room.TileIndices) world.TileAtIndex(tile).RoomListPosition = new RoomListPosition(101);
        Assert.Null(reference.Resolve(world, db, Keeper, RoomTypeId.GuardRoom));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DyingGuardsRemainReservedButCorpsesAreNotInTheOwnerList(bool corpse)
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup(small: true);
        Interrupt(guard);
        if (corpse) CreatureStateMachine.Release(guard);
        else StatePlacement.Place(guard, 217);
        RoomInstance room = RoomIndex.RoomAt(world, db, world.TileIndex(new TilePos(6, 4)))!;
        Assert.Equal(corpse, RoomCapacityRules.HasRoom(world, db, room));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectKeeperPickupDoesNotExecuteTheCursorCommandsMemoryClear(bool stored)
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup();
        RoomReference room = guard.ReturnRoom;
        var hand = new GameSimulation(db, world, 42, [new HandSystem { StoredCommandPickups = stored }]);
        hand.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, guard.Id)));
        Assert.Equal(JobType.Guard, guard.ReturnToJob);
        Assert.Equal(room, guard.ReturnRoom);
    }

    [Fact]
    public void ReleasedRecordDoesNotPassTheStoredCreatureCommandLookup()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup();
        RoomReference room = guard.ReturnRoom;
        CreatureStateMachine.Release(guard);
        var hand = new GameSimulation(db, world, 42, [new HandSystem { StoredCommandPickups = true }]);
        hand.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, guard.Id)));
        Assert.Equal(JobType.Guard, guard.ReturnToJob);
        Assert.Equal(room, guard.ReturnRoom);
    }

    [Fact]
    public void CopiedWorldContinuesTheRememberedGuardJob()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup();
        Interrupt(guard);
        var copy = WorldState.CopyOf(world);
        CreatureState copied = copy.CreatureById(guard.Id)!;
        var simulation = new GameSimulation(db, copy, 42, [new CreatureJobSystem()]);
        simulation.Tick();
        Assert.Equal(JobType.Guard, copied.CurrentJob);
        Assert.Equal(guard.ReturnRoom, copied.ReturnRoom);
    }

    [Fact]
    public void UpdatingACopiedWorldClearsOldGuardMemoryAfterCursorPickup()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation? sim, ListEventSink _) = Setup();
        var copy = WorldState.CopyOf(world);
        CreatureState copied = copy.CreatureById(guard.Id)!;
        Assert.Equal(JobType.Guard, copied.ReturnToJob);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, guard.Id)));
        copy.CopyFrom(world);
        Assert.Equal(JobType.None, copied.ReturnToJob);
        Assert.Equal(RoomReference.None, copied.ReturnRoom);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CursorPickupClearsMemoryBeforeAFullHandRefusesTheLift(bool stored)
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation _, ListEventSink _) = Setup();
        var hand = new GameSimulation(db, world, 42, [new HandSystem { StoredCommandPickups = stored }]);
        for (int i = 0; i < HandRules.Capacity(db); i++)
        {
            CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));
            hand.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, imp.Id)));
        }
        hand.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, guard.Id)));
        hand.Run(3);
        Assert.False(guard.InHand);
        Assert.Equal(HandRules.Capacity(db), world.Player(Keeper).Hand.Count);
        Assert.Equal(JobType.None, guard.ReturnToJob);
        Assert.Equal(RoomReference.None, guard.ReturnRoom);
    }

    [Fact]
    public void UnreachableSavedRoomFallsBackToAReachableGuardRoom()
    {
        (GameDatabase? db, WorldState? world, CreatureState? guard, GameSimulation? sim, ListEventSink _) = Setup();
        for (int y = 2; y <= 6; y++)
        for (int x = 4; x <= 8; x++)
            if (x is 4 or 8 || y is 2 or 6) world.Tile(x, y).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        for (int y = 3; y <= 5; y++)
        for (int x = 10; x <= 12; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.GuardRoom), Keeper);
        guard.PosX = CreatureState.CentreOf(11);
        guard.PosY = CreatureState.CentreOf(4);
        Interrupt(guard, JobType.Guard);
        sim.Navigation.Refresh();
        sim.Tick();
        RoomInstance target = RoomIndex.RoomAt(world, db, guard.JobTileIndex)!;
        Assert.Contains(world.TileIndex(new TilePos(11, 4)), target.TileIndices);
        Assert.True(guard.ReturnRoom.RefersTo(world, target));
    }
}
