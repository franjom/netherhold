// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>What imps cost to keep — the first thing here that takes mana away
/// from a keeper continuously. CONFIRMED (owner, 2026-08-30, Q53) from two
/// screenshots of the original's mana orb: with four imps the red figure read 28
/// a second, with five it read 35. Both divide by seven exactly, which pins the
/// rate and the unit at once. The 7 is DK2's own, off the Create Imp spell record
/// rather than the CREATURES record where a creature upkeep would be expected
/// (docs/research/imps-and-mana.md).</summary>
public class ManaUpkeepTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private sealed record Run(GameSimulation Sim, ListEventSink Events, GameDatabase Db)
    {
        public WorldState World => Sim.World;

        public void Tick(params PlayerCommand[] commands) =>
            Sim.Tick(commands.Length == 0 ? InputFrame.Empty : InputFrame.Of(commands));

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++) Tick();
        }

        public int Imps => ImpSupplyRules.ImpCount(World, Db, Keeper);

        public int Mana => World.Player(Keeper).Mana;

        /// <summary>The most recent mana event for our keeper, whichever way it went.</summary>
        public SimEvent LastManaEvent => Events.Events
            .Last(e => (e.Kind == "mana_gained" || e.Kind == "mana_drained")
                       && e.Detail.Contains($"player={Keeper} "));
    }

    private static Run World(int mana = 0, bool heart = true)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        if (heart)
        {
            ref TileState tile = ref world.Tile(2, 2);
            tile.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
            tile.Health = database.TerrainById(tile.TerrainId).StartingHealth;
            tile.OwnerPlayerId = Keeper;
        }

        world.Player(Keeper).Mana = mana;

        var sim = new GameSimulation(database, world, 5, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink, database);
    }

    /// <summary>What this keeper's land pays them per second.</summary>
    private static int LandIncome(Run run)
    {
        int[] gain = ManaRules.GainPerSecondByPlayerIndex(run.World, run.Db);
        for (int i = 0; i < run.World.PlayerCount; i++)
            if (run.World.PlayerAtIndex(i).PlayerId == Keeper) return gain[i];
        return 0;
    }

    /// <summary>Enough ticks for the free four to arrive and a second to elapse.</summary>
    private static void SettleWithFourImps(Run run) =>
        run.Ticks(ImpSupplyRules.FreeImpIntervalTicks(run.Db) * 5);

    /// <summary>Ticks on until the keeper's land income stops growing. A test about
    /// upkeep must not race four imps still claiming ground: income climbed 41 to 44
    /// over eight seconds here, and two tests read a rate on one side of that and
    /// paid for it on the other, which made them depend on the claim rate (ADR-0133).</summary>
    private static void SettleTheLand(Run run)
    {
        int last = LandIncome(run);
        for (int steady = 0, second = 0; steady < 5 && second < 300; second++)
        {
            run.Ticks(SimulationTiming.TicksPerSecond);
            int now = LandIncome(run);
            steady = now == last ? steady + 1 : 0;
            last = now;
        }
    }

    // ---- the measurement --------------------------------------------------

    /// <summary>
    /// <b>The owner's first screenshot, reproduced.</b> Four imps, and the red
    /// figure reads 28.
    /// </summary>
    [Fact]
    public void FourImpsCostTwentyEightManaASecond()
    {
        Run run = World();
        SettleWithFourImps(run);

        Assert.Equal(4, run.Imps);
        Assert.Contains("upkeep=28/s", run.LastManaEvent.Detail);
    }

    /// <summary>
    /// <b>The owner's second screenshot.</b> Summon a fifth imp and the red
    /// figure goes to 35 — which is what makes the rate per imp rather than a
    /// flat charge, and per second rather than per anything else.
    /// </summary>
    [Fact]
    public void AFifthImpTakesItToThirtyFive()
    {
        Run run = World(mana: 100_000);
        SettleWithFourImps(run);
        Assert.Contains("upkeep=28/s", run.LastManaEvent.Detail);

        run.Tick(new CreateImpCommand(Keeper));
        run.Ticks(SimulationTiming.TicksPerSecond);

        Assert.Equal(5, run.Imps);
        Assert.Contains("upkeep=35/s", run.LastManaEvent.Detail);
    }

    [Fact]
    public void AKeeperWithNoImpsPaysNothing()
    {
        // No heart, because there is no longer any such thing as "before the
        // first free imp arrives". This test used to run one second and rely on
        // the five-second gap; the free supply keeps a deadline rather than a
        // countdown, so a keeper with a heart and no imps is staffed on the very
        // first tick (2026-09-05, ImpSupplySystem). Nowhere to put an imp is now
        // the only way to have a keeper with land and no workforce.
        Run run = World(heart: false);

        run.Ticks(SimulationTiming.TicksPerSecond);

        Assert.Equal(0, run.Imps);
        Assert.Contains("upkeep=0/s", run.LastManaEvent.Detail);
    }

    [Fact]
    public void TheUpkeepIsSevenPerImp_WhateverTheCount()
    {
        Run run = World(mana: 100_000);
        SettleWithFourImps(run);
        SettleTheLand(run);

        for (int extra = 0; extra < 4; extra++)
        {
            run.Tick(new CreateImpCommand(Keeper));
            run.Ticks(SimulationTiming.TicksPerSecond);
            Assert.Contains(
                $"upkeep={run.Imps * ManaRules.ImpUpkeepPerSecond(run.Db)}/s",
                run.LastManaEvent.Detail);
        }
    }

    // ---- what it does to the economy --------------------------------------

    /// <summary>
    /// <b>Income and upkeep are reported separately</b>, because the original
    /// shows them separately: a green figure and a red one under the orb. A
    /// single netted number would lose which half moved.
    /// </summary>
    [Fact]
    public void BothHalvesAreReported_NotJustTheNet()
    {
        Run run = World();
        SettleWithFourImps(run);

        string detail = run.LastManaEvent.Detail;
        Assert.Contains("rate=", detail);
        Assert.Contains("upkeep=28/s", detail);
    }

    /// <summary>
    /// <b>The point of the whole mechanic.</b> Enough imps and a keeper's land
    /// cannot pay for them, so mana falls instead of rising — which is why a DK2
    /// keeper cannot buy imps until the map is full, and why this simulation
    /// used to let them.
    /// </summary>
    [Fact]
    public void EnoughImpsAndAKeepersManaGoesDownInsteadOfUp()
    {
        Run run = World(mana: 100_000);
        SettleWithFourImps(run);
        SettleTheLand(run);

        // Buy imps until they cost more per second than the land earns. The
        // land here is small, so this does not take many.
        int gain = LandIncome(run);
        while (run.Imps * ManaRules.ImpUpkeepPerSecond(run.Db) <= gain)
            run.Tick(new CreateImpCommand(Keeper));

        run.Ticks(SimulationTiming.TicksPerSecond);
        int before = run.Mana;
        run.Ticks(SimulationTiming.TicksPerSecond * 3);

        Assert.True(run.Mana < before,
            $"mana did not fall: {before} -> {run.Mana} with {run.Imps} imps against {gain}/s of land");
    }

    /// <summary>
    /// Mana floors at zero rather than going negative. <b>What the original does
    /// when a keeper cannot pay is UNKNOWN</b> —
    /// <c>InsufficientManaImpEvaluationPeriodSeconds</c> (10) says something
    /// happens on a ten-second clock, and the obvious guess is that imps start
    /// dying, but guessing it would be inventing behaviour. See Q57.
    /// </summary>
    [Fact]
    public void ManaNeverGoesNegative_HoweverManyImpsAKeeperHas()
    {
        Run run = World(mana: 100_000);

        // The workforce is sized against the land this keeper STARTS with, not
        // against what its free imps have claimed by the time they are all here:
        // the free supply hands out its first imp on tick zero, so by the end of
        // SettleWithFourImps the dungeon is measurably bigger and sizing against
        // it buys enough imps to outrun every unclaimed tile on the fixture map.
        // The claim under test — a working imp pays for itself — needs work left.
        int gain = LandIncome(run);
        SettleWithFourImps(run);

        while (run.Imps * ManaRules.ImpUpkeepPerSecond(run.Db) <= gain)
            run.Tick(new CreateImpCommand(Keeper));

        ref PlayerState player = ref run.World.Player(Keeper);
        player.Mana = 10;

        // Sampled every tick rather than checked at the end, because the end is
        // not where a negative would show: this keeper's imps are out claiming
        // land, so their income climbs to meet the upkeep and mana settles at a
        // low positive number rather than pinning at zero. What must hold all
        // the way through is that it never goes below it.
        for (int i = 0; i < SimulationTiming.TicksPerSecond * 60; i++)
        {
            run.Tick();
            Assert.True(run.Mana >= 0, $"mana went negative: {run.Mana}");
        }

        // And it recovers: this keeper started at 10 with more imps than their
        // land could pay for, and those imps spent the minute claiming more of
        // it. An imp that works is worth more than it costs, which is the shape
        // the economy ought to have.
        Assert.True(run.Mana > 10, $"mana never recovered: {run.Mana}");
    }

    /// <summary>
    /// <b>A keeper whose income beats their upkeep sits pinned at the ceiling.</b>
    /// CONFIRMED (owner, 2026-08-30) from a screenshot of a full keeper: the
    /// counter reads exactly <c>200000</c> while the orb shows <b>+400</b> and
    /// <b>-297</b>.
    ///
    /// <para>That fixes the <i>order</i> of two operations, which is the sort of
    /// thing that would otherwise have been chosen rather than known: income and
    /// upkeep are netted and then clamped to the ceiling, not clamped and then
    /// charged. The other order parks a full keeper 297 below the cap for ever,
    /// and nobody would have noticed for a long time.</para>
    /// </summary>
    [Fact]
    public void AFullKeeperStaysExactlyAtTheCeiling_NotUpkeepShortOfIt()
    {
        Run run = World();
        SettleWithFourImps(run);

        int threshold = ManaRules.Threshold(run.Db);
        ref PlayerState rich = ref run.World.Player(Keeper);
        rich.Mana = threshold;

        // This keeper earns more than their four imps cost, so they should stay
        // nailed to the ceiling rather than sagging below it.
        Assert.True(LandIncome(run) > 4 * ManaRules.ImpUpkeepPerSecond(run.Db),
            "this fixture no longer out-earns its imps; the test needs a richer keeper");

        run.Ticks(SimulationTiming.TicksPerSecond * 10);

        Assert.Equal(threshold, run.Mana);
    }

    // ---- the gate ---------------------------------------------------------

    [Fact]
    public void UpkeepSurvivesASnapshot()
    {
        Run run = World(mana: 5_000);
        SettleWithFourImps(run);

        byte[] saved = WorldSnapshot.ToBytes(run.Sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            saved, Dk2TerrainFixture.Database(), Dk2TerrainFixture.DefaultSystems());

        Assert.Equal(run.Sim.StateHash(), restored.StateHash());

        for (int i = 0; i < 200; i++)
        {
            run.Tick();
            restored.Tick();
            Assert.Equal(run.Sim.StateHash(), restored.StateHash());
        }
    }
}
