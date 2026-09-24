// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// The Library's accrual, read from DK2's per-tick hook for state category 4
/// and the keeper-side adder it calls. See docs/research/library-and-research.md.
/// </summary>
public sealed class ResearchTests
{
    private const byte Keeper = 4;
    private static readonly TilePos Room = new(3, 3);

    private static int Second => SimulationTiming.TicksPerSecond;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureJobSystem()]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>
    /// A Vampire already standing in a built Library and already at work, so
    /// the arithmetic is not entangled with the walk. The fixture's Vampire is
    /// the researcher: <c>ResearchPerSecond</c> 5, and its happy jobs are
    /// Research at 80% and Praying at 20%.
    /// </summary>
    private static CreatureState Researcher(GameDatabase db, WorldState world)
    {
        ref TileState tile = ref world.Tile(Room.X, Room.Y);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Library), Keeper);

        CreatureState it = world.SpawnCreature(
            db, Dk2TerrainFixture.VampireCreatureId, Keeper, Room);
        it.CurrentJob = JobType.Research;
        it.Activity = CreatureActivity.AtWork;
        CreatureStateMachine.Begin(it, JobRules.StateFor(it.CurrentJob)!.Value);
        it.JobTileIndex = (Room.Y * world.Width) + Room.X;
        return it;
    }

    // ---- the rate ---------------------------------------------------------

    [Fact]
    public void ASecondOfResearchArrivesInFourInstalmentsAndOneIsLostToTheLoader()
    {
        // THE TRUNCATION IS THE POINT. ResearchPerSecond is one of the ten
        // record fields DK2's creature loader divides by variable 19 as it
        // loads them, so the fixture Vampire's 5 becomes 1 a tick and delivers
        // FOUR a game second - not the five its own record claims. Same quirk
        // the Vampire's training rate has, on a different column.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureDef vampire = db.CreatureById(Dk2TerrainFixture.VampireCreatureId);
        CreatureState it = Researcher(db, world);

        Assert.Equal(5, vampire.ResearchPerSecond);
        Assert.Equal(1, ResearchRules.PerOriginalTick(db, vampire, it));

        Simulation(db, world).Run(Second * 10);

        Assert.Equal(40, world.Player(Keeper).ResearchPoints);
    }

    [Fact]
    public void ResearchIsCreditedToTheKeeperAndTheCreatureGainsNothingFromIt()
    {
        // Research is the first job whose entire effect lands on the PLAYER.
        // DK2's hook ends `MOV ECX,[ESI + 0x2A]` - the creature's player
        // pointer - and calls the keeper's adder; the creature is only the
        // rate's source. Asserted because every other paying job so far
        // (training, praying) changes the worker.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState it = Researcher(db, world);
        int experienceBefore = it.ExperiencePoints;
        int healthBefore = it.Health;

        Simulation(db, world).Run(Second * 3);

        Assert.Equal(12, world.Player(Keeper).ResearchPoints);
        Assert.Equal(experienceBefore, it.ExperiencePoints);
        Assert.Equal(healthBefore, it.Health);
    }

    [Fact]
    public void NothingChargesAKeeperForResearch_UnlikeTraining()
    {
        // The two rooms look symmetrical from outside and are not. Training
        // bills ModifyPlayerColdWhileTrainingPerSecond once a game second and
        // can evict a creature for poverty; the research hook has NO cost path
        // at all. A keeper with no gold researches perfectly well.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState it = Researcher(db, world);

        Simulation(db, world).Run(Second * 5);

        Assert.Equal(20, world.Player(Keeper).ResearchPoints);
        Assert.Equal(JobType.Research, it.CurrentJob);   // not evicted
        Assert.Equal(0, GoldStore.Total(world, Keeper));
    }

    // ---- the slap ---------------------------------------------------------

    [Fact]
    public void ASlappedResearcherWorksAtTripleRate_AndTheVariableSaysFiftyPercent()
    {
        // The arithmetic is the original's and it is not what the variable says:
        // DK2 multiplies by variable19 twice, so 4 x 50 x 4 / 100 = eight times the
        // per-tick amount once a game second on a base of four — +200 % where the
        // variable is named 50 %. Almost certainly a slip, and the sibling bonus
        // three lines away slips differently (ADD where these have IMUL, 25 into
        // 29, Q81). Reproduced, not corrected: library-and-research.md.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState it = Researcher(db, world);

        Assert.Equal(50, db[VariableTypeId.IncreasedWorkRatePecentageFromSlapping]);
        Assert.Equal(8, ResearchRules.SlapBonus(db, WithSlapAt(it, 1), 1, now: 1));

        it.LastSlapTick = 0;
        Simulation(db, world).Run(Second);
        int unslapped = world.Player(Keeper).ResearchPoints;

        // A second of research with the slap live: four instalments of 1 plus
        // one bonus of 8.
        it.LastSlapTick = Second;
        Simulation(db, world).Run(Second);
        int withSlap = world.Player(Keeper).ResearchPoints - unslapped;

        Assert.Equal(4, unslapped);
        Assert.Equal(12, withSlap);
    }

    [Fact]
    public void TheSlapsResearchBonusLastsSixtySecondsAndNotTheTwentyFiveOfItsWorkBonus()
    {
        // One slap, two unrelated effects, and this is the one that uses the
        // variable. Efficiency +10 points lasts 25 game seconds from a hardcoded
        // 100 ticks in the slap handler (Q159, CONFIRMED by the owner off the
        // status bar); this research bonus lasts 60, read from
        // IncreasedWorkRateDurationFromSlappingSeconds — which also rescues the
        // owner's hand-timed "after a minute": it is not the work rate.
        GameDatabase db = Db;
        CreatureState it = Researcher(db, World(db));

        Assert.Equal(60, ResearchRules.SlapBonusSeconds(db));
        Assert.NotEqual(EfficiencyRules.SlapBonusSeconds, ResearchRules.SlapBonusSeconds(db));

        int window = SimulationTiming.OriginalTicksPerGameSecond * 60;

        // Slapped on tick 1 - not tick 0, which the "never slapped" guard below
        // reads as no slap at all. Inside the window by one tick, then outside
        // it by exactly one.
        Assert.Equal(8, ResearchRules.SlapBonus(db, WithSlapAt(it, 1), 1, now: window));
        Assert.Equal(0, ResearchRules.SlapBonus(db, WithSlapAt(it, 1), 1, now: window + 1));

        // Still paying at 30 seconds, where the efficiency bonus is long gone.
        Assert.Equal(8, ResearchRules.SlapBonus(
            db, WithSlapAt(it, 1), 1, now: SimulationTiming.TicksFromSeconds(30)));
    }

    [Fact]
    public void ACreatureNeverSlappedEarnsNoBonus_BecauseTickZeroIsNotASlap()
    {
        // LastSlapTick defaults to zero and the original tests the field for
        // non-zero before using it, so a creature that has never been slapped
        // is not treated as having been slapped on tick 0. Worth a test because
        // "never" and "at the very start" are the same value.
        GameDatabase db = Db;
        CreatureState it = Researcher(db, World(db));
        it.LastSlapTick = 0;

        Assert.Equal(0, ResearchRules.SlapBonus(db, it, 1, now: 1));
    }

    // ---- state ------------------------------------------------------------

    [Fact]
    public void ResearchPointsSurviveASnapshotAndAreInTheHash()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Researcher(db, world);
        GameSimulation live = Simulation(db, world);

        live.Run(Second * 4);
        Assert.Equal(16, world.Player(Keeper).ResearchPoints);

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(live), db, [new CreatureJobSystem()]);

        Assert.Equal(16, restored.World.Player(Keeper).ResearchPoints);
        Assert.Equal(live.StateHash(), restored.StateHash());
    }

    /// <summary>
    /// Sets the slap stamp and returns the creature, so a bonus assertion reads
    /// as one expression.
    /// </summary>
    private static CreatureState WithSlapAt(CreatureState creature, long tick)
    {
        creature.LastSlapTick = tick;
        return creature;
    }
}
