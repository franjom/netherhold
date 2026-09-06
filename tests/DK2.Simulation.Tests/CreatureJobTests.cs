// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// Job selection for creatures that are not workers — M4's seventh and last
/// item (ADR-0082, <c>docs/research/job-selection.md</c>).
/// </summary>
public sealed class CreatureJobTests
{
    private const byte Keeper = 4;
    private const byte GoblinId = 7;
    private const byte VampireId = Dk2TerrainFixture.VampireCreatureId;
    private static readonly TilePos Floor = new(2, 2);

    private static int Second => SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    /// <summary>
    /// The chooser plus the mover, because a job is a walk to a room. The anger
    /// system is in as well, since <c>AngerNoWork</c> is half of what this is
    /// for and the ladder has to be able to see it.
    /// </summary>
    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new CreatureJobSystem(),
            new CreatureAngerSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static void Paint(
        GameDatabase db, WorldState world, RoomTypeId type, TilePos at, byte owner = Keeper)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(type), owner);
    }

    // ---- choosing ---------------------------------------------------------

    [Fact]
    public void AContentGoblinWalksToTheTrainingRoomBecauseThatIsItsWholeHappyTable()
    {
        // The shipped Goblin's HappyJobs is Train at 100%, and nothing else.
        // Before this system a Goblin stood where the portal put it unless it
        // was hungry, tired or leaving; this is the first thing one has ever
        // done because it was content.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 30);

        Assert.Equal(JobType.Train, goblin.CurrentJob);
        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
        Assert.Equal(new TilePos(3, 3), goblin.Tile);

        Assert.Contains(events.Events, e =>
            e.Kind == "job_taken" && e.Detail.Contains("job=Train"));
        Assert.Contains(events.Events, e => e.Kind == "job_started");
    }

    [Fact]
    public void AGoblinWithNowhereToTrainTakesAngerNoWorkAndNothingElseDoes()
    {
        // THE POINT OF THE WHOLE SYSTEM, and the last anger source in the game
        // to acquire a consumer. AngerNoWork is 50 a second for a Goblin, and
        // it is charged by the HAPPY chooser alone: when every job in the happy
        // table has failed, the creature that was fine is made cross about it.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 10);

        // Ten seconds at 50, and in the Work bucket rather than pooled with the
        // lairless grievance the same Goblin is also accruing (ADR-0081).
        Assert.Equal(10 * 50, goblin.Anger.Work);
        Assert.Equal(50, db.CreatureById(GoblinId).AngerNoWork);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(JobType.None, goblin.CurrentJob);

        Assert.Contains(events.Events, e =>
            e.Kind == "job_refused" && e.Detail.Contains("job=Train")
            && e.Detail.Contains("reason=no_room"));
        Assert.Contains(events.Events, e => e.Kind == "no_work");
    }

    [Fact]
    public void BuildingTheTrainingRoomStopsTheChargeOnTheSecondItAppears()
    {
        // The other half of the same fact, and the reason the charge is a
        // mechanic rather than a countdown: a keeper turns it off by building
        // one room. What it does NOT do is give back what it already cost -
        // only a Temple does that (ADR-0081).
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Run(Second * 10);
        int owed = goblin.Anger.Work;
        Assert.Equal(500, owed);

        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));
        sim.Run(Second * 60);

        Assert.Equal(JobType.Train, goblin.CurrentJob);
        Assert.Equal(owed, goblin.Anger.Work);
    }

    [Fact]
    public void ARolledJobThatFailsFallsThroughToTheRestOfTheTable()
    {
        // The original tries the job whose range holds the roll and then, on
        // failure, EVERY job in the table in record order. A Vampire's happy
        // table is Research 80% / Praying 20%, so with only a temple built the
        // 80% entry has to fail and the 20% one has to be reached whatever the
        // roll was.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Temple, new TilePos(3, 3));

        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, Floor);
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 30);

        Assert.Equal(JobType.Praying, vampire.CurrentJob);

        // how=fell_through IS the evidence, and it is better evidence than a
        // refusal line: it says the roll landed on an entry that was tried and
        // failed, and that the second pass over the table is what found this
        // job. A refusal is only logged on a game-second boundary (the decision
        // runs every tick and would otherwise fill the log twenty times over),
        // and this selection succeeds long before the first one.
        Assert.Contains(events.Events, e =>
            e.Kind == "job_taken" && e.Detail.Contains("job=Praying")
            && e.Detail.Contains("how=fell_through") && e.Detail.Contains("effect=built"));
    }

    [Fact]
    public void AnUnhappyCreatureIsNotAngeredByHavingNothingToDo()
    {
        // THE ASYMMETRY, and it is the original's. The happy chooser's tail
        // charges angerNoWork; the unhappy chooser's tail is a bare return, and
        // so is the angry one's. A creature that is already unhappy is never
        // made crosser by idleness - the grievance belongs to the creature that
        // was fine until its keeper ran out of work for it.
        //
        // Same dungeon as AGoblinWithNowhereToTrain, same empty tables, and the
        // only difference is where the creature starts on the ladder.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);

        // Unhappy, from a grievance that has nothing to do with work.
        goblin.Anger.Treatment = CreatureAngerRules.UnhappyAt(db.CreatureById(GoblinId));
        Assert.Equal(
            CreatureMood.Unhappy,
            CreatureAngerRules.MoodOf(db.CreatureById(GoblinId), goblin));

        Simulation(db, world).Run(Second * 10);

        // Its unhappy table is Drink at 100% and there is no casino, so it
        // found nothing - and was charged nothing for it.
        Assert.Equal(0, goblin.Anger.Work);
        Assert.Equal(JobType.None, goblin.CurrentJob);
    }

    [Fact]
    public void AnUnhappyGoblinGoesToTheCasino()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Casino, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.Anger.Treatment = CreatureAngerRules.UnhappyAt(db.CreatureById(GoblinId));

        Simulation(db, world).Run(Second * 30);

        Assert.Equal(JobType.Drink, goblin.CurrentJob);
        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
    }

    // ---- the one job with an effect ---------------------------------------

    [Fact]
    public void PrayingDrainsAllSixAngerBucketsAndIsTheOnlyThingThatDoes()
    {
        // Since ADR-0081 this is the whole reason the Temple exists: every
        // other calm in the game drains the bucket it fills, and the Temple
        // drains buckets it never filled. -275 a second, six times over.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Temple, Floor);

        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, Floor);
        vampire.CurrentJob = JobType.Praying;
        vampire.Activity = CreatureActivity.AtWork;
        vampire.Anger.Pay = 10_000;
        vampire.Anger.Lair = 8_000;
        vampire.Anger.Treatment = 500;

        Simulation(db, world).Run(Second * 4);

        // Nothing else in the game fills or drains the Pay bucket here, so this
        // one is the Temple's arithmetic on its own.
        Assert.Equal(10_000 - (4 * 275), vampire.Anger.Pay);

        // The Lair bucket is the more interesting number, because the anger
        // system is running in the same seconds and this Vampire has no bed:
        // -275 from the Temple against +100 of AngerNoLair, netting -175 a
        // second. A Temple out-drains a grievance that is still accruing.
        Assert.Equal(8_000 - (4 * (275 - 100)), vampire.Anger.Lair);

        // And the small one floors at zero rather than going negative, per
        // bucket, which is what the original's clamp does.
        Assert.Equal(0, vampire.Anger.Treatment);
    }

    [Fact]
    public void PrayingHealsThirtyThreeASecondAndStopsAtFull()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Temple, Floor);

        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, Floor);
        CreatureDef definition = db.CreatureById(VampireId);
        vampire.CurrentJob = JobType.Praying;
        vampire.Activity = CreatureActivity.AtWork;
        vampire.Health = 100;

        Simulation(db, world).Run(Second * 3);

        Assert.Equal(33, db[VariableTypeId.ModifyCreatureHealthWhilePrayingPerSecond]);
        Assert.Equal(100 + (3 * 33), vampire.Health);

        Simulation(db, world).Run(Second * 600);
        Assert.Equal(CreatureHealthRules.MaxHealth(db, definition, vampire), vampire.Health);
    }

    // ---- losing a job -----------------------------------------------------

    [Fact]
    public void SellingTheRoomOutFromUnderACreatureEndsTheJobThatSecond()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        var events = new ListEventSink();
        GameSimulation sim = Simulation(db, world, events);

        sim.Run(Second * 20);
        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);

        // The room stops being one. Not sold through the command - this is
        // about the job noticing, not about selling.
        ref TileState tile = ref world.Tile(3, 3);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;

        sim.Run(Second * 2);

        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Contains(events.Events, e =>
            e.Kind == "job_lost" && e.Detail.Contains("reason=room_gone"));
    }

    [Fact]
    public void AnAlliesTrainingRoomIsNoUseToACreature()
    {
        // The same rule the hatchery walk and the lair walk have: a creature
        // works in its own keeper's rooms and nobody else's.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3), owner: 0);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        var events = new ListEventSink();

        Simulation(db, world, events).Run(Second * 10);

        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Contains(events.Events, e =>
            e.Kind == "job_refused" && e.Detail.Contains("reason=no_room"));
    }

    // ---- who is exempt ----------------------------------------------------

    [Fact]
    public void AWorkerIsNotOfferedAJobBecauseItHasItsOwnSystem()
    {
        // An Imp's happy table is Explore at 100% in the shipped data, and
        // DK2 dispatches workers before any of this. Nothing here would know
        // what to do with it, and ImpTaskSystem's own priority order (ADR-0018)
        // is the thing that decides an imp's day.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));

        CreatureState imp = world.SpawnCreature(
            db, Dk2TerrainFixture.ImpCreatureId, Keeper, Floor);

        Simulation(db, world).Run(Second * 30);

        Assert.Equal(JobType.None, imp.CurrentJob);
        Assert.True(imp.Anger.IsCalm);
    }

    [Fact]
    public void ACaptiveChoosesNothing()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        PrisonRules.Imprison(
            world, db, goblin, Keeper, (Floor.Y * world.Width) + Floor.X);

        Simulation(db, world).Run(Second * 30);

        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Equal(0, goblin.Anger.Work);
    }

    [Fact]
    public void ADeserterDoesNotStopForWorkOnItsWayOut()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.Activity = CreatureActivity.Leaving;

        Simulation(db, world).Run(Second * 30);

        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Equal(CreatureActivity.Leaving, goblin.Activity);
    }

    // ---- the gate ---------------------------------------------------------

    [Fact]
    public void TheReselectionGateIsFiveGameSecondsAndItIsNotTheShippedVariable()
    {
        // All three choosers gate on 5 x variable 19 - a hardcoded five game
        // seconds. DK2 also ships MinimumJobDurationSeconds = 15, and a raw
        // scan of .text for its global finds exactly one hit: the loader stub
        // writing it. Nothing reads it (ADR-0082).
        Assert.Equal(5 * SimulationTiming.TicksPerSecond, JobRules.ReselectionGateTicks);
        Assert.Equal(CreatureAngerRules.ReselectionGateTicks, JobRules.ReselectionGateTicks);
    }

    [Fact]
    public void EachMoodKeepsItsOwnTimestampSoAMoodChangeIsNeverSilenced()
    {
        // The original keeps three side by side in the creature - happy at
        // +0xfd, unhappy at +0xf9, angry at +0x101. One shared gate would mean
        // a creature that had just rolled while content had to wait out the
        // five seconds before it could react to becoming unhappy.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Casino, new TilePos(1, 1));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        // A second with nothing to train in: the happy chooser fails, and a
        // FAILED selection stamps nothing - exactly as the angry chooser leaves
        // one unstamped, so that a creature keeps asking until the dungeon
        // changes rather than being gated out of noticing.
        sim.Run(Second);
        Assert.Equal(0, goblin.LastHappyChoiceTick);
        Assert.Equal(0, goblin.LastUnhappyChoiceTick);

        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));
        sim.Run(Second * 20);

        Assert.Equal(JobType.Train, goblin.CurrentJob);
        Assert.NotEqual(0, goblin.LastHappyChoiceTick);

        // And the unhappy timestamp is still untouched, which is the claim: a
        // creature that turns unhappy tomorrow is not gated by what it decided
        // while it was content.
        Assert.Equal(0, goblin.LastUnhappyChoiceTick);
    }

    // ---- the jobs that refuse for their own reasons ------------------------

    [Fact]
    public void ManufactureAlwaysRefusesBecauseNothingIsEverQueued()
    {
        // The original's workshop handler tests a per-player field that is
        // non-zero when something is queued. Traps and doors do not exist here,
        // so nothing is ever queued and this refuses always - which is the
        // faithful answer rather than a shortcut. A Troll in a dungeon with no
        // manufacturing order is a Troll with nothing to do in DK2 too.
        GameDatabase db = Db;
        CreatureDef bile = db.CreatureById(Dk2TerrainFixture.BileDemonCreatureId);
        var state = new CreatureState { Level = 1 };

        Assert.Equal(
            "nothing_queued", JobRules.Refused(db, bile, state, JobType.Manufacture));
    }

    [Fact]
    public void TrainingRefusesAtTheCapAndTheCapIsTheShippedFour()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);

        Assert.Equal(4, db[VariableTypeId.TrainingRoomMaxExperienceLevel]);
        Assert.Null(JobRules.Refused(db, goblin, new CreatureState { Level = 3 }, JobType.Train));
        Assert.Equal(
            "at_training_cap",
            JobRules.Refused(db, goblin, new CreatureState { Level = 4 }, JobType.Train));
    }

    [Fact]
    public void SulkingNeedsALairWhichIsNotWhatTheNameSuggests()
    {
        // The Sulk handler refuses unless the creature sleeps at all, owns a
        // bed, and that bed sits in a room - then walks it there and enters
        // state 0x55, one away from the sleep handler's 0x51 and by exactly the
        // same route. Sulking is going to your lair and moping in it.
        Assert.Equal(RoomTypeId.Lair, JobRules.RoomFor(JobType.Sulk));
        Assert.Equal(RoomTypeId.Lair, JobRules.RoomFor(JobType.Sleep));

        // And a creature that needs no bed cannot sulk at all - which for the
        // shipped data is the Imp, the Skeleton and Horny.
        GameDatabase db = Db;
        Assert.Equal(
            "needs_no_bed",
            JobRules.Refused(
                db, db.CreatureById(Dk2TerrainFixture.ImpCreatureId),
                new CreatureState(), JobType.Sulk));
    }

    [Fact]
    public void TheGuardRoomIsTheOneRoomTypeReadStraightOutOfTheBinary()
    {
        // Every other mapping here follows from which room-finder the handler
        // calls. The Guard handler passes the literal 9 to its search, and 9 is
        // RoomTypeId.GuardRoom - the one that is confirmed rather than inferred.
        Assert.Equal(RoomTypeId.GuardRoom, JobRules.RoomFor(JobType.Guard));
        Assert.Equal(9, (byte)RoomTypeId.GuardRoom);
    }
}
