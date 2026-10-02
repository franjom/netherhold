// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

using Cat = CreatureStateCategory;

/// <summary>The Casino, Torture and Manufacture leave hooks and the Workshop's maker count
/// (ADR-0322, <c>leave-hook-bodies.md</c>), each through <c>SetState</c>'s dispatch.</summary>
public class LeaveHookBodiesTests
{
    private const byte Keeper = 1; // TestWorld's dirt is player 1's
    private const int FearlessDelay = 120;

    // The Drink job's state, the one Casino state built; a walk from it back to it is a walk
    // between two states of the category.
    private static readonly int CasinoState = JobRules.StateFor(JobType.Drink)!.Value;
    private static readonly int EatingState = StatesOf(Cat.Eating)[0];
    private static readonly int ManufactureState = JobRules.StateFor(JobType.Manufacture)!.Value;
    private const int MakingAHomeState = 80; // the walk to bed's resume (debug-dumpers.md)

    // --- Casino leave (0x0047AF20) and the update's fearless countdown (0x0047AD70) ---------

    [Fact]
    public void CasinoLeave_TurnsTheFearlessFlagIntoFearlessUntil_NowPlusV19TimesV203()
    {
        (StateHookContext hooks, CreatureState creature) = InState(CasinoState);
        creature.CasinoGamblingFearless = true;

        Assert.True(CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle));

