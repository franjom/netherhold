// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>An idle worker's rest and wander, states 12 and 87 (imp-idle-and-wandering.md, ADR-0224).</summary>
public class ImpWanderTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Theory]
    [InlineData(100, 0, ImpStates.Resting, true)]
    [InlineData(0, 100, ImpStates.Wandering, true)]
    [InlineData(0, 0, ImpStates.TaskSelection, false)]
    public void RestWritesIdleBeforeActionWaitAndClearsOnlyOnDeadlineExit(
        long actionUntil, long deadline, int expectedState, bool idle)
    {
        var (db, world) = AllClaimed();
        var imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        imp.Dk2StateId = ImpStates.Resting;
        imp.ActionUntilTick = actionUntil;
        imp.StateDeadlineTick = deadline;
        new GameSimulation(db, world, 11, [new ImpTaskSystem()]).Tick();
        Assert.Equal(expectedState, imp.Dk2StateId);
        Assert.Equal(idle, imp.WorkerIdle);
    }

    [Fact]
    public void SelectionClearsIdleWhenItRunsRatherThanWhenRestIsEntered()
    {
        var (db, world) = AllClaimed();
        var imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        imp.Dk2StateId = ImpStates.TaskSelection;
        imp.WorkerIdle = true;
        imp.TimerTicks = 2;
        var sim = new GameSimulation(db, world, 11, [new ImpTaskSystem()]);
        sim.Tick();
        Assert.True(imp.WorkerIdle);
        sim.Tick();
        Assert.Equal(ImpStates.Resting, imp.Dk2StateId);
        Assert.False(imp.WorkerIdle);
        sim.Tick();
        Assert.True(imp.WorkerIdle);
    }

    [Fact]
    public void WanderArrivalPreservesIdleUntilTheNextSelection()
    {
        var (db, world) = AllClaimed();
        var imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        imp.WorkerIdle = true;
        CreatureStateMachine.BeginTravel(new StateHookContext(world, db), imp, 6, CreatureStateMachine.Idle);
        var sim = new GameSimulation(db, world, 11, [new ImpTaskSystem()]);
        sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, imp.Dk2StateId);
        Assert.True(imp.WorkerIdle);
        sim.Tick();   // 1 hands the imp to 10 (mining-cadence.md §6); still idle
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
        Assert.True(imp.WorkerIdle);
        sim.Tick();   // 10 chooses, and the choosing clears it
        Assert.False(imp.WorkerIdle);
    }

    [Theory]
    [InlineData(PlayerIds.Neutral)]
    [InlineData(Keeper)]
    public void StateElevenClearsIdleAndPreparesTheDropScan(byte owner)
    {
        var (db, world) = AllClaimed();
        var imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, owner, new TilePos(7, 6));
        imp.Dk2StateId = ImpStates.AfterDrop;
        imp.WorkerIdle = true;
        new GameSimulation(db, world, 11, [new ImpTaskSystem()]).Tick();
        Assert.False(imp.WorkerIdle);
        Assert.True(imp.JustDropped);
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
    }

    [Fact]
    public void HandPreservesIdleOnPickupAndClearsItOnTheCompressedWorkerDrop()
    {
        var (db, world) = AllClaimed();
        var imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        imp.WorkerIdle = true;
        var sim = new GameSimulation(db, world, 11, [new HandSystem()]);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        Assert.True(imp.InHand);
        Assert.True(imp.WorkerIdle);
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(7, 6))));
        Assert.False(imp.InHand);
        Assert.False(imp.WorkerIdle);
        Assert.True(imp.JustDropped);
    }

    [Fact]
    public void IdleFlagAffectsHashAndSurvivesSaveThenResume()
    {
        var (db, world) = AllClaimed();
        var imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        imp.Dk2StateId = ImpStates.Resting;
        imp.ActionUntilTick = 3;
        imp.StateDeadlineTick = 5;
        var sim = new GameSimulation(db, world, 11, [new ImpTaskSystem()]);
        ulong before = sim.StateHash();
        imp.WorkerIdle = true;
        Assert.NotEqual(before, sim.StateHash());
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new ImpTaskSystem()]);
        Assert.True(copy.World.CreatureById(imp.Id)!.WorkerIdle);
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(sim.StateHash(), copy.StateHash());
            sim.Tick();
            copy.Tick();
        }
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    /// <summary>The owner's run-stop-wait loop: each trip ends on one of the keeper's own tiles at
    /// ring radius 1–7 (<c>0x00480A00</c>), never on the Dungeon Heart.</summary>
    [Fact]
    public void AnIdleImpWandersToItsKeepersOwnTilesWithinSevenAndNeverOntoTheHeart()
    {
        (GameDatabase db, WorldState world) = AllClaimed();
        for (int y = 3; y <= 5; y++)
            for (int x = 10; x <= 12; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
        // Unowned water the imp can walk on and has no work in: reachable, never a destination.
        for (int y = 7; y <= 10; y++)
            for (int x = 2; x <= 6; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.Water;
                world.Tile(x, y).OwnerPlayerId = PlayerIds.Unowned;
            }
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        var sim = new GameSimulation(db, world, 11, [new ImpTaskSystem(), new CreatureMovementSystem()]);

        var aims = new List<TilePos>();
        var reach = new List<int>();
        TilePos from = imp.Tile;
        for (int t = 0; t < 2000; t++)
        {
            bool wasTravelling = imp.Dk2StateId == 6;
            sim.Tick();
            if (!wasTravelling && imp.Dk2StateId == 6 && imp.UtilityNewState == CreatureStateMachine.Idle)
            {
                var aim = new TilePos(imp.Body.PathTargetPos.X >> 16, imp.Body.PathTargetPos.Y >> 16);
                int d = Math.Max(Math.Abs(aim.X - from.X), Math.Abs(aim.Y - from.Y));
                Assert.InRange(d, 0, 7);
                aims.Add(aim);
                reach.Add(d);
            }
            if (imp.Dk2StateId != 6) from = imp.Tile;
        }

        Assert.True(aims.Count >= 5, $"only {aims.Count} trips");
        Assert.All(aims, a => Assert.Equal(Keeper, world.Tile(a.X, a.Y).OwnerPlayerId));
        Assert.DoesNotContain(aims, a => a.X is >= 10 and <= 12 && a.Y is >= 3 and <= 5);
        // The first radius tried is 4–7; nearer rings are fallbacks, so most trips go 4 or more.
        Assert.True(reach.Count(d => d >= 4) * 2 > reach.Count, $"trip lengths {string.Join(",", reach)}");
    }

    /// <summary>After a trip, 12 rolls: under 25 it wanders again, otherwise it plays action 43 or 44.
    /// The actions' length here is a guess (<see cref="ImpTaskSystem.IdleActionGuessTicks"/>).</summary>
    [Fact]
    public void BetweenTripsTheImpPlaysIdleActions_ForAGuessedLength()
    {
        (GameDatabase db, WorldState world) = AllClaimed();
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        var sim = new GameSimulation(db, world, 11, [new ImpTaskSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(2000);

        SimEvent[] actions = sink.Events.Where(e => e.Kind == "idle_action").ToArray();
        Assert.Contains(actions, e => e.Detail.Contains("action=43"));
        Assert.Contains(actions, e => e.Detail.Contains("action=44"));
    }

    /// <summary>With every ring radius failing, 87's destination is the imp's own position
    /// (<c>0x004808E0</c>), so a walled-in imp still loops 12, 87, a walk resuming 1, and 12 again.</summary>
    [Fact]
    public void AnImpWithNowhereToGo_StillTakesTripsOntoItsOwnSpot()
    {
        (GameDatabase db, WorldState world) = AllClaimed();
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                if (x != 7 || y != 6) world.Tile(x, y).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        var sim = new GameSimulation(db, world, 11, [new ImpTaskSystem(), new CreatureMovementSystem()]);

        var seen = new HashSet<int>();
        for (int t = 0; t < 200; t++) { sim.Tick(); seen.Add(imp.Dk2StateId); }

        Assert.Superset(new HashSet<int> { ImpStates.Resting, ImpStates.Wandering, 6, CreatureStateMachine.Idle }, seen);
        Assert.Equal(new TilePos(7, 6), imp.Tile);
    }

    /// <summary>All claimed floor of the first keeper inside the impenetrable border: nothing to dig,
    /// claim or reinforce.</summary>
    private static (GameDatabase, WorldState) AllClaimed()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        return (db, world);
    }
}
