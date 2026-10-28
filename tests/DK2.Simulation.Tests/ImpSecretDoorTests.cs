// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class ImpSecretDoorTests
{
    private sealed class ChooseOnly(CreatureState asker) : ISimulationSystem
    {
        public string Name => "choose-only";
        public void Execute(SimulationContext context) => new ImpTaskSystem().ChooseJob(context, asker);
    }

    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = Keeper + 1;
    private static readonly TilePos DoorTile = new(4, 2);

    private static (GameSimulation Sim, CreatureState Imp, DoorState Door, ListEventSink Events) Start(bool timed = false)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        var art = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        art[5] = new CreatureAnimationTiming { ResourceType = 6, Flags = CreatureAnimationFlags.Present,
            FrameCount = 15, FramesPerSecond = 20 };
        var db = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values,
            fixture.Creatures.Values.Select(c => timed && c.CreatureId == Dk2TerrainFixture.ImpCreatureId
                ? c with { ArtSlots = art } : c), fixture.Variables, objects: fixture.Objects.Values,
            doors: [new DoorDef { DoorId = 5, Name = "test secret door", Flags = DoorFlags.Secret,
                Health = 400, GoldCost = 0, ManufactureRequired = 0 }]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, Keeper, Rival]);
        ref TileState tile = ref world.Tile(DoorTile);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
        {
            X = DoorTile.X, Y = DoorTile.Y, PlayerId = Rival, DoorId = 5, Unknown_0011 = new byte[3],
        }]);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        var sim = new GameSimulation(db, world, 99, [new TileTagSystem(), new CreatureMovementSystem(), new ImpTaskSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, imp, world.Doors[0], events);
    }

    [Fact]
    public void MarkingASecretDoorOnFloorFilesWorkAndKeepsTheDigLastKind()
    {
        var (sim, imp, _, events) = Start();
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, DoorTile)));
        for (int i = 0; i < 4 && !events.Events.Any(e => e.Kind == "job_taken"); i++) sim.Tick();
        Assert.Contains(events.Events, e => e.Kind == "job_taken" && e.Detail.Contains("SecretDoor"));
        Assert.True(sim.World.Tile(DoorTile).SecretDoorTasks.Contains(Keeper));
        Assert.Equal(ImpTaskKind.Excavate, imp.LastTaskKind);
        Assert.Equal(38, imp.UtilityNewState);
    }

    [Fact]
    public void WorkCompletesByClearingConcealmentWithoutDamagingTheDoorOrItsFloor()
    {
        var (sim, imp, door, events) = Start();
        int health = sim.World.Tile(DoorTile).Health;
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Rival);
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, DoorTile)));
        for (int i = 0; i < 120 && !events.Events.Any(e => e.Kind == "secret_door_revealed"); i++) sim.Tick();
        Assert.Contains(events.Events, e => e.Kind == "secret_door_revealed");
        Assert.False(sim.World.Tile(DoorTile).SecretDoor);
        Assert.False(sim.World.Tile(DoorTile).SecretDoorTasks.Contains(Keeper));
        Assert.True(sim.World.Tile(DoorTile).SecretDoorTasks.Contains(Rival));
        Assert.Equal(400, door.Health);
        Assert.Equal(health, sim.World.Tile(DoorTile).Health);
        Assert.Equal(CreatureStateMachine.Idle, imp.Dk2StateId);
        Assert.Equal(ImpTaskKind.Excavate, imp.LastTaskKind);
    }

    [Fact]
    public void RectangleUntagDoesNotRemoveKind11AndItIsNotAnOrdinaryDigTag()
    {
        var (sim, _, _, _) = Start();
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, DoorTile)));
        sim.Tick(InputFrame.Of(TagTilesCommand.Untag(Keeper, DoorTile)));
        Assert.True(sim.World.Tile(DoorTile).SecretDoorTasks.Contains(Keeper));
        Assert.False(sim.World.Tile(DoorTile).IsTaggedBy(Keeper));
    }

    [Fact]
    public void SecretFlagAndEachKeepersTaskSurviveSnapshotAndChangeTheHash()
    {
        var (sim, _, _, _) = Start();
        ulong before = sim.StateHash();
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper).With(Rival);
        Assert.NotEqual(before, sim.StateHash());
        ulong tasksOnly = sim.StateHash();
        sim.World.Creatures[0].SecretDoorTask = true;
        Assert.NotEqual(tasksOnly, sim.StateHash());
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.True(restored.World.Tile(DoorTile).SecretDoor);
        Assert.Equal(sim.World.Tile(DoorTile).SecretDoorTasks, restored.World.Tile(DoorTile).SecretDoorTasks);
        Assert.True(restored.World.Creatures[0].SecretDoorTask);
        sim.World.Tile(DoorTile).SecretDoor = false;
        Assert.NotEqual(sim.StateHash(), restored.StateHash());
    }

    [Fact]
    public void ExplicitKind11RetryRunsItsWholeMapScannerBeforeTheOrdinaryTiers()
    {
        var (sim, imp, _, events) = Start();
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        sim.World.Tile(4, 1).TerrainId = Dk2TerrainFixture.Rock;
        sim.World.Tile(4, 1).SetTaggedBy(Keeper, true);
        imp.Dk2StateId = ImpStates.TaskSelection;
        imp.LastTaskKind = ImpTaskKind.SecretDoor;
        imp.DroppedTask = ImpTaskKind.SecretDoor;
        sim.Tick();
        Assert.Contains(events.Events, e => e.Kind == "job_taken" && e.Detail.Contains("SecretDoor"));
        Assert.Equal(ImpTaskKind.Excavate, imp.LastTaskKind);
    }

    [Fact]
    public void ASecretFlagUnderTheImpRefusesTheActionAndFallsToIdleWithoutCompletingTheTask()
    {
        var (sim, imp, _, events) = Start();
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        sim.World.Tile(imp.Tile).SecretDoor = true;
        imp.TargetTileIndex = DoorTile.Y * sim.World.Width + DoorTile.X;
        imp.SecretDoorTask = true;
        imp.Dk2StateId = ImpStates.SecretDoorApproach;
        imp.Heading = 512;
        for (int i = 0; i < 8 && imp.Dk2StateId == ImpStates.SecretDoorApproach; i++) sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, imp.Dk2StateId);
        Assert.True(sim.World.Tile(DoorTile).SecretDoor);
        Assert.True(sim.World.Tile(DoorTile).SecretDoorTasks.Contains(Keeper));
        Assert.DoesNotContain(events.Events, e => e.Kind == "secret_door_revealed");
    }

    [Fact]
    public void ARunningSecretDoorActionIsKeptUntilTheStoredAnimationFinishes()
    {
        var (sim, imp, _, events) = Start(timed: true);
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        imp.TargetTileIndex = DoorTile.Y * sim.World.Width + DoorTile.X;
        imp.SecretDoorTask = true;
        imp.Dk2StateId = ImpStates.SecretDoorApproach;
        imp.Heading = 512;
        for (int i = 0; i < 8 && imp.Dk2StateId == ImpStates.SecretDoorApproach; i++) sim.Tick();
        Assert.Equal(ImpStates.SecretDoorWork, imp.Dk2StateId);
        Assert.Equal((byte)5, imp.ActionArtSlot);
        Assert.True(sim.World.Tile(DoorTile).SecretDoor);
        sim.Tick();
        Assert.True(sim.World.Tile(DoorTile).SecretDoor);
        for (int i = 0; i < 20 && !events.Events.Any(e => e.Kind == "secret_door_revealed"); i++) sim.Tick();
        Assert.Contains(events.Events, e => e.Kind == "secret_door_revealed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DropScanCanTakeANearSecretDoorBeforeAFartherDigWhileOrdinaryTierADigsFirst(bool dropped)
    {
        var (sim, imp, _, events) = Start();
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        sim.World.Tile(5, 2).TerrainId = Dk2TerrainFixture.Rock;
        sim.World.Tile(5, 2).SetTaggedBy(Keeper, true);
        sim.World.Tile(5, 3).TerrainId = Dk2TerrainFixture.DirtPath;
        sim.World.Tile(4, 3).TerrainId = Dk2TerrainFixture.DirtPath;
        sim.World.Tile(3, 3).TerrainId = Dk2TerrainFixture.DirtPath;
        imp.Dk2StateId = ImpStates.TaskSelection;
        imp.JustDropped = dropped;
        sim.Tick();
        SimEvent taken = Assert.Single(events.Events, e => e.Kind == "job_taken");
        Assert.Contains(dropped ? "SecretDoor" : "(5,2) Dig", taken.Detail);
        if (dropped) Assert.Equal(ImpTaskKind.Excavate, imp.DroppedTask);
    }

    [Fact]
    public void AKind11EntryOwnsItsMiddleFaceSlotAndANearerImpCanStealIt()
    {
        var (sim, holder, _, events) = Start();
        holder.TargetTileIndex = DoorTile.Y * sim.World.Width + DoorTile.X;
        holder.SecretDoorTask = true;
        holder.WorkSlot = 7;
        var heldEntry = new SecretDoorTaskEntry { Order = 1 };
        heldEntry.Holders[3] = holder.Id;
        sim.World.SecretDoorEntries[(Keeper, DoorTile)] = heldEntry;
        holder.Dk2StateId = ImpStates.SecretDoorApproach;
        holder.PosX = CreatureState.CentreOf(1);
        var taker = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        taker.Dk2StateId = ImpStates.TaskSelection;
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        sim.Tick();
        Assert.Contains(events.Events, e => e.Kind == "task_stolen" && e.Detail.Contains($"imp={taker.Id} from={holder.Id}"));
        Assert.True(taker.SecretDoorTask);
        Assert.Equal((byte)7, taker.WorkSlot);
        Assert.False(holder.SecretDoorTask && holder.TargetTileIndex == taker.TargetTileIndex);
    }

    [Fact]
    public void ClosedDoorNavigationUsesTheSecretFlagRatherThanOrdinaryDigTags()
    {
        var (sim, _, _, _) = Start();
        var navigation = new WorldSteering(sim.Database, sim.World);
        navigation.Refresh();
        Assert.Equal((byte)((Rival << 4) | 2), navigation.NavByte(DoorTile.X, DoorTile.Y));
        sim.World.Tile(DoorTile).SecretDoor = false;
        sim.World.Tile(DoorTile).SetTaggedBy(Keeper, true);
        navigation.Refresh();
        Assert.Equal((byte)(Rival << 4), navigation.NavByte(DoorTile.X, DoorTile.Y));
    }

    [Fact]
    public void ARemovedDoorClearsTheFlagAndTheNextHandlerDiscardsTheStaleEntry()
    {
        var (sim, imp, door, events) = Start();
        var removing = new GameSimulation(sim.Database, sim.World, 99,
            [new DoorRemovalSystem(), new ImpTaskSystem()]);
        removing.ObserveWith(events);
        imp.Dk2StateId = ImpStates.TaskSelection;
        imp.LastTaskKind = ImpTaskKind.SecretDoor;
        imp.DroppedTask = ImpTaskKind.SecretDoor;
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        door.Health = 0;
        removing.Tick();
        Assert.Empty(sim.World.Doors);
        Assert.False(sim.World.Tile(DoorTile).SecretDoor);
        Assert.False(sim.World.Tile(DoorTile).SecretDoorTasks.Contains(Keeper));
        Assert.DoesNotContain(events.Events, e => e.Kind == "job_taken" && e.Detail.Contains("SecretDoor"));
    }

    [Fact]
    public void AnUnstealableFirstSideRetriesFourTimesRatherThanAdvancingToAFreeSide()
    {
        var (sim, holder, _, _) = Start();
        holder.SecretDoorTask = true;
        holder.TargetTileIndex = DoorTile.Y * sim.World.Width + DoorTile.X;
        holder.WorkSlot = 7;
        var heldEntry = new SecretDoorTaskEntry { Order = 1 };
        heldEntry.Holders[3] = holder.Id;
        sim.World.SecretDoorEntries[(Keeper, DoorTile)] = heldEntry;
        holder.Dk2StateId = 51;
        sim.World.Tile(4, 1).TerrainId = Dk2TerrainFixture.DirtPath;
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        CreatureState asker = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        asker.Dk2StateId = ImpStates.TaskSelection;
        var choosing = new GameSimulation(sim.Database, sim.World, 99, [new ChooseOnly(asker)]);
        choosing.Tick();
        Assert.False(asker.SecretDoorTask);
        Assert.True(holder.SecretDoorTask);
    }

    [Theory]
    [InlineData(448, true)]
    [InlineData(453, false)]
    public void PerTileStealingUsesStrictNativeSquaredDistanceWithFiftySlack(int offset, bool stolen)
    {
        var (sim, holder, _, _) = Start();
        var stance = MiningSlots.StancePoint(new TilePos(3, 2), 7);
        holder.PosX = (int)stance.X;
        holder.PosY = (int)stance.Y;
        holder.SecretDoorTask = true;
        holder.TargetTileIndex = DoorTile.Y * sim.World.Width + DoorTile.X;
        holder.WorkSlot = 7;
        var heldEntry = new SecretDoorTaskEntry { Order = 1 };
        heldEntry.Holders[3] = holder.Id;
        sim.World.SecretDoorEntries[(Keeper, DoorTile)] = heldEntry;
        holder.Dk2StateId = ImpStates.SecretDoorApproach;
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        CreatureState asker = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        asker.PosX = (int)stance.X - offset;
        asker.PosY = (int)stance.Y;
        asker.Dk2StateId = ImpStates.TaskSelection;
        var choosing = new GameSimulation(sim.Database, sim.World, 99, [new ChooseOnly(asker)]);
        choosing.Tick();
        Assert.Equal(stolen, asker.SecretDoorTask);
    }

    [Fact]
    public void CompletingASecretDoorBlueprintSetsTheGlobalConcealmentFlag()
    {
        var (sim, _, door, _) = Start();
        door.UnderConstruction = true;
        sim.World.Tile(DoorTile).SecretDoor = false;
        ObjectState crate = FloorGold.Drop(sim.World, sim.Database, 2 * sim.World.Width + 2, 100)!;
        CrateRules.Build(sim.World, sim.Database, new BlueprintRef(CrateRules.DoorKind, door.Id), crate);
        Assert.False(door.UnderConstruction);
        Assert.True(sim.World.Tile(DoorTile).SecretDoor);
        Assert.DoesNotContain(crate, sim.World.Objects);
    }

    [Fact]
    public void TheSingleDigActionStillStrikesWhenTheFlaggedTileIsADiggableWall()
    {
        var (sim, imp, _, events) = Start(timed: true);
        sim.World.Tile(DoorTile).TerrainId = Dk2TerrainFixture.Rock;
        sim.World.Tile(DoorTile).Health = 30000;
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        imp.TargetTileIndex = DoorTile.Y * sim.World.Width + DoorTile.X;
        imp.SecretDoorTask = true;
        imp.Dk2StateId = ImpStates.SecretDoorApproach;
        imp.Heading = 512;
        for (int i = 0; i < 8 && imp.Dk2StateId == ImpStates.SecretDoorApproach; i++) sim.Tick();
        sim.Tick();
        int afterStrike = sim.World.Tile(DoorTile).Health;
        Assert.InRange(afterStrike, 1, 29999);
        for (int i = 0; i < 20 && !events.Events.Any(e => e.Kind == "secret_door_revealed"); i++) sim.Tick();
        Assert.Contains(events.Events, e => e.Kind == "secret_door_revealed");
        Assert.Equal(afterStrike, sim.World.Tile(DoorTile).Health);
    }

    [Fact]
    public void CombatCancelsWorkWithoutRevealingTheDoorRemotelyAfterReturningToIdle()
    {
        var (sim, imp, _, events) = Start();
        sim.World.Tile(DoorTile).SecretDoorTasks = PlayerMask.None.With(Keeper);
        imp.Dk2StateId = ImpStates.TaskSelection;
        sim.Tick();
        Assert.True(imp.SecretDoorTask);
        var hooks = new StateHookContext(sim.World, sim.Database);
        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, 199));
        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, 202));
        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, CreatureStateMachine.Idle));
        imp.PosX = CreatureState.CentreOf(1);
        sim.Tick();
        Assert.True(sim.World.Tile(DoorTile).SecretDoor);
        Assert.True(sim.World.Tile(DoorTile).SecretDoorTasks.Contains(Keeper));
        Assert.DoesNotContain(events.Events, e => e.Kind == "secret_door_revealed");
        Assert.Equal(imp.Id, sim.World.SecretDoorEntries[(Keeper, DoorTile)].Holders[3]);
        var nextDig = new TilePos(1, 4);
        sim.World.Tile(nextDig).TerrainId = Dk2TerrainFixture.Rock;
        sim.World.Tile(nextDig).SetTaggedBy(Keeper, true);
        imp.PosY = CreatureState.CentreOf(3);
        var choosing = new GameSimulation(sim.Database, sim.World, 99, [new ChooseOnly(imp)]);
        choosing.Tick();
        Assert.False(imp.SecretDoorTask);
        Assert.Equal(nextDig.Y * sim.World.Width + nextDig.X, imp.TargetTileIndex);
        Assert.Equal(imp.Id, sim.World.SecretDoorEntries[(Keeper, DoorTile)].Holders[3]);
    }

    [Fact]
    public void AnotherKeepersTagCannotReorderThisKeepersEquidistantSecretDoorEntries()
    {
        var (sim, imp, _, _) = Start();
        var other = new TilePos(2, 4);
        sim.World.Tile(other).SecretDoor = true;
        var tagging = new GameSimulation(sim.Database, sim.World, 99, [new TileTagSystem()]);
        tagging.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, DoorTile)));
        tagging.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, other)));
        tagging.Tick(InputFrame.Of(TagTilesCommand.Tag(Rival, DoorTile)));
        imp.PosX = CreatureState.CentreOf(2);
        imp.PosY = CreatureState.CentreOf(2);
        imp.Dk2StateId = ImpStates.TaskSelection;
        imp.LastTaskKind = ImpTaskKind.SecretDoor;
        imp.DroppedTask = ImpTaskKind.SecretDoor;
        var choosing = new GameSimulation(sim.Database, sim.World, 99, [new ChooseOnly(imp)]);
        choosing.Tick();
        Assert.Equal(other.Y * sim.World.Width + other.X, imp.TargetTileIndex);
    }

    [Fact]
    public void EachKeepersEntryOrderAndRetainedHoldersAreHashedAndSaved()
    {
        var (sim, _, _, _) = Start();
        var tagging = new GameSimulation(sim.Database, sim.World, 99, [new TileTagSystem()]);
        tagging.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, DoorTile), TagTilesCommand.Tag(Rival, DoorTile)));
        SecretDoorTaskEntry entry = sim.World.SecretDoorEntries[(Keeper, DoorTile)];
        ulong before = sim.StateHash();
        entry.Holders[3] = 17;
        Assert.NotEqual(before, sim.StateHash());
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(entry.Order, restored.World.SecretDoorEntries[(Keeper, DoorTile)].Order);
        Assert.Equal(17, restored.World.SecretDoorEntries[(Keeper, DoorTile)].Holders[3]);
        Assert.Equal(sim.World.SecretDoorEntries[(Rival, DoorTile)].Order,
            restored.World.SecretDoorEntries[(Rival, DoorTile)].Order);
        before = restored.StateHash();
        restored.World.SecretDoorEntries[(Keeper, DoorTile)] = new SecretDoorTaskEntry { Order = entry.Order + 1 };
        restored.World.SecretDoorEntries[(Keeper, DoorTile)].Holders[3] = 17;
        Assert.NotEqual(before, restored.StateHash());
    }
}
