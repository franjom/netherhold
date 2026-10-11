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

    /// <summary>StealGold's states are not built, so its arm fails as a job with no target does: state
    /// 4 for twelve turns, then idle and the job again.</summary>
    [Fact]
    public void AJobThatCannotStartWaitsTwelveTurnsInStateFour_ThenTriesAgain()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(5, 5));
        hero.GoodJob = JobType.StealGold;
        GameSimulation sim = Simulation(db, world);

        long entered = -1;
        while (hero.Dk2StateId != HeroJobRules.WaitFailedState && sim.World.CurrentTick.Value < 10)
        {
            entered = sim.World.CurrentTick.Value;
            sim.Tick();
        }
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
        long deadline = hero.StateDeadlineTick;
        Assert.Equal(entered + 12, deadline);
        while (sim.World.CurrentTick.Value <= deadline) { sim.Tick(); Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId); }
        sim.Tick();
        Assert.NotEqual(HeroJobRules.WaitFailedState, hero.Dk2StateId);
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
            CreatureLevelRules.SpeedRaw(db, db.CreatureById(Knight), 1),
            CreatureLevelRules.SpeedRaw(db, db.CreatureById(Dk2TerrainFixture.ImpCreatureId), 1));

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

    /// <summary>Job 19 walks to a point from <c>0x004E4D90</c> in the room on the target's
    /// <c>DungeonHeartPosition</c>: a random tile of the heart, since its cell test accepts every one.</summary>
    [Fact]
    public void KillPlayer_WalksToARandomTileOfTheHeart()
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
            aimed.Add(new TilePos(hero.Body.PathTargetPos.X >> 16, hero.Body.PathTargetPos.Y >> 16));
        }

        Assert.All(aimed, p => Assert.True(p.X is >= 10 and <= 12 && p.Y is >= 4 and <= 6, $"{p} is not a heart tile"));
        Assert.True(aimed.Count > 2, $"only {aimed.Count} tiles aimed at");
    }

    /// <summary>177's fallback (<c>0x004625F7</c>) walks to the <c>DungeonHeartPosition</c> tile
    /// itself, the heart's centre, when the target player has no creature left.</summary>
    [Fact]
    public void KillCreatures_WithNoCreatureLeft_WalksToTheHeartsCentre()
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
        Assert.Equal(new TilePos(11, 5), new TilePos(hero.Body.PathTargetPos.X >> 16, hero.Body.PathTargetPos.Y >> 16));
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
