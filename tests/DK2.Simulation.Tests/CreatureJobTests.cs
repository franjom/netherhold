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

    /// <summary>
    /// Gold under the keeper, because <b>training is billed per game second</b>
    /// (Q205) and a keeper who cannot pay is refused the job outright. Every
    /// test that expects a Goblin to reach the training room has to fund it
    /// first, which is itself the mechanic.
    /// </summary>
    private static void Fund(WorldState world, int amount, byte owner = Keeper)
    {
        ref TileState tile = ref world.Tile(2, 3);
        tile.OwnerPlayerId = owner;
        tile.GoldHeld = amount;
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
        Fund(world, 10_000);

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

        // Funded, so that the refusal is about the ROOM and not about the
        // money. A broke keeper is refused earlier and for a different reason
        // (ABrokeKeeperIsRefusedTheTrainingRoomBeforeAnyoneWalksAnywhere).
        Fund(world, 10_000);

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
        Fund(world, 10_000);
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
        // The asymmetry is the original's: the happy chooser's tail charges
        // angerNoWork, the unhappy chooser's tail is a bare return and so is the
        // angry one's. A creature that is already unhappy is never made crosser by
        // idleness. Same dungeon as AGoblinWithNowhereToTrain and the same empty
        // tables; the only difference is where the creature starts on the ladder.
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
        goblin.GoldHeld = 100;

        Simulation(db, world).Run(Second * 30);

        Assert.Equal(JobType.Drink, goblin.CurrentJob);
        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
    }

    [Fact]
    public void AGoblinWithEightGoldOrLessIsRefusedTheCasino()
    {
        // The Drink handler 0x004560F0 wants more in the purse than the stake, 8, before
        // it looks for a casino (ADR-0187). A creature never paid has nothing.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Casino, new TilePos(3, 3));

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.Anger.Treatment = CreatureAngerRules.UnhappyAt(db.CreatureById(GoblinId));
        goblin.GoldHeld = 8;

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 30);

        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Contains(sink.Events, e => e.Kind == "job_refused"
            && e.Detail.Contains("job=Drink") && e.Detail.Contains("reason=cannot_afford_to_gamble"));
    }

    [Fact]
    public void AGamblerIsCalmedOnTreatmentCompanyAndWorkAndWinsACoinEachTimeTheHookActs()
    {
        // The Casino's update hook 0x0047AD70 in its default mode (ADR-0187): each time it
        // acts, -2000 to buckets 5, 0 and 3 and one gold into the purse. Pay is seeded
        // to show the hook leaves it alone.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Casino, Floor);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.CurrentJob = JobType.Drink;
        goblin.Activity = CreatureActivity.AtWork;
        CreatureStateMachine.Begin(goblin, JobRules.StateFor(goblin.CurrentJob)!.Value);
        goblin.GoldHeld = 100;
        goblin.Anger.Treatment = 10_000;
        goblin.Anger.Company = 10_000;
        goblin.Anger.Work = 10_000;
        goblin.Anger.Pay = 10_000;

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 4);

        // Every fifth turn from entry (Q424): turns 4, 9 and 14 of these sixteen.
        Assert.Equal(CreatureStateCategory.Casino, CreatureStateMachine.UpdatingCategory(goblin));
        Assert.Equal(10_000 - (3 * 2000), goblin.Anger.Treatment);
        Assert.Equal(10_000 - (3 * 2000), goblin.Anger.Company);
        Assert.Equal(10_000 - (3 * 2000), goblin.Anger.Work);
        Assert.Equal(10_000, goblin.Anger.Pay);
        Assert.Equal(103, goblin.GoldHeld);
        Assert.Equal(3, sink.Events.Count(e => e.Kind == "gambled"));
    }

    [Fact]
    public void AGamblerWithAPurseAtTheStakeIsSentIdleByTheHook()
    {
        // The same hook tests the purse every second before anything else and fails at 8
        // or below, which the dispatcher turns into state 1 (ADR-0187).
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Casino, Floor);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.CurrentJob = JobType.Drink;
        goblin.Activity = CreatureActivity.AtWork;
        CreatureStateMachine.Begin(goblin, JobRules.StateFor(goblin.CurrentJob)!.Value);
        goblin.GoldHeld = 8;
        goblin.Anger.Work = 10_000;

        // The hook's first turn to act is the fifth.
        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(5);

        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.False(CreatureStateMachine.HasState(goblin));
        Assert.Equal(8, goblin.GoldHeld);
        Assert.Equal(10_000, goblin.Anger.Work);
        Assert.Contains(sink.Events, e => e.Kind == "job_lost"
            && e.Detail.Contains("reason=cannot_afford_to_gamble"));
    }

    // ---- the one job with an effect ---------------------------------------

    [Fact]
    public void ASpectatorCoolsTreatmentCompanyAndWorkAndNothingElse()
    {
        // The Spectating update hook 0x0047AA70 (ADR-0186): once a game second,
        // ModifyAngerOfPitSpectatorPerSecond (-275) to buckets 5, 0 and 3. Pay and Lair
        // are seeded too, to show the hook leaves them alone.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.CombatPit, Floor);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.CurrentJob = JobType.CombatPitSpectate;
        goblin.Activity = CreatureActivity.AtWork;
        CreatureStateMachine.Begin(goblin, JobRules.StateFor(goblin.CurrentJob)!.Value);
        goblin.Anger.Treatment = 5_000;
        goblin.Anger.Company = 5_000;
        goblin.Anger.Work = 5_000;
        goblin.Anger.Pay = 5_000;

        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(Second * 4);

        // Every fifth turn from entry (Q424): turns 4, 9 and 14 of these sixteen.
        Assert.Equal(CreatureStateCategory.Spectating, CreatureStateMachine.UpdatingCategory(goblin));
        Assert.Equal(5_000 - (3 * 275), goblin.Anger.Treatment);
        Assert.Equal(5_000 - (3 * 275), goblin.Anger.Company);
        Assert.Equal(5_000 - (3 * 275), goblin.Anger.Work);
        Assert.Equal(5_000, goblin.Anger.Pay);
        Assert.Equal(3, sink.Events.Count(e => e.Kind == "spectated"));
    }

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
        CreatureStateMachine.Begin(vampire, JobRules.StateFor(vampire.CurrentJob)!.Value);
        vampire.Anger.Pay = 10_000;
        vampire.Anger.Lair = 8_000;
        vampire.Anger.Treatment = 500;

        Simulation(db, world).Run(Second * 4);

        // Nothing else in the game fills or drains the Pay bucket here, so this
        // one is the Temple's arithmetic on its own: a prayer every fifth turn.
        Assert.Equal(10_000 - (3 * 275), vampire.Anger.Pay);

        // The Lair bucket is the more interesting number, because the anger
        // system is running in the same seconds and this Vampire has no bed:
        // three prayers of -275 (turns 4, 9, 14; Q424) against four seconds of +100
        // AngerNoLair. A Temple out-drains a grievance that is still accruing.
        Assert.Equal(8_000 - (3 * 275) + (4 * 100), vampire.Anger.Lair);

        // And the small one floors at zero rather than going negative, per
        // bucket, which is what the original's clamp does.
        Assert.Equal(0, vampire.Anger.Treatment);
    }

    [Fact]
    public void PrayingHealsThirtyThreeEveryFifthTurnAndStopsAtFull()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Temple, Floor);

        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, Floor);
        CreatureDef definition = db.CreatureById(VampireId);
        vampire.CurrentJob = JobType.Praying;
        vampire.Activity = CreatureActivity.AtWork;
        CreatureStateMachine.Begin(vampire, JobRules.StateFor(vampire.CurrentJob)!.Value);
        vampire.Health = 100;

        Simulation(db, world).Run(Second * 3);

        // Turns 4 and 9 of these twelve (Q424).
        Assert.Equal(33, db[VariableTypeId.ModifyCreatureHealthWhilePrayingPerSecond]);
        Assert.Equal(100 + (2 * 33), vampire.Health);

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
        Fund(world, 10_000);

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

    // ---- on DK2's state ids (ADR-0177) --------------------------------------

    private static List<(int State, int Saved)> StatesSeen(
        GameSimulation sim, CreatureState creature, int ticks)
    {
        var seen = new List<(int State, int Saved)>();
        int last = creature.Dk2StateId;
        for (int t = 0; t < ticks; t++)
        {
            sim.Run(1);
            if (creature.Dk2StateId == last) continue;
            seen.Add((creature.Dk2StateId, creature.StateBeforeUtilityState));
            last = creature.Dk2StateId;
        }

        return seen;
    }

    [Fact]
    public void AGoblinWalksToTrainingInStateSevenAndTrainsInStateOneHundredAndEight()
    {
        // The Train handler calls the room helper from idle with 108 as the resume
        // (travel-state-call-sites.md 2). The saved caller is the evidence the walk came
        // from idle; selling the room drops the creature to 1 and the shim takes the id.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));
        Fund(world, 10_000);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        var events = new ListEventSink();
        GameSimulation sim = Simulation(db, world, events);

        List<(int State, int Saved)> seen = StatesSeen(sim, goblin, Second * 20);
        world.Tile(3, 3).TerrainId = Dk2TerrainFixture.ClaimedPath;
        seen.AddRange(StatesSeen(sim, goblin, Second * 2));

        Assert.Equal(
            [(7, CreatureStateMachine.Idle), (108, CreatureStateMachine.Idle),
             (CreatureStateMachine.NoState, CreatureStateMachine.Idle)],
            seen);
        Assert.Equal(CreatureStateMachine.Idle, goblin.UtilityNewState);
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AGoblinAlreadyInTheRoomTakesItsJobStateWithoutAWalk()
    {
        // The room helper's shortcut: already on a tile of the room is 1, then the resume.
        // No travel state is entered, so nothing is ever saved.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, Floor);
        Fund(world, 10_000);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        var events = new ListEventSink();

        Assert.Equal([(108, 0)], StatesSeen(Simulation(db, world, events), goblin, Second * 10));
        Assert.DoesNotContain(events.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void TheEatSystemsShimLeavesAWorkingGoblinsStateAlone()
    {
        // The trap this slice had and eating did not: AtWork holds the job's state, so
        // every system's release shim must count it retired, or the id goes on arrival.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));
        Fund(world, 10_000);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new CreatureJobSystem(),
            new CreatureEatSystem(),
        ]);
        sim.Run(Second * 20);

        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
        Assert.Equal(108, goblin.Dk2StateId);
    }

    [Fact]
    public void AGoblinTakenOffItsJobGivesItsStateIdBack()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3));
        Fund(world, 10_000);

        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);
        sim.Run(Second * 20);
        Assert.Equal(108, goblin.Dk2StateId);

        goblin.Activity = CreatureActivity.Leaving;
        sim.Run(1);

        Assert.Equal(CreatureStateMachine.NoState, goblin.Dk2StateId);
    }

    [Fact]
    public void AnAlliesTrainingRoomIsNoUseToACreature()
    {
        // The same rule the hatchery walk and the lair walk have: a creature
        // works in its own keeper's rooms and nobody else's.
        GameDatabase db = Db;
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3), owner: 0);
        Fund(world, 10_000);

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
        Fund(world, 10_000);

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
    public void ManufactureRefusesWithNothingOnTheKeepersList()
    {
        // The workshop handler tests the manufacture list's count (player+0xA1C):
        // a Troll with no order is a Troll with nothing to do (ADR-0203).
        GameDatabase db = Db;
        CreatureDef bile = db.CreatureById(Dk2TerrainFixture.BileDemonCreatureId);
        var state = new CreatureState { Level = 1 };

        Assert.Equal(
            "nothing_queued",
            JobRules.Refused(World(db), db, bile, state, JobType.Manufacture));
    }

    [Fact]
    public void TrainingRefusesAtTheCapAndTheCapIsTheShippedFour()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);

        WorldState world = World(db);
        Fund(world, 1000);

        Assert.Equal(4, db[VariableTypeId.TrainingRoomMaxExperienceLevel]);
        Assert.Null(JobRules.Refused(
            world, db, goblin,
            new CreatureState { Level = 3, OwnerPlayerId = Keeper }, JobType.Train));
        Assert.Equal(
            "at_training_cap",
            JobRules.Refused(
                world, db, goblin,
                new CreatureState { Level = 4, OwnerPlayerId = Keeper }, JobType.Train));
    }

    [Fact]
    public void ABrokeKeeperIsRefusedTheTrainingRoomBeforeAnyoneWalksAnywhere()
    {
        // The original's training handler tests the keeper's gold against the
        // per-second cost BEFORE it looks for a room, so poverty refuses the
        // job rather than interrupting it. This project had only the level half
        // of that test until Q205.
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db);
        var state = new CreatureState { Level = 1, OwnerPlayerId = Keeper };

        Assert.Equal(5, TrainingRules.GoldPerSecond(db));

        // Nothing in the treasury: refused, and the reason names the keeper.
        Assert.Equal(
            "keeper_cannot_pay", JobRules.Refused(world, db, goblin, state, JobType.Train));

        // One short of the second is still short - the test is >=, not >.
        Fund(world, 4);
        Assert.Equal(
            "keeper_cannot_pay", JobRules.Refused(world, db, goblin, state, JobType.Train));

        // Exactly the price passes.
        Fund(world, 5);
        Assert.Null(JobRules.Refused(world, db, goblin, state, JobType.Train));
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
                World(db), db, db.CreatureById(Dk2TerrainFixture.ImpCreatureId),
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
