// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class ReinforcementActionTests
{
    private static readonly TilePos Face = new(4, 3);
    private static readonly TilePos Stance = new(3, 3);

    // 00466390 begins action7; 004950C0 selects art12; 00489A68 polls before dispatch.
    [Fact]
    public void ANewWorkStateStartsArt12BeforeItsFirstApplication()
    {
        var sim = Start();
        CreatureState imp = sim.World.Creatures[0];
        sim.Run(2);
        Assert.Equal(12, imp.ActionArtSlot);
        Assert.Equal(0, imp.ExperiencePoints);
        sim.Tick();
        Assert.Equal(20, imp.ExperiencePoints);
    }

    // 00497960 final hit returns done on +F1&6; 00496AA0 clears before 00466390.
    [Fact]
    public void AFinalHitAfterTheFirstWrapLeaves22InThatSameUpdate()
    {
        var sim = Begin(ActionAnimationFlags.Loop | ActionAnimationFlags.Wrapped);
        CreatureState imp = sim.World.Creatures[0];
        sim.World.Tile(Face).Health = 659;
        sim.Tick();
        Assert.Equal(20, imp.ExperiencePoints);
        Assert.Equal(10, imp.Dk2StateId);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, sim.World.Tile(Face).TerrainId);
    }

    // 00497A60 returns running without a cycle flag; both 00496AA0 gates still apply.
    [Fact]
    public void AFinalHitBeforeTheFirstWrapRequestsStopAndRemainsBusy()
    {
        var sim = Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        sim.World.Tile(Face).Health = 659;
        sim.Tick();
        Assert.Equal(22, imp.Dk2StateId);
        Assert.True((imp.ActionAnimationFlags & ActionAnimationFlags.Stop) != 0);
        Assert.False(imp.ActionHandlerDone);
        sim.Tick();
        Assert.Equal(22, imp.Dk2StateId);
        Assert.Equal(20, imp.ExperiencePoints);
    }

    // 00496B3B skips a completed handler even if another worker took the wall.
    [Fact]
    public void ACompletedHandlerWaitsForTheClipWithoutRequestingStop()
    {
        var sim = Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        imp.ActionHandlerDone = true;
        sim.World.Tile(Face).OwnerPlayerId = 3;
        sim.Tick();
        Assert.Equal(22, imp.Dk2StateId);
        Assert.Equal(0, imp.ExperiencePoints);
        Assert.False((imp.ActionAnimationFlags & ActionAnimationFlags.Stop) != 0);
    }

    // 00497960 calls 00453DC0, including its final 00453B30 own-floor test.
    [Fact]
    public void LosingTheAdjacentOwnedFloorStopsTheHandlerWithoutApplyingWork()
    {
        var sim = Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        foreach (TilePos at in new[] { new TilePos(3, 3), new(5, 3), new(4, 2), new(4, 4) })
            sim.World.Tile(at).OwnerPlayerId = PlayerIds.Neutral;
        int before = sim.World.Tile(Face).Health;
        sim.Tick();
        Assert.Equal(before, sim.World.Tile(Face).Health);
        Assert.Equal(0, imp.ExperiencePoints);
        Assert.Equal(22, imp.Dk2StateId);
        Assert.True((imp.ActionAnimationFlags & ActionAnimationFlags.Stop) != 0);
    }

    // 00497960 does not read the kind6 task; 00466390 checks it only after the action.
    [Fact]
    public void RemovingTheTaskDoesNotSkipTheRunningActionsWork()
    {
        var sim = Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        sim.World.Tile(Face).SetTaggedBy(3, true);
        sim.Tick();
        Assert.Equal(20, imp.ExperiencePoints);
        Assert.Equal(22, imp.Dk2StateId);
    }

    // The action's +38D point is independent of the state's task point (00496980/00497960).
    [Fact]
    public void TheRunningActionKeepsItsPointWhenTheJobRecordChanges()
    {
        var sim = Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        imp.TargetTileIndex = -1;
        imp.Dk2StateId = 1;
        int before = sim.World.Tile(Face).Health;
        sim.Tick();
        Assert.True(sim.World.Tile(Face).Health > before);
        Assert.Equal(20, imp.ExperiencePoints);
    }

    // 00496D20 clears the action block; reusing its art is not a surviving work action.
    [Fact]
    public void EndingTheActionPreventsAnOldPointFromBeingWorkedAgain()
    {
        var sim = Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(sim.World, sim.Database), imp, 199));
        int before = sim.World.Tile(Face).Health;
        imp.Dk2StateId = 1;
        imp.TargetTileIndex = -1;
        sim.Tick();
        Assert.Equal(before, sim.World.Tile(Face).Health);
        Assert.Equal(0, imp.ExperiencePoints);
    }

    [Fact]
    public void SavingABusyFinalHitPreservesTheRemainingAction()
    {
        var original = Begin(ActionAnimationFlags.Loop);
        original.World.Tile(Face).Health = 659;
        original.Tick();
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), original.Database, [new ImpTaskSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        for (int i = 0; i < 16; i++)
        {
            original.Tick();
            restored.Tick();
            Assert.Equal(original.StateHash(), restored.StateHash());
        }
    }

    // 00488930 action poll at00489A68 precedes ProcessFight at00489AC7.
    [Fact]
    public void TheCombatScanPassHasAlreadyPolledTheWorkAction()
    {
        var sim = Start(defaultCreaturePass: true);
        sim.Run(2);
        CreatureState imp = sim.World.Creatures[0];
        imp.TimerTicks = 1;
        sim.Tick();
        Assert.Equal(20, AfterCombatScan.ExperienceSeen);
    }

    // 00496AA0 does not replace the handler-done gate with a zero-rate test.
    [Fact]
    public void AZeroRateCannotFinishAHandlerThatStillRequestsStop()
    {
        var sim = Begin(ActionAnimationFlags.Loop);
        CreatureState imp = sim.World.Creatures[0];
        imp.ActionAnimationRate = 0;
        sim.World.Tile(Face).Health = 659;
        sim.Run(2);
        Assert.Equal(22, imp.Dk2StateId);
        Assert.Equal(20, imp.ExperiencePoints);
        Assert.False(imp.ActionHandlerDone);
    }

    // 00496B8C-00496BC1 retains count1 and clears Loop when a count2 cycle is observed.
    [Fact]
    public void ACountedCycleDoesNotEndTheActionAtItsFirstWrap()
    {
        var sim = Begin(ActionAnimationFlags.Loop | ActionAnimationFlags.Wrapped);
        CreatureState imp = sim.World.Creatures[0];
        imp.ActionCount = 2;
        sim.Tick();
        Assert.Equal(1, imp.ActionCount);
        Assert.False((imp.ActionAnimationFlags & ActionAnimationFlags.Loop) != 0);
        Assert.True(imp.ActionHandlerDone);
        sim.Tick();
        Assert.Equal(20, imp.ExperiencePoints);
    }

    internal static GameSimulation Begin(ActionAnimationFlags flags)
    {
        GameSimulation sim = Start();
        sim.Run(2);
        CreatureState imp = sim.World.Creatures[0];
        imp.ActionArtSlot = 12;
        imp.ActionAnimationPhase = 0;
        imp.ActionAnimationRate = 512;
        imp.ActionAnimationFlags = flags;
        imp.ActionHandlerDone = false;
        imp.TimerTicks = 1;
        return sim;
    }

    private sealed class AfterCombatScan(ICreatureUpdateStep next) : ICreatureUpdateStep
    {
        public static int ExperienceSeen;
        public string Name => next.Name;
        public void BeginTurn(SimulationContext context) => next.BeginTurn(context);
        public void EndTurn(SimulationContext context) => next.EndTurn(context);
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature)
        {
            next.Update(context, creature);
            ExperienceSeen = creature.ExperiencePoints;
        }
    }

    internal static GameSimulation Start(bool defaultCreaturePass = false)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        var art = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        art[12] = new CreatureAnimationTiming
        {
            ResourceType = 6, Flags = CreatureAnimationFlags.Present | CreatureAnimationFlags.EndSegment,
            FrameCount = 15, FramesPerSecond = 20, EndSegmentFrames = 8,
        };
        var database = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values,
            fixture.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.ImpCreatureId ? c with { ArtSlots = art } : c),
            fixture.Variables, objects: fixture.Objects.Values, keeperSpells: fixture.KeeperSpells.Values);
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, (byte)3]);
        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, 3, Stance);
        world.Tile(Stance).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(Stance).OwnerPlayerId = 3;
        ref TileState tile = ref world.Tile(Face);
        tile.TerrainId = Dk2TerrainFixture.Rock;
        tile.OwnerPlayerId = PlayerIds.Neutral;
        tile.Health = 300;
        imp.TargetWork = TileWorkKind.Reinforce;
        imp.TargetTileIndex = Face.Y * world.Width + Face.X;
        imp.Dk2StateId = 22;
        imp.StateEnteredTick = 0;
        imp.TimerTicks = 1_000_000;
        ISimulationSystem[] systems = [new ImpTaskSystem()];
        if (defaultCreaturePass)
        {
            var creaturePass = SimulationSystems.Default().OfType<CreatureUpdateSystem>().Single();
            systems = [new CreatureUpdateSystem(creaturePass.Steps.Select(s => s is CombatSystem ? new AfterCombatScan(s) : s).ToArray())];
        }
        return new GameSimulation(database, world, 99, systems);
    }
}
