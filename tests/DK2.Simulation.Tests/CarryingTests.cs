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
/// explanation that a corpse is an object — a corpse is a creature in a state
/// (ADR-0063).</summary>
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
    private static void Down(CreatureState creature)
    {
        creature.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(creature));
    }

    /// <summary>Lays a room tile down without going through the build rules.</summary>
    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, TilePos at)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(type), Keeper);
    }

    // ---- the rescue -------------------------------------------------------

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
        Down(goblin);

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
        Down(goblin);

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
        Down(goblin);
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
        Down(theirs);
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

    [Fact]
    public void AnImpCarriesACorpseToTheGraveyard()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Graveyard, new TilePos(1, 1));

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
        Paint(db, world, RoomTypeId.Graveyard, new TilePos(1, 2));

        // The corpse is right beside the imp.
        CreatureState corpse = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, corpse);

        // The collapsed creature is further away, in the lair, with a bed.
        CreatureState hurt = world.SpawnCreature(db, GoblinId, Keeper, lair);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, hurt));
        hurt.PosX = CreatureState.CentreOf(2);
        hurt.PosY = CreatureState.CentreOf(1);
        Down(hurt);

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
        Paint(db, world, RoomTypeId.Graveyard, new TilePos(1, 1));

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
        Paint(db, world, RoomTypeId.Graveyard, new TilePos(1, 1));

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
        Paint(db, world, RoomTypeId.Graveyard, new TilePos(1, 1));

        CreatureState corpse = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, corpse);

        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(3, 3));
        imp.GoldHeld = 100;

        Assert.False(CarryRules.CanCarry(db.CreatureById(ImpId), imp, corpse));
    }

    [Fact]
    public void ARottedCorpseTakesItselfOutOfWhateverArmsWereHoldingIt()
    {
        // A long walk to a distant graveyard really can outlast the corpse, and
        // when it does the carrier must not be left pointing at a creature that
        // no longer exists (Q164).
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Graveyard, new TilePos(1, 1));

        CreatureState corpse = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, corpse);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(3, 3));

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 10 * Second && imp.CarriedCreatureId < 0; i++) sim.Tick();
        Assert.True(imp.CarriedCreatureId >= 0);

        // Wind the corpse clock to the end while it is being carried.
        corpse.DyingTicks = CreatureDeathRules.BodyWindowTicks(db);
        sim.Run(Second * 2);

        Assert.DoesNotContain(world.Creatures, c => c.Id == corpse.Id);
        Assert.Equal(-1, imp.CarriedCreatureId);
    }

    // ---- the round trip ---------------------------------------------------

    [Fact]
    public void ACarriedBodySurvivesASnapshot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Graveyard, new TilePos(1, 1));

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
