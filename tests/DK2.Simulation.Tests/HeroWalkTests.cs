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

        var seen = new HashSet<int>();
        for (int t = 0; t < 40; t++) { sim.Tick(); seen.Add(hero.Dk2StateId); }

        Assert.Contains(HeroJobRules.StandingJobState, seen);
        Assert.Contains(HeroJobRules.WaitingState, seen);
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

        sim.Run(2);
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
        long deadline = hero.StateDeadlineTick;
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
