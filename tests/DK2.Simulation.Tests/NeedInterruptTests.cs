// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>DK2's needs routine <c>0x00477C20</c> takes a creature off its job when hunger,
/// sleep or pay can move it (ADR-0198): the need's category entry mask against the job
/// state's category, read through a walk; group 7 is never asked.</summary>
public class NeedInterruptTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = 7;
    private const int Second = SimulationTiming.TicksPerSecond;
    private const int Training = 108;
    private const int Walk = 6;
    private const int WalkToRoom = 7;

    private static readonly TilePos InTraining = new(9, 3);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    /// <summary>Paved ground, a Training Room at (8..10, 2..4), a Hatchery with a bird at
    /// (2..4, 5..7), a Lair at (2..4, 1..3) and a treasury tile at (6, 7).</summary>
    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        Paint(db, world, RoomTypeId.TrainingRoom, 8, 2);
        Paint(db, world, RoomTypeId.Hatchery, 2, 5);
        Paint(db, world, RoomTypeId.Lair, 2, 1);
        Dk2TerrainFixture.FurnishRooms(db, world);
        world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, (6 * world.Width) + 3);

        ref TileState treasury = ref world.Tile(6, 7);
        treasury.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        treasury.GoldHeld = 100_000;
        return world;
    }

    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, int x0, int y0)
    {
        for (int y = y0; y < y0 + 3; y++)
        for (int x = x0; x < x0 + 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new CreatureEatSystem(),
            new CreatureSleepSystem(),
            new PaydaySystem(),
            new CreatureJobSystem(),
        ]);
        sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>A Goblin standing in the Training Room, at work in 108.</summary>
    private static CreatureState Trainee(GameDatabase db, WorldState world, GameSimulation sim)
    {
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, InTraining);
        for (int i = 0; i < 60 * Second && goblin.Dk2StateId != Training; i++) sim.Tick();
        Assert.Equal(CreatureActivity.AtWork, goblin.Activity);
        Assert.Equal(JobType.Train, goblin.CurrentJob);
        return goblin;
    }

    [Fact]
    public void AHungryTraineeBreaksOffAndGoesStraightFromOneOhEightToNinetyOne()
    {
        // 0x004633D0 sets 91 from the job's state: the Eating mask admits Training. Then the
        // room walk in 7, saved 91, resuming 92.
        GameDatabase db = Db;
        WorldState world = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState goblin = Trainee(db, world, sim);

        goblin.HungerNeed = 1;
        sim.Run(Second);

        Assert.Equal(CreatureActivity.SeekingFood, goblin.Activity);
        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Equal((WalkToRoom, EatingRules.FindHatcheryState, EatingRules.InHatcheryState),
            (goblin.Dk2StateId, goblin.StateBeforeUtilityState, goblin.UtilityNewState));
        Assert.Contains(sink.Events, e => e.Kind == "food_sought" && e.Detail.Contains($"creature={goblin.Id} "));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void ATraineeOwedWagesBreaksOffToBePaidFromItsJobState()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState goblin = Trainee(db, world, sim);

        goblin.WageOwed = 500;
        sim.Run(Second);

        Assert.Contains(sink.Events, e => e.Kind == "wages_sought" && e.Detail.Contains($"creature={goblin.Id} "));
        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Equal((Walk, PaydayRules.FindTheMoneyState, PaydayRules.ArrivingState),
            (goblin.Dk2StateId, goblin.StateBeforeUtilityState, goblin.UtilityNewState));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void ATiredTraineeWithABedBreaksOffForIt()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState goblin = Trainee(db, world, sim);
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, goblin));

        goblin.AwakeTicks = 1_000_000;
        sim.Run(Second);

        Assert.Equal(CreatureActivity.GoingToSleep, goblin.Activity);
        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.Contains(sink.Events, e => e.Kind == "going_to_sleep" && e.Detail.Contains($"creature={goblin.Id} "));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AHungryCreatureWalkingToAHatcheryIsNotSentAgainAndAWalkToTheLairIsNotInterrupted()
    {
        // Group 7 states are never asked, and a walk is judged by the state it resumes.
        CreatureState eating = Fresh(EatingRules.FindHatcheryState);
        Assert.False(NeedRules.Admits(eating, CreatureStateCategory.Eating));
        Assert.False(NeedRules.Admits(eating, CreatureStateCategory.Payday));

        // Payday's mask admits its own category; only the group-7 skip stops it.
        CreatureState paid = Fresh(PaydayRules.TakeItState);
        Assert.False(NeedRules.Admits(paid, CreatureStateCategory.Payday));

        CreatureState toLair = Fresh(1);
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, toLair, WalkToRoom, LairRules.MakingAHomeState);
        Assert.False(NeedRules.Admits(toLair, CreatureStateCategory.Eating));

        CreatureState toTraining = Fresh(1);
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, toTraining, WalkToRoom, Training);
        Assert.True(NeedRules.Admits(toTraining, CreatureStateCategory.Eating));
    }

    [Fact]
    public void ATiredCreatureOnItsWayToEatIsNotTurnedRoundForBed()
    {
        // Its walk resolves to Eating, which Sleeping's mask does not admit.
        GameDatabase db = Db;
        WorldState world = World(db);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        CreatureState goblin = Trainee(db, world, sim);
        goblin.HungerNeed = 1;
        sim.Run(Second);
        Assert.Equal(CreatureActivity.SeekingFood, goblin.Activity);

        goblin.AwakeTicks = 1_000_000;
        sim.Run(Second);

        Assert.Equal(CreatureActivity.SeekingFood, goblin.Activity);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "going_to_sleep");
    }

    [Theory]
    [InlineData(53, true)]    // Research
    [InlineData(108, true)]   // Training
    [InlineData(101, true)]   // Temple
    [InlineData(145, true)]   // Casino
    [InlineData(142, true)]   // Spectating
    [InlineData(97, true)]    // Guard Post
    [InlineData(128, true)]   // a torturer, category 0
    [InlineData(131, false)]  // on a torture device
    [InlineData(116, false)]  // a prisoner
    [InlineData(83, false)]   // asleep: Sleeping is not in Eating's mask
    [InlineData(122, false)]  // group 1, category 0: only the busy test refuses it
    public void TheThreeNeedsShareOneMask(int state, bool admitted)
    {
        CreatureState creature = Fresh(state);
        Assert.Equal(admitted, NeedRules.Admits(creature, CreatureStateCategory.Eating));
        if (CreatureStateTable.Get(state).Category != CreatureStateCategory.Sleeping)
            Assert.Equal(admitted, NeedRules.Admits(creature, CreatureStateCategory.Sleeping));
        Assert.Equal(admitted, NeedRules.Admits(creature, CreatureStateCategory.Payday));
    }

    [Fact]
    public void AMistressOnADeviceIsNotAskedWhateverSheNeeds()
    {
        CreatureState mistress = Fresh(TortureRules.TorturedState);
        StatePlacement.As(mistress, CreatureActivity.BeingTortured);
        Assert.False(NeedRules.MayAsk(mistress, CreatureStateCategory.Eating));
        Assert.False(NeedRules.MayAsk(mistress, CreatureStateCategory.Sleeping));
        Assert.False(NeedRules.MayAsk(mistress, CreatureStateCategory.Payday));
    }

    [Fact]
    public void TheCasinoStopsTheHungerClockButNotItsTimeSoAnOverdueHelpingLandsOnLeaving()
    {
        // Flag bit 3 on 145-159: the clock does not add while flagged, and adds at once after.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 4));
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureHealthSystem()]);
        StatePlacement.Place(goblin, 145);
        Assert.False(NeedRules.HungerClockRuns(goblin));
        Assert.False(NeedRules.TirednessClockRuns(goblin));

        int rate = EatingRules.HungerRateSeconds(db.CreatureById(GoblinId));
        sim.Run((rate + 5) * Second);
        Assert.Equal(0, goblin.HungerNeed);

        CreatureStateMachine.TrySetState(new StateHookContext(world, db), goblin, CreatureStateMachine.Idle);
        sim.Run(Second);
        Assert.True(goblin.HungerNeed > 0);
    }

    [Theory]
    [InlineData(-8, -8)]   // -2 a turn
    [InlineData(-5, -4)]   // IDIV truncates: -1 a turn, not -1.25
    [InlineData(-3, 0)]    // under one a turn is nothing
    [InlineData(0, 0)]     // as shipped
    public void ATiredCreatureNotAsleepLosesVariableEightyTwoOverFourATurn(int perSecond, int landed)
    {
        GameDatabase db = Db;
        db.Variables.Misc[VariableTypeId.CannotSleepModifyCreatureHealthPerSecond] =
            new MiscVariable(VariableTypeId.CannotSleepModifyCreatureHealthPerSecond, perSecond, 0, 0);
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 4));
        CreatureDef def = db.CreatureById(GoblinId);
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureHealthSystem()]);
        goblin.Health -= 100;
        int before = goblin.Health;

        sim.Run(Second);
        Assert.Equal(before, goblin.Health);

        goblin.AwakeTicks = SimulationTiming.TicksFromSeconds(def.TimeAwake);
        sim.Run(Second);
        Assert.Equal(before + landed, goblin.Health);
    }

    [Fact]
    public void NoLossAsleepInANeedsStateForAnImpOrForAHero()
    {
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        var tired = new CreatureState { Id = 1, OwnerPlayerId = Keeper,
            AwakeTicks = SimulationTiming.TicksFromSeconds(goblin.TimeAwake) };
        db.Variables.Misc[VariableTypeId.CannotSleepModifyCreatureHealthPerSecond] =
            new MiscVariable(VariableTypeId.CannotSleepModifyCreatureHealthPerSecond, -8, 0, 0);
        Assert.Equal(-8, NeedRules.CannotSleepHealthPerSecond(db, goblin, tired));

        // A walk to eat resolves to group 7; asleep is group 7 and Sleeping.
        StatePlacement.Place(tired, EatingRules.FindHatcheryState);
        Assert.Equal(0, NeedRules.CannotSleepHealthPerSecond(db, goblin, tired));
        StatePlacement.Place(tired, 83);
        Assert.Equal(0, NeedRules.CannotSleepHealthPerSecond(db, goblin, tired));

        // A walk (group 9) to the bed is judged by the Sleeping state it resumes.
        StatePlacement.Place(tired, 1);
        CreatureStateMachine.BeginTravel(DetachedHooks.Context, tired, Walk, 83);
        Assert.Equal(0, NeedRules.CannotSleepHealthPerSecond(db, goblin, tired));

        // At a job it is still tired and still awake.
        StatePlacement.Place(tired, Training);
        Assert.Equal(-8, NeedRules.CannotSleepHealthPerSecond(db, goblin, tired));

        tired.OwnerPlayerId = PlayerIds.Good;
        Assert.Equal(0, NeedRules.CannotSleepHealthPerSecond(db, goblin, tired));
        tired.OwnerPlayerId = Keeper;
        Assert.Equal(0, NeedRules.CannotSleepHealthPerSecond(db, db.CreatureById(Dk2TerrainFixture.ImpCreatureId), tired));
    }

    [Fact]
    public void PayIsTriedEveryFourthTurnNotEveryTick()
    {
        // 0x00477F69: +0x60 is stamped four turns on before the decision runs.
        GameDatabase db = Db;
        WorldState world = World(db);
        world.Tile(6, 7).GoldHeld = 0;
        var sim = new GameSimulation(db, world, seed: 42, [new PaydaySystem()]);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 4));
        goblin.WageOwed = 500;

        var tries = new List<int>();
        for (int t = 0; t < 12; t++)
        {
            int before = goblin.PayAttemptsFailed;
            sim.Tick();
            if (goblin.PayAttemptsFailed != before) tries.Add(t);
        }

        Assert.Equal(2, tries.Count);
        Assert.Equal(NeedRules.RetryTurns, tries[1] - tries[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHungryCreatureDroppedOnARoomIsLeftAloneForFiveSecondsAndOffOneItIsNot(bool onARoom)
    {
        // 76 sets WasLastDroppedOnARoom from the landing tile; the busy test refuses every need
        // while LastDroppedCounter is under five seconds. The drop stuns the creature first
        // (ADR-0226); this one has the shipped Goblin's one-second stun, not the fixture's five.
        const byte QuickGoblin = 51;
        GameDatabase db = Dk2TerrainFixture.Database(
            Dk2TerrainFixture.Goblin() with { CreatureId = QuickGoblin, Name = "Quick Goblin", StunDuration = Fp32.FromDouble(1) });
        WorldState world = World(db);
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, seed: 42,
            [new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureEatSystem()]);
        sim.ObserveWith(sink);
        CreatureState goblin = world.SpawnCreature(db, QuickGoblin, Keeper, new TilePos(6, 4));
        goblin.HungerNeed = 1;

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, onARoom ? InTraining : new TilePos(6, 3))));
        Assert.Equal(onARoom, goblin.DroppedOnARoom);

        int ticks = 0;
        while (goblin.Activity != CreatureActivity.SeekingFood && ticks < 60) { sim.Tick(); ticks++; }

        Assert.Equal(CreatureActivity.SeekingFood, goblin.Activity);
        if (onARoom) Assert.InRange(ticks, NeedRules.DroppedOnARoomGraceTicks - 2, NeedRules.DroppedOnARoomGraceTicks + 4);
        // Off a room: the stun, the get-up (257 holds for the type's eleven ticks, ADR-0345), then food.
        else Assert.InRange(ticks, 0, SimulationTiming.TicksPerSecond + DropLandingRules.GetUpTicks(db.CreatureById(QuickGoblin)) + 4);
    }

    private static CreatureState Fresh(int state)
    {
        var creature = new CreatureState { Id = 1 };
        StatePlacement.Place(creature, state);
        return creature;
    }
}
