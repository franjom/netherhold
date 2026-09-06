// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// Payday — the wage bill, and the walk to collect it (ADR-0078,
/// docs/research/payday.md).
///
/// <para>The assertions worth keeping honest are the two that make payday a
/// mechanic rather than an accounting entry: <b>wages accumulate</b>, so a
/// keeper who cannot pay builds a debt that compounds; and <b>the gold comes
/// off the tile the creature is standing on</b>, so payday can fail while the
/// keeper still has money.</para>
/// </summary>
public class PaydayTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;

    /// <summary>The claimed 3x3 room the fixture map puts in the corner.</summary>
    private static readonly TilePos Floor = new(2, 2);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    /// <summary>
    /// Payday and movement only. Everything else is left out so a test that
    /// measures a wage is not also measuring hunger, anger or the imp supply.
    /// </summary>
    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
            [new CreatureMovementSystem(), new PaydaySystem()]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>Turns one tile of the keeper's room into a treasury holding gold.</summary>
    private static TilePos Treasury(WorldState world, TilePos at, int gold)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        tile.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        tile.OwnerPlayerId = Keeper;
        tile.GoldHeld = gold;
        return at;
    }

    /// <summary>Runs to just past the first payday.</summary>
    private static void RunToFirstPayday(GameSimulation sim, GameDatabase db, int extraTicks = 40)
    {
        int ticks = PaydayRules.FirstPaydayTicks(db) + extraTicks;
        for (int i = 0; i < ticks; i++) sim.Tick(InputFrame.Empty);
    }

    // ---- the clock --------------------------------------------------------

    [Fact]
    public void TheFirstPaydayIsTenMinutesInRatherThanAtZero()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        // One tick short of TimeBeforeFirstPayDaySeconds: nobody is owed a
        // thing. A level does not open with the dungeon on strike.
        for (int i = 0; i < PaydayRules.FirstPaydayTicks(db); i++) sim.Tick(InputFrame.Empty);
        Assert.Equal(0, goblin.WageOwed);

        sim.Tick(InputFrame.Empty);
        Assert.Equal(500, goblin.WageOwed);
    }

    [Fact]
    public void AGoblinIsOwedItsRecordsPayAndAnImpIsOwedNothing()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(1, 1));
        GameSimulation sim = Simulation(db, world);

        RunToFirstPayday(sim, db);

        // The Goblin's record says pay=500. The Imp's says pay=0, which is why
        // no rule in PaydayRules names the imp: it falls out of the data.
        Assert.Equal(500, goblin.WageOwed);
        Assert.Equal(0, imp.WageOwed);
    }

    [Fact]
    public void AVeteranCostsMoreThanARecruit()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState recruit = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        CreatureState veteran = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));
        veteran.Level = 10;

        GameSimulation sim = Simulation(db, world);
        RunToFirstPayday(sim, db);

        // The shipped level table's Pay row runs to 250% at level 10, and this
        // is its first consumer in the project - the row has been parsed since
        // variables were first read and looked at by nothing (Q119).
        Assert.Equal(500, recruit.WageOwed);
        Assert.Equal(1250, veteran.WageOwed);
    }

    [Fact]
    public void WagesAccumulateAcrossPaydaysTheKeeperCannotMeet()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        // No treasury anywhere, so nothing is ever collected.
        RunToFirstPayday(sim, db);
        Assert.Equal(500, goblin.WageOwed);

        for (int i = 0; i < PaydayRules.IntervalTicks(db); i++) sim.Tick(InputFrame.Empty);

        // The original's accrual ADDS to the field rather than assigning it, so
        // a debt compounds instead of resetting. This is the whole reason
        // payday is worth having: it is a keeper's running failure, not a tick.
        Assert.Equal(1000, goblin.WageOwed);
    }

    [Fact]
    public void SkipNextPaymentIsSpentWhetherOrNotItWasNeeded()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.SkipNextPayment = true;

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        RunToFirstPayday(sim, db);

        // Not "paid early and skipped": the payday does not happen for this
        // creature at all, and the ticket is gone.
        Assert.Equal(0, goblin.WageOwed);
        Assert.False(goblin.SkipNextPayment);
        Assert.Contains(sink.Events, e => e.Kind == "payday" && e.Detail.Contains("skipped=1"));

        // And the next one bills it normally.
        for (int i = 0; i < PaydayRules.IntervalTicks(db); i++) sim.Tick(InputFrame.Empty);
        Assert.Equal(500, goblin.WageOwed);
    }

    // ---- collecting -------------------------------------------------------

    [Fact]
    public void AGoblinWalksToTheTreasuryAndTakesItsWagesOffTheFloor()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        TilePos vault = Treasury(world, new TilePos(1, 3), 2000);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        RunToFirstPayday(sim, db, extraTicks: 200);

        Assert.Equal(0, goblin.WageOwed);
        Assert.Equal(vault, goblin.Tile);

        // Off the tile, not off a player counter - DK2 has none (ADR-0015), and
        // payday is the strongest confirmation of that this project has.
        Assert.Equal(1500, world.Tile(vault.X, vault.Y).GoldHeld);
        Assert.Contains(sink.Events, e => e.Kind == "paid" && e.Detail.Contains("took=500"));
    }

    [Fact]
    public void AKeeperWithATreasuryAndNoGoldInItCannotPay()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Treasury(world, new TilePos(1, 3), gold: 0);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        RunToFirstPayday(sim, db, extraTicks: 200);

        // The original will not walk to an empty room, and says so with a
        // different message from the one it uses when there is no treasury at
        // all - a keeper who has spent everything has a different problem from
        // one who has not dug a vault.
        Assert.Equal(500, goblin.WageOwed);
        Assert.Contains(sink.Events, e =>
            e.Kind == "wages_unreachable" && e.Detail.Contains("reason=treasury_empty"));
    }

    [Fact]
    public void AKeeperWithNoTreasuryAtAllSaysSoDifferently()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SpawnCreature(db, GoblinId, Keeper, Floor);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        RunToFirstPayday(sim, db, extraTicks: 200);

        Assert.Contains(sink.Events, e =>
            e.Kind == "wages_unreachable" && e.Detail.Contains("reason=no_treasury"));
    }

    [Fact]
    public void ACreatureGivesUpAfterTwoFailedAttemptsAndTriesAgainNextPayday()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Treasury(world, new TilePos(1, 3), gold: 0);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        RunToFirstPayday(sim, db, extraTicks: 400);

        // Two, hardcoded in the original, and it stops there however long the
        // run goes on - otherwise a broke keeper's whole dungeon would spend
        // every tick re-deciding.
        Assert.Equal(PaydayRules.MaxFailedAttempts, goblin.PayAttemptsFailed);
        Assert.Equal(
            PaydayRules.MaxFailedAttempts,
            sink.Events.Count(e => e.Kind == "wages_unreachable"));

        // The next payday refills the budget for everybody, including one that
        // had given up.
        for (int i = 0; i < PaydayRules.IntervalTicks(db); i++) sim.Tick(InputFrame.Empty);
        Assert.Equal(1000, goblin.WageOwed);
        Assert.Equal(4, sink.Events.Count(e => e.Kind == "wages_unreachable"));
    }

    [Fact]
    public void ATreasuryTooPoorToPayInFullLeavesTheRestOwed()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        TilePos vault = Treasury(world, new TilePos(1, 3), 200);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);

        GameSimulation sim = Simulation(db, world);
        RunToFirstPayday(sim, db, extraTicks: 300);

        // It takes what is there and stays owed the rest, which is what makes
        // the debt in WagesAccumulate... reachable in play rather than only
        // when a keeper has no vault at all.
        Assert.Equal(0, world.Tile(vault.X, vault.Y).GoldHeld);
        Assert.Equal(300, goblin.WageOwed);
    }

    [Fact]
    public void TheDungeonHeartPaysWagesToo()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        // A keeper's first treasury is bought out of the dungeon heart
        // (ADR-0015), so a keeper who has not dug a vault yet still has
        // somewhere for wages to come from - and the original's search admits
        // room type 5 beside type 1 for exactly that reason.
        ref TileState heart = ref world.Tile(1, 3);
        heart.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
        heart.OwnerPlayerId = Keeper;
        heart.GoldHeld = 900;

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);
        RunToFirstPayday(sim, db, extraTicks: 300);

        Assert.Equal(0, goblin.WageOwed);
        Assert.Equal(400, world.Tile(1, 3).GoldHeld);
    }

    [Fact]
    public void APrisonerIsNotOnThePayroll()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Treasury(world, new TilePos(1, 3), 2000);

        CreatureState captive = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        PrisonRules.Imprison(world, db, captive, Keeper, (Floor.Y * world.Width) + Floor.X);

        GameSimulation sim = Simulation(db, world);
        RunToFirstPayday(sim, db, extraTicks: 200);

        Assert.Equal(0, captive.WageOwed);
    }

    [Fact]
    public void TheWageBillSurvivesASnapshot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.SkipNextPayment = true;
        goblin.WageOwed = 750;
        goblin.PayAttemptsFailed = 1;
        world.Player(Keeper).NextPaydayTick = new GameTick(12345);

        GameSimulation sim = Simulation(db, world);
        GameSimulation reloaded = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(sim), db, SimulationSystems.Default());
        WorldState restored = reloaded.World;

        CreatureState back = restored.CreatureById(goblin.Id)!;
        Assert.Equal(750, back.WageOwed);
        Assert.Equal(1, back.PayAttemptsFailed);
        Assert.True(back.SkipNextPayment);
        Assert.Equal(12345, restored.Player(Keeper).NextPaydayTick.Value);
    }

    // ---- and the anger it causes (ADR-0079) --------------------------------

    /// <summary>Payday, movement and anger — the three that make a wage bite.</summary>
    private static GameSimulation WithAnger(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
            [new CreatureMovementSystem(), new PaydaySystem(), new CreatureAngerSystem()]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>
    /// Sets the debt directly and runs a short window.
    ///
    /// <para>The first draft of these ran the full ten minutes to the real
    /// payday, and every one of them failed for the same reason: a Goblin with
    /// no bed takes AngerNoLair at 100 a second, so after six hundred seconds it
    /// is pinned at the 32767 ceiling and has already drawn Leave from its angry
    /// job table. The measurement was of a creature that had deserted. Anger is
    /// measured over seconds, not over paydays.</para>
    /// </summary>
    private const int AngerWindowSeconds = 10;

    [Fact]
    public void AnUnpaidCreatureGetsAngryAndBeingOwedIsTheWholeTrigger()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.WageOwed = 500;

        GameSimulation sim = WithAnger(db, world);
        int before = goblin.Anger;
        for (int i = 0; i < AngerWindowSeconds * SimulationTiming.TicksPerSecond; i++)
            sim.Tick(InputFrame.Empty);

        // Being owed is the whole trigger: the original tests the outstanding
        // wage against zero and nothing else. There is no treasury on this map
        // and no attempt has failed, and it is angry anyway.
        //
        // Both charges land, because a Goblin on this map has no bed either -
        // which is exactly what the additive sum is for.
        CreatureDef def = db.CreatureById(GoblinId);
        Assert.Equal(
            before + (AngerWindowSeconds * (def.AngerNoLair + def.AngerNoPay)), goblin.Anger);
    }

    [Fact]
    public void PayingItStopsThatHalfOfTheAngerAtOnce()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        GameSimulation sim = WithAnger(db, world);

        CreatureDef def = db.CreatureById(GoblinId);

        goblin.WageOwed = 500;
        int before = goblin.Anger;
        for (int i = 0; i < AngerWindowSeconds * SimulationTiming.TicksPerSecond; i++)
            sim.Tick(InputFrame.Empty);
        int owing = goblin.Anger - before;

        // Debt cleared by hand, standing in for a treasury it reached.
        goblin.WageOwed = 0;
        int settled = goblin.Anger;
        for (int i = 0; i < AngerWindowSeconds * SimulationTiming.TicksPerSecond; i++)
            sim.Tick(InputFrame.Empty);
        int paid = goblin.Anger - settled;

        // The lair charge continues either way; the wage charge stops with the
        // debt, and the difference between the two windows is exactly it.
        Assert.Equal(AngerWindowSeconds * (def.AngerNoLair + def.AngerNoPay), owing);
        Assert.Equal(AngerWindowSeconds * def.AngerNoLair, paid);
    }

    [Fact]
    public void APrisonerTakesNoNeedAngerAtAll()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = world.SpawnCreature(db, GoblinId, Keeper, Floor);

        // Owed from before it was taken, which is the only way a captive can
        // carry a debt: the payday clock skips prisoners entirely.
        captive.WageOwed = 500;
        PrisonRules.Imprison(world, db, captive, Keeper, (Floor.Y * world.Width) + Floor.X);
        int before = captive.Anger;

        GameSimulation sim = WithAnger(db, world);
        for (int i = 0; i < AngerWindowSeconds * SimulationTiming.TicksPerSecond; i++)
            sim.Tick(InputFrame.Empty);

        // Not the wage, and NOT the missing bed either. The original's
        // need-anger routine refuses outright on bit 1 of the state record's
        // flag byte, and all eleven "being held captive" states carry it - so a
        // captive's anger is the prison's business alone. This test used to
        // pass a weaker claim and caught the difference (ADR-0079).
        Assert.Equal(500, captive.WageOwed);
        Assert.Equal(before, captive.Anger);
    }

    [Fact]
    public void ABedOutpacesAnUnpaidWageNineToOne()
    {
        GameDatabase db = Db;
        CreatureDef def = db.CreatureById(GoblinId);

        // Not a simulation run: the two shipped numbers, side by side, because
        // this is the fact the wage-bill scenario turned up and prose is not
        // where it belongs.
        //
        // A creature asleep in its own bed calms at ModifyAngerOfCreatureInLair-
        // PerSecond, which ships at -550. Being owed a wage costs it
        // AngerNoPay, 60 for a Goblin. So a well-housed creature sleeps off
        // unpaid resentment about NINE TIMES faster than it accrues it, and
        // wages alone will never drive one out of a dungeon that has beds.
        //
        // That is why the wage-bill scenario shows no mood change at all: the
        // lairs it needs to make the Goblins idle enough to collect are also
        // what keeps them calm. Failing to pay is not on its own a way to lose
        // a creature - it has to be one of several things going wrong, which is
        // what the anger ladder is for.
        int calm = -db[VariableTypeId.ModifyAngerOfCreatureInLairPerSecond];
        Assert.Equal(550, calm);
        Assert.Equal(60, def.AngerNoPay);
        Assert.True(calm > 9 * def.AngerNoPay);
    }
}
