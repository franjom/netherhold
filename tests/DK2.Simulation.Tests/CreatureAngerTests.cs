// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Anger. The mood ceiling is 32767 and <c>UnhappyThreshold</c> is 50
/// on all 46 creatures; 50 % <i>of what</i> is in none of the 214 balance
/// variables, not in the creature record, and not in OpenKeeper (whose
/// <c>CreatureMoodSystem</c> is an empty stub). It was settled by the owner
/// timing a Goblin in a dungeon with no lair: unhappy at ~2:45, angry at ~5:30 —
/// 32767/2 and 32767 to within a percent, and the 2:1 ratio pins the structure
/// independently of the ceiling. That measurement is
/// <see cref="TheOwnersStopwatch_ALairlessGoblinIsUnhappyAt165sAndAngryAt330s"/>,
/// the one assertion here that came from the original game rather than from a
/// file.</summary>
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
        // OWNER MEASUREMENT (Q106, 2026-09-04), the only assertion here whose
        // expected values came from playing the original: unhappy after approx
        // 02:45, angry after approx 05:30. At AngerNoLair = 100/s that is 16 500
        // and 33 000 against 16 383 (32767/2) and 32 767 (short.MaxValue), both
        // inside 1 %. Asserted as the tick the mood *changes*, one-second
        // tolerance, because the systems run on the last tick of each game second.
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
        // The control: one lair tile is the difference the player makes. The tile
        // is a diagonal step away rather than under the creature's feet because a
        // bed is walked to (ADR-0060). The two seconds of walk cost 200 anger at
        // AngerNoLair's 100 a second, and placing the bed wipes them (ADR-0179).
        GameDatabase db = Db;
        WorldState world = World(db);
        PaintLair(db, world, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 400);

        Assert.True(LairRules.HasBed(world, db, goblin));
        Assert.Equal(CreatureMood.Content, CreatureAngerRules.MoodOf(db.CreatureById(GoblinId), goblin));

        // Nothing left of the walk, and not one point more in four hundred.
        Assert.Equal(0, goblin.Anger.Max);
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_mood");
        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
    }

    [Fact]
    public void AnAngryGoblinWalksToThePortalAndLeavesThroughIt()
    {
        // CONFIRMED (owner, Q116, 2026-09-04): "angry evil creature goes to the
        // nearest available portal and leaves the dungeon." The creature is not
        // gone on the second it decides: it gives up its bed, sets off, and is
        // still in the dungeon — killable, and still on the payroll — for the
        // length of the walk.
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
        Assert.True(goblin.HasRoute);

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
    public void AnAngryCreatureOnATortureDeviceCannotChooseToLeave()
    {
        // Job 10 sets 194, and the rule refuses 194 from the Torture category.
        GameDatabase db = DeserterDb;
        WorldState world = World(db);
        PaintPortal(db, world, new TilePos(3, 3));
        CreatureState goblin = world.SpawnCreature(db, DeserterId, Keeper, new TilePos(1, 1));
        goblin.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        StatePlacement.Place(goblin, TortureRules.TorturedState);
        goblin.Activity = CreatureActivity.BeingTortured;
        var events = new ListEventSink();

        Walking(db, world, events).Run(Second * 3);

        Assert.Equal(CreatureActivity.BeingTortured, goblin.Activity);
        Assert.Equal(TortureRules.TorturedState, goblin.Dk2StateId);
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_leaving");
        Assert.Contains(events.Events, e => e.Kind == "state_refused" && e.Detail.Contains($"to={LeavingRules.ChoosingState}"));
    }

    [Fact]
    public void AKeeperWithNoPortalKeepsTheCreatureThatWantedToLeave()
    {
        // The consequence of Q116 that is behaviour rather than bookkeeping: with
        // nowhere to walk to, the creature does not leave — it stands in the
        // dungeon, angry, having given up its bed. The fixture map has no portal
        // at all, so a rule that removed the creature regardless would pass every
        // other test in this file.
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

        // Ours stands in 194 and retries; DK2's no-entrance fallback, wandering (87), is Q41.
        Assert.Equal(LeavingRules.ChoosingState, goblin.Dk2StateId);

        // Reported once, on the second it first failed, and not once a second
        // for the next minute.
        Assert.Single(events.Events, e => e.Kind == "creature_leave_blocked");
    }

    [Fact]
    public void ADeserterDoesNotTakeABedOnItsWayOut()
    {
        // The bed is released when the creature decides to go, and the sleep
        // system skips a leaver, so a lair that becomes free while a deserter
        // walks past stays free for a creature that intends to stay. Without the
        // guard in CreatureSleepSystem the deserter claims that bed — it is a
        // creature with no bed beside a free lair tile — and walks out holding it.
        GameDatabase db = DeserterDb;
        WorldState world = World(db);
        PaintLair(db, world, new TilePos(2, 2));

        // Angry about its work, not its lair: a bed wipes the Lair bucket as it goes
        // down (ADR-0179), and would cure a lair grievance before it could leave.
        CreatureState goblin = world.SpawnCreature(db, DeserterId, Keeper, new TilePos(2, 2));
        goblin.Anger.Work = CreatureAngerRules.Ceiling + 1;

        Walking(db, world).Run(Second * 30);

        Assert.Equal(CreatureActivity.Leaving, goblin.Activity);
        Assert.Equal(-1, LairRules.BedTileIndex(world, db, goblin));
        Assert.False(LairRules.BedTaken(world, db, (2 * world.Width) + 2));
    }

    [Fact]
    public void AnUnhappyGoblinGivenABedIsContentTheMomentTheBedGoesDown()
    {
        // CONFIRMED (owner, Q115): the ladder is reversible — a mood is a pure
        // function of the anger value, no latch, no hysteresis. What reverses this
        // one is not the -550 a second of cooling, which drains bucket 5: state 80
        // wipes the Lair bucket outright as it places the bed (ADR-0179).
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
        // each of those seconds is still charged 100 (ADR-0060).
        int waited = 0;
        while (!LairRules.HasBed(world, db, goblin) && waited++ < Second * 20) sim.Run(1);
        Assert.True(LairRules.HasBed(world, db, goblin), "never got its bed");
        Assert.True(peak >= CreatureAngerRules.UnhappyAt(definition));

        // Not one point of the four minutes without a bed is left, on the same tick.
        Assert.Equal(0, goblin.Anger.Lair);
        Assert.Equal(CreatureMood.Content, CreatureAngerRules.MoodOf(definition, goblin));

        // Long enough for it to get tired (TimeAwake 240) and sleep once. A bedded
        // creature is charged nothing more, so it stays at zero.
        sim.Run(Second * 300);

        Assert.Contains(world.Creatures, c => c.Id == goblin.Id);
        Assert.Equal(0, goblin.Anger.Max);
        Assert.Contains(events.Events, e =>
            e.Kind == "creature_mood" && e.Detail.Contains("Unhappy->Content"));
    }

    [Fact]
    public void AVampireThatDrawsRebelAndHasNoPortalTurnsGood()
    {
        // Rebelling is built as of ADR-0172: job 16 walks out through its own keeper's
        // nearest Portal, and with none reachable waits 30 ticks and changes owner to
        // the hero side. This world has no Portal, so the fallback is the whole test. A
        // Vampire, because its angry table is Rebel outright where a Goblin's is a coin
        // toss re-tossed (ADR-0061) - here the branch under test is the only one.
        GameDatabase db = Db;
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, new TilePos(2, 2));

        vampire.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 30);

        Assert.Single(world.Creatures);
        Assert.Equal(RebellionRules.DefectsTo, vampire.OwnerPlayerId);
        Assert.Equal(Keeper, vampire.RebelAgainstPlayerId);
        Assert.Equal(JobType.KillPlayer, vampire.CurrentJob);

        // Still angry, and CROSSER than it started - which it could not have
        // been before ADR-0081, when a counter stopped dead at the angry band.
        // It saturates at 65533 instead, so thirty more seconds of having no
        // bed cost it three thousand more it will have to pray off. Changing
        // sides does not settle a grievance.
        Assert.Equal(CreatureMood.Angry, CreatureAngerRules.MoodOf(db.CreatureById(VampireId), vampire));
        Assert.Equal(CreatureAngerRules.Ceiling + 1 + (30 * 100), vampire.Anger.Lair);

        List<SimEvent> rolls = events.Events.Where(e => e.Kind == "creature_angry").ToList();
        Assert.NotEmpty(rolls);
        Assert.All(rolls, e =>
        {
            Assert.Contains("chose=Rebel", e.Detail);
            Assert.Contains("result=started", e.Detail);
        });

        // It rebelled once and defected once, however many times it rolled.
        Assert.Single(events.Events, e => e.Kind == "creature_rebelling");
        SimEvent gone = Assert.Single(events.Events, e => e.Kind == "creature_defected");
        Assert.Contains($"player={Keeper}->{RebellionRules.DefectsTo}", gone.Detail);
    }

    [Fact]
    public void TheAngryJobIsRolledAfreshEveryFiveGameSecondsAndNotOftener()
    {
        // STRONGLY INFERRED from the binary (Q122, 2026-09-05): the angry chooser
        // rolls afresh on every allowed selection, and a selection is allowed five
        // game seconds after the last one that came to something —
        // LastGameTickSuccessfullyChoseAngryJob is that timestamp and the only
        // thing the chooser keeps. A Vampire's Rebel roll is unbuilt but still a
        // selection, so it stamps: thirty seconds of anger is six rolls, not thirty.
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
        // ADR-0061 corrects ADR-0053's draw-once rule: with Leave(50%) re-rolled
        // every five game seconds an angry Goblin walks out, it just takes a round
        // or two. Drawing once left half of all angry Goblins standing in the
        // dungeon for ever; the original's own record says a coin is tossed.
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
        // docs/research/lair-acquisition.md §8: the unhappy and angry choosers
        // both try for a lair before rolling — the same attempt (FUN_0046f390)
        // the idle state makes, which is why it is one class here (LairSeeker).
        // The sleep system is left out so what is asserted is the anger ladder's
        // own attempt, not the idle cadence's seven ticks earlier; the cost is an
        // unfinished journey — putting the bed down is CreatureSleepSystem's.
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
        Assert.True(goblin.HasRoute);

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
        // and tests lo <= roll <= hi — inclusive at both ends, so ranges touch and
        // the earlier entry wins the tie. A Goblin's coin toss is therefore 51/49
        // rather than 50/50, and since rand(100) stops at 99 the last entry's
        // upper bound is unreachable. Both quirks are reproduced, not tidied.
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
        // The original's chooser makes a second pass in record order when the first
        // found nothing, which is how a table whose chances do not reach 100
        // resolves: 99 rolls in 100 land outside a 1 % table. So a creature with
        // Leave(1%) leaves on its first selection by the fall-through; without it
        // nothing would be stamped and the creature would roll again a second
        // later for ever.
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
        // LastGameTickSuccessfullyChoseAngryJob only where its chooser reaches the
        // stamp, past a started job. A creature with nothing to select never gets
        // there, so nothing latches shut behind it. The Imp is the creature whose
        // angry table is empty, and the one that would take nine hours to anger,
        // so its anger is set here rather than waited for.
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
        // where a slap and the Hand of Evil land (ADR-0081). Seeding the Lair
        // bucket instead would show nothing happening at all, which is the
        // point of ABedStopsTheGrievanceGrowingButDoesNotUndoIt.
        goblin.Anger.Treatment = 10_000;
        goblin.Activity = CreatureActivity.Sleeping;
        StatePlacement.Place(goblin, LairRules.SleepingState);

        Simulation(db, world).Run(Second);

        Assert.Equal(10_000 - 550, goblin.Anger.Treatment);
        Assert.Equal(-550, db[VariableTypeId.ModifyAngerOfCreatureInLairPerSecond]);
        Assert.Equal(100, db.CreatureById(GoblinId).AngerNoLair);
    }

    [Theory]
    [InlineData(79)]
    [InlineData(LairRules.MakingAHomeState)]
    [InlineData(LairRules.GoingToBedState)]
    public void AnAwakeCreatureCoolsToo_BecauseCoolingIsCategoryGatedNotBedGated(int state)
    {
        // Q123: the lair effect runs from the hooks of categories 6, 9 and 10 - seven
        // states, only three of them sleeping - and tests nothing about tile or bed
        // (ADR-0171). No bed is placed on purpose: asking whether the creature was
        // asleep ON its own bed tile is exactly what undercooled, and the test that
        // existed covered only the sleeping case, so it passed throughout.
        GameDatabase db = Db;
        WorldState world = World(db);
        var lair = new TilePos(4, 4);
        PaintLair(db, world, lair);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, lair);
        StatePlacement.Place(goblin, state);

        // The rule, not a run: the sleep system moves a lair-seeker on within the same
        // tick, so driving this through a simulation would measure the ordering rather
        // than the gate.
        CreatureAnger delta = CreatureAngerRules.PerSecond(
            world, db, db.CreatureById(GoblinId), goblin);

        Assert.Equal(-550, delta.Treatment);
    }

    [Fact]
    public void WorkingIsNotCoolingButSulkingIs_BecauseSulkingIsCategoryTen()
    {
        // The other side of the widening: categories 6, 9 and 10 and nothing else.
        // Without this the rule above could be "cooling always happens" and pass.
        GameDatabase db = Db;
        WorldState world = World(db);
        var lair = new TilePos(4, 4);
        PaintLair(db, world, lair);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, lair);
        StatePlacement.Place(goblin, JobRules.StateFor(JobType.Research)!.Value);

        CreatureAnger working = CreatureAngerRules.PerSecond(
            world, db, db.CreatureById(GoblinId), goblin);
        Assert.Equal(0, working.Treatment);

        // And the fourth arm of the widening, category 10: sulking is a lair job.
        CreatureState sulker = world.SpawnCreature(db, GoblinId, Keeper, lair);
        StatePlacement.Place(sulker, JobRules.StateFor(JobType.Sulk)!.Value);
        CreatureAnger sulking = CreatureAngerRules.PerSecond(
            world, db, db.CreatureById(GoblinId), sulker);
        Assert.Equal(-550, sulking.Treatment);
    }

    [Fact]
    public void AWalkCoolsOnlyBetweenTwoStatesOfOneLairCategory()
    {
        // The dispatcher runs a travel state's update hook only when the saved caller and
        // the resume share a category (ADR-0185). 80's re-walk to another candidate tile
        // is 80 to 80 and cools; the attempt's walk in from idle, 1 to 80, does not; and
        // 81's walk to the bed, 81 to 82, crosses from Lair to Sleeping and does not.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureDef definition = db.CreatureById(GoblinId);

        int CoolingOf(int from, int travel, int resume)
        {
            CreatureState walker = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
            StatePlacement.Place(walker, from);
            CreatureStateMachine.BeginTravel(walker, travel, resume);
            return CreatureAngerRules.PerSecond(world, db, definition, walker).Treatment;
        }

        Assert.Equal(-550, CoolingOf(LairRules.MakingAHomeState, 6, LairRules.MakingAHomeState));
        Assert.Equal(0, CoolingOf(CreatureStateMachine.Idle, 7, LairRules.MakingAHomeState));
        Assert.Equal(0, CoolingOf(LairRules.GoingToBedState, 6, LairRules.LyingDownState));
    }

    [Fact]
    public void ACreatureWithNoStateIdDoesNotCoolWhateverItsActivitySays()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        goblin.Activity = CreatureActivity.Sleeping;

        Assert.Equal(0, CreatureAngerRules.PerSecond(
            world, db, db.CreatureById(GoblinId), goblin).Treatment);
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
        Assert.Equal(-100, CreatureAngerRules.Apply(db.CreatureById(GoblinId), goblin, AngerBucket.Treatment, -5000));
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
        // after the creature has already turned angry (ADR-0081).
        goblin.Anger.Lair = CreatureAngerRules.Saturation - 10;
        Assert.Equal(10, CreatureAngerRules.Apply(db.CreatureById(GoblinId), goblin, AngerBucket.Lair, 5000));
        Assert.Equal(CreatureAngerRules.Saturation, goblin.Anger.Lair);
        Assert.Equal(65533, CreatureAngerRules.Saturation);
        Assert.True(CreatureAngerRules.Saturation > CreatureAngerRules.Ceiling);
    }

    // ---- the discrete deltas, which Q114 has not settled ------------------

    [Fact]
    public void TheOwnersSlapCount_SixToUnhappyAndFiveMoreToAngry()
    {
        // OWNER MEASUREMENT (Q114, 2026-09-04) on a Salamander, whose AngerSlap is
        // also 3000: exactly 6 slaps to unhappy, then 5 more to angry. A slap is a
        // discrete delta so it scales with the ceiling where a rate does not —
        // timing alone cannot separate 100/s into 32767 from 200/s into 65535 (the
        // Q47 trap). 5 not enough / 6 enough brackets the ceiling in (30000,
        // 36000], 10 / 11 in (30000, 33000]: only 32767. 65535 would need 11 and 22.
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

    // ---- the never-angered flag (Q210) ------------------------------------
    //
    // DK2 puts one test at the top of the one wrapper every anger site calls,
    // so a flagged creature takes nothing from anywhere. The gate lives in
    // CreatureAngerRules.Apply for the same reason: there is no second door.

    /// <summary>
    /// A flagged creature takes no anger from any bucket, whatever the delta —
    /// and the deltas here are the ones that used to reach it.
    /// </summary>
    [Fact]
    public void NoBucketMovesOnACreatureCarryingTheNeverAngeredFlag()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureDef skeleton = db.CreatureById(Dk2TerrainFixture.SkeletonCreatureId);
        CreatureState state = world.SpawnCreature(
            db, Dk2TerrainFixture.SkeletonCreatureId, Keeper, new TilePos(2, 2));

        Assert.False(CreatureAngerRules.CanBeAngered(skeleton));

        for (int i = 0; i < CreatureAnger.Count; i++)
        {
            var bucket = (AngerBucket)i;
            Assert.Equal(0, CreatureAngerRules.Apply(skeleton, state, bucket, 5000));
            Assert.Equal(0, state.Anger[bucket]);
        }

        Assert.True(state.Anger.IsCalm);
    }

    /// <summary>
    /// The flag beats the values, which is the whole point of it: a creature
    /// with populated anger rates and a positive slap still takes nothing.
    ///
    /// <para>This is Jack, synthesised — the fixture has no Jack, and the real
    /// record is asserted in
    /// <c>CreatureNeedsOnRealDataTests.TheSkeletonAndJackCannotBeAngeredAndOnlyTheFlagSaysSoAboutJack</c>.
    /// Every value here would move the needle if the values were the rule.</para>
    /// </summary>
    [Fact]
    public void PopulatedAngerRatesDoNotOverrideTheFlag()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        CreatureDef jackish = db.CreatureById(GoblinId) with
        {
            Name = "Jackish",
            Flags2 = CreatureFlags2.NeverAngered,
            AngerNoLair = 120,
            AngerNoFood = 110,
            AngerNoPay = 60,
            AngerNoWork = 50,
            AngerInHand = 500,
            AngerSlap = -5000,
        };

        Assert.False(CreatureAngerRules.CanBeAngered(jackish));

        // Every discrete source, including the two that reached anger without
        // asking CanBeAngered before Q210.
        Assert.Equal(0, CreatureAngerRules.Slap(jackish, state));
        Assert.Equal(0, CreatureAngerRules.Held(jackish, state));
        Assert.Equal(0, CreatureAngerRules.Apply(
            jackish, state, AngerBucket.Work, jackish.AngerNoWork));
        Assert.True(state.Anger.IsCalm);

        // And a whole tick's worth at once.
        CreatureAngerRules.Apply(jackish, state, CreatureAngerRules.PerSecond(
            world, db, jackish, state));
        Assert.True(state.Anger.IsCalm);
        Assert.Equal(CreatureMood.Content, CreatureAngerRules.MoodOf(jackish, state));
    }

    /// <summary>
    /// A negative delta is refused as firmly as a positive one. Jack's slap is
    /// −5000, so the pre-Q210 code let a keeper <i>calm</i> him by slapping;
    /// DK2 never reaches the adder at all, so there is nothing to calm.
    /// </summary>
    [Fact]
    public void SlappingAFlaggedCreatureNeitherAngersNorCalmsIt()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        CreatureDef calmedBySlaps = db.CreatureById(GoblinId) with
        {
            Flags2 = CreatureFlags2.NeverAngered,
            AngerSlap = -5000,
        };

        state.Anger.Treatment = 10000;
        Assert.Equal(0, CreatureAngerRules.Slap(calmedBySlaps, state));
        Assert.Equal(10000, state.Anger.Treatment);
    }

    /// <summary>
    /// The flag is what is read, not the zero rates — so a creature with all
    /// four rates zero and <b>no</b> flag can still be angered by a slap. That
    /// is the case the old value-based predicate got wrong in the other
    /// direction, and nothing in the shipped data exercises it.
    /// </summary>
    [Fact]
    public void ZeroRatesWithoutTheFlagStillAllowAnger()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState state = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));

        CreatureDef unflagged = db.CreatureById(GoblinId) with
        {
            AngerNoLair = 0,
            AngerNoFood = 0,
            AngerNoPay = 0,
            AngerNoWork = 0,
            AngerSlap = 3000,
        };

        Assert.True(CreatureAngerRules.CanBeAngered(unflagged));
        Assert.Equal(3000, CreatureAngerRules.Slap(unflagged, state));
    }
}
