// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A hero's StealGold, the arm <c>0x00481FA0</c> and states 163–169 and 226 (steal-gold.md, ADR-0219).</summary>
public class StealGoldTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte Knight = Dk2TerrainFixture.KnightCreatureId;
    private const byte Strong = 50;

    [Fact]
    public void AHeroTakesWhatItsPurseHoldsRoundTheWholeTreasury_KeepsHalf_AndIsGoneWhereItWasMade()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, Keeper, 10, 5, [10, 9000, 9000, 9000]);
        CreatureState hero = Thief(db, world, Keeper, new TilePos(3, 5));
        int purse = CreatureLevelRules.MaxGoldHeld(db, db.CreatureById(Knight), 1);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        RunUntilGone(sim, world, hero);

        SimEvent stolen = Assert.Single(sink.Events, e => e.Kind == "gold_stolen");
        Assert.Contains($"taken={purse} kept={purse / 2}", stolen.Detail);
        Assert.Equal(27010 - purse, GoldIn(world, 10, 5));
        SimEvent gone = Assert.Single(sink.Events, e => e.Kind == "creature_escaped");
        Assert.Contains($"at=(3,5) gold_lost={purse / 2} effect=15", gone.Detail);
    }

    /// <summary>163 walks to the first keeper's Treasury whatever the job names; 164 then wants the
    /// job's player's, so a job against the rival loops through idle and never takes a coin.</summary>
    [Fact]
    public void TheRaidIsOnTheFirstKeeper_SoAJobAgainstAnotherTakesNothing()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, Keeper, 10, 5, [500, 500, 500, 500]);
        Treasury(world, Rival, 10, 9, [500, 500, 500, 500]);
        CreatureState hero = Thief(db, world, Rival, new TilePos(3, 5));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        var tiles = new HashSet<TilePos>();
        for (int t = 0; t < 1500; t++) { sim.Tick(); tiles.Add(hero.Tile); }

        Assert.Contains(tiles, p => p.Y is 5 or 6 && p.X is 10 or 11);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "gold_stolen");
        Assert.Equal(2000, GoldIn(world, 10, 5));
        Assert.Equal(2000, GoldIn(world, 10, 9));
    }

    /// <summary>The arm picks the strongest player when the job names none but does not write it
    /// back, so 164 compares the tile with player 0 and fails.</summary>
    [Fact]
    public void AJobNamingNoPlayer_ReachesTheTreasury_ButNeverTakes()
    {
        (GameDatabase db, WorldState world) = OpenWorld(Dk2TerrainFixture.Goblin() with { CreatureId = Strong, Name = "Strong", Unknown_0F4C = 60 });
        Treasury(world, Keeper, 10, 5, [500, 500, 500, 500]);
        world.SpawnCreature(db, Strong, Keeper, new TilePos(12, 8));
        CreatureState hero = Thief(db, world, 0, new TilePos(3, 5));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        var seen = new HashSet<int>();
        for (int t = 0; t < 1500; t++) { sim.Tick(); seen.Add(hero.Dk2StateId); }

        Assert.Contains(StealGoldRules.ToTheTreasuryState, seen);
        Assert.Equal(0, hero.GoodJobPlayerId);
        Assert.Equal(2000, GoldIn(world, 10, 5));
    }

    [Fact]
    public void TheArmFails_WithoutATreasuryOfTheJobsPlayer()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, Keeper, 10, 5, [500, 500, 500, 500]);
        CreatureState hero = Thief(db, world, Rival, new TilePos(3, 5));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Run(2);
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
        Assert.Equal(JobType.None, hero.CurrentJob);
        sim.Tick();
        Assert.Equal(ImpStates.Wandering, hero.Dk2StateId);
        sim.Run(5);
        Assert.DoesNotContain(sink.Events, e => e.Kind is "hero_job" or "gold_stolen");
        Assert.Equal(2000, GoldIn(world, 10, 5));
    }

    [Fact]
    public void TheArmRoutesThroughTheKeepersClosedDoor_AsItsRouteWordHasNoEightyBit()
    {
        // 0x00471740(room, 3): no 0x80, so a foreign door is routed through and fought when met
        // (door-blocking.md). Level 3's Thieves stood 400 turns at their gate behind the corridor
        // doors while ours tested the route with the fight's 0x80 (ADR-0324). A rock wall with the
        // keeper's door as its only gap lies between the Thief and the Treasury.
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, Keeper, 10, 5, [500, 500, 500, 500]);
        WallWithDoor(db, world, 7, new TilePos(7, 5), Keeper);
        CreatureState hero = Thief(db, world, Keeper, new TilePos(3, 5));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick();
        sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "hero_job" && e.Detail.Contains($"creature={hero.Id} job=StealGold", StringComparison.Ordinal));
    }

    [Fact]
    public void TheArmFails_WhenRockWallsTheTreasuryOff()
    {
        // The control for the door test: the same wall with no gap at all is not routed through.
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, Keeper, 10, 5, [500, 500, 500, 500]);
        WallWithDoor(db, world, 7, null, Keeper);
        CreatureState hero = Thief(db, world, Keeper, new TilePos(3, 5));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Tick();
        sim.Tick();

        Assert.DoesNotContain(sink.Events, e => e.Kind == "hero_job");
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
    }

    [Fact]
    public void TheArmFails_AgainstTheHerosOwnPlayer()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, PlayerIds.Good, 10, 5, [500, 500, 500, 500]);
        CreatureState hero = Thief(db, world, PlayerIds.Good, new TilePos(3, 5));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        sim.Run(2);
        Assert.Equal(HeroJobRules.WaitFailedState, hero.Dk2StateId);
        Assert.Equal(JobType.None, hero.CurrentJob);
        sim.Tick();
        Assert.Equal(ImpStates.Wandering, hero.Dk2StateId);
        sim.Run(5);
        Assert.DoesNotContain(sink.Events, e => e.Kind is "hero_job" or "gold_stolen");
        Assert.Equal(2000, GoldIn(world, 10, 5));
    }

    /// <summary>A leader in 164 hands job 13 to its followers in 222, which run it from 226.</summary>
    [Fact]
    public void ALeaderInTheTreasuryRecruitsItsFollowers_WhoStealToo()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, Keeper, 10, 5, [3000, 3000, 3000, 3000]);
        // The follower first in the list, so the leader's recruiting is still visible at the tick's end.
        CreatureState follower = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(2, 5));
        CreatureState leader = Thief(db, world, Keeper, new TilePos(3, 5));
        leader.PartyId = 2;
        leader.Flags = 0x02;
        follower.PartyId = 2;
        follower.Flags = 0x04;
        follower.GoodJob = JobType.Wait;
        HeroPartyRules.LinkFollowers(world, 2);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        var seen = new HashSet<int>();
        for (int t = 0; t < 3000 && world.CreatureById(follower.Id) is not null; t++)
        {
            sim.Tick();
            if (world.CreatureById(follower.Id) is not null) seen.Add(follower.Dk2StateId);
        }

        Assert.Contains(StealGoldRules.RunTheJobState, seen);
        Assert.Equal(JobType.StealGold, follower.GoodJob);
        Assert.Equal(Keeper, follower.GoodJobPlayerId);
        Assert.Contains(sink.Events, e => e.Kind == "gold_stolen" && e.Detail.Contains($"creature={follower.Id} "));
    }

    /// <summary>A keeper's Thief runs job 13 through the same states (its unhappy table's StealGold).
    /// The first keeper's own Thief is refused at 163: 163 raids the first keeper.</summary>
    [Fact]
    public void TheFirstKeepersThief_IsRefusedAt163()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, Keeper, 10, 5, [500, 500, 500, 500]);
        CreatureState thief = world.SpawnCreature(db, Dk2TerrainFixture.ThiefCreatureId, Keeper, new TilePos(3, 5));
        CreatureStateMachine.TrySetState(new StateHookContext(world, db), thief, StealGoldRules.ToTheTreasuryState);
        var sim = new GameSimulation(db, world, 7, [new CreatureJobSystem(), new CreatureMovementSystem()]);

        sim.Tick();

        Assert.NotEqual(StealGoldRules.ToTheTreasuryState, thief.Dk2StateId);
        Assert.False(thief.IsRouted);
        Assert.Equal(2000, GoldIn(world, 10, 5));
    }

    /// <summary>A rival's Thief walks to the first keeper's Treasury, then 164 wants its own keeper's,
    /// so it takes nothing: DK2's job 13 for a keeper's creature.</summary>
    [Fact]
    public void ARivalsThief_WalksToTheFirstKeepersTreasury_AndTakesNothing()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Treasury(world, Keeper, 10, 5, [500, 500, 500, 500]);
        CreatureState thief = world.SpawnCreature(db, Dk2TerrainFixture.ThiefCreatureId, Rival, new TilePos(3, 5));
        CreatureStateMachine.TrySetState(new StateHookContext(world, db), thief, StealGoldRules.ToTheTreasuryState);
        var sim = new GameSimulation(db, world, 7, [new CreatureJobSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        var tiles = new HashSet<TilePos>();
        for (int t = 0; t < 600 && !tiles.Any(p => p.X is 10 or 11 && p.Y is 5 or 6); t++) { sim.Tick(); tiles.Add(thief.Tile); }
        sim.Run(20);

        Assert.Contains(tiles, p => p.X is 10 or 11 && p.Y is 5 or 6);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "gold_stolen");
        Assert.Equal(2000, GoldIn(world, 10, 5));
    }

    [Fact]
    public void ACreatureRemembersTheTileItWasMadeOn()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState c = world.SpawnCreature(db, Knight, PlayerIds.Good, new TilePos(7, 4));
        c.PosX = CreatureState.CentreOf(9);

        Assert.Equal((4 * world.Width) + 7, c.OriginTileIndex);
    }

    private static void RunUntilGone(GameSimulation sim, WorldState world, CreatureState creature)
    {
        for (int t = 0; t < 3000 && world.CreatureById(creature.Id) is not null; t++) sim.Tick();
        Assert.Null(world.CreatureById(creature.Id));
    }

    private static CreatureState Thief(GameDatabase db, WorldState world, byte target, TilePos at)
    {
        CreatureState hero = world.SpawnCreature(db, Knight, PlayerIds.Good, at);
        hero.GoodJob = JobType.StealGold;
        hero.GoodJobPlayerId = target;
        return hero;
    }

    /// <summary>A rock column at <paramref name="x"/> from the top to the bottom of the open world, with
    /// <paramref name="gap"/>, when given, a claimed tile of <paramref name="owner"/> holding a closed
    /// Wooden Door as the only way through.</summary>
    private static void WallWithDoor(GameDatabase db, WorldState world, int x, TilePos? gap, byte owner)
    {
        for (int y = 1; y < world.Height - 1; y++)
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.Rock;
        if (gap is not TilePos door) return;
        ref TileState tile = ref world.Tile(door);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = owner;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = door.X, Y = door.Y, PlayerId = owner, Flags = 0, DoorId = 1, Unknown_0011 = new byte[3] }]);
    }

    /// <summary>A 2x2 Treasury at (x, y), gold in tile-index order.</summary>
    private static void Treasury(WorldState world, byte owner, int x, int y, int[] gold)
    {
        int k = 0;
        for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                ref TileState tile = ref world.Tile(x + dx, y + dy);
                tile.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
                tile.OwnerPlayerId = owner;
                tile.GoldHeld = gold[k++];
            }
    }

    private static int GoldIn(WorldState world, int x, int y) =>
        world.Tile(x, y).GoldHeld + world.Tile(x + 1, y).GoldHeld + world.Tile(x, y + 1).GoldHeld + world.Tile(x + 1, y + 1).GoldHeld;

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, 7, [new HeroSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

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
