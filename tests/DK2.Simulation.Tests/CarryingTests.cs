// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Carrying: an imp picks a body off the floor and takes it somewhere.
/// CONFIRMED (owner, Q99): a downed creature lies on the ground for a period,
/// during which an imp can return it to a bed in the lair for healing, or to
/// prison if it is an enemy; when that timer runs out the creature dies and the
/// corpse lies there for a second period, during which an imp can carry it to a
/// graveyard; if that timer runs out too the corpse disappears. Both timers are
/// sixty seconds and had been parsed for months with no reader, on the false
/// explanation that a corpse is an object. It is not an object record; DK2 makes it a
/// CDeadBody thing, and ours keeps it as a creature with no state id (ADR-0063,
/// ADR-0206).</summary>
public class CarryingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new ImpTaskSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>
    /// Puts a creature on the floor before tick 0.
    ///
    /// <para>Setting health to zero is not enough: the collapse is booked once a
    /// game second, so a creature dropped that way is not carryable until t19 —
    /// by which time an imp has taken a Tier B reinforce job and the test is
    /// about job switching rather than about carrying.</para>
    /// </summary>
    private static void Down(in StateHookContext hooks, CreatureState creature)
    {
        creature.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(hooks, creature));
    }

    /// <summary>Lays a room tile down without going through the build rules.</summary>
    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, TilePos at)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(type), Keeper);
    }

    /// <summary>A 3x3 Graveyard at (4..6, 1..3), opened out of the rock beside the fixture's floor: a
    /// corpse needs a floor slot, which a smaller one lacks (ADR-0414).</summary>
    private static void Graveyard(GameDatabase db, WorldState world)
    {
        for (int y = 1; y <= 3; y++)
            for (int x = 4; x <= 6; x++)
                Paint(db, world, RoomTypeId.Graveyard, new TilePos(x, y));
    }

    // ---- the rescue -------------------------------------------------------

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CarrierDeathReleasesItsPassengerAtTheCarrierPosition(bool passengerIsCorpse, bool carrierLeavesBody)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState passenger = world.SpawnCreature(db, GoblinId, Rival, new TilePos(1, 1));
        Down(new StateHookContext(world, db), passenger);
        if (passengerIsCorpse) CreatureDeathRules.BecomeCorpse(world, db, passenger);
        CreatureState carrier = world.SpawnCreature(db, carrierLeavesBody ? GoblinId : ImpId,
            Keeper, new TilePos(2, 3));
        CarryRules.PickUp(carrier, passenger);
        carrier.GoldHeld = 1001;
        carrier.Health = 0;
        if (carrierLeavesBody)
        {
            Down(new StateHookContext(world, db), carrier);
            carrier.DyingTicks = CreatureDeathRules.DyingWindowTicks(db);
        }
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        sim.Run(Second);

        Assert.Equal(carrierLeavesBody, world.CreatureById(carrier.Id) is not null);
        if (carrierLeavesBody) Assert.True(CreatureDeathRules.IsCorpse(carrier));
        Assert.Same(passenger, world.CreatureById(passenger.Id));
        Assert.Equal(-1, carrier.CarriedCreatureId);
        Assert.Equal(-1, passenger.CarriedByCreatureId);
        Assert.Equal(carrier.PosX, passenger.PosX);
        Assert.Equal(carrier.PosY, passenger.PosY);
        Assert.Equal(Rival, passenger.OwnerPlayerId);
        Assert.Equal(passengerIsCorpse, CreatureDeathRules.IsCorpse(passenger));
        Assert.Equal(1000, FloorGold.Total(world, db));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CarrierDeathAlsoReleasesItsObject(bool carrierLeavesBody)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState carrier = world.SpawnCreature(db, carrierLeavesBody ? GoblinId : ImpId,
            Keeper, new TilePos(2, 3));
        ObjectState cargo = world.SpawnObject(db, Dk2TerrainFixture.AlarmCrateObjectId, Keeper, 0);
        ObjectCarryRules.PickUp(carrier, cargo);
        carrier.Health = 0;
        if (carrierLeavesBody)
        {
            Down(new StateHookContext(world, db), carrier);
            carrier.DyingTicks = CreatureDeathRules.DyingWindowTicks(db);
        }
        new GameSimulation(db, world, 42, [new CreatureHealthSystem()]).Run(Second);
        Assert.Same(cargo, world.ObjectById(cargo.Id));
        Assert.Equal(-1, cargo.CarriedByCreatureId);
        Assert.Equal(-1, carrier.CarriedObjectId);
        Assert.Equal(3 * world.Width + 2, cargo.TileIndex);
    }

    [Fact]
    public void CarrierCleanupToleratesAnAlreadyRemovedPassenger()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState passenger = world.SpawnCreature(db, GoblinId, Rival, new TilePos(1, 1));
        CreatureState carrier = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        CarryRules.PickUp(carrier, passenger);
        world.RemoveCreature(passenger.Id);
        carrier.Health = 0;
        new GameSimulation(db, world, 42, [new CreatureHealthSystem()]).Run(Second);
        Assert.Null(world.CreatureById(carrier.Id));
        Assert.Equal(-1, carrier.CarriedCreatureId);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(4, true, false)]
    [InlineData(4, false, true)]
    public void OurMarkPromotesAForeignCasualtyOnlyWithinTheSelectedTile(
        ushort mask, bool foreignWins, bool foreignFartherAway)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, new TilePos(1, 1));
        for (int y = 4; y <= 6; y++)
            for (int x = 4; x <= 6; x++)
                Paint(db, world, RoomTypeId.Prison, new TilePos(x, y));

        // A way in: the rescue selector carries an enemy only to a Prison its route reaches.
        world.Tile(4, 3).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(4, 3).OwnerPlayerId = Keeper;
        CreatureState foreign = world.SpawnCreature(db, GoblinId, Rival, new TilePos(3, 3));
        foreign.RescuePriorityMask = mask;
        if (foreignFartherAway) foreign.PosX = CreatureState.CentreOf(4);
        Down(new StateHookContext(world, db), foreign);
        CreatureState own = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, own));
        own.PosX = CreatureState.CentreOf(3);
        own.PosY = CreatureState.CentreOf(3);
        Down(new StateHookContext(world, db), own);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        var sim = new GameSimulation(db, world, 42, [new ImpTaskSystem()]);
        sim.Tick(InputFrame.Empty); // 1 hands the imp to 10, which chooses on the next tick (mining-cadence.md §6)
        sim.Tick(InputFrame.Empty);
        Assert.Equal(foreignWins ? foreign.Id : own.Id, imp.TargetBodyId);
    }

    [Fact]
    public void AnImpCarriesACollapsedCreatureBackToItsOwnBed()
    {
        // The owner's rescue, end to end. This is what the bed was being held
        // for through the whole dying window (CreatureDeathRules.Collapse).
        GameDatabase db = Db;
        WorldState world = World(db);
        var lair = new TilePos(1, 1);
        Paint(db, world, RoomTypeId.Lair, lair);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, lair);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));

        // Drop it somewhere else in the room, so the imp has a walk.
        goblin.PosX = CreatureState.CentreOf(3);
        goblin.PosY = CreatureState.CentreOf(3);
        Down(new StateHookContext(world, db), goblin);

        world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        for (int i = 0; i < 30 * Second; i++)
        {
            sim.Tick();
            if (sink.Events.Any(e => e.Kind == "creature_rescued")) break;
        }

        Assert.Contains(sink.Events, e => e.Kind == "creature_rescued");

        // Asleep in its own bed, which is where a lair heals - and the ONLY
        // state it heals in (ADR-0049). Asserted at the moment of the rescue
        // because a Goblin's TimeSleep is nine seconds and it gets up again.
        Assert.Equal(CreatureActivity.Sleeping, goblin.Activity);
        Assert.True(LairRules.HasBed(world, db, goblin));

        // Note what the rescue does NOT do: it adds no health. The lair puts it
        // back on, at 100 a second, from the zero it collapsed at.
        sim.Run(Second * 3);
        Assert.True(goblin.Health > 0);
    }

    [Fact]
    public void ARescuedCreatureIsNeverSeenToDie()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var lair = new TilePos(1, 1);
        Paint(db, world, RoomTypeId.Lair, lair);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, lair);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));
        goblin.PosX = CreatureState.CentreOf(3);
        goblin.PosY = CreatureState.CentreOf(3);
        Down(new StateHookContext(world, db), goblin);

        world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 120);

        // Twice the dying window, and it never reached the end of the first.
        Assert.DoesNotContain(sink.Events, e => e.Kind == "creature_died");
        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
    }

    [Fact]
    public void ACollapsedCreatureWithNoBedIsLeftWhereItFell()
    {
        // Nowhere to carry it to, so nobody sets off. Deciding the destination
        // before the walk rather than on arrival is what stops an imp crossing
        // the dungeon to stand over a body and shrug (Q107).
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        Down(new StateHookContext(world, db), goblin);
        world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 30);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "body_claimed");
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
    }

    [Fact]
    public void AnImpWillNotCarryARivalsCollapsedCreatureBecauseThereIsNoPrison()
    {
        // DK2 has an imp task for it - kind 13's other branch, room 11 - and
        // this project has no prison at all. So an enemy on the floor is left
        // there rather than quietly rescued into the wrong dungeon (Q163).
        GameDatabase db = Db;
        WorldState world = World(db);
        var lair = new TilePos(1, 1);
        Paint(db, world, RoomTypeId.Lair, lair);

        CreatureState theirs = world.SpawnCreature(db, GoblinId, Rival, new TilePos(3, 3));
        Down(new StateHookContext(world, db), theirs);
        world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 30);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "body_claimed");
    }

    // ---- the corpse -------------------------------------------------------

    [Fact]
    public void AGoblinThatDiesLeavesACorpseAndAnImpDoesNot()
    {
        // GenerateDeadBody is the flag, and it is why the two look different:
        // the owner said "Imps are dead right away, they don't have a lying
        // state".
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(4, 3));
        goblin.Health = 0;
        imp.Health = 0;

        GameSimulation sim = new(db, world, 42, [new CreatureHealthSystem()]);
        sim.Run(Second * 61);

        Assert.Equal(CreatureActivity.Dead, goblin.Activity);
        Assert.DoesNotContain(world.Creatures, c => c.Id == imp.Id);
    }

    // ---- dying on DK2's state ids (ADR-0206) ---------------------------------

    [Fact]
    public void ACollapseFallsInTwoHundredSixteenAndLiesInTwoHundredSeventeen()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        goblin.Health = 0;
        var sink = new ListEventSink();
        // With the systems that hand back a state id their creature has left beside it.
        GameSimulation sim = new(db, world, 42,
            [new CreatureSleepSystem(), new CreatureJobSystem(), new CreatureHealthSystem()]);
        sim.ObserveWith(sink);

        for (int i = 0; i < 2 * Second && !sink.Events.Any(e => e.Kind == "creature_collapsed"); i++) sim.Tick();
        Assert.Equal(CreatureDeathRules.FallingState, goblin.Dk2StateId);

        sim.Run(Second);
        Assert.Equal(CreatureDeathRules.DyingState, goblin.Dk2StateId);
        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
    }

    [Fact]
    public void ACreatureCollapsesOutOfWhateverStateItHeld()
    {
        // The rule admits 216 from every state, the Refused category included (group 3's
        // passthrough), and the state is set rather than released and begun again.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        StatePlacement.Place(goblin, TortureRules.ReleasedState);
        goblin.Health = 0;

        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, db), goblin));

        Assert.Equal(CreatureDeathRules.FallingState, goblin.Dk2StateId);
    }

    [Fact]
    public void ARescuedCreatureLeavesTheDyingStatesThroughIdleIntoSleep()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var lair = new TilePos(1, 1);
        Paint(db, world, RoomTypeId.Lair, lair);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, lair);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));
        goblin.PosX = CreatureState.CentreOf(3);
        goblin.PosY = CreatureState.CentreOf(3);
        Down(new StateHookContext(world, db), goblin);
        Assert.Equal(CreatureDeathRules.FallingState, goblin.Dk2StateId);
        world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        for (int i = 0; i < 30 * Second && !sink.Events.Any(e => e.Kind == "creature_rescued"); i++) sim.Tick();

        Assert.Equal(LairRules.SleepingState, goblin.Dk2StateId);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void ACorpseHoldsNoStateBecauseDk2MakesItADeadBodyThing()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        goblin.Health = 0;
        GameSimulation sim = new(db, world, 42, [new CreatureHealthSystem()]);

        sim.Run(Second * 10);
        Assert.Equal(CreatureDeathRules.DyingState, goblin.Dk2StateId);
        sim.Run(Second * 55);

        Assert.Equal(CreatureActivity.Dead, goblin.Activity);
        Assert.False(CreatureStateMachine.HasState(goblin));
    }

    // ---- on DK2's state ids (ADR-0184) ---------------------------------------

    [Fact]
    public void ARescueIsFortyTwoThenFortyFiveAndEndsInTaskSelection()
    {
        // 10 walks in 6 to 42; 42 chooses the keeper's own Lair and walks in 6 to 45, so the
        // saved caller turns from 10 to 42; 45 delivers and sets 10.
        GameDatabase db = Db;
        WorldState world = World(db);
        var lair = new TilePos(1, 1);
        Paint(db, world, RoomTypeId.Lair, lair);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, lair);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));
        goblin.PosX = CreatureState.CentreOf(3);
        goblin.PosY = CreatureState.CentreOf(3);
        Down(new StateHookContext(world, db), goblin);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        Assert.Equal(
            [(ImpStates.TaskSelection, 0, 0),
             (6, ImpStates.TaskSelection, ImpStates.PickingUpCasualty),
             (6, ImpStates.PickingUpCasualty, ImpStates.CarryingToLair),
             (ImpStates.TaskSelection, ImpStates.PickingUpCasualty, CreatureStateMachine.Idle)],
            ImpStateTrace.Seen(sim, imp, 30 * Second,
                () => sink.Events.Any(e => e.Kind == "creature_rescued")));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void ABurialIsFortyThenFortyOneAndEndsInTaskSelection()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Graveyard(db, world);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, goblin);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        Assert.Equal(
            [(ImpStates.TaskSelection, 0, 0),
             (6, ImpStates.TaskSelection, ImpStates.PickingUpCorpse),
             (6, ImpStates.PickingUpCorpse, ImpStates.CarryingCorpse),
             (ImpStates.TaskSelection, ImpStates.PickingUpCorpse, CreatureStateMachine.Idle)],
            ImpStateTrace.Seen(sim, imp, 30 * Second,
                () => sink.Events.Any(e => e.Kind == "corpse_buried")));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AnImpCarriesACorpseToTheGraveyard()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Graveyard(db, world);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, goblin);

        world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 30);

        SimEvent buried = Assert.Single(sink.Events, e => e.Kind == "corpse_buried");
        Assert.Contains("decompose_value=", buried.Detail);
        Assert.Contains("tally=200 raised=none", buried.Detail);
        Assert.DoesNotContain(world.Creatures, c => c.Id == goblin.Id);
    }

    [Fact]
    public void ACorpseWithNoGraveyardRotsWhereItLies()
    {
        // "If that timer runs out and no one carries the corpse it just
        // disappears."
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, goblin);
        world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(Second * 59);
        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);

        sim.Run(Second * 2);
        Assert.DoesNotContain(world.Creatures, c => c.Id == goblin.Id);
        Assert.Contains(sink.Events, e => e.Kind == "corpse_rotted");
    }

    [Fact]
    public void ACorpseGetsAFreshSixtySecondsRatherThanTheRemainsOfTheFirstClock()
    {
        // The rescue window and the corpse window share one field, because they
        // never run at once. Which makes the reset at the seam load-bearing: a
        // corpse inheriting a spent clock would vanish the instant it appeared.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        goblin.Health = 0;

        GameSimulation sim = new(db, world, 42, [new CreatureHealthSystem()]);
        sim.Run(Second * 61);

        Assert.Equal(CreatureActivity.Dead, goblin.Activity);
        Assert.Equal(0, goblin.DyingTicks);
    }

    // ---- the ordering and the arms ---------------------------------------

    [Fact]
    public void AnUnconsciousCreatureOutranksACorpse()
    {
        // DK2's Tier A table is 14, 8, 9, 1, 3, 13, 12 - kind 13 before kind
        // 12 - so a collapsed creature across the map beats a corpse next door.
        GameDatabase db = Db;
        WorldState world = World(db);
        var lair = new TilePos(1, 1);
        Paint(db, world, RoomTypeId.Lair, lair);
        Graveyard(db, world);

        // The corpse is right beside the imp.
        CreatureState corpse = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, corpse);

        // The collapsed creature is further away, in the lair, with a bed.
        CreatureState hurt = world.SpawnCreature(db, GoblinId, Keeper, lair);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, hurt));
        hurt.PosX = CreatureState.CentreOf(2);
        hurt.PosY = CreatureState.CentreOf(1);
        Down(new StateHookContext(world, db), hurt);

        world.SpawnCreature(db, ImpId, Keeper, new TilePos(3, 2));

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 3);

        SimEvent first = sink.Events.First(e => e.Kind == "body_claimed");
        Assert.Contains("kind=unconscious", first.Detail);
        Assert.Contains($"body={hurt.Id}", first.Detail);
    }

    [Fact]
    public void ACarriedBodyTravelsWithTheArmsHoldingIt()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Graveyard(db, world);

        CreatureState corpse = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, corpse);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(3, 3));

        GameSimulation sim = Simulation(db, world);

        // Let the imp take it and start walking.
        for (int i = 0; i < 10 * Second && imp.CarriedCreatureId < 0; i++) sim.Tick();
        Assert.Equal(corpse.Id, imp.CarriedCreatureId);
        Assert.Equal(imp.Id, corpse.CarriedByCreatureId);

        // Both ends stay in step, every tick, for as long as it is held.
        for (int i = 0; i < 5 && imp.CarriedCreatureId >= 0; i++)
        {
            sim.Tick();
            if (imp.CarriedCreatureId < 0) break;
            Assert.Equal(imp.PosX, corpse.PosX);
            Assert.Equal(imp.PosY, corpse.PosY);
        }
    }

    [Fact]
    public void OneImpCarriesOneThingAndNotTwo()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Graveyard(db, world);

        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, first);
        CreatureDeathRules.BecomeCorpse(world, db, second);

        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(3, 3));

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 60 * Second; i++)
        {
            sim.Tick();
            Assert.True(
                imp.CarriedCreatureId < 0
                || (first.CarriedByCreatureId < 0 || second.CarriedByCreatureId < 0),
                "the imp had both bodies at once");
        }
    }

    [Fact]
    public void AnImpWithGoldInItsHandsIsGoingToATreasuryAndNotToABody()
    {
        // A worker holding gold is running to put the load down (Q31,
        // ADR-0018), and is not offered a body at all.
        GameDatabase db = Db;
        WorldState world = World(db);
        Graveyard(db, world);

        CreatureState corpse = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, corpse);

        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(3, 3));
        imp.GoldHeld = 100;

        Assert.False(CarryRules.CanCarry(db.CreatureById(ImpId), imp, corpse));
    }

    /// <summary>Q164: a body's clocks stand still while it is carried. The corpse lifetime counts
    /// down in <c>0x004B12D0</c> only while the carried flag <c>+0x138</c> is clear, and the
    /// dying handler <c>0x004599D0</c> skips the rescue countdown while BeingDragged is set
    /// (<c>0x00459BE8</c>). Ours let a corpse rot in an imp's arms.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABodysClockStandsStillWhileItIsCarried_AndRunsAgainOnceItIsPutDown(bool corpse)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState body = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(3, 3));
        var sim = new GameSimulation(db, world, 42, [new CreatureHealthSystem()]);
        if (corpse) CreatureDeathRules.BecomeCorpse(world, db, body);
        else Down(new StateHookContext(world, db), body);
        int window = corpse ? CreatureDeathRules.BodyWindowTicks(db) : CreatureDeathRules.DyingWindowTicks(db);
        body.DyingTicks = window - 1;
        CarryRules.PickUp(imp, body);

        sim.Run(Second * 3);
        Assert.Contains(body, world.Creatures);
        Assert.Equal(window - 1, body.DyingTicks);

        CarryRules.PutDown(imp, body);
        sim.Run(Second * 2);
        if (corpse) Assert.DoesNotContain(body, world.Creatures); // rotted away
        else Assert.True(CreatureDeathRules.IsCorpse(body));      // died, and is now a corpse
    }

    // ---- the round trip ---------------------------------------------------

    [Fact]
    public void ACarriedBodySurvivesASnapshot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Graveyard(db, world);

        CreatureState corpse = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, corpse);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(3, 3));

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 10 * Second && imp.CarriedCreatureId < 0; i++) sim.Tick();
        Assert.True(imp.CarriedCreatureId >= 0);

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(sim), db, SimulationSystems.Default());

        CreatureState carrier = restored.World.CreatureById(imp.Id)!;
        CreatureState body = restored.World.CreatureById(corpse.Id)!;

        Assert.Equal(corpse.Id, carrier.CarriedCreatureId);
        Assert.Equal(imp.Id, body.CarriedByCreatureId);
        Assert.Equal(CreatureActivity.Dead, body.Activity);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }
}
