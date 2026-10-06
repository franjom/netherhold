// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class PossessionTests
{
    private const byte Keeper = PlayerIds.FirstKeeper, SpellId = 2;

    private static (GameDatabase Db, WorldState World, CreatureState Target) Setup(bool horny = false)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var spell = new KeeperSpellDef
        {
            Name = "Possession", KeeperSpellId = SpellId, ManaCost = 500,
            ManaUpkeepPerSecond = 25, ShotTypeId = 32, BonusShotTypeId = 32,
            CooldownRaw = 0, LocationSelector = 5, TargetSelector = 6,
        };
        ShotDef shot = new(new byte[239])
        {
            Name = "Possess Creature", MeshResource = basis.Shots[4].MeshResource,
            Light = basis.Shots[4].Light, SoundCategory = "", ShotId = 32,
            Speed = 2048, Radius = 819, Health = 1, Flags = 6,
            ShotProcessFlags = 4, CollideType = 1, ProcessType = 26,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => horny && c.CreatureId == Dk2TerrainFixture.GoblinCreatureId
                ? c with { Flags = c.Flags | CreatureFlags.IsHorny } : c), basis.Variables, [spell],
            objects: basis.Objects.Values, shots: basis.Shots.Values.Append(shot));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        var at = new TilePos(10, 10);
        world.Tile(at).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(at).OwnerPlayerId = Keeper;
        world.Player(Keeper).Mana = 1500;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, SpellId, 3));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, at);
        return (db, world, target);
    }

    [Fact]
    public void CastSetsPossessedIdPaysAndKeepsStateAndAction_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = 199;
        target.SwingInProgress = true;
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Run(7);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        Assert.Equal(target.Id, world.Player(Keeper).PossessedCreatureId);
        Assert.Equal(1000, world.Player(Keeper).Mana);
        Assert.Equal(500, world.Player(Keeper).OneShotManaSpent);
        Assert.Equal((7L, 7L), (world.Player(Keeper).Availabilities[0].LastCastTick,
            world.Player(Keeper).Availabilities[0].LastCheckedTick));
        Assert.Equal(199, target.Dk2StateId);
        Assert.True(target.SwingInProgress);
        Assert.Equal(Keeper, target.OwnerPlayerId);
    }

    [Fact]
    public void ExistingPossessionRefusesEffectAfterOrdinaryCastPayment_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        CreatureState first = world.SpawnCreature(db, target.CreatureTypeId, Keeper, new TilePos(11, 10));
        world.Player(Keeper).PossessedCreatureId = first.Id;
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        Assert.Equal(first.Id, world.Player(Keeper).PossessedCreatureId);
        Assert.Equal(1000, world.Player(Keeper).Mana);
        Assert.Equal(500, world.Player(Keeper).OneShotManaSpent);
    }

    [Fact]
    public void PossessedPointerSurvivesSnapshotAndActivatesInstantDeathGate_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        var systems = new ISimulationSystem[] { new KeeperSpellSystem(), new ShotSystem() };
        var sim = new GameSimulation(db, world, 1, systems);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new KeeperSpellSystem(), new ShotSystem()]);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        CreatureState restored = copy.World.CreatureById(target.Id)!;
        Assert.True(restored.Possessed);
        Assert.True(CreatureDeathRules.IsPossessed(copy.World, restored));
        Assert.False(CreatureDeathRules.HasDyingState(copy.World, db,
            db.CreatureById(restored.CreatureTypeId), restored));
    }

    [Theory]
    [InlineData(193)]
    [InlineData(209)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(278)]
    [InlineData(279)]
    [InlineData(280)]
    public void TargetSixRefusesTheOriginalStateBranchesWithoutPaying(int state)
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = state;
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        Assert.Equal(0, world.Player(Keeper).PossessedCreatureId);
        Assert.Equal(1500, world.Player(Keeper).Mana);
    }

    [Fact]
    public void LeaveClearsPointerAndFlagAndRunsTheCombatLeaveHook_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = 199;
        var sim = new GameSimulation(db, world, 1,
            [new KeeperSpellSystem(), new ShotSystem(), new PossessionSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        Assert.True(target.Possessed);
        sim.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));
        Assert.Equal(0, world.Player(Keeper).PossessedCreatureId);
        Assert.False(target.Possessed);
        Assert.Equal(CreatureStateMachine.Idle, target.Dk2StateId);
    }

    [Fact]
    public void InfluenceDeadlineAndFlagsSurviveSnapshot_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        target.Possessed = true;
        target.InfluencedByPossessed = true;
        target.NextPossessionInfluenceTick = new GameTick(45);
        var sim = new GameSimulation(db, world, 1, []);
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        CreatureState restored = copy.World.CreatureById(target.Id)!;
        Assert.True(restored.Possessed);
        Assert.True(restored.InfluencedByPossessed);
        Assert.Equal(target.NextPossessionInfluenceTick, restored.NextPossessionInfluenceTick);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    [Fact]
    public void InfluenceRefreshIsStrictAndHasNoRadius_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(new TilePos(x, y)).OwnerPlayerId = Keeper;
            }
        CreatureState distant = world.SpawnCreature(db, target.CreatureTypeId, Keeper, new TilePos(1, 10));
        CreatureState enemy = world.SpawnCreature(db, target.CreatureTypeId, PlayerIds.Good, target.Tile);
        target.Possessed = true;
        target.NextPossessionInfluenceTick = new GameTick(2);
        world.Player(Keeper).PossessedCreatureId = target.Id;
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        sim.Run(3);
        Assert.False(distant.InfluencedByPossessed);
        sim.Tick(InputFrame.Empty);
        Assert.True(distant.InfluencedByPossessed);
        Assert.False(enemy.InfluencedByPossessed);
        Assert.Equal(new GameTick(23), target.NextPossessionInfluenceTick);
        world.Tile(new TilePos(5, 10)).TerrainId = Dk2TerrainFixture.Rock;
        sim.Run(20);
        Assert.True(distant.InfluencedByPossessed);
        sim.Tick(InputFrame.Empty);
        Assert.False(distant.InfluencedByPossessed);
        enemy.InfluencedByPossessed = true;
        sim.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));
        Assert.False(distant.InfluencedByPossessed);
        Assert.True(enemy.InfluencedByPossessed);
    }

    [Fact]
    public void LeavingFrozenPossessionEndsTheActionAndEntersFrozen_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        target.Possessed = true;
        target.FrozenTicks = 5;
        target.SwingInProgress = true;
        world.Player(Keeper).PossessedCreatureId = target.Id;
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        sim.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));
        Assert.False(target.SwingInProgress);
        Assert.Equal(CreatureActivity.Frozen, target.Activity);
        Assert.Equal(5, target.FrozenTicks);
    }

    [Fact]
    public void PossessionKeepsTrainingClaimButPausesWorkAndLeaveReleasesIt_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        for (int y = 9; y <= 11; y++)
            for (int x = 9; x <= 11; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.TrainingRoom), Keeper);
        Dk2TerrainFixture.FurnishRooms(db, world);
        world.Tile(12, 10).TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        world.Tile(12, 10).OwnerPlayerId = Keeper;
        world.Tile(12, 10).GoldHeld = 100_000;
        int training = JobRules.StateFor(JobType.Train)!.Value;
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), target, training));
        RoomPlace claim = target.HeldPlace;
        Assert.True(claim.IsHeld);
        var sim = new GameSimulation(db, world, 1,
            [new KeeperSpellSystem(), new ShotSystem(), new PossessionSystem(), new CreatureJobSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        int experience = target.ExperiencePoints;
        sim.Run(12);
        Assert.Equal(training, target.Dk2StateId);
        Assert.Equal(claim, target.HeldPlace);
        Assert.Equal(experience, target.ExperiencePoints);
        var leaveOnly = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        leaveOnly.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));
        Assert.Equal(RoomPlace.None, target.HeldPlace);
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.ImpCreatureId)]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId)]
    public void OwnLandHealingContinuesWhileNeedsPause_ExistingPerSecondCadenceDeviation(byte creatureType)
    {
        var (db, world, target) = Setup();
        CreatureState imp = world.SpawnCreature(db, creatureType, Keeper, target.Tile);
        imp.Possessed = true;
        imp.Health = 100;
        imp.HungerTicks = 17;
        world.Player(Keeper).PossessedCreatureId = imp.Id;
        int regen = CreatureHealthRules.OwnLandRegenPerSecond(world, db.CreatureById(imp.CreatureTypeId), imp);
        if (creatureType == Dk2TerrainFixture.ImpCreatureId) Assert.True(regen > 0);
        var sim = new GameSimulation(db, world, 1, [new CreatureHealthSystem()]);
        sim.Run(SimulationTiming.TicksPerSecond);
        Assert.Equal(100 + regen, imp.Health);
        Assert.Equal(17, imp.HungerTicks);
    }

    [Fact]
    public void PossessedDeathLeavesImmediatelyAndClearsInfluence_StronglyInferredFromBinary()
    {
        var (db, world, target) = Setup();
        target.Possessed = true;
        target.Health = 0;
        world.Player(Keeper).PossessedCreatureId = target.Id;
        CreatureState ally = world.SpawnCreature(db, target.CreatureTypeId, Keeper, target.Tile);
        ally.InfluencedByPossessed = true;
        var sim = new GameSimulation(db, world, 1, [new CreatureHealthSystem()]);
        sim.Run(SimulationTiming.TicksPerSecond);
        Assert.Equal(0, world.Player(Keeper).PossessedCreatureId);
        Assert.False(target.Possessed);
        Assert.False(ally.InfluencedByPossessed);
        Assert.False(CreatureDeathRules.IsDying(target));
    }

    [Theory]
    [InlineData(true, false, 4095)]
    [InlineData(false, true, 2826)]
    [InlineData(true, true, 4095)]
    [InlineData(false, false, 1884)]
    public void BodySpeedUsesTheSpeedStatAndPossessionPriority_StronglyInferredFromBinary(
        bool possessed, bool influenced, int expectedEntitySpeed)
    {
        var (db, world, target) = Setup();
        target = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, target.Tile);
        db.Variables.CreatureStats.Add(new((int)CreatureStatId.Speed, 200, 1, true));
        target.Possessed = possessed;
        target.InfluencedByPossessed = influenced;
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(expectedEntitySpeed << 4, target.Body.MaxSpeed);
    }

    [Theory]
    [InlineData(33, false, 134)]
    [InlineData(33, true, 67)]
    [InlineData(-101, false, 4095)]
    [InlineData(-100, false, 0)]
    [InlineData(100, true, 101)]
    public void PossessionSpeedTruncatesIntegerUnitsAndClampsUnsignedProducts(
        int stat, bool influenced, int expectedEntitySpeed)
    {
        var (basis, world, target) = Setup();
        basis.Variables.CreatureStats.Add(new((int)CreatureStatId.Speed, stat, 0, true));
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(d => d.CreatureId == target.CreatureTypeId
                ? d with { Speed = new Fp32(404) } : d), basis.Variables);
        target.Possessed = !influenced;
        target.InfluencedByPossessed = influenced;
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(expectedEntitySpeed << 4, target.Body.MaxSpeed);
    }

    [Fact]
    public void PossessionScalesTheAlreadyClampedPrecedingSpeedEffect()
    {
        var (db, world, target) = Setup();
        db.Variables.CreatureStats.Add(new((int)CreatureStatId.Speed, -50, 0, true));
        target.Possessed = true;
        target.SpeedEffectTicks = 5;
        target.SpeedPercent = 600;
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(2047 << 4, target.Body.MaxSpeed);
    }

    [Theory]
    [InlineData("held")]
    [InlineData("pit")]
    [InlineData("chicken")]
    [InlineData("rebel")]
    [InlineData("enemy")]
    [InlineData("horny")]
    public void TargetSixRefusesItsFlagAndOwnershipBranchesWithoutPayment(string refusal)
    {
        var (db, world, target) = Setup(horny: refusal == "horny");
        switch (refusal)
        {
            case "held": target.InHand = true; break;
            case "pit": target.CombatPitFighter = true; break;
            case "chicken": target.ChickenTicks = 1; break;
            case "rebel": target.RebelActive = true; break;
            case "enemy": target.OwnerPlayerId = PlayerIds.Good; break;
        }
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, SpellId, target.Id)));
        Assert.Equal(1500, world.Player(Keeper).Mana);
        Assert.Equal(0, world.Player(Keeper).PossessedCreatureId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryNewPossessionFieldChangesTheStateHash(int field)
    {
        var (db, world, target) = Setup();
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        switch (field)
        {
            case 0: target.Possessed = true; break;
            case 1: target.InfluencedByPossessed = true; break;
            case 2: target.NextPossessionInfluenceTick = new GameTick(1); break;
        }
        Assert.NotEqual(before, sim.StateHash());
    }

    [Theory]
    [InlineData(CallToArmsRules.GoingState)]
    [InlineData(CallToArmsRules.ArrivedState)]
    [InlineData(CallToArmsRules.MillingState)]
    [InlineData(6)]
    public void PossessionPausesCallToArmsStateAndArrivalWithoutRandomDraws(int state)
    {
        var (db, world, target) = Setup();
        target.Possessed = true;
        target.Dk2StateId = state;
        target.UtilityNewState = CallToArmsRules.ArrivedState;
        var sim = new GameSimulation(db, world, 1, [new CallToArmsSystem()]);
        var random = sim.Rng.SaveState();
        sim.Tick(InputFrame.Empty);
        Assert.Equal(state, target.Dk2StateId);
        Assert.Equal(CallToArmsRules.ArrivedState, target.UtilityNewState);
        Assert.False(target.HasRoute);
        Assert.Equal(random, sim.Rng.SaveState());
    }

    [Fact]
    public void PossessionKeepsTheCallToArmsFlagRecallRunning()
    {
        var (db, world, target) = Setup();
        target.Possessed = true;
        target.HeedingCallToArms = true;
        target.Dk2StateId = CallToArmsRules.ArrivedState;
        ObjectState flag = world.SpawnObject(db, CallToArmsRules.ObjectTypeId, Keeper,
            target.Tile.Y * world.Width + target.Tile.X);
        flag.ProcessState = CallToArmsRules.Recalling;
        flag.ProcessDeadline = -1;
        world.Player(Keeper).CallToArms = new(true, flag.Id, (ushort)target.Tile.X,
            (ushort)target.Tile.Y, 100);
        var sim = new GameSimulation(db, world, 1, [new CallToArmsSystem()]);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(CallToArmsRules.ArrivedState, target.Dk2StateId);
        Assert.Equal(1, flag.ProcessCount);
        Assert.Equal(SimulationTiming.OriginalTicksPerGameSecond, flag.ProcessDeadline);
    }

    [Theory]
    [InlineData(ChickenRules.ChickenState)]
    [InlineData(6)]
    public void PossessionPausesChickenStateAndArrivalWhileItsCountdownContinues(int state)
    {
        var (db, world, target) = Setup();
        target.Possessed = true;
        target.Dk2StateId = state;
        target.UtilityNewState = ChickenRules.ChickenState;
        target.ChickenTicks = 5;
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem(), new ChickenSystem()]);
        var random = sim.Rng.SaveState();
        sim.Tick(InputFrame.Empty);
        Assert.Equal(state, target.Dk2StateId);
        Assert.Equal(ChickenRules.ChickenState, target.UtilityNewState);
        Assert.Equal(4, target.ChickenTicks);
        Assert.False(target.HasRoute);
        Assert.Equal(random, sim.Rng.SaveState());
        sim.Run(4);
        Assert.Equal(0, target.ChickenTicks);
        Assert.Equal(CreatureStateMachine.Idle, target.Dk2StateId);
        Assert.True(target.Possessed);
    }

    [Fact]
    public void AStoredActionStillLandsWhilePossessedWithoutAutonomousFightAcquisition()
    {
        var (db, world, target) = Setup();
        CreatureState enemy = world.SpawnCreature(db, target.CreatureTypeId, PlayerIds.Good, target.Tile);
        target.Possessed = true;
        world.Player(Keeper).PossessedCreatureId = target.Id;
        target.SwingInProgress = true;
        target.SwingKind = SpellRules.MeleeSwing;
        target.SwingStartTick = 100;
        target.SwingTargetKind = FightTargetKind.Creature;
        target.SwingTargetId = enemy.Id;
        int before = enemy.Health;
        var sim = new GameSimulation(db, world, 1, [new CombatSystem()]);
        sim.Tick(InputFrame.Empty);
        Assert.True(enemy.Health < before);
        Assert.False(target.SwingInProgress);
        Assert.Equal(CreatureStateMachine.Idle, target.Dk2StateId);
        Assert.Equal(FightTargetKind.None, target.FightTargetKind);
    }
}
