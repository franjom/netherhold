// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>Where imps come from. CONFIRMED (owner, 2026-08-30): every keeper
/// starts with four imps and four is a floor rather than a starting figure — drop
/// them all into the dungeon heart and they come back at roughly five-second
/// intervals — and beyond that an imp must be bought with the Create Imp spell,
/// at 1500 mana for the first and 1500 more each time after.</summary>
/// <remarks>The shipped data names the first two: <c>MinimumImpThreshold</c> is 4
/// and <c>TimeBeforeFreeImpGeneratedSeconds</c> is 5. The tests below assert
/// against the variables rather than against 4 and 5, so they test the rule and
/// not the transcription.</remarks>
public class ImpSupplyTests
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

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);

        public int Imps => ImpSupplyRules.ImpCount(World, Db, Keeper);

        public int Mana => World.Player(Keeper).Mana;
    }

    /// <summary>
    /// A keeper with a dungeon heart and no imps. The heart is laid over the
    /// fixture's claimed-path room, which is already theirs.
    /// </summary>
    private static Run WithAHeart(int mana = 0, bool heart = true)
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

    // ---- the free supply --------------------------------------------------

    [Fact]
    public void AKeeperWithNoImpsIsGivenFourOfThem()
    {
        Run run = WithAHeart();
        int minimum = ImpSupplyRules.MinimumImps(run.Db);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);

        run.Ticks(interval * minimum + 1);

        Assert.Equal(minimum, run.Imps);
        Assert.Equal(minimum, run.Of("imp_generated").Count());
    }

    [Fact]
    public void TheFreeImpsArriveOneEveryInterval_NotAllAtOnce()
    {
        // TimeBeforeFreeImpGeneratedSeconds is the gap between them, which is
        // what the owner measured by emptying a dungeon of imps and watching.
        Run run = WithAHeart();
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);

        run.Ticks(interval * 4 + 1);

        List<long> at = run.Of("imp_generated").Select(e => e.Tick.Value).ToList();
        Assert.Equal(4, at.Count);
        for (int i = 1; i < at.Count; i++)
            Assert.Equal(interval, (int)(at[i] - at[i - 1]));
    }

    [Fact]
    public void TheFourthImpIsTheLastFreeOne()
    {
        Run run = WithAHeart();
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);

        run.Ticks(interval * 12);

        Assert.Equal(ImpSupplyRules.MinimumImps(run.Db), run.Imps);
    }

    [Fact]
    public void KillOneAndItComesBack()
    {
        // The floor is a floor, not a starting allowance: this is the rule the
        // owner established by dropping every imp into the heart.
        Run run = WithAHeart();
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);
        run.Ticks(interval * 5);
        Assert.Equal(4, run.Imps);

        run.World.RemoveCreature(run.World.Creatures[0].Id);
        Assert.Equal(3, run.Imps);

        run.Ticks(interval + 1);
        Assert.Equal(4, run.Imps);
    }

    [Fact]
    public void AReplacementAfterALongSpellAtFullStrengthArrivesAtOnce()
    {
        // CONFIRMED (owner, filmed 2026-09-05): thirty seconds sitting at exactly
        // four imps, then one imp dropped into the dungeon heart, and the
        // replacement walking around about 1.4 seconds later — not five. The
        // original keeps a DEADLINE and writes it only when an imp is made, so a
        // keeper who has not been owed one for half a minute has a deadline half a
        // minute in the past and the very next evaluation pays out.
        Run run = WithAHeart();
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);
        run.Ticks(interval * 4);
        Assert.Equal(4, run.Imps);

        run.Ticks(interval * 20);
        int before = run.Of("imp_generated").Count();

        run.World.RemoveCreature(run.World.Creatures[0].Id);
        run.Tick();

        Assert.Equal(4, run.Imps);
        Assert.Equal(before + 1, run.Of("imp_generated").Count());
    }

    [Fact]
    public void OnlyTheFirstReplacementIsFree_TheRestAreAnIntervalApart()
    {
        // The other half of the recording: "when several Imps were missing,
        // subsequent replacements appeared at approximately 5-second intervals".
        // So the deadline being stale buys exactly one immediate imp, and from
        // there the interval governs.
        Run run = WithAHeart();
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);
        run.Ticks(interval * 4);
        run.Ticks(interval * 20);

        for (int i = 0; i < 3; i++) run.World.RemoveCreature(run.World.Creatures[0].Id);
        Assert.Equal(1, run.Imps);

        long from = run.Sim.CurrentTick.Value;
        run.Ticks(interval * 3);

        List<long> at = run.Of("imp_generated")
            .Where(e => e.Tick.Value >= from)
            .Select(e => e.Tick.Value - from)
            .ToList();

        Assert.Equal(3, at.Count);
        Assert.Equal(0, at[0]);
        Assert.Equal(interval, at[1]);
        Assert.Equal(interval * 2, at[2]);
    }

    [Fact]
    public void AKeeperWithNowhereToPutAnImpStillBurnsTheInterval()
    {
        // The original advances the deadline without looking at whether the cast
        // succeeded, so a keeper with no heart is evaluated, places nothing and
        // still pays the interval — the first imp after a heart appears is not
        // instant. STRONGLY INFERRED from the instruction order: the deadline is
        // written unconditionally on the line after the call. Nothing observable
        // turns on it in a shipped level; a keeper with no heart has lost (Q40).
        Run run = WithAHeart(heart: false);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);

        run.Ticks(interval * 4 + 1);
        Assert.Empty(run.Of("imp_generated"));

        // Now give them somewhere to put one, one tick after a deadline was set.
        ref TileState tile = ref run.World.Tile(2, 2);
        tile.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
        tile.Health = run.Db.TerrainById(tile.TerrainId).StartingHealth;
        tile.OwnerPlayerId = Keeper;

        run.Tick();
        Assert.Equal(0, run.Imps);

        run.Ticks(interval);
        Assert.Equal(1, run.Imps);
    }

    [Fact]
    public void NoDungeonHeartMeansNoImps()
    {
        // In DK2 losing your heart is defeat (Q40), so "no heart, no imps" needs
        // no rule of its own — it falls out of there being nowhere to put one.
        Run run = WithAHeart(heart: false);

        run.Ticks(ImpSupplyRules.FreeImpIntervalTicks(run.Db) * 6);

        Assert.Equal(0, run.Imps);
        Assert.Empty(run.Of("imp_generated"));
    }

    // ---- Create Imp -------------------------------------------------------

    [Fact]
    public void CreateImpBuysAnImpAndSpendsTheMana()
    {
        Run run = WithAHeart(mana: 10_000);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);
        run.Ticks(interval * 5);          // let the free four arrive first
        int before = run.Mana;

        run.Tick(new CreateImpCommand(Keeper));

        Assert.Equal(5, run.Imps);
        Assert.Equal(before - ImpSupplyRules.CreateImpBaseCost(run.Db), run.Mana);
        Assert.Contains(run.Of("imp_created"), e => e.Detail.Contains("cost=1500"));
    }

    /// <summary>A cast onto a tile (packet action 54) makes the imp there, not at the heart.
    /// The tile is trusted: the position cast's location check is not built for Create Imp.</summary>
    [Fact]
    public void CreateImpCastOntoATileMakesTheImpThere_TheLocationCheckIsNotBuilt()
    {
        Run run = WithAHeart(mana: 10_000);
        run.Ticks(ImpSupplyRules.FreeImpIntervalTicks(run.Db) * 5);
        var at = new TilePos(2, 3);

        run.Tick(new CreateImpCommand(Keeper, at));

        Assert.Equal(5, run.Imps);
        Assert.Contains(run.Of("imp_created"), e => e.Detail.Contains("at=(2,3)"));
        Assert.Contains(run.World.Creatures, c => c.Tile == at);
    }

    [Fact]
    public void EachBoughtImpCostsFifteenHundredMore()
    {
        // The rule that stops a rich keeper spamming imps: 1500, then 3000, then
        // 4500. The base is DK2's own — the mana cost on its Create Imp record —
        // and the escalation is the owner's description (Q54).
        Run run = WithAHeart(mana: 100_000);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);
        run.Ticks(interval * 5);

        var costs = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            int before = run.Mana;
            run.Tick(new CreateImpCommand(Keeper));
            costs.Add(before - run.Mana);
        }

        Assert.Equal([1500, 3000, 4500], costs);
        Assert.Equal(7, run.Imps);
    }

    /// <summary>
    /// <b>The free four are free, and the price falls back.</b> CONFIRMED by the
    /// owner: "if you drop down to 4 (something killed your imps), cost also
    /// drops to base 1500".
    ///
    /// This is what makes the cost a function of the imps a keeper <i>has</i>
    /// rather than a tally of what they have ever bought — and it is the whole
    /// difference between the implemented rule and the obvious alternative,
    /// which would keep charging 4500 for a keeper who had been wiped out.
    /// </summary>
    [Fact]
    public void LosingImpsPutsThePriceBackToTheBase()
    {
        Run run = WithAHeart(mana: 100_000);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);
        run.Ticks(interval * 5);

        run.Tick(new CreateImpCommand(Keeper));
        run.Tick(new CreateImpCommand(Keeper));
        Assert.Equal(6, run.Imps);
        Assert.Equal(4500, ImpSupplyRules.CreateImpCost(run.World, run.Db, Keeper));

        // Something kills the two bought ones. The keeper is back on their free
        // four, so the next imp is back to base price.
        run.World.RemoveCreature(run.World.Creatures[^1].Id);
        run.World.RemoveCreature(run.World.Creatures[^1].Id);

        Assert.Equal(4, run.Imps);
        Assert.Equal(
            ImpSupplyRules.CreateImpBaseCost(run.Db),
            ImpSupplyRules.CreateImpCost(run.World, run.Db, Keeper));

        int before = run.Mana;
        run.Tick(new CreateImpCommand(Keeper));
        Assert.Equal(before - 1500, run.Mana);
    }

    [Fact]
    public void AKeeperWhoCannotAffordAnImpGetsNothingAndIsToldWhy()
    {
        Run run = WithAHeart(mana: 100);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);
        run.Ticks(interval * 5);

        // This keeper's land pays them while they wait, so the assertion is that
        // mana never went DOWN, not that it stayed at 100. It is still short of
        // the 1500 the spell wants.
        int before = run.Mana;
        Assert.True(before < ImpSupplyRules.CreateImpBaseCost(run.Db),
            $"the keeper earned {before} while waiting, which is enough to buy an imp");

        run.Tick(new CreateImpCommand(Keeper));

        Assert.Equal(4, run.Imps);
        Assert.True(run.Mana >= before, $"mana fell from {before} to {run.Mana} on a refused cast");
        SimEvent refused = Assert.Single(run.Of("create_imp_refused"));
        Assert.Contains("reason=not_enough_mana", refused.Detail);
        Assert.Contains("cost=1500", refused.Detail);
    }

    [Fact]
    public void BuyingAnImpWithNoHeartIsRefused_AndCostsNothing()
    {
        Run run = WithAHeart(mana: 10_000, heart: false);

        run.Tick(new CreateImpCommand(Keeper));

        Assert.Equal(0, run.Imps);
        Assert.Equal(10_000, run.Mana);
        Assert.Contains(run.Of("create_imp_refused"), e => e.Detail.Contains("reason=no_dungeon_heart"));
    }

    /// <summary>
    /// <b>The first thing in this simulation that spends mana.</b> Worth its own
    /// test: until now every scenario run said "mana here is income only, nothing
    /// spends it", and that sentence is now false.
    /// </summary>
    [Fact]
    public void ManaCanNowGoDown_NotJustUp()
    {
        Run run = WithAHeart(mana: 5_000);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);
        run.Ticks(interval * 5);

        int peak = run.Mana;
        run.Tick(new CreateImpCommand(Keeper));

        Assert.True(run.Mana < peak, $"mana did not fall: {peak} -> {run.Mana}");
    }

    // ---- the gate ---------------------------------------------------------

    [Fact]
    public void AnImpDeadlineSurvivesASnapshot()
    {
        Run run = WithAHeart();
        run.Ticks(ImpSupplyRules.FreeImpIntervalTicks(run.Db) / 2);

        byte[] saved = WorldSnapshot.ToBytes(run.Sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            saved, Dk2TerrainFixture.Database(), Dk2TerrainFixture.DefaultSystems());

        Assert.Equal(run.Sim.StateHash(), restored.StateHash());

        for (int i = 0; i < 400; i++)
        {
            run.Tick();
            restored.Tick();
            Assert.Equal(run.Sim.StateHash(), restored.StateHash());
        }
    }

    // ---- only a keeper is staffed (Q91) -----------------------------------

    /// <summary>
    /// The same dungeon, but the heart and the land belong to whichever id is
    /// asked for — so a non-keeper can be given everything a keeper has and
    /// still get nothing.
    /// </summary>
    private static Run WithAHeartFor(byte owner, int mana = 0)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [PlayerIds.Unowned, PlayerIds.Good,
                                                PlayerIds.Neutral, Keeper]);

        // Hand the whole starting room over, then put a heart in it.
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                world.Tile(x, y).OwnerPlayerId = owner;

        ref TileState tile = ref world.Tile(2, 2);
        tile.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
        tile.Health = database.TerrainById(tile.TerrainId).StartingHealth;
        tile.OwnerPlayerId = owner;

        world.Player(owner).Mana = mana;

        var sim = new GameSimulation(database, world, 5, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink, database);
    }

    [Theory]
    [InlineData(PlayerIds.Unowned)]
    [InlineData(PlayerIds.Good)]
    [InlineData(PlayerIds.Neutral)]
    public void ANonKeeperIsNeverGivenFreeImpsEvenWithAHeartOfItsOwn(byte notAKeeper)
    {
        // The defect Q91 named: ImpSupplySystem looped every player, and 0, 1 and
        // 2 are all real ids that own real terrain — 167 of the campaign's portals
        // belong to Neutral. It was harmless only because no shipped level gives
        // any of them a dungeon heart, so the rule held because of a fact about
        // the levels rather than because anything checked it. This test gives them
        // the heart; before the gate, the hero side staffed itself with imps.
        Run run = WithAHeartFor(notAKeeper);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);

        run.Ticks(interval * (ImpSupplyRules.MinimumImps(run.Db) + 2));

        Assert.Empty(run.World.Creatures);
        Assert.Empty(run.Of("imp_generated"));
    }

    [Fact]
    public void AKeeperWithTheSameHeartIsStaffed_WhichIsWhatMakesTheGateAGate()
    {
        // The control. A guard that refuses everybody proves nothing, so the
        // identical world with a keeper's id on it must still produce imps.
        Run run = WithAHeartFor(Keeper);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(run.Db);

        run.Ticks(interval * ImpSupplyRules.MinimumImps(run.Db) + 1);

        Assert.Equal(ImpSupplyRules.MinimumImps(run.Db), run.Imps);
    }

    [Theory]
    [InlineData(PlayerIds.Good)]
    [InlineData(PlayerIds.Neutral)]
    public void ANonKeeperCannotCastCreateImpHoweverMuchManaItHolds(byte notAKeeper)
    {
        // Refused *distinctly* from "no such player": these ids exist and own
        // terrain. They simply may not have imps.
        Run run = WithAHeartFor(notAKeeper, mana: 100_000);

        run.Tick(new CreateImpCommand(notAKeeper));

        Assert.Empty(run.World.Creatures);
        Assert.Contains(run.Of("create_imp_refused"),
            e => e.Detail.Contains("reason=not_a_keeper"));

        // And the mana is untouched, because the refusal happens before the
        // cost is even looked up.
        Assert.Equal(100_000, run.World.Player(notAKeeper).Mana);
    }
}
