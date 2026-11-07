// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class PossessedExcavationTests
{
    private static ISimulationSystem[] Systems() =>
        [new PossessionSystem(), new CreatureUpdateSystem(), new TileWorkSystem()];

    private static (GameSimulation Sim, CreatureState Imp) Setup(bool mine = false, bool art = true, int health = 30000)
    {
        CreatureDef definition = Dk2TerrainFixture.Imp();
        if (art)
        {
            var clip = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 15, FramesPerSecond = 20 };
            var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
            slots[5] = clip with { Flags = (CreatureAnimationFlags)0x800 };
            definition = definition with { ArtSlots = slots, AlternateArt = clip };
        }
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == definition.CreatureId ? definition : c),
            basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < 19; y++)
        for (int x = 1; x < 19; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
        }
        foreach (int x in new[] { 4, 6 })
        {
            world.Tile(x, 5).TerrainId = mine ? Dk2TerrainFixture.Gold : Dk2TerrainFixture.Rock;
            world.Tile(x, 5).OwnerPlayerId = PlayerIds.Neutral;
            world.Tile(x, 5).Health = health;
        }
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.FirstKeeper, new TilePos(5, 5));
        imp.Possessed = true;
        imp.Dk2StateId = ImpStates.TaskSelection;
        imp.TimeLastUsedMelee = -100;
        world.Player(PlayerIds.FirstKeeper).PossessedCreatureId = imp.Id;
        return (new GameSimulation(db, world, new SimRandom(new Dk2Lcg(1)), Systems()), imp);
    }

    private static InputFrame Press(CreatureState imp, ushort yaw = 0x200) => InputFrame.Of(
        new ActPossessedCreatureCommand(PlayerIds.FirstKeeper,
            new(new(new(imp.PosX), new(imp.PosY), new(imp.PosZ)), new(yaw), new(0x200)), true, new(0)));

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ThePressStartsTheExcavationClockAndDrawsOneInstallCoin(bool mine, int action)
    {
        var (sim, imp) = Setup(mine);
        uint before = ((Dk2Lcg)sim.Rng.Algorithm).Seed;
        var expected = new Dk2Lcg(before);
        expected.NextInt(2);

        sim.Tick(Press(imp));

        Assert.Equal(action, CreatureCurrentAction.IndexOf(imp, sim.Database.CreatureById(imp.CreatureTypeId), sim.CurrentTick));
        Assert.Equal(5, imp.ActionArtSlot);
        Assert.Equal(1365, imp.ActionAnimationPhase);
        Assert.Equal(expected.Seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
        Assert.Equal(-1, imp.TargetTileIndex);
    }

    [Fact]
    public void ASecondPressKeepsTheUnfinishedLoopWithoutAnotherCoin()
    {
        var (sim, imp) = Setup();
        sim.Tick(Press(imp));
        uint seed = ((Dk2Lcg)sim.Rng.Algorithm).Seed;
        imp.TimeLastUsedMelee = -100;

        sim.Tick(Press(imp));

        Assert.Equal(seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Equal(2730, imp.ActionAnimationPhase);
    }

    [Fact]
    public void AnIntactWallIsStruckOnceWithoutAnotherPress()
    {
        var (sim, imp) = Setup();
        sim.Tick(Press(imp));
        int health = sim.World.Tile(6, 5).Health;
        Assert.True(health < 30000);
        Assert.True(CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));

        sim.Run(5);

        Assert.Equal(health, sim.World.Tile(6, 5).Health);
        Assert.False(CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));
        Assert.Equal(0, imp.ActionArtSlot);
    }

    [Fact]
    public void ARemovedWallKeepsItsActionUntilTheStoppedClipCompletes()
    {
        var (sim, imp) = Setup(health: 1);
        sim.Tick(Press(imp));
        Assert.Equal(Dk2TerrainFixture.DirtPath, sim.World.Tile(6, 5).TerrainId);
        Assert.True((imp.ActionAnimationFlags & ActionAnimationFlags.Stop) != 0);
        Assert.False(imp.ActionHandlerDone);
        sim.Run(3);
        Assert.True(CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));

        sim.Tick();

        Assert.False(CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));
        Assert.Equal(0, imp.ActionArtSlot);
    }

    [Fact]
    public void TheCurrentActionPointChangesTheHashIndependentlyOfAiJobFields()
    {
        var (east, eastImp) = Setup();
        var (west, westImp) = Setup();
        east.Tick(Press(eastImp));
        west.Tick(Press(westImp, 0x600));
        foreach (var sim in new[] { east, west })
            foreach (int x in new[] { 4, 6 }) sim.World.Tile(x, 5).Health = 30000;

        Assert.NotEqual(east.StateHash(), west.StateHash());
    }

    [Fact]
    public void ASnapshotRetainsThePendingStoppedActionAndItsContinuation()
    {
        var (sim, imp) = Setup(health: 1);
        sim.Tick(Press(imp));
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, Systems());
        CreatureState restored = copy.World.CreatureById(imp.Id)!;
        Assert.True(CreatureCurrentAction.IsRunning(restored, copy.CurrentTick));
        Assert.Equal(sim.StateHash(), copy.StateHash());
        for (int i = 0; i < 5; i++)
        {
            sim.Tick();
            copy.Tick();
            Assert.Equal(sim.StateHash(), copy.StateHash());
        }
    }

    [Fact]
    public void AFixtureWithoutTimingKeepsTheExistingInstantWorkAdapter()
    {
        var (sim, imp) = Setup(art: false);
        sim.Tick(Press(imp));
        Assert.True(sim.World.Tile(6, 5).Health < 30000);
        Assert.False(CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));
    }

    [Theory]
    [InlineData(SpellRules.MeleeSwing, CreatureActionTable.MeleeSwing)]
    [InlineData(SpellRules.SpellCast, CreatureActionTable.SpellCast)]
    [InlineData(SpellRules.FreeAimCast, CreatureActionTable.FreeAimCast)]
    public void AReplacementAttackOwnsTheCurrentAction(byte kind, int action)
    {
        var (sim, imp) = Setup();
        sim.Tick(Press(imp));
        new GameSimulation(sim.Database, sim.World, sim.Rng,
            [new CreatureUpdateSystem(new ReplacementAttack(kind))]).Tick();

        Assert.Equal(action, CreatureCurrentAction.IndexOf(imp, sim.Database.CreatureById(imp.CreatureTypeId), sim.CurrentTick));
    }

    [Fact]
    public void ATravelReplacementClearsTheActionBeforeTheLaterAnimationChoice()
    {
        var (sim, imp) = Setup();
        sim.Tick(Press(imp));
        int health = sim.World.Tile(6, 5).Health;
        CreatureStateMachine.BeginTravel(new StateHookContext(sim.World, sim.Database), imp, 6, 1);
        Assert.False(CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));

        sim.Tick();

        Assert.Equal(0, imp.ActionArtSlot);
        Assert.Equal(health, sim.World.Tile(6, 5).Health);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStoppedZeroRateClipNeedsItsTerminalFlagToCompleteTheHandler(bool terminal)
    {
        var (sim, imp) = Setup();
        sim.Tick(Press(imp));
        sim.World.Tile(6, 5).TerrainId = Dk2TerrainFixture.DirtPath;
        imp.ActionHandlerDone = false;
        imp.ActionAnimationRate = 0;
        imp.ActionAnimationFlags = terminal ? ActionAnimationFlags.Complete : 0;

        sim.Tick();

        Assert.Equal(!terminal, CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));
    }

    [Fact]
    public void AWorldCopyRetainsThePendingActionAndOwnsItsValueState()
    {
        var (sim, imp) = Setup(health: 1);
        sim.Tick(Press(imp));
        WorldState copy = WorldState.CopyOf(sim.World);
        CreatureState cloned = copy.CreatureById(imp.Id)!;
        Assert.Equal(1, CreatureCurrentAction.IndexOf(cloned, sim.Database.CreatureById(cloned.CreatureTypeId), sim.CurrentTick));
        Hash64 liveHash = Hash64.Start(), copiedHash = Hash64.Start();
        sim.World.HashInto(ref liveHash);
        copy.HashInto(ref copiedHash);
        Assert.Equal(liveHash.Value, copiedHash.Value);

        sim.Run(5);

        Assert.True(CreatureCurrentAction.IsRunning(cloned, sim.CurrentTick));
        Assert.False(CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));
        cloned.CopyFrom(imp);
        Assert.False(CreatureCurrentAction.IsRunning(cloned, sim.CurrentTick));
    }

    private sealed class ReplacementAttack(byte kind) : ICreatureUpdateStep
    {
        public string Name => "ReplacementAttack";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature) =>
            FightRules.BeginPossessedAction(context, creature, kind, 0, FightTargetKind.None, CombatRules.NoTarget);
    }
}
