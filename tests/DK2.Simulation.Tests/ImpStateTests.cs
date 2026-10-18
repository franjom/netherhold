// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>An imp's tile work on DK2's own state ids (ADR-0181, <c>imp-state-exits.md</c>).
/// State 10 and the entry states last less than a tick, so each trace records the saved caller
/// and the resume slot beside the state: they are the evidence the short states ran.</summary>
public class ImpStateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int Idle = CreatureStateMachine.Idle;
    private const int Walk = 6;
    private const int Select = ImpStates.TaskSelection;

    private static (GameSimulation Sim, ListEventSink Events) Start(TilePos impAt, ushort turnRate = 4096)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        if (turnRate != 4096)
            database = new GameDatabase(database.Terrain.Values, database.Rooms.Values,
                database.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.ImpCreatureId
                    ? c with { TurnRate = turnRate } : c), database.Variables, objects: database.Objects.Values,
                keeperSpells: database.KeeperSpells.Values);
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, impAt);

        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    [Theory]
    [InlineData(1, 2, 0)]
    [InlineData(3, 2, 512)]
    [InlineData(2, 1, 1024)]
    [InlineData(2, 3, 1536)]
    public void AReinforcingImpFacesItsWallInsteadOfItsTravelHeading(int x, int y, int heading)
    {
        (GameSimulation sim, _) = Start(new TilePos(x, y));
        CreatureState imp = sim.World.Creatures[0];
        ReachReinforcementEntry(sim, imp);
        imp.Heading = heading;
        int target = WorkHeading(sim, imp);
        sim.Tick();
        Assert.Equal(22, imp.Dk2StateId);
        Assert.Equal(target, imp.Heading);
    }

    [Fact]
    public void AReinforcementEntryWaitsForTheTurnBeforeStartingWork()
    {
        (GameSimulation sim, _) = Start(new TilePos(2, 2), turnRate: 128);
        CreatureState imp = sim.World.Creatures[0];
        ReachReinforcementEntry(sim, imp);
        int target = WorkHeading(sim, imp);
        imp.Heading = (target + 1024) & 2047;
        int timer = imp.TimerTicks;
        for (int i = 0; i < 7; i++)
        {
            sim.Tick();
            Assert.Equal(21, imp.Dk2StateId);
            Assert.Equal(timer, imp.TimerTicks);
        }
        sim.Tick();
        Assert.Equal(22, imp.Dk2StateId);
        Assert.Equal(target, imp.Heading);
        Assert.Equal(timer - 1, imp.TimerTicks);
    }

    private static void ReachReinforcementEntry(GameSimulation sim, CreatureState imp)
    {
        for (int i = 0; i < 300 && imp.Dk2StateId != 21; i++) sim.Tick();
        Assert.Equal(21, imp.Dk2StateId);
    }

    private static int WorkHeading(GameSimulation sim, CreatureState imp) =>
        DK2.Simulation.Steering.Bearing.Of(CreatureState.CentreOf(imp.TargetTileIndex % sim.World.Width) - imp.PosX,
            CreatureState.CentreOf(imp.TargetTileIndex / sim.World.Width) - imp.PosY);

    private static List<(int State, int Saved, int Resume)> StatesSeen(
        GameSimulation sim, int ticks, params PlayerCommand[] first)
    {
        CreatureState imp = sim.World.Creatures[0];
        var seen = new List<(int, int, int)>();
        (int, int, int) last = (imp.Dk2StateId, imp.StateBeforeUtilityState, imp.UtilityNewState);
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick(t == 0 && first.Length > 0 ? InputFrame.Of(first) : InputFrame.Empty);
            (int, int, int) now = (imp.Dk2StateId, imp.StateBeforeUtilityState, imp.UtilityNewState);
            if (now == last) continue;
            seen.Add(now);
            last = now;
        }

        return seen;
    }

    [Fact]
    public void AnImpDigsInFourteenClaimsInSixteenAndReinforcesInTwentyTwo()
    {
        // 1 hands the imp to 10 first. 10 walks in 6 resuming in 13; 13 holds one tick and
        // moves to 14, which swings until the rock is dug and sets 10. The claim of the tile
        // it opened is 15 into 16 the same way. The wall beside it is walked to as well: from the
        // tile's centre to the reinforce point, one tick in 6, then 21 holds its tick before 22
        // (mining-cadence.md §5, §6; a walk ends at its point, imp-task-objects-and-walks.md §4).
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));

        Assert.Equal(
            [(Select, 0, 0),
             (Walk, Select, 13), (13, Select, Idle), (14, Select, Idle), (Select, Select, Idle),
             (Walk, Select, 15), (15, Select, Idle), (16, Select, Idle), (Select, Select, Idle),
             (Walk, Select, 21), (21, Select, Idle), (22, Select, Idle)],
            StatesSeen(sim, SimulationTiming.TicksFromSeconds(10),
                TagTilesCommand.Tag(Keeper, new TilePos(4, 2))));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AnImpKeepsFourteenAcrossEverySwingUntilTheRockIsGone()
    {
        // The even state repeats its action in place (Q420): no 10 between the swings.
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));
        CreatureState imp = sim.World.Creatures[0];
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, new TilePos(4, 2))));

        var states = new List<int>();
        while (!events.Events.Any(e => e.Kind == "destroyed"))
        {
            sim.Tick(InputFrame.Empty);
            states.Add(imp.Dk2StateId);
        }

        int first = states.IndexOf(14);
        Assert.True(first >= 0);
        Assert.All(states.Skip(first), s => Assert.Equal(14, s));
        Assert.True(states.Count - first > SimulationTiming.WorkApplicationPeriodTicks);
    }

    [Fact]
    public void UnmarkingTheRockMidWalkEndsTheWalkThroughThirteenAndTen()
    {
        // The walk ends in 13, which finds the task gone and sets 10.
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));
        CreatureState imp = sim.World.Creatures[0];

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, new TilePos(4, 2))));
        sim.Tick(InputFrame.Empty); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)
        Assert.Equal((Walk, 13), (imp.Dk2StateId, imp.UtilityNewState));

        sim.Tick(InputFrame.Of(TagTilesCommand.Untag(Keeper, new TilePos(4, 2))));

        Assert.Single(events.Events, e => e.Kind == "job_abandoned");
        Assert.Equal(Select, imp.Dk2StateId);
        Assert.Equal((Select, Idle), (imp.StateBeforeUtilityState, imp.UtilityNewState));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    private static void Store(GameSimulation sim, TilePos at, RoomTypeId type)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        ref TileState tile = ref sim.World.Tile(at);
        tile.TerrainId = db.Room(type).TerrainId;
        tile.Health = db.TerrainById(tile.TerrainId).StartingHealth;
        tile.OwnerPlayerId = Keeper;
    }

    private static void OpenTheSeam(GameSimulation sim)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        foreach (TilePos p in new TilePos[] { new(4, 3), new(4, 4), new(4, 5) })
        {
            ref TileState tile = ref sim.World.Tile(p);
            tile.TerrainId = Dk2TerrainFixture.DirtPath;
            tile.Health = db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }
    }

    [Fact]
    public void AStoreArrivalAndDepositEachKeepTheirOwnUpdate()
    {
        (GameSimulation sim, CreatureState imp, ListEventSink events) = ReachStoreArrival();
        int held = imp.GoldHeld;
        Assert.True(held > 0);
        Assert.DoesNotContain(events.Events, e => e.Kind == "gold_delivered");

        imp.Heading = WorkStoreHeading(imp);
        sim.Tick();
        Assert.Equal(ImpStates.PuttingDownGold, imp.Dk2StateId);
        Assert.Equal(held, imp.GoldHeld);
        sim.Tick();
        Assert.Equal(Select, imp.Dk2StateId);
        Assert.Equal(0, imp.GoldHeld);
        Assert.Single(events.Events, e => e.Kind == "gold_delivered");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACompletedStoreWalkStopsBeforeTurningAndDepositing(bool pathFinished)
    {
        (GameSimulation initial, _) = Start(new TilePos(3, 3), turnRate: 128);
        Store(initial, new TilePos(3, 3), RoomTypeId.Treasury);
        CreatureState imp = initial.World.Creatures[0];
        imp.GoldHeld = 1000;
        CreatureStateMachine.BeginTravel(new StateHookContext(initial.World, initial.Database),
            imp, Walk, ImpStates.AtTheStore);
        var position = new Fixed16Vector(imp.PosX << 4, imp.PosY << 4);
        imp.Body.Pos = position + new Fixed16Vector(7, 7);
        RouteWindow.Load(imp.Body, [position], position, 0);
        imp.Body.PathTotalDist = 16;
        imp.Body.PathFinished = pathFinished;
        imp.Body.Vel = new Fixed16Vector(2000, -1000);
        imp.Heading = (WorkStoreHeading(imp) + 1024) & 2047;
        Assert.Equal(Fixed16.One, FollowPathRules.Progress(imp.Body));
        Assert.Equal(!pathFinished, imp.HasRoute);
        // Start at the boundary after movement, before the state-6 arrival handler.
        var sim = new GameSimulation(initial.Database, initial.World, 99,
            [new ImpTaskSystem(), new CreatureMovementSystem()]);

        sim.Tick();

        Assert.Equal(ImpStates.AtTheStore, imp.Dk2StateId);
        Assert.False(imp.IsRouted);
        Assert.Equal(position, imp.Body.Pos);
        Assert.Equal(default, imp.Body.Vel);
        for (int i = 0; i < 10 && imp.GoldHeld > 0; i++)
        {
            sim.Tick();
            Assert.Equal(position, imp.Body.Pos);
            Assert.Equal(default, imp.Body.Vel);
            Assert.Equal((position.X >> 4, position.Y >> 4), (imp.PosX, imp.PosY));
        }
        Assert.Equal(0, imp.GoldHeld);
        Assert.Equal(Select, imp.Dk2StateId);
    }

    [Fact]
    public void AStoreArrivalTurnsTowardTheTileCentreBeforeDepositing()
    {
        (GameSimulation sim, CreatureState imp, _) = ReachStoreArrival(turnRate: 128);
        int held = imp.GoldHeld;
        imp.Heading = (WorkStoreHeading(imp) + 1024) & 2047;
        for (int i = 0; i < 7; i++)
        {
            sim.Tick();
            Assert.Equal(ImpStates.AtTheStore, imp.Dk2StateId);
            Assert.Equal(held, imp.GoldHeld);
        }
        sim.Tick();
        Assert.Equal(ImpStates.PuttingDownGold, imp.Dk2StateId);
        Assert.Equal(WorkStoreHeading(imp), imp.Heading);
        Assert.Equal(held, imp.GoldHeld);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStoreArrivalOrDepositResumesAfterASnapshot(bool deposit)
    {
        (GameSimulation sim, CreatureState imp, _) = ReachStoreArrival();
        if (deposit)
        {
            imp.Heading = WorkStoreHeading(imp);
            sim.Tick();
        }
        Assert.Equal(deposit ? ImpStates.PuttingDownGold : ImpStates.AtTheStore, imp.Dk2StateId);
        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(sim), sim.Database, Dk2TerrainFixture.DefaultSystems());
        Assert.Equal(sim.StateHash(), restored.StateHash());
        restored.Run(5);
        sim.Run(5);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    [Fact]
    public void AStoreThatDisappearsBeforeDepositLeavesTheImpHoldingItsGold()
    {
        (GameSimulation sim, CreatureState imp, ListEventSink events) = ReachStoreArrival();
        int held = imp.GoldHeld;
        imp.Heading = WorkStoreHeading(imp);
        sim.Tick();
        Assert.Equal(ImpStates.PuttingDownGold, imp.Dk2StateId);
        sim.World.Tile(imp.Tile).TerrainId = Dk2TerrainFixture.ClaimedPath;
        sim.Tick();
        Assert.Equal(Idle, imp.Dk2StateId);
        Assert.Equal(held, imp.GoldHeld);
        Assert.DoesNotContain(events.Events, e => e.Kind == "gold_delivered");
    }

    private static int WorkStoreHeading(CreatureState imp) =>
        DK2.Simulation.Steering.Bearing.Of(CreatureState.CentreOf(imp.Tile.X) - imp.PosX,
            CreatureState.CentreOf(imp.Tile.Y) - imp.PosY);

    private static (GameSimulation Sim, CreatureState Imp, ListEventSink Events) ReachStoreArrival(
        ushort turnRate = 4096)
    {
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(1, 1), turnRate);
        Store(sim, new TilePos(3, 3), RoomTypeId.Treasury);
        int hands = sim.Database.CreatureById(Dk2TerrainFixture.ImpCreatureId).MaxGoldHeld;
        FloorGold.Drop(sim.World, sim.Database, (2 * sim.World.Width) + 2, hands);
        CreatureState imp = sim.World.Creatures[0];
        for (int i = 0; i < 200 && imp.Dk2StateId != ImpStates.AtTheStore; i++) sim.Tick();
        Assert.Equal(ImpStates.AtTheStore, imp.Dk2StateId);
        return (sim, imp, events);
    }

    [Fact]
    public void FloorGoldIsPickedUpInTwentySevenAndPutDownThroughTwentyEightAndTwentyNine()
    {
        // 10 walks in 6 to 27; 27 takes the pile, full hands, and walks to the store in 6
        // resuming in 28, so the saved caller turns from 10 to 27. 28 moves to 29, which
        // deposits and sets 10.
        GameDatabase db = Dk2TerrainFixture.Database();
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(1, 1));
        Store(sim, new TilePos(3, 3), RoomTypeId.Treasury);
        int hands = db.CreatureById(Dk2TerrainFixture.ImpCreatureId).MaxGoldHeld;
        FloorGold.Drop(sim.World, db, (2 * sim.World.Width) + 2, hands);

        Assert.Equal(
            [(Select, 0, 0), (Walk, Select, ImpStates.PickingUpGold),
             (Walk, ImpStates.PickingUpGold, ImpStates.AtTheStore),
             (ImpStates.AtTheStore, ImpStates.PickingUpGold, Idle),
             (ImpStates.PuttingDownGold, ImpStates.PickingUpGold, Idle),
             (Select, ImpStates.PickingUpGold, Idle)],
            StatesSeen(sim, 100).Take(6));
        Assert.Contains(events.Events, e => e.Kind == "gold_delivered" && e.Detail.Contains($"amount={hands}"));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AFullMinerWalksFromTwentySixAndDepositsAlongTheChainIn29()
    {
        // 26 calls the store chooser once its hands are full. 29 hands the room the whole load,
        // the chain from the tile reached taking 1000 and the next tile the rest, and sets 10,
        // which goes back to the seam: no second walk to the store (0x00466AC0, ADR-0306).
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(4, 4));
        OpenTheSeam(sim);
        Store(sim, new TilePos(3, 3), RoomTypeId.DungeonHeart);
        Store(sim, new TilePos(3, 2), RoomTypeId.DungeonHeart);

        Assert.Equal(
            [(Select, 0, 0), (Walk, Select, 25), (25, Select, Idle), (26, Select, Idle),
             (Walk, 26, ImpStates.AtTheStore), (ImpStates.AtTheStore, 26, Idle),
             (ImpStates.PuttingDownGold, 26, Idle), (Select, 26, Idle), (Walk, Select, 25)],
            StatesSeen(sim, 32, TagTilesCommand.Tag(Keeper, new TilePos(5, 5))).Take(9));
        // The chain starts at a random slab, so either tile may be the one that fills.
        int[] tiles = [sim.World.Tile(3, 3).GoldHeld, sim.World.Tile(3, 2).GoldHeld];
        Assert.Equal(1000, tiles.Max());
        Assert.InRange(tiles.Min(), 90, 92);
        SimEvent first = events.Events.First(e => e.Kind == "gold_delivered");
        Assert.Matches("amount=109[0-2] .* floor=0", first.Detail);
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.SpellBookObjectId, 31)]
    [InlineData(Dk2TerrainFixture.RevealMapSpecialObjectId, 33)]
    public void ALooseObjectIsPickedUpInThirtyAndCarriedInTheStateForItsKind(byte type, int carrying)
    {
        // 10 walks in 6 to 30; 30 picks it up and walks it to the Library in 6 resuming in 31
        // for a spell book, 33 for a Special; the carry state stores it and sets 10.
        GameDatabase db = Dk2TerrainFixture.Database();
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));
        foreach (TilePos p in new TilePos[] { new(3, 3), new(3, 2) })
        {
            ref TileState tile = ref sim.World.Tile(p);
            RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Library), Keeper);
        }

        sim.World.SpawnObject(db, type, PlayerIds.Unowned, (1 * sim.World.Width) + 1);

        Assert.Equal(
            [(Select, 0, 0), (Walk, Select, ImpStates.PickingUpObject),
             (Walk, ImpStates.PickingUpObject, carrying),
             (Select, ImpStates.PickingUpObject, Idle)],
            StatesSeen(sim, 200).Take(4));
        Assert.Contains(events.Events, e => e.Kind == "object_stored");
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AnImpMinesInTwentySix()
    {
        (GameSimulation sim, ListEventSink events) = Start(new TilePos(2, 2));
        OpenTheSeam(sim);

        List<(int State, int Saved, int Resume)> seen = StatesSeen(
            sim, SimulationTiming.TicksFromSeconds(4), TagTilesCommand.Tag(Keeper, new TilePos(5, 5)));

        Assert.Equal([(Select, 0, 0), (Walk, Select, 25), (25, Select, Idle), (26, Select, Idle)], seen.Take(4));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }
}

/// <summary>Each change of an imp's (state, saved caller, resume slot), tick by tick.</summary>
internal static class ImpStateTrace
{
    public static List<(int State, int Saved, int Resume)> Seen(
        GameSimulation sim, CreatureState imp, int ticks, Func<bool>? until = null)
    {
        var seen = new List<(int, int, int)>();
        (int, int, int) last = (imp.Dk2StateId, imp.StateBeforeUtilityState, imp.UtilityNewState);
        for (int t = 0; t < ticks && !(until?.Invoke() ?? false); t++)
        {
            sim.Tick(InputFrame.Empty);
            (int, int, int) now = (imp.Dk2StateId, imp.StateBeforeUtilityState, imp.UtilityNewState);
            if (now == last) continue;
            seen.Add(now);
            last = now;
        }

        return seen;
    }
}
