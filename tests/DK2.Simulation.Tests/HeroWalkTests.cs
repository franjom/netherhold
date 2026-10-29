// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A hero's standing job and its party walk, against hero-party-walk.md (ADR-0216).</summary>
public class HeroWalkTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte Knight = Dk2TerrainFixture.KnightCreatureId;
    private const byte Strong = 50;

    [Fact]
    public void AnIdleHeroRunsItsStandingJob_AndWaitStrollsInsideItsOwnTile()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(5, 5));
        hero.GoodJob = JobType.Wait;
        GameSimulation sim = Simulation(db, world);
        int startX = hero.PosX, startY = hero.PosY;
        bool moved = false;

        var seen = new HashSet<int>();
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            seen.Add(hero.Dk2StateId);
            moved |= hero.PosX != startX || hero.PosY != startY;
            Assert.Equal(new TilePos(5, 5), hero.Tile);
        }

        Assert.Contains(HeroJobRules.StandingJobState, seen);
        Assert.Contains(HeroJobRules.WaitingState, seen);
        Assert.True(moved, "waiting requests must move the hero to a sub-tile point");
        Assert.Equal(new TilePos(5, 5), hero.Tile);
        Assert.InRange(hero.StateDeadlineTick - 2, 50, 84);
        Assert.Equal(JobType.Wait, hero.CurrentJob);
    }

    [Fact]
    public void SendToActionPoint_WalksToThePoint_RoamsItsWaitTime_ThenTheNextWaypoint_ThenStandsIn227()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        ThingProjection.Place(world, db,
        [
            new ActionPointThing(new byte[56]) { StartX = 8, StartY = 5, EndX = 10, EndY = 7, ActionPointId = 3, NextWaypointId = 4, WaitDelay = 2, Name = "" },
            new ActionPointThing(new byte[56]) { StartX = 12, StartY = 5, EndX = 12, EndY = 5, ActionPointId = 4, WaitDelay = 1, Name = "" },
        ]);
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(3, 5));
        hero.GoodJob = JobType.SendToActionPoint;
        hero.GoodJobActionPointId = 3;
        GameSimulation sim = Simulation(db, world);

        var seen = new List<int>();
        for (int t = 0; t < 600 && hero.Dk2StateId != HeroJobRules.NoPointState; t++)
        {
            sim.Tick();
            if (seen.Count == 0 || seen[^1] != hero.Dk2StateId) seen.Add(hero.Dk2StateId);
        }

        Assert.Equal(HeroJobRules.NoPointState, hero.Dk2StateId);
        Assert.Equal(0, hero.GoodJobActionPointId);
        Assert.Contains(HeroJobRules.RoamingState, seen);
        Assert.InRange(hero.Tile.X, 11, 13);
        Assert.InRange(hero.Tile.Y, 4, 6);
    }

    [Fact]
    public void KillCreatures_NamingNoPlayer_TakesTheStrongest_AndWalksToItsFirstCreature()
    {
        (GameDatabase db, WorldState world) = OpenWorld(Dk2TerrainFixture.Goblin() with { CreatureId = Strong, Name = "Strong", Unknown_0F4C = 60 });
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(12, 8));
        CreatureState prey = world.SpawnCreature(db, Strong, Keeper, new TilePos(12, 5));
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(3, 5));
        hero.GoodJob = JobType.KillCreatures;
        GameSimulation sim = Simulation(db, world);

        sim.Run(4);

        Assert.Equal(Keeper, hero.GoodJobPlayerId);
        Assert.Equal(6, hero.Dk2StateId);   // travelling to a point
        Assert.Equal(HeroJobRules.HuntingState, hero.UtilityNewState);
        Assert.Equal(prey.PosX, hero.Body.PathTargetPos.X >> 4);
    }

    [Fact]
    public void AFailedHuntWandersBeforeItsRetryDeadline()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(5, 5));
        hero.GoodJob = JobType.KillCreatures;
        hero.GoodJobPlayerId = Keeper;
        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 10 && hero.Dk2StateId != HeroJobRules.WaitFailedState; i++) sim.Tick();
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
        long deadline = hero.StateDeadlineTick;
        int startX = hero.PosX, startY = hero.PosY;
        sim.Tick();
        Assert.True(world.CurrentTick.Value < deadline);
        Assert.Equal(ImpStates.Wandering, hero.Dk2StateId);
        for (int i = 0; i < 100 && hero.PosX == startX && hero.PosY == startY; i++) sim.Tick();
        Assert.True(hero.PosX != startX || hero.PosY != startY);
        Assert.Equal(JobType.KillCreatures, hero.GoodJob);
    }

    // 00486CF0 starts action 43; 00477850 keeps it on a failed job; 00477680 waits for it before the deadline.
    [Fact]
    public void AWaitingHerosIdleActionOutlivesItsFailedJobDeadline()
    {
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        slots[20] = new CreatureAnimationTiming
        {
            ResourceType = 6, Flags = CreatureAnimationFlags.Present,
            FrameCount = 150, FramesPerSecond = 20,
        };
        (GameDatabase db, WorldState world) = OpenWorld(Dk2TerrainFixture.Knight() with
        {
            CreatureId = Strong, ArtSlots = slots,
        });
        CreatureState hero = world.SpawnCreature(db, Strong, PlayerIds.Good, new TilePos(5, 5));
        StatePlacement.Place(hero, HeroJobRules.WaitingState);
        hero.StateDeadlineTick = 2;
        GameSimulation sim = Simulation(db, world);

        sim.Tick();
        long actionEnd = hero.ActionUntilTick;
        Assert.True(actionEnd > HeroJobRules.RetryTurns);
        sim.Run(3);
        Assert.Equal(HeroJobRules.WaitingState, hero.Dk2StateId);
        Assert.Equal(new TilePos(5, 5), hero.Tile);

        hero.GoodJob = JobType.KillCreatures;
        hero.GoodJobPlayerId = Keeper;
        StatePlacement.Place(hero, HeroJobRules.StandingJobState);
        sim.Tick();
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
        long retry = hero.StateDeadlineTick;
        Assert.True(retry < actionEnd);
        while (sim.CurrentTick.Value <= retry) sim.Tick();
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
        Assert.Equal(actionEnd, hero.ActionUntilTick);
        while (sim.CurrentTick.Value <= actionEnd) sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, hero.Dk2StateId);
    }

    [Fact]
    public void AFailedJobWaitKeepsItsActionThenExpiresAtItsDeadline()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(5, 5));
        StatePlacement.Place(hero, HeroJobRules.WaitFailedState);
        hero.StateDeadlineTick = 3;
        hero.ActionUntilTick = 3;
        GameSimulation sim = Simulation(db, world);
        sim.Run(3);
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
        sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, hero.Dk2StateId);
    }

    // 00486CF0: with no action running and the turn not above +0x7E, action 43 starts again.
    [Fact]
    public void AWaitingHeroRestartsItsIdleActionWhileTheDeadlineIsLive()
    {
        (GameSimulation sim, CreatureState hero) = WaitingHeroWithIdleArt(deadline: 5, actionUntil: 5);
        int x = hero.PosX, y = hero.PosY;

        sim.Run(5);
        Assert.Equal(5, hero.ActionUntilTick);
        sim.Tick(); // turn 5: the action has ended and 5 is not above the deadline

        Assert.Equal(HeroJobRules.WaitingState, hero.Dk2StateId);
        Assert.True(hero.ActionUntilTick > 6, $"action 43 should restart, until={hero.ActionUntilTick}");
        Assert.Equal(5, hero.StateDeadlineTick);
        Assert.Equal((x, y), (hero.PosX, hero.PosY));
    }

    // 00486CF0: once the action ends on a turn above +0x7E, the walk arm runs, not the action.
    [Fact]
    public void AWaitingHeroWalksWhenItsActionEndsAfterTheDeadline()
    {
        (GameSimulation sim, CreatureState hero) = WaitingHeroWithIdleArt(deadline: 4, actionUntil: 5);

        sim.Run(5);
        Assert.Equal(HeroJobRules.WaitingState, hero.Dk2StateId);
        Assert.Equal(4, hero.StateDeadlineTick);
        sim.Tick(); // turn 5: the action has ended and 5 is above the deadline

        Assert.Equal(6, hero.Dk2StateId);   // travelling to a point
        Assert.Equal(HeroJobRules.WaitingState, hero.UtilityNewState);
        Assert.InRange(hero.StateDeadlineTick, 5 + 50, 5 + 84);
        // The route helper ends the action (0x00471890, Q671); it had run out already, so only the stamp moves.
        Assert.Equal(0, hero.ActionUntilTick);
    }

    private static (GameSimulation, CreatureState) WaitingHeroWithIdleArt(long deadline, long actionUntil)
    {
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        slots[20] = new CreatureAnimationTiming
        {
            ResourceType = 6, Flags = CreatureAnimationFlags.Present,
            FrameCount = 150, FramesPerSecond = 20,
        };
        (GameDatabase db, WorldState world) = OpenWorld(Dk2TerrainFixture.Knight() with
        {
            CreatureId = Strong, ArtSlots = slots,
        });
        CreatureState hero = world.SpawnCreature(db, Strong, PlayerIds.Good, new TilePos(5, 5));
        StatePlacement.Place(hero, HeroJobRules.WaitingState);
        hero.StateDeadlineTick = deadline;
        hero.ActionUntilTick = actionUntil;
        return (Simulation(db, world), hero);
    }

    /// <summary>DK2's facing test (<c>h &gt; 0x700 &amp;&amp; h &lt; 0x100</c>) never picks direction 0,
    /// so a leader facing 0 is trailed from direction 3; the depth is half the follower's number,
    /// at least one; a blocked step turns a quarter.</summary>
    [Theory]
    [InlineData(0x000, 1, 9, 5)]
    [InlineData(0x200, 1, 7, 5)]
    [InlineData(0x400, 1, 8, 4)]
    [InlineData(0x600, 1, 9, 5)]
    [InlineData(0x200, 4, 6, 5)]
    public void AFollowerTrailsItsLeader_WithDk2sUnreachableDirectionZero(int heading, byte position, int x, int y)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        CreatureState follower = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(3, 5));
        leader.Heading = heading;
        follower.PositionInParty = position;

        Assert.Equal((x, y), HeroJobRules.FollowTile(world, db, follower, leader));
    }

    [Fact]
    public void FollowersChooseBeforeTheLeaderStartsItsNextWalk()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        ThingProjection.Place(world, db,
        [
            new ActionPointThing(new byte[56]) { StartX = 8, StartY = 9, EndX = 8, EndY = 9, ActionPointId = 3, Name = "" },
        ]);
        CreatureState leader = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        leader.GoodJob = JobType.SendToActionPoint;
        leader.GoodJobActionPointId = 3;
        StatePlacement.Place(leader, HeroJobRules.ToThePointState);
        CreatureState follower = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        follower.LeaderId = leader.Id;
        follower.PositionInParty = 1;
        StatePlacement.Place(follower, HeroJobRules.FollowingState);
        CreatureState[] storage = world.Creatures.ToArray();

        new GameSimulation(db, world, 7, [new HeroSystem()]).Tick();

        Assert.Equal(new TilePos(9, 5), follower.Destination);
        Assert.Equal(new TilePos(8, 9), leader.Destination);
        Assert.Equal(storage, world.Creatures);
    }

    [Fact]
    public void PartyFollowersAreNumberedNewestFirst()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        leader.PartyId = 1;
        leader.Flags = 2;
        CreatureState[] followers = Enumerable.Range(0, 3).Select(_ =>
        {
            CreatureState follower = world.SpawnCreature(db, Knight, PlayerIds.Good, leader.Tile);
            follower.PartyId = 1;
            follower.Flags = 4;
            return follower;
        }).ToArray();

        HeroPartyRules.LinkFollowers(world, 1);

        Assert.Equal(new byte[] { 3, 2, 1 }, followers.Select(c => c.PositionInParty));
        Assert.All(followers, c => Assert.Equal(leader.Id, c.LeaderId));
        Assert.Equal((byte)3, leader.PositionInParty);
    }

    [Fact]
    public void RemovingANewerHeroDoesNotSkipAnOlderHerosTurn()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState survivor = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        CreatureState escaping = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(9, 5));
        StatePlacement.Place(escaping, StealGoldRules.EscapingState);
        CreatureState waiting = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(10, 5));
        StatePlacement.Place(waiting, HeroJobRules.StandingJobState);
        waiting.GoodJob = JobType.Wait;
        StatePlacement.Place(survivor, CreatureStateMachine.Idle);

        new GameSimulation(db, world, 7, [new HeroSystem()]).Tick();

        Assert.Null(world.CreatureById(escaping.Id));
        Assert.Equal(HeroJobRules.StandingJobState, survivor.Dk2StateId);
        Assert.Equal(HeroJobRules.WaitingState, waiting.Dk2StateId);
        Assert.Equal(new[] { survivor, waiting }, world.Creatures);
    }

    [Fact]
    public void ABlockedTrailingStepTurnsAQuarter()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        CreatureState follower = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(3, 5));
        leader.Heading = 0x200;
        follower.PositionInParty = 1;
        world.Tile(7, 5).TerrainId = Dk2TerrainFixture.Rock;

        Assert.Equal((8, 6), HeroJobRules.FollowTile(world, db, follower, leader));
    }

    [Fact]
    public void APartyWalksAtItsSlowestMembersPace_UntilItRuns()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(5, 5));
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, PlayerIds.Good, new TilePos(6, 5));
        knight.PartyId = 2;
        imp.PartyId = 2;
        int slowest = Math.Min(
            db.CreatureById(Knight).Speed.Raw,
            db.CreatureById(Dk2TerrainFixture.ImpCreatureId).Speed.Raw);

        HeroPartyRules.SetPace(world, db, 2);

        Assert.Equal(slowest, knight.PartySpeedRaw);
        Assert.Equal(slowest, imp.PartySpeedRaw);
        knight.Running = true;
        new GameSimulation(db, world, 1, [new CreatureMovementSystem()]).Tick();
        Assert.Equal(0, knight.PartySpeedRaw);
        Assert.Equal(slowest, imp.PartySpeedRaw);
    }

    /// <summary>DK2's state 1 tests the hero owner before the worker flag (<c>0x004773D0</c>), so a
    /// hero's worker is a hero: from its resting 10 it idles, goes to 90 and, as a follower, to 222.
    /// The imp task system leaves it alone.</summary>
    [Fact]
    public void AHeroWorkerIsAHero_ItFollowsItsLeaderAndTakesNoImpTask()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        leader.GoodJob = JobType.Wait;
        CreatureState dwarf = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, PlayerIds.Good, new TilePos(3, 5));
        dwarf.Flags = 0x04;
        dwarf.LeaderId = leader.Id;
        dwarf.PositionInParty = 1;
        StatePlacement.Place(dwarf, ImpStates.TaskSelection);
        int beside = (5 * world.Width) + 4;
        world.TileAtIndex(beside).OwnerPlayerId = PlayerIds.Good;
        FloorGold.Drop(world, db, beside, 100);
        var sim = new GameSimulation(db, world, 7, [new HeroSystem(), new ImpTaskSystem(), new CreatureMovementSystem()]);

        var seen = new HashSet<int>();
        for (int t = 0; t < 12; t++) { sim.Tick(); seen.Add(dwarf.Dk2StateId == 6 ? dwarf.UtilityNewState : dwarf.Dk2StateId); }

        Assert.Contains(HeroJobRules.StandingJobState, seen);
        Assert.Contains(HeroJobRules.FollowingState, seen);
        Assert.Equal(ImpTaskKind.None, dwarf.LastTaskKind);
        Assert.Equal(0, dwarf.GoldHeld);
    }

    /// <summary>State 90 sends a follower to 222 and never runs the follower's own standing job.</summary>
    [Fact]
    public void AFollowerInNinetyTrailsItsLeader_AndNeverRunsItsOwnJob()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        leader.GoodJob = JobType.Wait;
        CreatureState follower = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(3, 5));
        follower.GoodJob = JobType.Wait;
        follower.Flags = 0x04;
        follower.LeaderId = leader.Id;
        follower.PositionInParty = 1;
        GameSimulation sim = Simulation(db, world);

        var seen = new HashSet<int>();
        for (int t = 0; t < 30; t++) { sim.Tick(); seen.Add(follower.Dk2StateId == 6 ? follower.UtilityNewState : follower.Dk2StateId); }

        Assert.Contains(HeroJobRules.FollowingState, seen);
        Assert.DoesNotContain(HeroJobRules.WaitingState, seen);
        Assert.Equal(JobType.None, follower.CurrentJob);
    }

    [Fact]
    public void AFollowerAlreadyInItsTrailingTileStillWalksBetweenSubTilePoints()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        leader.GoodJob = JobType.SendToActionPoint;
        CreatureState follower = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(7, 5));
        follower.Flags = 0x04;
        follower.LeaderId = leader.Id;
        follower.PositionInParty = 1;
        GameSimulation sim = Simulation(db, world);
        var positions = new HashSet<(int, int)>();
        for (int t = 0; t < 120; t++)
        {
            sim.Tick();
            if (t >= 60) positions.Add((follower.PosX, follower.PosY));
        }
        Assert.Equal(HeroJobRules.NoPointState, leader.Dk2StateId);
        Assert.True(positions.Count > 5, "the follower must keep moving after reaching its trailing tile");
    }

    /// <summary>One handler a turn: the turn a walk ends the hero is in the state it resumes, which
    /// runs from the next turn. A follower that has reached its point is in 222 before it walks again.</summary>
    [Fact]
    public void AWalksEndHandsOverToItsStateWhichRunsFromTheNextTurn()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState leader = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(8, 5));
        leader.GoodJob = JobType.Wait;
        CreatureState follower = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(6, 5));
        follower.Flags = 0x04;
        follower.LeaderId = leader.Id;
        follower.PositionInParty = 1;
        GameSimulation sim = Simulation(db, world);

        bool walked = false;
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            if (follower.Dk2StateId == 6) { walked = true; continue; }
            if (!walked) continue;
            Assert.Equal(HeroJobRules.FollowingState, follower.Dk2StateId);
            return;
        }

        Assert.Fail("the follower never finished a walk");
    }

    /// <summary><c>0x0050EA20</c> keeps a sum only when it is strictly greater: on a tie the first
    /// player in order wins.</summary>
    [Fact]
    public void TheStrongestPlayerOnATieIsTheFirstInOrder()
    {
        (GameDatabase db, WorldState world) = OpenWorld(Dk2TerrainFixture.Goblin() with { CreatureId = Strong, Name = "Strong", Unknown_0F4C = 60 });
        world.SpawnCreature(db, Strong, Rival, new TilePos(12, 8));
        world.SpawnCreature(db, Strong, Keeper, new TilePos(12, 5));

        Assert.Equal(Keeper, HeroJobRules.StrongestPlayer(world, db));
    }

    /// <summary>The keepers' choosers never see a hero: DK2's state 1 sends it to 90 instead.</summary>
    [Fact]
    public void TheJobSystemLeavesAHeroAlone()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(5, 5));
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 7, [new CreatureJobSystem()]);
        sim.ObserveWith(sink);

        sim.Run(SimulationTiming.TicksPerSecond * 12);

        Assert.DoesNotContain(sink.Events, e => e.System == "CreatureJob" && e.Detail.Contains($"creature={hero.Id} "));
    }

    /// <summary>Job 19 chooses a random Heart tile; only core destinations are moved.</summary>
    [Fact]
    public void KillPlayer_KeepsRandomHeartDestinationsOutsideTheCore()
    {
        var aimed = new HashSet<TilePos>();
        for (ulong seed = 1; seed <= 24; seed++)
        {
            (GameDatabase db, WorldState world) = OpenWorld();
            Heart(world, Keeper, 10, 4);
            CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(3, 5));
            hero.GoodJob = JobType.KillPlayer;
            hero.GoodJobPlayerId = Keeper;
            var sim = new GameSimulation(db, world, seed, [new HeroSystem(), new CreatureMovementSystem()]);
            for (int t = 0; t < 8 && hero.UtilityNewState != HeroJobRules.AtTheHeartState; t++) sim.Tick();

            Assert.Equal(HeroJobRules.AtTheHeartState, hero.UtilityNewState);
            var centre = new DK2.Simulation.Steering.Fixed16Vector(CreatureState.CentreOf(11) << 4, CreatureState.CentreOf(5) << 4);
            Assert.True((hero.Body.PathTargetPos - centre).Length() >= 1 << 15);
            aimed.Add(new TilePos(hero.Body.PathTargetPos.X >> 16, hero.Body.PathTargetPos.Y >> 16));
        }

        Assert.All(aimed, p => Assert.True(p.X is >= 10 and <= 12 && p.Y is >= 4 and <= 6));
        Assert.True(aimed.Count > 2, $"only {aimed.Count} tiles aimed at");
    }

    /// <summary>177's fallback (<c>0x004625F7</c>) walks to the <c>DungeonHeartPosition</c> tile
    /// itself, the heart's centre, when the target player has no creature left.
    /// The core guard resolves that target outside the central object's disk.</summary>
    [Fact]
    public void KillCreatures_WithNoCreatureLeft_WalksToTheLiveHeartsEdge()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Heart(world, Keeper, 10, 4);
        CreatureState prey = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 8));
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(3, 5));
        hero.GoodJob = JobType.KillCreatures;
        hero.GoodJobPlayerId = Keeper;
        GameSimulation sim = Simulation(db, world);

        for (int t = 0; t < 600 && hero.Dk2StateId != HeroJobRules.HuntingState; t++) sim.Tick();
        Assert.Equal(HeroJobRules.HuntingState, hero.Dk2StateId);
        world.RemoveCreature(prey.Id);
        for (int t = 0; t < 8 && hero.UtilityNewState != HeroJobRules.AtTheHeartState; t++) sim.Tick();

        Assert.Equal(HeroJobRules.AtTheHeartState, hero.UtilityNewState);
        var centre = new DK2.Simulation.Steering.Fixed16Vector(CreatureState.CentreOf(11) << 4, CreatureState.CentreOf(5) << 4);
        Assert.InRange((hero.Body.PathTargetPos - centre).Length(), 1 << 15, 3 << 14);
    }

    /// <summary>A 3x3 heart with its top-left at (x, y).</summary>
    private static void Heart(WorldState world, byte owner, int x, int y)
    {
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
            {
                ref TileState tile = ref world.Tile(x + dx, y + dy);
                tile.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
                tile.OwnerPlayerId = owner;
            }
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world) =>
        new(db, world, 7, [new HeroSystem(), new CreatureMovementSystem()]);

    private static (GameDatabase, WorldState) OpenWorld(params CreatureDef[] extra)
    {
        GameDatabase db = Dk2TerrainFixture.Database(extra);
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                if (world.Tile(x, y).TerrainId != Dk2TerrainFixture.ClaimedPath)
                    world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }
}
