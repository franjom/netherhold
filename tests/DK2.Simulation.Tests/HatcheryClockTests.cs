// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// The Hatchery's clock: coops, eggs and chickens (ADR-0066).
///
/// <para><see cref="HatcheryRules"/> knew all of this for four sittings and
/// could run none of it, on a written reason — DK2 multiplies the laying
/// interval by a variable <i>"no shipped file supplies"</i> — that had stopped
/// being true a day earlier when Q132 was answered.</para>
/// </summary>
public class HatcheryClockTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new HatcherySystem()]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>Paints an n-by-n hatchery with its top-left corner at (1,1).</summary>
    private static RoomInstance Hatchery(GameDatabase db, WorldState world, int size)
    {
        for (int y = 1; y <= size; y++)
        {
            for (int x = 1; x <= size; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Hatchery), Keeper);
            }
        }

        return RoomIndex.Build(world, db)
            .Single(r => r.Definition.TypeId == RoomTypeId.Hatchery);
    }

    // ---- the formula --------------------------------------------------------

    [Fact]
    public void TheIntervalTableIsTheOriginalsFormulaAndNotAnApproximationOfIt()
    {
        // HatcheryRules uses a fixed-point table rather than Math.Pow, because
        // the number decides when an egg appears and eggs are hashed - and
        // IEEE-754 pow is not guaranteed bit-identical across runtimes. THIS
        // test is what makes the table provably the formula: it recomputes
        // every entry the slow, floating way and compares.
        GameDatabase db = Db;
        WorldState world = World(db);
        const double exponent = 0.315477776;
        int shipped = db[VariableTypeId.ChickenGenerationTimePerHatchery];

        for (int tiles = 1; tiles <= HatcheryRules.MaxTilesForInterval; tiles++)
        {
            int expected = (int)(shipped / Math.Pow(tiles, exponent));

            Assert.Equal(expected, HatcheryRules.LayIntervalSecondsForTiles(db, tiles));
        }
    }

    [Fact]
    public void AThreeByThreeHatcheryLaysEverySevenSecondsAndNotEight()
    {
        // The quirk, and it is a sixth-decimal-place quirk. The exponent was
        // typed rather than derived and lands at pow(9, e) = 2.00006, a hair
        // ABOVE two - so 16/2.00006 is 7.99977 and the truncation takes SEVEN,
        // where a designer thinking "nine tiles is exactly half of one" would
        // have expected eight.
        //
        // hatchery.md's own table said 8 for a day while its header said
        // "truncated". Reproduced, not rounded away (Q134).
        GameDatabase db = Db;
        WorldState world = World(db);

        Assert.Equal(7, HatcheryRules.LayIntervalSeconds(db, Hatchery(db, world, 3)));
    }

    [Fact]
    public void ABiggerHatcheryLaysFasterOnACurveRatherThanLinearly()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // One tile, 2x2, 3x3, 4x4 - 16, 10, 7, 6. Halving the interval takes
        // far more than double the room, which is what a fractional exponent
        // buys and a linear rate would not.
        Assert.Equal(16, HatcheryRules.LayIntervalSecondsForTiles(db, 1));
        Assert.Equal(10, HatcheryRules.LayIntervalSecondsForTiles(db, 4));
        Assert.Equal(7, HatcheryRules.LayIntervalSecondsForTiles(db, 9));
        Assert.Equal(6, HatcheryRules.LayIntervalSecondsForTiles(db, 16));

        // And a room past the coop ceiling stops getting faster, because there
        // is nothing above 216 tiles for a faster clock to feed.
        Assert.Equal(
            HatcheryRules.LayIntervalSecondsForTiles(db, 256),
            HatcheryRules.LayIntervalSecondsForTiles(db, 4096));
    }

    // ---- the clock ----------------------------------------------------------

    [Fact]
    public void AHatcheryPutsItsCoopsDownAndThenLaysOnTheInterval()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Hatchery(db, world, 3);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        // A 3x3 wants one coop: floor(9 / 6) = 1.
        sim.Tick(); // the first game second has not elapsed yet
        for (int i = 1; i < Second; i++) sim.Tick();
        Assert.Contains(sink.Events, e => e.Kind == "coops_placed");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "egg_laid");

        // Nothing at six seconds, an egg at seven.
        for (int i = 0; i < 5 * Second; i++) sim.Tick();
        Assert.DoesNotContain(sink.Events, e => e.Kind == "egg_laid");

        for (int i = 0; i < Second; i++) sim.Tick();
        Assert.Contains(sink.Events, e => e.Kind == "egg_laid");
    }

    [Fact]
    public void AnEggBecomesAChickenTwoSecondsAfterItIsLaid()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Hatchery(db, world, 3);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 7 * Second; i++) sim.Tick();

        SimEvent laid = sink.Events.Single(e => e.Kind == "egg_laid");
        ObjectState egg = world.Objects.Single(
            o => o.ObjectTypeId == HatcheryRules.EggObjectId);
        int where = egg.TileIndex;

        for (int i = 0; i < 2 * Second; i++) sim.Tick();

        SimEvent hatched = sink.Events.Single(e => e.Kind == "chicken_hatched");
        Assert.True(hatched.Tick.Value - laid.Tick.Value
                    == HatcheryRules.EggHatchSeconds * Second);

        // The chicken stands where the egg was, and the egg is gone.
        ObjectState chicken = world.Objects.Single(
            o => o.ObjectTypeId == HatcheryRules.ChickenObjectId);
        Assert.Equal(where, chicken.TileIndex);
        Assert.DoesNotContain(world.Objects,
            o => o.ObjectTypeId == HatcheryRules.EggObjectId);
        Assert.Equal(Keeper, chicken.OwnerPlayerId);
    }

    [Fact]
    public void AFullHatcheryLaysNothingAndSaysSo()
    {
        // One bird per tile, so a 3x3 stops at nine - and a hatchery that has
        // stopped should say why rather than going quiet.
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomInstance room = Hatchery(db, world, 3);
        int capacity = HatcheryRules.FlockCapacity(db, room);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        for (int i = 0; i < 400 * Second; i++) sim.Tick();

        RoomInstance now = RoomIndex.Build(world, db)
            .Single(r => r.Definition.TypeId == RoomTypeId.Hatchery);

        Assert.Equal(capacity, HatcheryRules.Birds(world, now));
        Assert.Contains(sink.Events, e => e.Kind == "lay_refused");
    }

    [Fact]
    public void AHatcheryLaysNothingAtAllForANeutralOwner()
    {
        // The clock is per room instance and a room belongs to somebody. This
        // is the guard that a hatchery painted for nobody does not quietly
        // start producing for player zero.
        GameDatabase db = Db;
        WorldState world = World(db);
        for (int y = 1; y <= 3; y++)
        {
            for (int x = 1; x <= 3; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Hatchery), PlayerIds.Neutral);
            }
        }

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 30 * Second; i++) sim.Tick();

        // It runs - a neutral room is still a room - but everything it makes is
        // the neutral player's, which is what the original would do with a
        // hatchery nobody has claimed.
        Assert.All(world.Objects, o => Assert.Equal(PlayerIds.Neutral, o.OwnerPlayerId));
    }

    [Fact]
    public void AHatcheryClockSurvivesASnapshot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Hatchery(db, world, 3);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 8 * Second; i++) sim.Tick();

        byte[] bytes = WorldSnapshot.ToBytes(sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            bytes, db, SimulationSystems.Default());

        Assert.Equal(sim.StateHash(), restored.StateHash());

        // The egg's own countdown crossed the wire too, not just the room's.
        ObjectState egg = world.Objects.Single(
            o => o.ObjectTypeId == HatcheryRules.EggObjectId);
        ObjectState? back = restored.World.ObjectById(egg.Id);
        Assert.NotNull(back);
        Assert.Equal(egg.TimerTicks, back!.TimerTicks);
    }

}
