// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class TrainingCycleTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Vampire = Dk2TerrainFixture.VampireCreatureId;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PickupBeforeATrainingHitCancelsTheAction(bool spell)
    {
        var (db, world, trainee) = Setup(112, spell);
        ObjectState target = world.SpawnObject(db, spell ? (byte)29 : (byte)14, Keeper, 63);
        StartPendingHit(trainee, target, spell);
        var sim = new GameSimulation(db, world, 42,
            [new HandSystem(), new TrainingActionSystem(), new CreatureJobSystem()]);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, trainee.Id)));
        Assert.True(trainee.InHand);
        sim.Run(20);
        Assert.Equal(0, trainee.TrainingActionId);
        Assert.Equal(0, trainee.ActionUntilTick);
        Assert.Equal(0, target.ProcessState);
        Assert.Empty(world.Shots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombatEntryBeforeATrainingHitCancelsTheAction(bool spell)
    {
        var (db, world, trainee) = Setup(112, spell);
        ObjectState target = world.SpawnObject(db, spell ? (byte)29 : (byte)14, Keeper, 63);
        StartPendingHit(trainee, target, spell);
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), trainee, 199, 0));
        var sim = new GameSimulation(db, world, 42, [new TrainingActionSystem()]);
        sim.Run(20);
        Assert.Equal(0, trainee.TrainingActionId);
        Assert.Equal(0, trainee.ActionUntilTick);
        Assert.Equal(0, target.ProcessState);
        Assert.Empty(world.Shots);
    }

    private static void StartPendingHit(CreatureState trainee, ObjectState target, bool spell)
    {
        trainee.TrainingActionId = spell ? (byte)31 : (byte)30;
        trainee.TrainingActionStartedTick = 0;
        trainee.TrainingTargetObjectId = target.Id;
        trainee.ActionUntilTick = 100;
    }

    private static (GameDatabase Db, WorldState World, CreatureState Trainee) Setup(int state = 108, bool spell = false)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var clip = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 8, FramesPerSecond = 8 };
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        foreach (int slot in new[] { 1, 5, 6, 20 }) slots[slot] = clip;
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Vampire ? c with
            {
                ArtSlots = slots,
                Flags = c.Flags | CreatureFlags.CanBePickedUp,
                Spells = spell ? [new CreatureSpellSlot(Dk2TerrainFixture.FireballSpellId, 1, new Fp32(4096), 1)] : [],
            } : c),
            basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values,
            creatureSpells: basis.CreatureSpells.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 5; y++)
        for (int x = 1; x <= 5; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.TrainingRoom), Keeper);
        Dk2TerrainFixture.FurnishRooms(db, world);
        world.Tile(1, 1).GoldHeld = 100_000;
        CreatureState trainee = world.SpawnCreature(db, Vampire, Keeper, new TilePos(3, 3));
        trainee.CurrentJob = JobType.Train;
        StatePlacement.As(trainee, CreatureActivity.AtWork);
        StatePlacement.Place(trainee, state);
        trainee.HeldPlace = new RoomPlace(63, RoomCapacityRules.Units);
        trainee.JobTileIndex = 63;
        return (db, world, trainee);
    }

    [Fact]
    public void ATrainingDummyOnTheCurrentTileStillRequiresWalkingToItsStandOff()
    {
        var (db, world, trainee) = Setup();
        foreach (ObjectState item in world.Objects.ToArray()) world.RemoveObject(item.Id);
        world.SpawnObject(db, 14, Keeper, 63);
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        sim.Run(2);
        Assert.Equal(CreatureState.CentreOf(3), trainee.StateTargetX);
        Assert.Equal(CreatureState.CentreOf(3), trainee.StateTargetY);
        Assert.True(trainee.HasRoute);
        Assert.Equal(1, trainee.Body.PathTotalCount);
        long dx = (trainee.Body.PathTargetPos.X >> 4) - trainee.StateTargetX;
        long dy = (trainee.Body.PathTargetPos.Y >> 4) - trainee.StateTargetY;
        Assert.InRange(dx * dx + dy * dy, 2045L * 2045, 2049L * 2049);
        for (int i = 0; i < 100 && trainee.Dk2StateId != 112; i++) sim.Tick();
        Assert.Equal(112, trainee.Dk2StateId);
        Assert.True(Math.Abs(trainee.PosX - trainee.StateTargetX) > 512 || Math.Abs(trainee.PosY - trainee.StateTargetY) > 512);
    }

    [Fact]
    public void TrainingNaturallyWalksToADummyStrikesItAndChoosesAgain()
    {
        var (db, world, trainee) = Setup();
        Assert.Contains(world.Objects, o => o.ObjectTypeId == 14);
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        var seen = new HashSet<int>();
        bool struck = false;
        for (int i = 0; i < 100; i++)
        {
            sim.Tick();
            seen.Add(CreatureStateTable.Get(trainee.Dk2StateId).IsUtility ? trainee.UtilityNewState : trainee.Dk2StateId);
            struck |= world.Objects.Any(o => o.ObjectTypeId == 14 && o.ProcessState == 18);
        }
        Assert.Subset(seen, new HashSet<int> { 108, 111, 112 });
        Assert.True(struck);
        Assert.Equal(JobType.Train, trainee.CurrentJob);
    }

    [Fact]
    public void ATraineeMovedAfterItsFirstWorkingTurnEndsItsAction()
    {
        var (db, world, trainee) = Setup();
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        for (int i = 0; i < 100 && trainee.Dk2StateId != 112; i++) sim.Tick();
        Assert.Equal(112, trainee.Dk2StateId);
        sim.Tick();
        trainee.PosX += 32;
        sim.Tick();
        Assert.Equal(108, trainee.Dk2StateId);
        Assert.Equal(0, trainee.TrainingActionId);
    }

    [Fact]
    public void TrainingSpellStartsItsCooldownAndLaunchesAfterTheShotDelay()
    {
        var (db, world, trainee) = Setup(spell: true);
        foreach (ObjectState item in world.Objects.ToArray()) world.RemoveObject(item.Id);
        world.SpawnObject(db, 29, Keeper, 63);
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        for (int i = 0; i < 100 && trainee.TrainingActionId != 31; i++) sim.Tick();
        Assert.Equal(31, trainee.TrainingActionId);
        Assert.Equal(112, trainee.Dk2StateId);
        Assert.Equal(trainee.TrainingActionStartedTick, trainee.SpellStamps[0]);
        Assert.Empty(world.Shots);
        sim.Tick();
        Assert.Empty(world.Shots);
        for (int i = 0; i < 10 && world.Shots.Count == 0; i++) sim.Tick();
        Assert.Single(world.Shots);
    }

    [Theory]
    [InlineData(111)]
    [InlineData(112)]
    public void MachineAndWorkingStatesRefuseTheTrainingCap(int state)
    {
        var (db, world, trainee) = Setup(state);
        trainee.Level = (byte)TrainingRules.LevelCap(db);
        var sim = new GameSimulation(db, world, 42, [new CreatureJobSystem()]);
        sim.Tick();
        Assert.Equal(1, trainee.Dk2StateId);
        Assert.Equal(JobType.None, trainee.CurrentJob);
    }

    [Fact]
    public void ATrainingHitRunsTheDummyReactionOnTheSameDefaultTick()
    {
        var (db, world, trainee) = Setup(112);
        ObjectState target = world.Objects.First(o => o.ObjectTypeId == 14);
        trainee.TrainingActionId = 30;
        trainee.TrainingActionStartedTick = 0;
        trainee.TrainingTargetObjectId = target.Id;
        trainee.ActionUntilTick = 100;
        var sim = new GameSimulation(db, world, 42, TrainingPipeline());
        for (int i = 0; i < 20 && target.ProcessState != 18; i++) sim.Tick();
        Assert.Equal(18, target.ProcessState);
        Assert.Equal(1, target.ProcessAgeTicks);
        Assert.Equal(0x200, target.TimerTicks);
    }

    [Fact]
    public void ATrainingShotTakesItsFirstStepOnTheSameDefaultTick()
    {
        var (db, world, trainee) = Setup(112, spell: true);
        ObjectState target = world.SpawnObject(db, 29, Keeper, 84);
        trainee.TrainingActionId = 31;
        trainee.TrainingActionStartedTick = 0;
        trainee.TrainingTargetObjectId = target.Id;
        trainee.ActionUntilTick = 100;
        var sim = new GameSimulation(db, world, 42, TrainingPipeline());
        for (int i = 0; i < 20 && world.Shots.Count == 0; i++) sim.Tick();
        ShotState shot = Assert.Single(world.Shots);
        Assert.Equal(1, shot.Age);
        Assert.NotEqual((trainee.PosX, trainee.PosY), (shot.PosX, shot.PosY));
    }

    // The default order, cut to the trainee's update and what its action makes.
    private static ISimulationSystem[] TrainingPipeline() =>
    [
        new CreatureUpdateSystem(new CreatureJobSystem(), new CreatureMovementSystem()),
        new TrainingActionSystem(),
        new ShotSystem(),
        new ObjectReactionSystem(),
    ];
}
