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

    private const byte DeserterId = Dk2TerrainFixture.DeserterCreatureId;
    private const byte VampireId = Dk2TerrainFixture.VampireCreatureId;

    /// <summary>
    /// The roster plus a Goblin that can only choose <c>Leave</c>. Since
    /// ADR-0061 the angry job is re-rolled, so a test about the walk out is
    /// written on a creature with nothing to roll — otherwise it is a test
    /// about seed 42.
    /// </summary>
    private static GameDatabase DeserterDb =>
        Dk2TerrainFixture.Database(Dk2TerrainFixture.Deserter());

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        // The mover is in here too, since 2026-09-05: a bed is walked to, so a
        // ladder test with a lair in it needs the creature to be able to get
        // there (ADR-0060).
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
            new CreatureAngerSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static void PaintLair(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Lair), Keeper);
    }

    /// <summary>
    /// The ladder plus the mover, which is what a deserter needs: leaving is a
    /// walk to the nearest portal (Q116), so without
    /// <see cref="CreatureMovementSystem"/> a creature decides to go and then
    /// stands there for ever.
    /// </summary>
    private static GameSimulation Walking(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
            new CreatureAngerSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static void PaintPortal(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Portal), Keeper);
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
        //
        // The tile is a diagonal step away rather than under the creature's
        // feet, because a bed is walked to (ADR-0060). The two seconds of walk
        // cost 200 anger at AngerNoLair's 100 a second, and THAT 200 IS NEVER
        // REPAID — the bed drains a different counter (ADR-0080), so what a
        // keeper who builds late buys is the bleeding stopping, not the wound
        // healing. 200 out of a 16383 unhappy band is nothing, which is the
        // real claim here: the run never gets within sight of a mood change.
        GameDatabase db = Db;
        WorldState world = World(db);
        PaintLair(db, world, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 400);

        Assert.True(LairRules.HasBed(world, db, goblin));
        Assert.Equal(CreatureMood.Content, CreatureAngerRules.MoodOf(db.CreatureById(GoblinId), goblin));

        // Two seconds of walking to it, and not one point more in four hundred.
        Assert.InRange(goblin.Anger.Lair, 1, 400);
        Assert.Equal(goblin.Anger.Lair, goblin.Anger.Max);
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_mood");
        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
    }

    [Fact]
    public void AnAngryGoblinWalksToThePortalAndLeavesThroughIt()
    {
        // OWNER, Q116 (2026-09-04): "angry evil creature goes to the nearest
        // available portal and leaves the dungeon."
        //
        // Which is what this test is really about: the creature is NOT gone on
        // the second it decides. It gives up its bed, sets off, and is still in
        // the dungeon — killable, and still on the payroll — for the length of
        // the walk. This method previously removed it on the spot and said
        // via=nowhere_in_particular in its own event.
        GameDatabase db = DeserterDb;
        WorldState world = World(db);
        PaintPortal(db, world, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, DeserterId, Keeper, new TilePos(1, 1));
        goblin.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        GameSimulation sim = Walking(db, world, events);

        // One second in: decided, bed given up, walking, still here.
        sim.Run(Second);
        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
        Assert.Equal(CreatureActivity.Leaving, goblin.Activity);
        Assert.Equal(-1, LairRules.BedTileIndex(world, db, goblin));
        Assert.NotEmpty(goblin.Path);

        SimEvent leaving = Assert.Single(
            events.Events, e => e.Kind == "creature_leaving");
        Assert.Contains("portal=(3,3)", leaving.Detail);

        // And then it gets there. Two diagonal tiles at 0.7 tiles a second.
        sim.Run(Second * 20);

        Assert.DoesNotContain(world.Creatures, c => c.Id == goblin.Id);
        SimEvent left = Assert.Single(events.Events, e => e.Kind == "creature_left");
        Assert.Contains("via=portal", left.Detail);
    }

    [Fact]
    public void AKeeperWithNoPortalKeepsTheCreatureThatWantedToLeave()
    {
        // The consequence of Q116 that is behaviour rather than bookkeeping: if
        // there is nowhere to walk to, the creature does not leave. It stands
        // in the dungeon, angry, having given up its bed.
        //
        // The fixture map has no portal at all, which makes this the default
        // case here and is why the assertion is worth writing down: a rule that
        // removed the creature regardless would pass every other test in this
        // file.
        GameDatabase db = DeserterDb;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, DeserterId, Keeper, new TilePos(2, 2));
        goblin.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        Walking(db, world, events).Run(Second * 60);

        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
        Assert.Equal(CreatureActivity.Leaving, goblin.Activity);
        Assert.Equal(-1, LairRules.BedTileIndex(world, db, goblin));
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_left");

        // Reported once, on the second it first failed, and not once a second
        // for the next minute.
        Assert.Single(events.Events, e => e.Kind == "creature_leave_blocked");
    }

    [Fact]
    public void ADeserterDoesNotTakeABedOnItsWayOut()
    {
        // The bed is released when it decides to go, and the sleep system skips
        // a leaver — so a lair that becomes free while a deserter is walking
        // past it stays free for a creature that intends to stay.
        //
        // Without the guard in CreatureSleepSystem the deserter claims that
        // bed, because it is a creature with no bed standing next to a free
        // lair tile, and then walks out of the dungeon holding it.
        GameDatabase db = DeserterDb;
        WorldState world = World(db);
        PaintLair(db, world, new TilePos(2, 2));

        CreatureState goblin = world.SpawnCreature(db, DeserterId, Keeper, new TilePos(2, 2));
        goblin.Anger.Lair = CreatureAngerRules.Ceiling + 1;

        Walking(db, world).Run(Second * 30);

        Assert.Equal(CreatureActivity.Leaving, goblin.Activity);
        Assert.Equal(-1, LairRules.BedTileIndex(world, db, goblin));
        Assert.False(LairRules.BedTaken(world, db, (2 * world.Width) + 2));
    }

    [Fact]
    public void AnUnhappyGoblinGivenABedCoolsBackToContentOneSleepAtATime()
    {
        // OWNER, Q115 (2026-09-04): "anger can be cooled down to unhappy, and
        // unhappy can cool down to happy."
        //
        // So the ladder is reversible and a mood is a pure function of the
        // anger value — no latch, no hysteresis, no "once angry always angry".
        // That is what CreatureAngerRules.MoodOf already is, and this test is
        // what would notice if somebody made a transition sticky to stop a
        // creature flapping between moods.
        //
        // The shape of the run matters: the lair is painted *after* the goblin
        // is already unhappy, because a lair from the start means it claims a
        // bed on tick 0 and never angers at all (see the control above).
        //
        // **And the rate is the finding.** Cooling is -550 a second *asleep in
        // its own bed*, and a Goblin sleeps for 9 seconds out of every ~250
        // (TimeSleep 9, TimeAwake 240). So a bed does not cure anger smoothly:
        // it cures it in instalments of exactly 9 x 550 = 4950, once every four
        // minutes. One sleep is enough to cross back under the halfway mark
        // from where 170 seconds of lairlessness left it, and it is nowhere
        // near enough to reach zero — which is asserted, because "a bed fixes
        // it" is the intuition and the arithmetic disagrees. See Q123 for the
        // reading this depends on.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        CreatureDef definition = db.CreatureById(GoblinId);
        var events = new ListEventSink();
        GameSimulation sim = Walking(db, world, events);

        // 170 s of no lair at 100 a second: past the 16383 halfway mark.
        sim.Run(Second * 170);
        Assert.Equal(CreatureMood.Unhappy, CreatureAngerRules.MoodOf(definition, goblin));
        int peak = goblin.Anger.Lair;

        // Now the player does something about it. One tile.
        PaintLair(db, world, new TilePos(2, 3));

        // It has to notice and walk there — one tile, a couple of seconds, and
        // each of those seconds is still charged 100 (ADR-0060). The exact
        // arithmetic below starts from the anger it had when the bed went down.
        int waited = 0;
        while (!LairRules.HasBed(world, db, goblin) && waited++ < Second * 20) sim.Run(1);
        Assert.True(LairRules.HasBed(world, db, goblin), "never got its bed");
        int atBed = goblin.Anger.Lair;
        Assert.True(atBed >= peak);

        // Long enough for it to get tired (TimeAwake 240) and sleep once.
        sim.Run(Second * 300);

        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);

        // AND HERE IS THE CORRECTION (ADR-0080). This test used to assert that
        // the creature came back to Content, nine seconds of sleep at -550
        // apiece having undone the lairless grievance. It does not, because
        // DK2 does not keep one anger counter: the lairless charge went into
        // bucket 2 and ModifyAngerOfCreatureInLairPerSecond drains bucket 5.
        //
        // So a bed STOPS THE BLEEDING AND DOES NOT HEAL THE WOUND. The Lair
        // bucket is frozen exactly where the bed found it - not one point of it
        // is repaid - and the creature stays unhappy about the four minutes it
        // spent without one.
        Assert.Equal(atBed, goblin.Anger.Lair);
        Assert.Equal(CreatureMood.Unhappy, CreatureAngerRules.MoodOf(definition, goblin));

        // The sleep did run and its calm did land; it landed in a bucket that
        // was already empty, so it bought nothing. What that -550 is FOR is a
        // slap, which is the one thing that fills the bucket it drains.
        Assert.Equal(0, goblin.Anger.Treatment);

        // And the ladder never came back down, so the log has no such line.
        Assert.DoesNotContain(events.Events, e =>
            e.Kind == "creature_mood" && e.Detail.Contains("Unhappy->Content"));
    }

    [Fact]
    public void AVampireThatDrawsRebelStaysPutAndSaysSo()
    {
        // The unimplemented half, tested for what it *is* rather than quietly
        // turned into desertion. Rebelling needs a creature to change sides and
        // become a hero; RebelLeavingPercentage and its two siblings have
        // nothing to act on. So the creature stays, angry, and the log says it.
        //
        // A Vampire, because its angry table is Rebel outright where a Goblin's
        // is a coin toss — and since ADR-0061 a coin toss is re-tossed. This is
        // the creature for which the unbuilt outcome is the *only* outcome, and
        // no fall-through may turn it into a walk to the portal.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, new TilePos(2, 2));

        vampire.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 30);

        Assert.Single(world.Creatures);
        Assert.Equal(CreatureActivity.Idle, vampire.Activity);

        // Still angry, and CROSSER than it started — which it could not have
        // been before ADR-0080, when a counter stopped dead at the angry band.
        // It saturates at 65533 instead, so thirty more seconds of having no
        // bed cost it three thousand more it will have to pray off.
        Assert.Equal(CreatureMood.Angry, CreatureAngerRules.MoodOf(db.CreatureById(VampireId), vampire));
        Assert.Equal(CreatureAngerRules.Ceiling + 1 + (30 * 100), vampire.Anger.Lair);

        Assert.All(
            events.Events.Where(e => e.Kind == "creature_angry"),
            e =>
            {
                Assert.Contains("chose=Rebel", e.Detail);
                Assert.Contains("result=unbuilt", e.Detail);
            });
    }

    [Fact]
    public void TheAngryJobIsRolledAfreshEveryFiveGameSecondsAndNotOftener()
    {
        // Q122, from the binary (2026-09-05): the angry chooser rolls afresh on
        // every allowed selection, and a selection is allowed five game seconds
        // after the last one that came to something —
        // LastGameTickSuccessfullyChoseAngryJob is that timestamp, and it is
        // the only thing the chooser keeps.
        //
        // A Vampire rolls Rebel, which is unbuilt: the selection is *made* (the
        // original would have started the job), so it stamps, so the next one
        // is five seconds later. Thirty seconds of anger is six rolls, not
        // thirty.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, new TilePos(2, 2));

        vampire.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 30);

        List<SimEvent> rolls = events.Events
            .Where(e => e.Kind == "creature_angry").ToList();
        Assert.Equal(6, rolls.Count);

        // And exactly five game seconds apart, every time.
        for (int i = 1; i < rolls.Count; i++)
        {
            Assert.Equal(
                5 * Second, rolls[i].Tick.Value - rolls[i - 1].Tick.Value);
        }

        Assert.Equal(rolls[^1].Tick.Value, vampire.LastAngryChoiceTick);
    }

    [Fact]
    public void AGoblinLeavesInTheEndBecauseTheCoinIsTossedAgain()
    {
        // The behaviour ADR-0053 chose *not* to have, and the reason it gave
        // for not having it is a fact about DK2 rather than an argument against
        // it: with Leave(50%) re-rolled every five seconds an angry Goblin
        // walks out — it just takes a round or two.
        //
        // Which is the point of the correction. The old rule drew once, so half
        // of all angry Goblins stood in the dungeon for ever; the original's
        // own record says a coin is tossed, and this is what tossing it
        // repeatedly comes to.
        GameDatabase db = Db;
        WorldState world = World(db);
        PaintPortal(db, world, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));
        goblin.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        Walking(db, world, events).Run(Second * 120);

        Assert.DoesNotContain(world.Creatures, c => c.Id == goblin.Id);
        Assert.Single(events.Events, e => e.Kind == "creature_left");

        // Every roll but the last was the unbuilt half of its own table, and
        // the last one is the one it acted on.
        List<SimEvent> rolls = events.Events
            .Where(e => e.Kind == "creature_angry").ToList();
        Assert.NotEmpty(rolls);
        Assert.Contains("chose=Leave", rolls[^1].Detail);
        Assert.All(rolls[..^1], e => Assert.Contains("chose=Rebel", e.Detail));
    }

    [Fact]
    public void AnAngryCreatureSetsOffForALairBeforeItRollsAnythingAtAll()
    {
        // lair-acquisition.md §8: the unhappy and angry choosers both try for a
        // lair before rolling — the same attempt (FUN_0046f390) the idle state
        // makes, which is why it is one class here too (LairSeeker).
        //
        // The sleep system is left OUT of this simulation deliberately, so that
        // what is asserted is the anger ladder's own attempt rather than the
        // idle cadence's, which would fire seven ticks earlier and pass this
        // test for the wrong reason. The cost is that the journey does not
        // finish: arriving, testing the tile and putting the bed down are the
        // lair state, and the lair state belongs to CreatureSleepSystem. In a
        // dungeon with both — every real one — this creature gets its bed.
        GameDatabase db = Db;
        WorldState world = World(db);
        PaintLair(db, world, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));
        goblin.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new CreatureAngerSystem(),
        ]);
        sim.ObserveWith(events);
        sim.Run(Second);

        Assert.Equal(CreatureActivity.SeekingLair, goblin.Activity);
        Assert.NotEmpty(goblin.Path);

        // The selection was spent on the journey rather than on a roll: the
        // attempt is reported under the anger system's own name, and no roll
        // happened at all.
        SimEvent sought = Assert.Single(events.Events, e => e.Kind == "lair_sought");
        Assert.Equal("CreatureAnger", sought.System);
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_angry");
        Assert.Equal(sought.Tick.Value, goblin.LastAngryChoiceTick);
    }

    [Fact]
    public void TheCumulativeRangesOverlapAtEveryBoundarySoTheEarlierEntryIsFavoured()
    {
        // The original builds each entry's range as [running, running + chance]
        // and then tests lo <= roll <= hi — inclusive at BOTH ends, so the
        // ranges touch and the earlier entry wins the tie.
        //
        // A Goblin's coin toss is therefore 51/49 rather than 50/50, and since
        // its rand(100) stops at 99 the last entry's upper bound is unreachable
        // as well. Both are quirks; both are reproduced rather than tidied.
        var ranges = new List<(JobType Job, int Low, int High)>();
        CreatureAngerRules.CumulativeRanges(
            Db.CreatureById(GoblinId).AngryJobs, ranges);

        Assert.Equal(
            [(JobType.Leave, 0, 50), (JobType.Rebel, 50, 100)],
            ranges);

        // Which is 51 of the 100 rolls for the first entry and 49 for the
        // second, written out so that a "fix" to exclusive bounds fails here
        // rather than quietly re-balancing every angry creature in the game.
        int first = Enumerable.Range(0, 100).Count(
            roll => ranges.First(r => r.Low <= roll && roll <= r.High).Job == JobType.Leave);
        Assert.Equal(51, first);
    }

    [Fact]
    public void ARollOutsideEveryRangeFallsThroughToTheFirstJobInTheRecord()
    {
        // The original's chooser makes a second pass in record order when the
        // first found nothing, and a table whose chances do not reach 100 is
        // how that happens: 99 rolls out of 100 land outside a 1% table.
        //
        // So a creature with Leave(1%) does not stand around for 99 selections
        // out of 100 — it leaves on the first one, by the fall-through. Without
        // it, the roll would come to nothing, nothing would be stamped, and the
        // creature would roll again a second later for ever.
        CreatureDef longshot = Dk2TerrainFixture.Deserter() with
        {
            Name = "Longshot",
            CreatureId = 45,
            AngryJobs = [new(JobType.Leave, 0, 0, 1)],
        };

        GameDatabase db = Dk2TerrainFixture.Database(longshot);
        WorldState world = World(db);
        PaintPortal(db, world, new TilePos(3, 3));

        CreatureState creature = world.SpawnCreature(db, 45, Keeper, new TilePos(1, 1));
        creature.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        Walking(db, world, events).Run(Second);

        SimEvent chose = Assert.Single(events.Events, e => e.Kind == "creature_angry");
        Assert.Contains("chose=Leave", chose.Detail);
        Assert.Equal(CreatureActivity.Leaving, creature.Activity);

        // And it was the fall-through that did it, not the range: the roll is
        // in the event, and it is outside [0,1].
        int roll = int.Parse(chose.Detail.Split("roll=")[1].Split(' ')[0]);
        Assert.True(roll > 1, $"roll {roll} landed inside the 1% range");
    }

    [Fact]
    public void ASelectionThatStartsNothingIsNeverStamped()
    {
        // The gate is on *success*: the original writes
        // LastGameTickSuccessfullyChoseAngryJob only where its chooser reaches
        // the stamp, which is past a started job. A creature with nothing in
        // its table to select never gets there, so nothing latches shut behind
        // it and a table that becomes selectable later still is.
        //
        // The Imp is the creature whose angry table is empty, and it is also
        // the one that would take nine hours to anger, so its anger is set here
        // rather than waited for.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, 1, Keeper, new TilePos(2, 2));

        imp.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 30);

        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_angry");
        Assert.Equal(0, imp.LastAngryChoiceTick);
        Assert.Contains(world.Creatures, c => c.Id == imp.Id);
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
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));

        // Seeded in the bucket the bed actually drains - Treatment, which is
        // where a slap and the Hand of Evil land (ADR-0080). Seeding the Lair
        // bucket instead would show nothing happening at all, which is the
        // point of ABedStopsTheGrievanceGrowingButDoesNotUndoIt.
        goblin.Anger.Treatment = 10_000;
        goblin.Activity = CreatureActivity.Sleeping;

        Simulation(db, world).Run(Second);

        Assert.Equal(10_000 - 550, goblin.Anger.Treatment);
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

        goblin.Anger.Treatment = 100;
        Assert.Equal(-100, CreatureAngerRules.Apply(goblin, AngerBucket.Treatment, -5000));
        Assert.True(goblin.Anger.IsCalm);
    }

    [Fact]
    public void AngerCeilsAtTheCeiling()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        // AT 65533, NOT AT THE ANGRY BAND. The original clamps each bucket to
        // [0, 0xFFFD] and only then compares the largest of them against the
        // bands, so a grievance goes on climbing for a second ceiling's worth
        // after the creature has already turned angry (ADR-0080).
        goblin.Anger.Lair = CreatureAngerRules.Saturation - 10;
        Assert.Equal(10, CreatureAngerRules.Apply(goblin, AngerBucket.Lair, 5000));
        Assert.Equal(CreatureAngerRules.Saturation, goblin.Anger.Lair);
        Assert.Equal(65533, CreatureAngerRules.Saturation);
        Assert.True(CreatureAngerRules.Saturation > CreatureAngerRules.Ceiling);
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

        goblin.Anger.Treatment = 1000;
        CreatureAngerRules.Held(def, goblin);

        Assert.Equal(-250, def.AngerInHand);
        Assert.Equal(750, goblin.Anger.Treatment);
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

        Assert.True(imp.Anger.IsCalm);
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
        Assert.True(goblin.Anger.IsCalm);
    }
}
