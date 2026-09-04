// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// Anger, and the first test in this file is the owner's stopwatch.
///
/// <para>The ceiling — 32767 — blocked this mechanic for four sittings. Every
/// rate was in the shipped data, <c>UnhappyThreshold</c> was 50 on all 46
/// creatures, and 50% <i>of what</i> was in none of the 214 balance variables,
/// not in the creature record, and not in OpenKeeper (whose
/// <c>CreatureMoodSystem</c> is an empty stub carrying the comment
/// <c>// I guess the initial mood is 10000</c>).</para>
///
/// <para>It was settled by putting a Goblin in a dungeon with no lair and
/// timing it: <b>unhappy at ~2:45, angry at ~5:30</b>. Those are 32767/2 and
/// 32767 to within a percent, and the 2:1 ratio pins the structure
/// independently of the ceiling. That measurement is
/// <see cref="TheOwnersStopwatch_ALairlessGoblinIsUnhappyAt165sAndAngryAt330s"/>,
/// and if a refactor ever moves those numbers this suite fails on the one
/// assertion that came from the original game rather than from a file.</para>
/// </summary>
public class CreatureAngerTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = 7;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
            [new CreatureSleepSystem(), new CreatureHealthSystem(), new CreatureAngerSystem()]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static void PaintLair(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Lair), Keeper);
    }

    // ---- the measurement --------------------------------------------------

    [Fact]
    public void TheOwnersStopwatch_ALairlessGoblinIsUnhappyAt165sAndAngryAt330s()
    {
        // OWNER MEASUREMENT (Q106, 2026-09-04), and the only assertion in this
        // project whose expected values came from playing the original:
        //
        //   "it became unhappy after approx 02:45ish minutes and angry after
        //    approx 05:30ish minutes"
        //
        // At AngerNoLair = 100/s that is 16 500 and 33 000, against 16 383
        // (32767/2) and 32 767 (short.MaxValue). Both inside 1%.
        //
        // Asserted as the tick the mood *changes*, with a one-second tolerance,
        // because the systems run on the last tick of each game second.
        GameDatabase db = Db;
        WorldState world = World(db);
        var events = new ListEventSink();

        // No lair anywhere, so AngerNoLair is the only source running.
        world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Simulation(db, world, events).Run(Second * 400);

        List<SimEvent> moods = events.Events.Where(e => e.Kind == "creature_mood").ToList();
        Assert.Equal(2, moods.Count);

        long unhappyAt = moods[0].Tick.Value / Second;
        long angryAt = moods[1].Tick.Value / Second;

        Assert.Contains("Content->Unhappy", moods[0].Detail);
        Assert.Contains("Unhappy->Angry", moods[1].Detail);

        // 16383 / 100 = 163.83 s; the owner's watch said "approx 2:45" = 165 s.
        Assert.InRange(unhappyAt, 163, 165);

        // 32767 / 100 = 327.67 s; the owner said "approx 5:30" = 330 s.
        Assert.InRange(angryAt, 327, 329);

        // And the structural half, which is the stronger evidence: angry lands
        // at twice unhappy, because the thresholds are 50% and 100%. The
        // tolerance is the one-second quantisation, not slack — 327/163 is
        // 2.006, and both endpoints are rounded down to a whole second by the
        // per-second cadence.
        Assert.InRange((double)angryAt / unhappyAt, 1.99, 2.02);
    }

    [Fact]
    public void TheCeilingIsShortMaxValueAndUnhappyIsHalfOfIt()
    {
        CreatureDef goblin = Db.CreatureById(GoblinId);

        Assert.Equal(32767, CreatureAngerRules.Ceiling);
        Assert.Equal(short.MaxValue, CreatureAngerRules.Ceiling);
        Assert.Equal(16383, CreatureAngerRules.UnhappyAt(goblin));
        Assert.Equal(32767, CreatureAngerRules.AngryAt(goblin));

        // The same saturated s16 the record already uses to mean "never" for
        // hunger and sleep. One house style, two meanings.
        Assert.Equal(CreatureAngerRules.Ceiling, CreatureHealthRules.Never);
    }

    [Fact]
    public void TenThousandIsRefutedByTheMeasurement()
    {
        // OpenKeeper's guess, in a comment, never implemented. Worth an explicit
        // test because it is the number a reader is most likely to find while
        // looking for corroboration — and it predicts 50 s and 100 s against an
        // observed 165 s and 330 s, so it is out by a factor of 3.3.
        CreatureDef goblin = Db.CreatureById(GoblinId);

        const int openKeepersGuess = 10_000;
        int wouldBeUnhappyAt = openKeepersGuess / 2 / goblin.AngerNoLair;

        Assert.Equal(50, wouldBeUnhappyAt);
        Assert.NotInRange(wouldBeUnhappyAt, 160, 170);   // nowhere near 2:45
    }

    // ---- the ladder -------------------------------------------------------

    [Fact]
    public void AGoblinWithABedNeverGetsAngryAtAll()
    {
        // The control. A mechanic that angers everybody is not a mechanic, and
        // this is the difference the *player* makes: one lair tile.
        GameDatabase db = Db;
        WorldState world = World(db);
        PaintLair(db, world, new TilePos(4, 4));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 400);

        Assert.Equal(0, goblin.Anger);
        Assert.Equal(CreatureMood.Content, CreatureAngerRules.MoodOf(db.CreatureById(GoblinId), goblin));
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_mood");
        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
    }

    [Fact]
    public void AnAngryGoblinLeavesTheDungeonAndGivesUpItsBed()
    {
        // Seeded so the 50/50 Leave/Rebel draw comes out Leave. Both outcomes
        // are real; only one is implemented, and this covers the one that is.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Anger = CreatureAngerRules.Ceiling;
        goblin.AngryChoice = JobType.Leave;      // the outcome under test
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second);

        Assert.Empty(world.Creatures);
        Assert.Contains(events.Events, e => e.Kind == "creature_left");
        Assert.Equal(-1, goblin.LairTileIndex);
    }

    [Fact]
    public void AGoblinThatDrawsRebelStaysPutAndSaysSo()
    {
        // The unimplemented half, tested for what it *is* rather than quietly
        // turned into desertion. Rebelling needs a creature to change sides and
        // become a hero; RebelLeavingPercentage and its two siblings have
        // nothing to act on. So the creature stays, angry, and the log says it.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Anger = CreatureAngerRules.Ceiling;
        goblin.AngryChoice = JobType.Rebel;

        Simulation(db, world).Run(Second * 30);

        Assert.Single(world.Creatures);
        Assert.Equal(CreatureAngerRules.Ceiling, goblin.Anger);
    }

    [Fact]
    public void TheAngryChoiceIsDrawnOnceAndKept()
    {
        // Re-rolling every second would eventually draw Leave by luck, which
        // would report the unbuilt outcome as the built one. So the draw sticks.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Anger = CreatureAngerRules.Ceiling;
        goblin.AngryChoice = JobType.Rebel;
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 50);

        // One decision, not fifty.
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_angry");
        Assert.Equal(JobType.Rebel, goblin.AngryChoice);
    }

    // ---- recovery ---------------------------------------------------------

    [Fact]
    public void SleepingInItsOwnBedCalmsAGoblinFiveAndAHalfTimesFasterThanItAngered()
    {
        // ModifyAngerOfCreatureInLairPerSecond is -550 against AngerNoLair's
        // +100, so a bed undoes in one second what five and a half seconds
        // without one did. That asymmetry is the shipped data's, and it is why
        // a keeper who builds a lair late still recovers.
        GameDatabase db = Db;
        WorldState world = World(db);
        var bed = new TilePos(4, 4);
        PaintLair(db, world, bed);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, bed);
        Assert.True(LairRules.TryClaimBed(world, db, goblin));

        goblin.Anger = 10_000;
        goblin.Activity = CreatureActivity.Sleeping;

        Simulation(db, world).Run(Second);

        Assert.Equal(10_000 - 550, goblin.Anger);
        Assert.Equal(-550, db[VariableTypeId.ModifyAngerOfCreatureInLairPerSecond]);
        Assert.Equal(100, db.CreatureById(GoblinId).AngerNoLair);
    }

    [Fact]
    public void AngerFloorsAtZeroRatherThanGoingNegative()
    {
        // Ours, not the data's (Q115). Without the floor a creature that had
        // just won a combat pit (-16400) would be immune to every grievance for
        // nearly three minutes.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Anger = 100;
        Assert.Equal(-100, CreatureAngerRules.Apply(goblin, -5000));
        Assert.Equal(0, goblin.Anger);
    }

    [Fact]
    public void AngerCeilsAtTheCeiling()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Anger = CreatureAngerRules.Ceiling - 10;
        Assert.Equal(10, CreatureAngerRules.Apply(goblin, 5000));
        Assert.Equal(CreatureAngerRules.Ceiling, goblin.Anger);
    }

    // ---- the discrete deltas, which Q114 has not settled ------------------

    [Fact]
    public void TheOwnersSlapCount_SixToUnhappyAndFiveMoreToAngry()
    {
        // OWNER MEASUREMENT (Q114, 2026-09-04), on a Salamander — whose
        // AngerSlap is also 3000:
        //
        //   "exactly 6 slaps made a creature (salamander) unhappy, and after
        //    that 5 slaps made it angry"
        //
        // This is the second of the two measurements, and it is the one that
        // closed the mechanic. Timing alone could not separate (100/s into
        // 32767) from (200/s into 65535), because doubling both preserves every
        // duration — the Q47 trap. A slap is a *discrete* delta, so it scales
        // with the ceiling where a rate does not.
        //
        // The two counts BRACKET the ceiling rather than merely agreeing:
        //
        //   5 slaps not enough, 6 enough  -> unhappy in (15000, 18000]
        //                                 -> ceiling in (30000, 36000]
        //   10 not enough, 11 enough      -> angry   in (30000, 33000]
        //                                 -> ceiling in (30000, 33000]
        //
        // 32767 is the only meaningful number in the intersection. 65535 would
        // have needed 11 slaps to *unhappy* and 22 to angry.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureDef def = db.CreatureById(GoblinId);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        Assert.Equal(3000, def.AngerSlap);

        int toUnhappy = 0;
        while (CreatureAngerRules.MoodOf(def, goblin) == CreatureMood.Content)
        {
            CreatureAngerRules.Slap(def, goblin);
            toUnhappy++;
            Assert.True(toUnhappy < 50, "slapping never made it unhappy");
        }

        int thenToAngry = 0;
        while (CreatureAngerRules.MoodOf(def, goblin) == CreatureMood.Unhappy)
        {
            CreatureAngerRules.Slap(def, goblin);
            thenToAngry++;
            Assert.True(thenToAngry < 50, "slapping never made it angry");
        }

        Assert.Equal(6, toUnhappy);
        Assert.Equal(5, thenToAngry);
        Assert.Equal(11, toUnhappy + thenToAngry);
    }

    [Fact]
    public void TheSlapCountExcludesTheCeilingsTheTimingCouldNot()
    {
        // The bracketing argument as an assertion, because it is the whole
        // reason the ceiling is Confirmed rather than StronglyInferred. If
        // someone later "tidies" Ceiling to 65535 or 10000, this says why not.
        CreatureDef goblin = Db.CreatureById(GoblinId);
        const int slap = 3000;

        // Under the true ceiling: the 6th slap tips unhappy, the 11th tips angry.
        Assert.True(5 * slap < CreatureAngerRules.UnhappyAt(goblin));
        Assert.True(6 * slap >= CreatureAngerRules.UnhappyAt(goblin));
        Assert.True(10 * slap < CreatureAngerRules.AngryAt(goblin));
        Assert.True(11 * slap >= CreatureAngerRules.AngryAt(goblin));

        // Under 65535 the observation would have been 11 and 22, not 6 and 11.
        const int refuted = 65535;
        Assert.True(6 * slap < refuted / 2, "65535 would have needed 11 slaps to unhappy");

        // Under 10000 it would have been 2 and 4.
        const int alsoRefuted = 10_000;
        Assert.True(2 * slap >= alsoRefuted / 2, "10000 would have needed only 2 slaps");
    }

    [Fact]
    public void BeingPickedUpCalmsAGoblinAndAnnoysJack()
    {
        // The sign flip that made the signedness bug obvious (ADR-0050). Read
        // unsigned, a Goblin's AngerInHand is 65286.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureDef def = db.CreatureById(GoblinId);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Anger = 1000;
        CreatureAngerRules.Held(def, goblin);

        Assert.Equal(-250, def.AngerInHand);
        Assert.Equal(750, goblin.Anger);
    }

    // ---- who is exempt ----------------------------------------------------

    [Fact]
    public void AnImpIsNeverAngeredBecauseItHasNoBedToMiss()
    {
        // The Imp's AngerNoLair is 1, not 0 — so it *can* be angered in
        // principle, and at 32767 a lairless imp would take nine hours. But it
        // needs no bed at all (LairObjectId 0), so PerSecond never charges it.
        // The DK2 wiki says flatly "Imps never get angry"; the data says the
        // same thing without a special case.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 2));

        Simulation(db, world).Run(Second * 400);

        Assert.Equal(0, imp.Anger);
        Assert.Equal(1, db.CreatureById(Dk2TerrainFixture.ImpCreatureId).AngerNoLair);
    }

    [Fact]
    public void ADyingCreatureAccruesNoGrievance()
    {
        // Something lying on the floor waiting to be carried to bed is not
        // getting crosser about not having got there — and a dying creature
        // that deserted would leave the rescue window by a second route.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        goblin.Health = 0;
        Simulation(db, world).Run(Second * 30);

        Assert.Equal(CreatureActivity.Dying, goblin.Activity);
        Assert.Equal(0, goblin.Anger);
    }
}
