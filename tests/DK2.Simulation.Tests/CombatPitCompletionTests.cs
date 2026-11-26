// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;
namespace DK2.Simulation.Tests;
public sealed class CombatPitCompletionTests
{
    private static void PutPit(GameSimulation sim, GameDatabase db)
    {
        for (int y = 2; y <= 6; y++)
        for (int x = 4; x <= 8; x++)
            RoomRules.Place(db, ref sim.World.Tile(x, y), db.Room(RoomTypeId.CombatPit), PlayerIds.FirstKeeper);
    }

    [Fact]
    public void NonfighterIdleClearsTheKeeperBindingAndCurrentJob()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase _) = Start(CreatureStateMachine.Idle);
        c.CombatPitFighter = false;
        c.PrisonOwnerPlayerId = PlayerIds.FirstKeeper;
        c.CurrentJob = JobType.Train;
        sim.Tick();
        Assert.Equal(PrisonRules.NoPrison, c.PrisonOwnerPlayerId);
        Assert.Equal(JobType.None, c.CurrentJob);
    }

    [Fact]
    public void IdleFighterKeepsItsBindingAndJobWhenReturningToThePit()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(CreatureStateMachine.Idle);
        PutPit(sim, db);
        c.PrisonOwnerPlayerId = PlayerIds.FirstKeeper;
        c.CurrentJob = JobType.Train;
        sim.Tick();
        Assert.Equal(138, c.Dk2StateId);
        Assert.True(c.CombatPitFighter);
        Assert.Equal(PlayerIds.FirstKeeper, c.PrisonOwnerPlayerId);
        Assert.Equal(JobType.Train, c.CurrentJob);
    }

    [Fact]
    public void IdleFighterRefollowsBeforeReturningToThePitWhenItsBindingIsClear()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(CreatureStateMachine.Idle);
        PutPit(sim, db);
        CreatureState leader = sim.World.SpawnCreature(db, c.CreatureTypeId, c.OwnerPlayerId, new TilePos(7, 4));
        leader.Possessed = true;
        c.FollowLeaderId = leader.Id;
        c.PrisonOwnerPlayerId = PrisonRules.NoPrison;
        sim.Tick();
        Assert.Equal(PossessionRules.FollowingState, c.Dk2StateId);
        Assert.Equal(leader.Id, c.FollowLeaderId);
        Assert.True(c.CombatPitFighter);
    }

    [Fact]
    public void RefusedIdleRefollowClearsTheLeaderBeforeReturningTheBoundFighter()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(CreatureStateMachine.Idle);
        PutPit(sim, db);
        c.FollowLeaderId = 12345;
        c.PrisonOwnerPlayerId = PlayerIds.FirstKeeper;
        sim.Tick();
        Assert.Equal(138, c.Dk2StateId);
        Assert.Equal(0, c.FollowLeaderId);
        Assert.Equal(PlayerIds.FirstKeeper, c.PrisonOwnerPlayerId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdleRefollowWaitsOneHandlerTurnAndThenRoutesEvenForAFighter(bool fighter)
    {
        (GameSimulation? basis, CreatureState? c, GameDatabase? db) = Start(CreatureStateMachine.Idle);
        PutPit(basis, db);
        c.CombatPitFighter = fighter;
        CreatureState leader = basis.World.SpawnCreature(db, c.CreatureTypeId, c.OwnerPlayerId, new TilePos(7, 4));
        leader.Possessed = true;
        c.FollowLeaderId = leader.Id;
        var sim = new GameSimulation(db, basis.World, seed: 42,
            [new CreatureUpdateSystem(new CombatPitSystem(), new CreatureJobSystem())]);
        sim.Tick();
        Assert.Equal(PossessionRules.FollowingState, c.Dk2StateId);
        Assert.False(c.HasRoute);
        sim.Tick();
        Assert.True(c.HasRoute);
    }
    private static (GameSimulation Sim, CreatureState Creature, GameDatabase Db) Start(int state, int frames = 20)
    {
        var art = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        art[8] = art[31] = new CreatureAnimationTiming
        { ResourceType = 6, FrameCount = (ushort)frames, FramesPerSecond = 20 };
        CreatureDef goblin = Dk2TerrainFixture.Goblin() with { CreatureId = 54, ArtSlots = art };
        GameDatabase db = Dk2TerrainFixture.Database(goblin);
        db.Variables.Misc[VariableTypeId.ModifyAngerOfCombatPitVictor] =
            new MiscVariable(VariableTypeId.ModifyAngerOfCombatPitVictor, -100, 0, 0);
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        CreatureState c = world.SpawnCreature(db, goblin.CreatureId, PlayerIds.FirstKeeper, new TilePos(6, 4));
        StatePlacement.Place(c, state);
        c.PitStateEnteredTick = -1;
        c.CombatPitFighter = true;
        return (new GameSimulation(db, world, seed: 42, [new CombatPitSystem()]), c, db);
    }
    [Fact]
    public void LevelCapStartsPlainAction44AndWaitsForItsAnimationBeforeAddingAnger()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(139, frames: 100);
        c.Level = (byte)db[VariableTypeId.CombatPitMaxExperienceLevel];
        c.ActionUntilTick = 1000;
        sim.Tick();
        Assert.Equal(44, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
        Assert.Equal(31, c.ActionArtSlot);
        Assert.Equal(0, c.ActionCount);
        Assert.Equal(0, c.ActionUntilTick);
        for (int i = 0; i < 5; i++) sim.Tick();
        Assert.Equal(139, c.Dk2StateId);
        Assert.Equal(0, c.Anger[AngerBucket.Work]);
        Assert.Equal(0, c.Anger[AngerBucket.Company]);
        for (int i = 0; i < 100 && c.Anger[AngerBucket.Work] == 0; i++) sim.Tick();
        Assert.True(c.Anger[AngerBucket.Work] > 0);
    }
    [Fact]
    public void LoadedVictoryStateStartsAction35AndWaitsPastTheAgeLimitForItsAnimation()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(140, frames: 100);
        c.Anger[AngerBucket.Treatment] = 1000;
        sim.Tick();
        Assert.Equal(35, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
        Assert.Equal(8, c.ActionArtSlot);
        for (int i = 0; i < 5; i++) sim.Tick();
        Assert.Equal(140, c.Dk2StateId);
        Assert.Equal(1000, c.Anger[AngerBucket.Treatment]);
        for (int i = 0; i < 100 && c.Dk2StateId == 140; i++) sim.Tick();
        Assert.Equal(138, c.Dk2StateId);
        Assert.Equal(900, c.Anger[AngerBucket.Treatment]);
    }
    [Fact]
    public void MissingVictoryArtStillStartsTheNativeAction()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(140, frames: 0);
        sim.Tick();
        Assert.Equal(35, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
    }
    [Fact]
    public void CapActionDoesNotRestartAfterItsFirstAgeTick()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(139, frames: 0);
        c.Level = (byte)db[VariableTypeId.CombatPitMaxExperienceLevel];
        sim.Tick();
        sim.Tick();
        Assert.Equal(0, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
        Assert.True(c.Anger[AngerBucket.Work] > 0);
    }
    [Fact]
    public void VictoryAgeBoundaryIncludesFourTicksPerConfiguredSecond()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase _) = Start(140, frames: 0);
        c.PitStateEnteredTick = -16;
        c.Anger[AngerBucket.Treatment] = 1000;
        sim.Tick();
        Assert.Equal(140, c.Dk2StateId);
        sim.Tick();
        Assert.Equal(138, c.Dk2StateId);
        Assert.Equal(900, c.Anger[AngerBucket.Treatment]);
    }

    [Fact]
    public void VictoryUsesTheNativeConfiguredTicksPerSecond()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(140, frames: 0);
        db.Variables.Misc[VariableTypeId.TicksPerSecond] = new MiscVariable(VariableTypeId.TicksPerSecond, 1, 0, 0);
        c.PitStateEnteredTick = -5;
        c.Anger[AngerBucket.Treatment] = 1000;
        sim.Tick();
        Assert.Equal(138, c.Dk2StateId);
        Assert.Equal(900, c.Anger[AngerBucket.Treatment]);
    }
    [Fact]
    public void VictoryWaitsForAnExistingActionWithoutReplacingIt()
    {
        (GameSimulation? sim, CreatureState? c, GameDatabase? db) = Start(140);
        c.ActionUntilTick = 10;
        c.PitStateEnteredTick = -17;
        c.Anger[AngerBucket.Treatment] = 1000;
        sim.Tick();
        Assert.Equal(140, c.Dk2StateId);
        Assert.Equal(0, c.ActionArtSlot);
        Assert.Equal(1000, c.Anger[AngerBucket.Treatment]);
        Assert.Equal(0, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
        c.ActionUntilTick = 0;
        sim.Tick();
        Assert.Equal(138, c.Dk2StateId);
        Assert.Equal(900, c.Anger[AngerBucket.Treatment]);
    }
}