        Assert.False(creature.CasinoGamblingFearless);
        Assert.Equal(hooks.World.CurrentTick.Value + (SimulationTiming.OriginalTicksPerGameSecond * FearlessDelay),
            creature.FearlessUntilTick);
        Assert.True(CasinoRules.IsFearlessFromGambling(hooks.World, creature));
    }

    [Fact]
    public void CasinoLeave_WithoutTheFlag_WritesNoStamp()
    {
        (StateHookContext hooks, CreatureState creature) = InState(CasinoState);

        Assert.True(CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle));

        Assert.Equal(0, creature.FearlessUntilTick);
        Assert.False(CasinoRules.IsFearlessFromGambling(hooks.World, creature));
    }

    [Fact]
    public void CasinoLeave_DoesNotRunOnAWalkBetweenTwoCasinoStates()
    {
        (StateHookContext hooks, CreatureState creature) = InState(CasinoState);
        creature.CasinoGamblingFearless = true;

        CreatureStateMachine.BeginTravel(hooks, creature, 6, CasinoState);
        Assert.True(CreatureStateMachine.Arrive(hooks, creature));

        Assert.True(creature.CasinoGamblingFearless);
        Assert.Equal(0, creature.FearlessUntilTick);
    }

    [Fact]
    public void CasinoLeave_RunsOnArrivingFromAWalkOut_NotOnSettingOff()
    {
        (StateHookContext hooks, CreatureState creature) = InState(CasinoState);
        creature.CasinoGamblingFearless = true;

        CreatureStateMachine.BeginTravel(hooks, creature, 6, EatingState);
        Assert.True(creature.CasinoGamblingFearless);

        Assert.True(CreatureStateMachine.Arrive(hooks, creature));
        Assert.False(creature.CasinoGamblingFearless);
        Assert.NotEqual(0, creature.FearlessUntilTick);
    }

    [Fact]
    public void TheFearlessCountdown_IsSeededFromV203_AndSetsTheFlagOnItsLastCasinoSecond()
    {
        GameDatabase db = Database();
        WorldState world = TestWorld.World(db);
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        Assert.Equal(FearlessDelay, creature.CasinoGamblingDuration);

        for (int second = 1; second < FearlessDelay; second++)
            Assert.False(CasinoRules.StepFearlessCountdown(db, creature));

        Assert.True(CasinoRules.StepFearlessCountdown(db, creature));
        Assert.True(creature.CasinoGamblingFearless);
        Assert.Equal(FearlessDelay, creature.CasinoGamblingDuration);
    }

    [Fact]
    public void TheFearlessCountdown_AtZeroNeverFires()
    {
        var creature = new CreatureState { Id = 1, CasinoGamblingDuration = 0 };

        Assert.False(CasinoRules.StepFearlessCountdown(Database(), creature));
        Assert.False(creature.CasinoGamblingFearless);
    }

    [Fact]
    public void FearlessUntil_ZeroesTheFearCounterWhileItRuns()
    {
        GameDatabase db = Database();
        WorldState world = TestWorld.World(db);
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureDef mine = db.CreatureById(me.CreatureTypeId);
        me.CurrentFear = ushort.MaxValue;
        me.OverThreatCounter = 2;
        me.FearlessUntilTick = world.CurrentTick.Value + 1;

        Assert.False(Combat.FearRules.OverThreat(world, db, me, mine));
        Assert.Equal(0, me.OverThreatCounter);

        me.FearlessUntilTick = world.CurrentTick.Value; // the window has closed: now < until is false
        Assert.False(CasinoRules.IsFearlessFromGambling(world, me));
    }

    [Fact]
    public void FearlessUntil_KeepsAHurtCreatureFromWithdrawing_UntilItCloses()
    {
        // Owner 2: player 1 is Good here, whose heart distance counts as 0 and never withdraws. With no
        // heart the distance is measured from tile (1, 1), so (12, 12) is more than five tiles out.
        GameDatabase db = Database();
        WorldState world = TestWorld.World(db);
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 2, new TilePos(12, 12));
        CreatureDef mine = db.CreatureById(me.CreatureTypeId);
        me.Health = 1;
        var noHeart = new Combat.HeartSites();
        Assert.True(Combat.FearRules.ShouldWithdraw(world, db, noHeart, me, mine));

        me.FearlessUntilTick = world.CurrentTick.Value + 1;
        Assert.False(Combat.FearRules.ShouldWithdraw(world, db, noHeart, me, mine));

        me.FearlessUntilTick = world.CurrentTick.Value;
        Assert.True(Combat.FearRules.ShouldWithdraw(world, db, noHeart, me, mine));
    }

    // --- Torture leave (0x0047B4A0) -------------------------------------------------------

    [Fact]
    public void TortureLeave_ForgetsTheDroppedJob_OfACreatureNotOnItsDeviceByChoice()
    {
        (StateHookContext hooks, CreatureState creature) = InState(TortureRules.TorturedState);
        creature.DroppedToDoJob = JobType.Torture;

        Assert.True(CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle));

        Assert.Equal(JobType.None, creature.DroppedToDoJob);
    }

    [Fact]
    public void TortureLeave_KeepsAVolunteersDroppedJob_AndClearsTortureVoluntary()
    {
        (StateHookContext hooks, CreatureState creature) = InState(TortureRules.TorturedState);
        creature.DroppedToDoJob = JobType.Torture;
        creature.TortureVoluntary = true;

        Assert.True(CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle));

        Assert.Equal(JobType.Torture, creature.DroppedToDoJob);
        Assert.False(creature.TortureVoluntary);
    }

    [Fact]
    public void TortureLeave_DoesNotRunOnAWalkBetweenTwoTortureStates()
    {
        (StateHookContext hooks, CreatureState creature) = InState(TortureRules.StrappingState);
        creature.DroppedToDoJob = JobType.Torture;

        CreatureStateMachine.BeginTravel(hooks, creature, 6, TortureRules.TorturedState);
        Assert.True(CreatureStateMachine.Arrive(hooks, creature));

        Assert.Equal(JobType.Torture, creature.DroppedToDoJob);
    }

    // --- Manufacture enter (0x0047B0A0), leave (0x0047B120) and the decay (0x004C3340) -----

    [Fact]
    public void ManufactureEnter_RaisesTheTileKeepersCount_EvenWhenTheClaimIsRefused()
    {
        // No Workshop under the creature, so the claim refuses; DK2 counts first and never undoes it.
        (StateHookContext hooks, CreatureState creature) = InState(CreatureStateMachine.Idle);

        Assert.False(CreatureStateMachine.TrySetState(hooks, creature, ManufactureState));

        Assert.Equal(1, hooks.World.Player(Keeper).ManufacturingCreatures);
    }

    [Fact]
    public void ManufactureLeave_LowersTheCount_OnceOnARealLeave()
    {
        (StateHookContext hooks, CreatureState creature) = InState(ManufactureState);
        hooks.World.Player(Keeper).ManufacturingCreatures = 1;

        Assert.True(CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle));
        Assert.True(CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle));

        Assert.Equal(0, hooks.World.Player(Keeper).ManufacturingCreatures);
    }

    [Fact]
    public void AMakerWalkingFromWorkToBed_KeepsTheCountUp_AndThePointsDoNotDecayUntilItArrives()
    {
        // The needs routine starts the walk to bed from the work state; hunger instead sets 91
        // first, which leaves category 5 at once (leave-hook-bodies.md section 4).
        (StateHookContext hooks, CreatureState creature) = InState(ManufactureState);
        WorldState world = hooks.World;
        world.Player(Keeper).ManufacturingCreatures = 1;
        world.Player(Keeper).ManufacturePoints = 10;

        CreatureStateMachine.BeginTravel(hooks, creature, 7, MakingAHomeState);
        ManufactureRules.Decay(world, hooks.Database);
        Assert.Equal(1, world.Player(Keeper).ManufacturingCreatures);
        Assert.Equal(10, world.Player(Keeper).ManufacturePoints);

        Assert.True(CreatureStateMachine.Arrive(hooks, creature));
        ManufactureRules.Decay(world, hooks.Database);
        Assert.Equal(0, world.Player(Keeper).ManufacturingCreatures);
        Assert.Equal(9, world.Player(Keeper).ManufacturePoints);
    }

    [Fact]
    public void TheCountHasNoFloor_AsDk2sDecrementHasNoGuard_AndANegativeCountHoldsThePoints()
    {
        (StateHookContext hooks, CreatureState creature) = InState(ManufactureState);
        WorldState world = hooks.World;
        world.Player(Keeper).ManufacturePoints = 10;

        Assert.True(CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle));
        ManufactureRules.Decay(world, hooks.Database);

        Assert.Equal(-1, world.Player(Keeper).ManufacturingCreatures);
        Assert.Equal(10, world.Player(Keeper).ManufacturePoints);
    }

    [Fact]
    public void TheNewFieldsSurviveASnapshot()
    {
        GameDatabase db = Database();
        WorldState world = TestWorld.World(db);
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        c.CasinoGamblingDuration = 7;
        c.CasinoGamblingFearless = true;
        c.FearlessUntilTick = 1234;
        c.TortureVoluntary = true;
        world.Player(Keeper).ManufacturingCreatures = -2;
        var sim = new GameSimulation(db, world, seed: 1, []);

        GameSimulation loaded = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);

        CreatureState back = Assert.Single(loaded.World.Creatures);
        Assert.Equal(7, back.CasinoGamblingDuration);
        Assert.True(back.CasinoGamblingFearless);
        Assert.Equal(1234, back.FearlessUntilTick);
        Assert.True(back.TortureVoluntary);
        Assert.Equal(-2, loaded.World.Player(Keeper).ManufacturingCreatures);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    private static (StateHookContext Hooks, CreatureState Creature) InState(int stateId)
    {
        GameDatabase db = Database();
        WorldState world = TestWorld.World(db);
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        StatePlacement.Place(creature, stateId);
        return (new StateHookContext(world, db), creature);
    }

    private static GameDatabase Database()
    {
        GameDatabase db = TestWorld.Database();
        Set(db, VariableTypeId.GamblingDelayBeforeBecomingFearlessSeconds, FearlessDelay);
        Set(db, VariableTypeId.ManufacturePointLossWhileNoWorkersPerSecond, 1);
        return db;
    }

    private static void Set(GameDatabase db, VariableTypeId id, int value) =>
        db.Variables.Misc[id] = new MiscVariable(id, value, 0, 0);

    private static int[] StatesOf(Cat category) =>
        Enumerable.Range(0, CreatureStateTable.Count).Where(id => CreatureStateTable.Get(id).Category == category).ToArray();
}
