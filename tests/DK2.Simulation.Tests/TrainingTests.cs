// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// What a second in the training room is worth, and what it costs — Q205, read
/// out of DKII.EXE on 2026-09-07. Until this existed the training room was a
/// job a creature chose, walked to, stood in, and was paid nothing for.
/// </summary>
public sealed class TrainingTests
{
    private const byte Keeper = 4;
    private const byte GoblinId = 7;
    private static readonly TilePos Room = new(3, 3);

    private static int Second => SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    /// <summary>Just the job system, so nothing else can move a number.</summary>
    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureJobSystem()]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static void Fund(WorldState world, int amount)
    {
        ref TileState tile = ref world.Tile(2, 3);
        tile.OwnerPlayerId = Keeper;
        tile.GoldHeld = amount;
    }

    /// <summary>
    /// A creature already standing in a built training room and already at
    /// work, so that the arithmetic is not entangled with the walk.
    /// </summary>
    private static CreatureState Trainee(
        GameDatabase db, WorldState world, byte creatureId = GoblinId, int gold = 100_000)
    {
        ref TileState tile = ref world.Tile(Room.X, Room.Y);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.TrainingRoom), Keeper);
        Fund(world, gold);

        CreatureState it = world.SpawnCreature(db, creatureId, Keeper, Room);
        it.CurrentJob = JobType.Train;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, JobRules.StateFor(it.CurrentJob)!.Value);
        it.JobTileIndex = (Room.Y * world.Width) + Room.X;
        return it;
    }

    // ---- the rate ---------------------------------------------------------

    [Fact]
    public void ASecondOfTrainingIsWorthTheRecordsRateAndTheGoblinsDividesCleanly()
    {
        // The Goblin's ExpPerSecondTraining is 40, which divides by variable 19
        // exactly: 10 a tick, four ticks, 40 a game second. So for this creature
        // the shipped name is the shipped truth and nothing is lost.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = Trainee(db, world);

        Assert.Equal(40, db.CreatureById(GoblinId).ExpPerSecondTraining);
        Assert.Equal(10, TrainingRules.ExperiencePerOriginalTick(
            db, db.CreatureById(GoblinId), goblin));

        Simulation(db, world).Run(Second * 10);

        Assert.Equal(400, goblin.ExperiencePoints);
        Assert.Equal(1, goblin.Level);
    }

    [Fact]
    public void AVampireTrainsAtTwentyEightASecondAndNotThirtyBecauseOfTheLoadersDivide()
    {
        // Not a rounding detail: DK2's creature loader stores these rates as
        // `file value / variable 19`, truncating, so a record that says 30 becomes
        // 7 a tick and delivers 28 a game second. Twenty-six of the forty-six
        // shipped creatures lose something to it — eighteen carry 30, seven carry
        // 15 and lose a full fifth (Horny and the heroes' lords among them), and
        // the Maiden Of The Nest's 25 is the only odd value in the column.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureDef vampire = db.CreatureById(Dk2TerrainFixture.VampireCreatureId);
        CreatureState it = Trainee(db, world, Dk2TerrainFixture.VampireCreatureId);

        Assert.Equal(30, vampire.ExpPerSecondTraining);
        Assert.Equal(7, TrainingRules.ExperiencePerOriginalTick(db, vampire, it));

        Simulation(db, world).Run(Second * 10);

        Assert.Equal(280, it.ExperiencePoints);
        Assert.NotEqual(300, it.ExperiencePoints);
    }

    [Fact]
    public void TheAwardLandsFourTimesAGameSecondAndTheCadenceIsBehaviour()
    {
        // Not a schedule detail. DK2 ZEROES the balance on promotion
        // (CreatureLevelRules.Grant), so how big each award is decides how much
        // is thrown away - and collapsing four awards of 10 into one of 40 would
        // leave a different number behind on any second that crosses a
        // threshold. That is why this pays on the original's tick.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = Trainee(db, world);
        GameSimulation sim = Simulation(db, world);

        Assert.Equal(4, SimulationTiming.OriginalTicksPerGameSecond);

        // One of our ticks IS one of the original's, now that Q1 is settled
        // (2026-09-07): the award falls every tick. The cadence still has to be
        // stated, because it is what decides how much a promotion throws away -
        // it is the period that collapsed to 1, not the requirement.
        Assert.Equal(1, SimulationTiming.OriginalTickPeriodTicks);

        sim.Run(1);
        Assert.Equal(10, goblin.ExperiencePoints);

        // And it accrues in tens, one per tick, not in one lump of forty at the
        // second boundary. That is the whole point of the test and it survives
        // the tick rate changing: what changed is that a group is one tick wide.
        sim.Run(1);
        Assert.Equal(20, goblin.ExperiencePoints);
        sim.Run(1);
        Assert.Equal(30, goblin.ExperiencePoints);
        sim.Run(1);
        Assert.Equal(40, goblin.ExperiencePoints);
    }

    // ---- the cap ----------------------------------------------------------

    [Fact]
    public void TrainingTakesAGoblinToLevelFourAndThenStopsPayingIt()
    {
        // OWNER (Q124): "creatures can get up to level 4 using the training
        // room". The thresholds are 3000, 3900 and 5400 - the record's 3000 at
        // 100%, 130% and 180% of the ExperienceForNextLevel column - so 12300
        // points at 40 a second is 307.5 seconds, a little over five minutes,
        // and then nothing. That is a number a stopwatch can check.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureDef def = db.CreatureById(GoblinId);
        CreatureState goblin = Trainee(db, world);
        var events = new ListEventSink();
        GameSimulation sim = Simulation(db, world, events);

        Assert.Equal(4, TrainingRules.LevelCap(db));
        Assert.Equal(3000, CreatureLevelRules.ExperienceForNextLevel(db, def, 1));
        Assert.Equal(3900, CreatureLevelRules.ExperienceForNextLevel(db, def, 2));
        Assert.Equal(5400, CreatureLevelRules.ExperienceForNextLevel(db, def, 3));

        sim.Run(Second * 400);

        Assert.Equal(4, goblin.Level);

        // At the cap it earns nothing at all, so the balance stops moving.
        int banked = goblin.ExperiencePoints;
        Assert.Equal(0, TrainingRules.ExperiencePerOriginalTick(db, def, goblin));
        sim.Run(Second * 60);
        Assert.Equal(banked, goblin.ExperiencePoints);

        // Three promotions and no more, each logged with what it discarded.
        Assert.Equal(3, events.Events.Count(e => e.Kind == "level_up"));
        Assert.Contains(events.Events, e =>
            e.Kind == "level_up" && e.Detail.Contains("3->4")
            && e.Detail.Contains("via=training") && e.Detail.Contains("cap=4"));
    }

    /// <summary>State 108 (<c>0x00485310</c>) sends a creature at or above the cap to 110, and 110
    /// (<c>0x00485580</c>) returns 0, so it idles: on Level 17 DK2's level-7 Black Knights go 108,
    /// 110, idle on consecutive turns and Harkan lifts them in again (Q509, ADR-0393). The goblin
    /// earns nothing on the way.</summary>
    [Fact]
    public void ACappedCreatureGoesFromOneHundredEightThroughOneTenToIdle()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = Trainee(db, world);
        goblin.Level = 4;
        var events = new ListEventSink();
        GameSimulation sim = Simulation(db, world, events);

        Assert.Equal(
            "at_training_cap",
            JobRules.Refused(world, db, db.CreatureById(GoblinId), goblin, JobType.Train));
        Assert.Equal(TrainingRules.WalkToMachineState, goblin.Dk2StateId);

        var seen = new List<int>();
        for (int t = 0; t < 12 && goblin.CurrentJob == JobType.Train; t++)
        {
            sim.Run(1);
            seen.Add(goblin.Dk2StateId);
        }

        // 110 for one turn, then idle.
        int at = seen.IndexOf(TrainingRules.CapRefusalState);
        Assert.True(at >= 0 && at == seen.Count - 2, string.Join(",", seen));
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Single(events.Events, e => e.Kind == "training_refused");
        Assert.Equal(0, goblin.ExperiencePoints);
    }

    /// <summary>The control: below the cap, 108 keeps the trainee and nothing refuses it.</summary>
    [Fact]
    public void ATraineeBelowTheCapContinuesTrainingWhileApproachingAMachine()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = Trainee(db, world);
        var events = new ListEventSink();
        GameSimulation sim = Simulation(db, world, events);

        sim.Run(Second * 5);

        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
        Assert.Equal(JobType.Train, goblin.CurrentJob);
        Assert.DoesNotContain(events.Events, e => e.Kind == "training_refused");
        Assert.True(goblin.ExperiencePoints > 0);
    }

    // ---- the bill ---------------------------------------------------------

    [Fact]
    public void TrainingBillsTheKeeperFiveGoldEveryFifthTurn()
    {
        // ModifyPlayerColdWhileTrainingPerSecond is -5, "Cold" is the shipped
        // spelling of gold, and the original negates the tabled figure before
        // spending it. Charged on a countdown seeded and reloaded from variable 19,
        // which acts on the turn after it reaches zero: turns 4, 9, 14 ... (Q424). So 16
        // charges in 20 seconds, not 20, while the experience is every turn.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = Trainee(db, world, gold: 1000);

        Assert.Equal(5, TrainingRules.GoldPerSecond(db));

        Simulation(db, world).Run(Second * 20);

        Assert.Equal(1000 - (16 * 5), GoldStore.Total(world, Keeper));
        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
    }

    [Fact]
    public void AKeeperWhoRunsOutOfGoldLosesTheCreaturesJobMidTraining()
    {
        // THE ONLY JOB IN THE GAME A KEEPER CAN BE EVICTED FROM BY BEING POOR.
        // The original tests the keeper's gold on every charge and returns
        // failure from the category hook when it fails, which its state
        // dispatcher turns into "back to idle".
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = Trainee(db, world, gold: 15);
        var events = new ListEventSink();
        GameSimulation sim = Simulation(db, world, events);

        // Three charges, on turns 4, 9 and 14, are exactly the money.
        sim.Run(15);
        Assert.Equal(0, GoldStore.Total(world, Keeper));
        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
        Assert.Equal(150, goblin.ExperiencePoints);

        // The fourth, on turn 19, cannot be paid for, and the job ends.
        sim.Run(5);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(JobType.None, goblin.CurrentJob);

        Assert.Contains(events.Events, e =>
            e.Kind == "job_lost" && e.Detail.Contains("reason=cannot_pay")
            && e.Detail.Contains("cost=5"));
    }

    [Fact]
    public void ACreatureIsPaidForTheTickItTrainedEvenIfThatIsTheTickTheMoneyRanOut()
    {
        // The order in the original's hook is award, then charge - so the second
        // that bankrupts the keeper is still a second the creature learned
        // something in. Cheap to get backwards and invisible unless asserted.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = Trainee(db, world, gold: 4);

        // The first charge is on the fifth turn, and it fails.
        Simulation(db, world).Run(5);

        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(50, goblin.ExperiencePoints);
        Assert.Equal(4, GoldStore.Total(world, Keeper));
    }

    // ---- the level column -------------------------------------------------

    [Fact]
    public void TheTrainingRateDoesNotScaleWithLevelButTheThresholdDoes()
    {
        // Stat 16 is 100% at every level in the shipped table, so a level-3
        // creature trains at exactly the rate a level-1 one does. What makes the
        // later levels slower is the ExperienceForNextLevel column, which runs
        // 130% / 180% / 250% - so each level costs more for the same income.
        // Both facts had to be read; either alone would give the wrong curve.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureDef def = db.CreatureById(GoblinId);
        CreatureState goblin = Trainee(db, world);

        for (int level = 1; level <= 4; level++)
        {
            Assert.Equal(100, CreatureLevelRules.Percent(
                db, CreatureStatId.ExpPerSecondTraining, level));
        }

        goblin.Level = 1;
        int atOne = TrainingRules.ExperiencePerOriginalTick(db, def, goblin);
        goblin.Level = 3;
        int atThree = TrainingRules.ExperiencePerOriginalTick(db, def, goblin);

        Assert.Equal(atOne, atThree);
        Assert.Equal(10, atThree);

        // And the threshold is what changes.
        Assert.Equal(3000, CreatureLevelRules.ExperienceForNextLevel(db, def, 1));
        Assert.Equal(7500, CreatureLevelRules.ExperienceForNextLevel(db, def, 4));
    }
}
