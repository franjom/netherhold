// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// A hungry creature walks to a hatchery and eats a chicken (ADR-0067), read
/// from the original's own <c>HungerState.cpp</c>.
///
/// <para>The oldest "cannot" in the project: a creature got hungry, and the
/// only thing that ever happened next was anger.</para>
/// </summary>
public class EatingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const int Second = SimulationTiming.TicksPerSecond;
    private const byte GoblinId = 7;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(
            db, world, seed: 42,
            [new CreatureMovementSystem(), new CreatureEatSystem()]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>
    /// Claims floor over a rectangle. The fixture map is rock but for a 3x3
    /// room at (1..3, 1..3), so anything that walks has to be given somewhere
    /// to walk.
    /// </summary>
    private static void Floor(
        WorldState world, int x, int y, int w, int h, byte owner = Keeper)
    {
        for (int j = y; j < y + h; j++)
        {
            for (int i = x; i < x + w; i++)
            {
                ref TileState tile = ref world.Tile(i, j);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = owner;
            }
        }
    }

    /// <summary>Paints a w-by-h hatchery with its top-left corner at (x,y).</summary>
    private static RoomInstance Hatchery(
        GameDatabase db, WorldState world, int x, int y, int w, int h, byte owner)
    {
        for (int j = y; j < y + h; j++)
        {
            for (int i = x; i < x + w; i++)
            {
                ref TileState tile = ref world.Tile(i, j);
                RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Hatchery), owner);
            }
        }

        return RoomIndex.Build(world, db)
            .Single(r => r.Definition.TypeId == RoomTypeId.Hatchery
                         && r.OwnerPlayerId == owner
                         && r.TileIndices.Contains((y * world.Width) + x));
    }

    private static ObjectState Chicken(
        GameDatabase db, WorldState world, int x, int y, byte owner = Keeper) =>
        world.SpawnObject(
            db, HatcheryRules.ChickenObjectId, owner, (y * world.Width) + x);

    /// <summary>Makes this creature owe exactly one chicken, without waiting three minutes.</summary>
    private static void MakeHungry(CreatureState creature, int need = 1) =>
        creature.HungerNeed = need;

    // ---- the clock ----------------------------------------------------------

    [Fact]
    public void HungerAccumulatesAndIsNotCapped()
    {
        // The original does HungerNeed += HungerFill on a period and caps
        // nothing, so what a creature owes is a count of the helpings it has
        // been given rather than a flag.
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        var state = new CreatureState
        {
            Id = 1, CreatureTypeId = GoblinId, OwnerPlayerId = Keeper, Health = 1500,
        };

        int period = SimulationTiming.TicksFromSeconds(
            EatingRules.HungerRateSeconds(goblin));

        for (int helping = 1; helping <= 5; helping++)
        {
            state.HungerTicks = period;
            Assert.True(EatingRules.Tick(db, goblin, state));
            Assert.Equal(helping, state.HungerNeed);
            Assert.Equal(0, state.HungerTicks);
        }
    }

    [Fact]
    public void AnImpIsNeverHungryBecauseItsHungerFillIsZero()
    {
        GameDatabase db = Db;
        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        var state = new CreatureState
        {
            Id = 1, CreatureTypeId = Dk2TerrainFixture.ImpCreatureId,
            OwnerPlayerId = Keeper, Health = 100, HungerTicks = 999_999,
        };

        Assert.False(EatingRules.Tick(db, imp, state));
        Assert.Equal(0, state.HungerNeed);
        Assert.False(EatingRules.IsHungry(db, imp, state));
    }

    // ---- the walk -----------------------------------------------------------

    [Fact]
    public void AHungryGoblinWalksToTheHatcheryAndEatsTheChicken()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);
        ObjectState bird = Chicken(db, world, 7, 7);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Health = 500;
        MakeHungry(goblin);

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 40);

        Assert.Null(world.ObjectById(bird.Id));
        Assert.Equal(875, goblin.Health);                 // 375 from the chicken
        Assert.Equal(0, goblin.HungerNeed);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Contains(sink.Events, e => e.Kind == "chicken_eaten");
    }

    [Fact]
    public void TheWalksAreDk2sStateSevenToTheRoomAndStateSixToTheBird()
    {
        // ADR-0176: eating is the first errand on DK2's own state ids, and this is the
        // trace travel-states.md 8 reads out of the binary - 91 walks to the room in
        // state 7 resuming in 92, and 92 walks to the bird in state 6 resuming in 93.
        // States inside one tick are transient, so the saved caller is the evidence:
        // StateBeforeUtilityState is DK2's own record of where each walk came from.
        // The bird is in a corner so the second walk is long enough to be seen.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);
        Chicken(db, world, 8, 8);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Health = 500;
        MakeHungry(goblin);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        var seen = new List<(int State, int Saved)>();
        int last = goblin.Dk2StateId;
        for (int t = 0; t < Second * 40; t++)
        {
            sim.Run(1);
            if (goblin.Dk2StateId == last) continue;
            seen.Add((goblin.Dk2StateId, goblin.StateBeforeUtilityState));
            last = goblin.Dk2StateId;
        }

        Assert.Equal(
            [(7, EatingRules.FindHatcheryState), (6, EatingRules.InHatcheryState),
             (CreatureStateMachine.NoState, EatingRules.InHatcheryState)],
            seen);

        // Every arrival put the resume slot back to 1, and nothing was refused: the whole
        // chain is legal under DK2's rule, so a refusal would mean the mapping is wrong.
        Assert.Equal(CreatureStateMachine.Idle, goblin.UtilityNewState);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
        Assert.Contains(sink.Events, e => e.Kind == "chicken_eaten");
    }

    [Fact]
    public void AGoblinPulledOutOfTheChainGivesItsStateIdBack()
    {
        // The migration rule (ADR-0176): a creature holds a state id only while it is in
        // a retired activity. The hand, a fight or a death still set Activity directly,
        // so the eat system hands the id back the next time it sees the creature.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);
        Chicken(db, world, 8, 8);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Health = 500;
        MakeHungry(goblin);

        GameSimulation sim = Simulation(db, world);
        while (goblin.Dk2StateId != 7) sim.Run(1);

        goblin.Activity = CreatureActivity.Leaving;
        sim.Run(1);

        Assert.Equal(CreatureStateMachine.NoState, goblin.Dk2StateId);
    }

    [Fact]
    public void AGoblinStandingInTheHatcheryDoesNotNeedToWalkAnywhere()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);
        Chicken(db, world, 7, 7);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(7, 7));
        goblin.Health = 500;
        MakeHungry(goblin);

        Simulation(db, world).Run(Second);

        Assert.Equal(875, goblin.Health);
        Assert.Equal(0, goblin.HungerNeed);
    }

    [Fact]
    public void AnEmptyHatcheryIsNotAFailureAndTheGoblinWaitsInIt()
    {
        // The original's own answer: its search state re-enters itself when
        // nothing turns up. The room lays, so standing in it is the right thing
        // to do.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);

        // A coop, because the original will not set out for a hatchery whose
        // object list is empty and a coop is what normally fills it. Birdless,
        // not empty - see TheGoblinWillNotWalkToAHatcheryThatHoldsNothingAtAll.
        world.SpawnObject(
            db, HatcheryRules.CoopObjectId, Keeper, (6 * world.Width) + 6);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        MakeHungry(goblin);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Run(Second * 40);

        Assert.Equal(CreatureActivity.SeekingFood, goblin.Activity);
        Assert.True(EatingRules.HatcheryUnder(world, db, goblin) is not null);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "food_attempt_failed");

        // And a bird laid while it stands there is eaten without a new decision.
        Chicken(db, world, 8, 8);
        sim.Run(Second * 5);
        Assert.Equal(0, goblin.HungerNeed);
    }

    [Fact]
    public void TheGoblinWillNotWalkToAHatcheryThatHoldsNothingAtAll()
    {
        // The original's gate is the room's OBJECT LIST rather than its flock,
        // and the room dump names that field. A coop satisfies it, so this rules
        // out exactly one thing: a hatchery too small to earn a coop, which can
        // therefore never lay. Walking to one would be walking somewhere food
        // will never appear.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        MakeHungry(goblin);

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 5);

        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(1, goblin.HungerNeed);
        Assert.Contains(
            sink.Events,
            e => e.Kind == "food_attempt_failed"
                 && e.Detail.Contains("reason=hatchery_empty")
                 && e.Detail.Contains("hatcheries=1"));
    }

    [Fact]
    public void WithNoHatcheryAtAllTheGoblinSaysSoOnceASecondAndStaysHungry()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        MakeHungry(goblin);

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 10);

        Assert.Equal(1, goblin.HungerNeed);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);

        // Once a game second, not once a tick: EatingRules.RetryTicks is the
        // original's LastAttemptedHunger + 4.
        int failures = sink.Events.Count(e => e.Kind == "food_attempt_failed");
        Assert.InRange(failures, 9, 10);
        Assert.All(
            sink.Events.Where(e => e.Kind == "food_attempt_failed"),
            e => Assert.Contains("reason=no_hatchery", e.Detail));
    }

    [Fact]
    public void ARivalsHatcheryIsNoUseAtAll()
    {
        // The original passes "allow allied" as false on the tile test, so even
        // a friend's hatchery does not feed you — and a rival's certainly does
        // not.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Rival);
        Chicken(db, world, 7, 7, Rival);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        MakeHungry(goblin);

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 10);

        Assert.Equal(1, goblin.HungerNeed);
        Assert.Contains(
            sink.Events,
            e => e.Kind == "food_attempt_failed" && e.Detail.Contains("reason=no_hatchery"));
    }

    // ---- choosing the bird --------------------------------------------------

    [Fact]
    public void TheNearestChickenWinsByStraightLineDistanceAndNotByPathLength()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        RoomInstance room = Hatchery(db, world, 5, 5, 5, 5, Keeper);

        ObjectState far = Chicken(db, world, 9, 9);
        ObjectState near = Chicken(db, world, 6, 6);

        // Spawn order puts the far one first, so an implementation that took the
        // first it met would take the wrong bird.
        Assert.True(far.Id < near.Id);
        Assert.Same(near, EatingRules.NearestChickenIn(world, room, new TilePos(5, 5)));
    }

    [Fact]
    public void ATieGoesToTheOlderChicken()
    {
        // Reproducibility rather than fidelity: the original takes whichever its
        // own tile chain reaches first, and ours settles on spawn order so that
        // the answer does not depend on how the room was painted (ADR-0001).
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        RoomInstance room = Hatchery(db, world, 5, 5, 5, 5, Keeper);

        ObjectState first = Chicken(db, world, 6, 7);
        Chicken(db, world, 8, 7);

        Assert.Same(first, EatingRules.NearestChickenIn(world, room, new TilePos(7, 7)));
    }

    [Fact]
    public void AChickenOutsideTheRoomIsNotOnTheMenu()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        RoomInstance room = Hatchery(db, world, 6, 6, 3, 3, Keeper);
        Chicken(db, world, 2, 2);

        Assert.Null(EatingRules.NearestChickenIn(world, room, new TilePos(7, 7)));
    }

    [Fact]
    public void AChickenInSomebodysHandsIsNotOnTheMenuEither()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        RoomInstance room = Hatchery(db, world, 6, 6, 3, 3, Keeper);
        ObjectState bird = Chicken(db, world, 7, 7);
        bird.CarriedByCreatureId = 99;

        Assert.Null(EatingRules.NearestChickenIn(world, room, new TilePos(7, 7)));
    }

    // ---- the race -----------------------------------------------------------

    [Fact]
    public void TwoHungryGoblinsRaceForOneChickenAndOnlyOneOfThemEatsIt()
    {
        // CONFIRMED by the owner for prisoners (Q172): "they rush toward that
        // chicken, the first one to it gets it". Nothing is reserved, and this
        // is the test that says so — both creatures pick the same bird.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 5, 5, 5, 5, Keeper);
        ObjectState bird = Chicken(db, world, 5, 5);

        CreatureState near = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(7, 5));
        CreatureState far = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(9, 9));
        near.Health = 500;
        far.Health = 500;
        MakeHungry(near);
        MakeHungry(far);

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 30);

        Assert.Null(world.ObjectById(bird.Id));
        Assert.Equal(0, near.HungerNeed);
        Assert.Equal(1, far.HungerNeed);            // still owes one
        Assert.Equal(875, near.Health);
        Assert.Equal(500, far.Health);              // and got nothing for the walk

        // The loser is told the bird went, and is left standing in the hatchery
        // looking for another rather than dropped back to idle.
        Assert.Contains(sink.Events, e => e.Kind == "chicken_lost");
        Assert.Equal(CreatureActivity.SeekingFood, far.Activity);
    }

    [Fact]
    public void ABileDemonEatsFiveBirdsInARowWithoutDecidingFiveTimes()
    {
        // HungerFill is 5 for a Bile Demon and each chicken pays off exactly
        // one, so a full meal is five birds — the asymmetry with the prison,
        // where one bird pays off the lot, is the next test.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 4, 4, 5, 5, Keeper);
        for (int i = 0; i < 5; i++) Chicken(db, world, 4 + i, 6);

        CreatureDef demon = db.CreatureById(Dk2TerrainFixture.BileDemonCreatureId);
        Assert.Equal(5, demon.HungerFill);

        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(6, 6));
        state.Health = 1;
        state.HungerNeed = 5;

        Simulation(db, world).Run(Second * 30);

        Assert.Equal(0, state.HungerNeed);
        Assert.DoesNotContain(
            world.Objects, o => o.ObjectTypeId == HatcheryRules.ChickenObjectId);
    }

    [Fact]
    public void OneBirdFillsAPrisonerWhereFiveWouldFillAFreeCreature()
    {
        // Not a tidier version of the same rule: the original's prison state
        // passes the level-scaled HungerFill to the same subtract that its
        // hatchery state passes a literal 1 to. Reproduced rather than
        // reconciled.
        GameDatabase db = Db;
        CreatureDef demon = db.CreatureById(Dk2TerrainFixture.BileDemonCreatureId);
        var free = new CreatureState
        {
            Id = 1, CreatureTypeId = demon.CreatureId,
            OwnerPlayerId = Keeper, Health = 1, HungerNeed = 5,
        };
        var caged = new CreatureState
        {
            Id = 2, CreatureTypeId = demon.CreatureId,
            OwnerPlayerId = Keeper, Health = 1, HungerNeed = 5,
        };

        EatingRules.Eat(db, demon, free);
        EatingRules.EatInPrison(db, demon, caged);

        Assert.Equal(4, free.HungerNeed);
        Assert.Equal(0, caged.HungerNeed);
    }

    // ---- health -------------------------------------------------------------

    [Fact]
    public void AChickenNeverHealsPastTheLevelledMaximum()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        var state = new CreatureState
        {
            Id = 1, CreatureTypeId = GoblinId, OwnerPlayerId = Keeper,
            Health = 1400, HungerNeed = 1,
        };

        int gained = EatingRules.Eat(db, goblin, state);

        Assert.Equal(100, gained);              // not the record's 375
        Assert.Equal(1500, state.Health);
    }

    [Fact]
    public void EatingRestartsTheHungerClockEveryTimeAndNotOnlyWhenFull()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        var state = new CreatureState
        {
            Id = 1, CreatureTypeId = GoblinId, OwnerPlayerId = Keeper,
            Health = 100, HungerNeed = 3, HungerTicks = 999,
        };

        EatingRules.Eat(db, goblin, state);

        Assert.Equal(2, state.HungerNeed);      // still owes two
        Assert.Equal(0, state.HungerTicks);     // and the clock still restarted
    }

    // ---- what this does not do ----------------------------------------------

    [Fact]
    public void ASleepingGoblinIsLeftAsleepEvenWhenItIsHungry()
    {
        // The ORIGINAL'S rule, and not a narrowing (Q176, read from the binary
        // 2026-09-06). Its needs routine's first line refuses outright when the
        // current state's category byte is 7, and the three sleep states carry
        // 7 - corroborated by the sleep block separately refusing to fire while
        // the creature is already in the sleeping category. A hungry creature
        // does not get up to eat.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);
        Chicken(db, world, 7, 7);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Activity = CreatureActivity.Sleeping;
        MakeHungry(goblin);

        Simulation(db, world).Run(Second * 20);

        Assert.Equal(CreatureActivity.Sleeping, goblin.Activity);
        Assert.Equal(1, goblin.HungerNeed);
    }

    [Fact]
    public void APrisonerDoesNotEatHereAtAll()
    {
        // Prisoners eat by a different state in the original, and cannot move
        // here yet (Q172). Their second belongs to PrisonSystem.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);
        Chicken(db, world, 7, 7);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Rival, new TilePos(7, 7));
        goblin.Activity = CreatureActivity.Imprisoned;
        goblin.PrisonOwnerPlayerId = Keeper;
        MakeHungry(goblin);

        Simulation(db, world).Run(Second * 20);

        Assert.Equal(1, goblin.HungerNeed);
        Assert.NotNull(world.Objects.FirstOrDefault(
            o => o.ObjectTypeId == HatcheryRules.ChickenObjectId));
    }

    [Fact]
    public void ACreatureWalkingOutOfTheDungeonDoesNotStopForLunch()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        Hatchery(db, world, 6, 6, 3, 3, Keeper);
        Chicken(db, world, 7, 7);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Activity = CreatureActivity.Leaving;
        MakeHungry(goblin);

        Simulation(db, world).Run(Second * 20);

        Assert.Equal(CreatureActivity.Leaving, goblin.Activity);
        Assert.Equal(1, goblin.HungerNeed);
    }

    // ---- the flag and the id ------------------------------------------------

    [Fact]
    public void TheHatcherySearchMatchesTheChickensIdAndNotTheFoodFlag()
    {
        // TWO of the 145 OBJECTS records carry TypeFood — the Chicken and the
        // Torture Pendulum — and the original's two searches do not agree on
        // which test to use: the hatchery matches id 9, the prison matches the
        // flag. So a pendulum standing in a hatchery is not lunch, and this
        // test is the reason that sentence is written down anywhere.
        GameDatabase db = Db;
        WorldState world = World(db);
        Floor(world, 1, 1, 10, 10);
        RoomInstance room = Hatchery(db, world, 6, 6, 3, 3, Keeper);

        ObjectDef? pendulum = db.Objects.Values.FirstOrDefault(
            o => o.IsFood && o.ObjectId != HatcheryRules.ChickenObjectId);
        if (pendulum is null) return;   // a fixture without one has nothing to say

        ObjectState thing = world.SpawnObject(
            db, pendulum.ObjectId, Keeper, (7 * world.Width) + 7);

        Assert.True(EatingRules.IsFood(db, thing));                 // by the flag
        Assert.False(EatingRules.IsEdibleChicken(thing));           // but not by id
        Assert.Null(EatingRules.NearestChickenIn(world, room, new TilePos(7, 7)));
    }
}
