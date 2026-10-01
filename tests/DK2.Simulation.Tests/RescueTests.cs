// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Freeing comrades from a Torture Chamber (ADR-0201): the contact test
/// <c>0x004E8660</c> sends a creature with <c>Flags</c> bit 5 to 134 when it steps into an
/// enemy's chamber that holds an ally on a device; 134 frees it.</summary>
public class RescueTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Hero = PlayerIds.Good;
    private const byte GoblinId = 7;

    private static readonly TilePos Cell = new(3, 3);
    private static readonly TilePos Wheel = new(8, 3);
    private static readonly TilePos Door = new(6, 3);
    private static readonly TilePos Inside = new(7, 3);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    /// <summary>The keeper's prison at (2..4, 2..4) and Torture Chamber at (7..9, 2..4), a wheel
    /// at (8, 3) with a hero's Goblin captive on it.</summary>
    private static (WorldState World, CreatureState Victim) World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, Hero, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        for (int y = 2; y <= 4; y++)
        {
            for (int x = 2; x <= 4; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
            for (int x = 7; x <= 9; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.TortureChamber), Keeper);
        }

        world.SpawnObject(db, TortureRules.WheelObjectId, Keeper, Index(world, Wheel));
        CreatureState victim = world.SpawnCreature(db, GoblinId, Hero, Cell);
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), victim, Keeper, Index(world, Cell)));
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), victim, Keeper, Index(world, Wheel)));
        return (world, victim);
    }

    private static int Index(WorldState world, TilePos at) => (at.Y * world.Width) + at.X;

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new TortureSystem(),
            new RescueSystem(),
        ]);
        sim.ObserveWith(sink);
        return sim;
    }

    private static CreatureState Rescuer(GameDatabase db, WorldState world, byte owner, byte flags)
    {
        CreatureState rescuer = world.SpawnCreature(db, GoblinId, owner, Door);
        rescuer.Flags = flags;
        return rescuer;
    }

    private static void StepInside(WorldState world, CreatureState creature)
    {
        creature.PosX = CreatureState.CentreOf(Inside.X);
        creature.PosY = CreatureState.CentreOf(Inside.Y);
    }

    [Fact]
    public void AHeroSteppingIntoTheChamberWalksToItsComradeAndFreesIt()
    {
        GameDatabase db = Db;
        (WorldState world, CreatureState victim) = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState hero = Rescuer(db, world, Hero, RescueRules.FreesFriendsFlag);
        sim.Tick();

        StepInside(world, hero);
        sim.Tick();
        Assert.Equal((CreatureActivity.Rescuing, RescueRules.RescuingState), (hero.Activity, hero.Dk2StateId));

        for (int i = 0; i < 200 && TortureRules.IsBeingTortured(victim); i++) sim.Tick();

        Assert.False(TortureRules.IsBeingTortured(victim));
        Assert.False(PrisonRules.IsImprisoned(victim));
        Assert.Equal(Hero, victim.OwnerPlayerId);
        Assert.Equal(CreatureActivity.Idle, hero.Activity);
        // 134 returns 0 and the rescuer is in state 1; 133 drops the victim to 1 as well.
        Assert.Equal((CreatureStateMachine.Idle, CreatureStateMachine.Idle), (hero.Dk2StateId, victim.Dk2StateId));
        Assert.Equal(Wheel, hero.Tile);
        Assert.Contains(sink.Events, e => e.Kind == "rescuer_to_victim");
        Assert.Contains(sink.Events, e => e.Kind == "captive_rescued" && e.Detail.Contains($"victim={victim.Id} "));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void OnTheVictimsTileItWaitsTwoTurnsBeforeFreeingIt()
    {
        // 134 returns 1 while +0x1C is under variable 19 halved.
        GameDatabase db = Db;
        (WorldState world, CreatureState victim) = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState hero = Rescuer(db, world, Hero, RescueRules.FreesFriendsFlag);
        sim.Tick();

        hero.PosX = CreatureState.CentreOf(Wheel.X);
        hero.PosY = CreatureState.CentreOf(Wheel.Y);
        sim.Tick();
        Assert.Equal(CreatureActivity.Rescuing, hero.Activity);
        sim.Tick();
        Assert.True(TortureRules.IsBeingTortured(victim));
        sim.Tick();
        Assert.False(TortureRules.IsBeingTortured(victim));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "rescuer_to_victim");
    }

    [Fact]
    public void WithoutFlagBitFiveNobodyIsFreed()
    {
        GameDatabase db = Db;
        (WorldState world, CreatureState victim) = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState hero = Rescuer(db, world, Hero, (byte)(0xFF & ~RescueRules.FreesFriendsFlag));
        sim.Tick();

        StepInside(world, hero);
        sim.Run(40);

        Assert.True(TortureRules.IsBeingTortured(victim));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "rescue_started");
    }

    [Fact]
    public void ACreatureNotAlliedToTheVictimDoesNotFreeItAndAnAllyDoes()
    {
        GameDatabase db = Db;
        (WorldState world, CreatureState victim) = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState stranger = Rescuer(db, world, PlayerIds.Neutral, RescueRules.FreesFriendsFlag);
        sim.Tick();
        StepInside(world, stranger);
        sim.Run(20);
        Assert.True(TortureRules.IsBeingTortured(victim));

        // The keeper's own chamber: allied with its owner, nothing to free.
        CreatureState own = Rescuer(db, world, Keeper, RescueRules.FreesFriendsFlag);
        sim.Tick();
        StepInside(world, own);
        sim.Run(20);
        Assert.True(TortureRules.IsBeingTortured(victim));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "rescue_started");
    }

    [Fact]
    public void NoRescueInTheChamberOfAKeeperAlliedToTheRescuer()
    {
        // 0x004BF3D0 against the room's owner first.
        GameDatabase db = Db;
        (WorldState world, CreatureState victim) = World(db);
        world.Player(Hero).AlliedWith |= 1 << Keeper;
        world.Player(Keeper).AlliedWith |= 1 << Hero;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState hero = Rescuer(db, world, Hero, RescueRules.FreesFriendsFlag);
        sim.Tick();
        StepInside(world, hero);
        sim.Run(40);

        Assert.True(TortureRules.IsBeingTortured(victim));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "rescue_started");
    }

    [Fact]
    public void TheTestRunsOnContactSoOneAlreadyInsideWaitsForItsNextStep()
    {
        GameDatabase db = Db;
        (WorldState world, CreatureState victim) = World(db);
        TortureRules.Unstrap(new StateHookContext(world, db), victim);
        PrisonRules.Release(new StateHookContext(world, db), victim);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState hero = Rescuer(db, world, Hero, RescueRules.FreesFriendsFlag);
        StepInside(world, hero);
        sim.Tick();

        // A comrade strapped on after it arrived: no step, no test.
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), victim, Keeper, Index(world, Cell)));
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), victim, Keeper, Index(world, Wheel)));
        sim.Run(10);
        Assert.Equal(CreatureActivity.Idle, hero.Activity);

        hero.PosY = CreatureState.CentreOf(Inside.Y - 1);
        sim.Tick();
        Assert.Equal(CreatureActivity.Rescuing, hero.Activity);
    }

    [Fact]
    public void AnImpNeverRescues()
    {
        GameDatabase db = Db;
        (WorldState world, CreatureState victim) = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Hero, Door);
        imp.Flags = RescueRules.FreesFriendsFlag;
        sim.Tick();
        StepInside(world, imp);
        sim.Run(20);

        Assert.True(TortureRules.IsBeingTortured(victim));
    }
}
