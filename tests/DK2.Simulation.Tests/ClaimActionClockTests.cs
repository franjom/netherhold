// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class ClaimActionClockTests
{
    private static readonly TilePos Target = new(4, 3);

    // Original-function controls in imp-claim-action-clock.md; tick 0 begins the action.
    [Theory]
    [InlineData(TileWorkKind.Claim, 0, 1, 2, 5, 7)]
    [InlineData(TileWorkKind.Claim, 2000, 1, 2, 5, 6)]
    [InlineData(TileWorkKind.Claim, 0, 5, 2, 5, 7)]
    [InlineData(TileWorkKind.AttackTile, 0, 1, 4, 26, 27)]
    [InlineData(TileWorkKind.AttackTile, 2000, 1, 3, 25, 26)]
    [InlineData(TileWorkKind.AttackTile, 0, 5, 2, 24, 25)]
    [InlineData(TileWorkKind.ConvertRoom, 0, 1, 4, 25, 25)]
    [InlineData(TileWorkKind.ConvertRoom, 2000, 1, 3, 24, 24)]
    [InlineData(TileWorkKind.ConvertRoom, 0, 5, 2, 23, 23)]
    public void ApplicationsAndCompletionFollowTheNativeClock(
        TileWorkKind kind, int phase, byte flags, int second, int last, int end)
    {
        GameSimulation sim = Start(kind, phase, (ActionAnimationFlags)flags);
        CreatureState imp = sim.World.Creatures[0];
        sim.Tick();
        Assert.Equal(0, imp.ExperiencePoints);
        var hits = new List<int>();
        int left = -1;
        for (int tick = 1; tick <= 40; tick++)
        {
            int experience = imp.ExperiencePoints;
            sim.Tick();
            if (imp.ExperiencePoints > experience) hits.Add(tick);
            if (imp.Dk2StateId == ImpStates.For(kind).Work) continue;
            left = tick;
            break;
        }
        Assert.Equal(new[] { 1 }.Concat(Enumerable.Range(second, last - second + 1)), hits);
        Assert.Equal(end, left);
    }

    // Level 5: an Imp's attack clip completes on turn 1786, the claim's own starts from phase 0 on
    // 1789, and the Imp leaves the turn after that clip completes. With five applications it
    // completes a turn after the last, as a claim begun from a walk does.
    [Fact]
    public void AnAttackedFloorsClaimStartsItsOwnClipFromPhaseZero()
    {
        GameSimulation sim = Start(TileWorkKind.AttackTile);
        CreatureState imp = sim.World.Creatures[0];
        sim.Tick();
        int broken = -1, left = -1;
        var claims = new List<int>();
        for (int tick = 1; tick <= 40; tick++)
        {
            byte terrain = sim.World.Tile(Target).TerrainId;
            int experience = imp.ExperiencePoints;
            sim.Tick();
            if (terrain == Dk2TerrainFixture.ClaimedPath && sim.World.Tile(Target).TerrainId == Dk2TerrainFixture.DirtPath)
                broken = tick;
            if (terrain == Dk2TerrainFixture.DirtPath && imp.ExperiencePoints > experience) claims.Add(tick);
            if (sim.World.Tile(Target).OwnerPlayerId != 3 || imp.Dk2StateId != 10) continue;
            left = tick;
            break;
        }
        Assert.Equal(26, broken);
        Assert.Equal(new[] { 30, 31, 32, 33, 34 }, claims);
        Assert.Equal(36, left);
    }

    [Theory]
    [InlineData(TileWorkKind.Claim, 4)]
    [InlineData(TileWorkKind.AttackTile, 5)]
    [InlineData(TileWorkKind.ConvertRoom, 10)]
    public void TheWorkStateStartsItsOwnActionBeforeApplyingWork(TileWorkKind kind, int action)
    {
        GameSimulation sim = Start(kind);
        sim.Tick();
        CreatureState imp = sim.World.Creatures[0];
        Assert.Equal(13, imp.ActionArtSlot);
        Assert.Equal(action, CreatureCurrentAction.IndexOf(imp, sim.Database.CreatureById(1), sim.CurrentTick));
        Assert.Equal(0, imp.ExperiencePoints);
    }

    [Fact]
    public void RemovingTheJobDoesNotDiscardTheRunningActionsPoint()
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        sim.Tick();
        CreatureState imp = sim.World.Creatures[0];
        imp.TargetTileIndex = -1;
        sim.Tick();
        Assert.Equal(165, sim.World.Tile(Target).Health);
        Assert.Equal(20, imp.ExperiencePoints);
        Assert.Equal(16, imp.Dk2StateId);
    }

    [Fact]
    public void AClaimActionRejectsFloorThatIsNoLongerNeutral()
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        sim.Tick();
        sim.World.Tile(Target).OwnerPlayerId = 4;
        sim.Tick();
        Assert.Equal(1, sim.World.Tile(Target).Health);
        Assert.Equal(0, sim.World.Creatures[0].ExperiencePoints);
    }

    [Fact]
    public void FrozenAnimationDoesNotSuppressTheClaimHandler()
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        sim.Tick();
        CreatureState imp = sim.World.Creatures[0];
        imp.FrozenTicks = 20;
        int phase = imp.ActionAnimationPhase;
        sim.Run(2);
        Assert.Equal(329, sim.World.Tile(Target).Health);
        Assert.Equal(phase, imp.ActionAnimationPhase);
    }

    [Fact]
    public void AnUnrelatedBusyActionPreventsStartingClaimWork()
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        CreatureState imp = sim.World.Creatures[0];
        imp.ActionUntilTick = 100;
        sim.Run(4);
        Assert.Equal(1, sim.World.Tile(Target).Health);
        Assert.Equal(100, imp.ActionUntilTick);
    }

    [Fact]
    public void ReplacingTheActionCancelsItsWorkBeforeTheNextPoll()
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        sim.Tick();
        CreatureState imp = sim.World.Creatures[0];
        ActionAnimationClock.BeginCounted(imp, sim.Database.CreatureById(1), 6, 1, sim.Rng);
        imp.ActionUntilTick = 100;
        sim.Tick();
        Assert.Equal(1, sim.World.Tile(Target).Health);
        Assert.Equal(0, imp.ExperiencePoints);
    }

    [Fact]
    public void LeavingTheWorkerCategoryEndsTheClaimAction()
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        sim.Tick();
        CreatureState imp = sim.World.Creatures[0];
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(sim.World, sim.Database),
            imp, HandRules.PreparationState));
        sim.Tick();
        Assert.Equal(1, sim.World.Tile(Target).Health);
        Assert.Equal(0, imp.ExperiencePoints);
    }

    [Fact]
    public void PossessionPausesTheStateHandlerButKeepsTheScaledClaimAction()
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        sim.Tick();
        sim.World.Creatures[0].Possessed = true;
        sim.Tick();
        Assert.Equal(660, sim.World.Tile(Target).Health);
        Assert.Equal(3, sim.World.Tile(Target).OwnerPlayerId);
    }

    [Fact]
    public void ZeroRateStillProcessesTheCountedCycleBeforeSaving()
    {
        GameSimulation sim = Start(TileWorkKind.Claim, flags: ActionAnimationFlags.Loop | ActionAnimationFlags.Wrapped);
        sim.Tick();
        CreatureState imp = sim.World.Creatures[0];
        imp.ActionCount = 2;
        imp.ActionAnimationRate = 0;
        sim.Tick();
        Assert.Equal(1, imp.ActionCount);
        Assert.True(imp.ClaimAction.AnimationComplete);
        Assert.False(imp.ActionHandlerDone);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new ImpTaskSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        sim.Run(3); restored.Run(3);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    [Fact]
    public void AClaimPollPrecedesTheFightScan()
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        var worker = new ImpTaskSystem();
        var combat = new CombatSystem();
        var observation = new ObserveExperience();
        ((List<ISimulationSystem>)sim.Systems)[0] = new CreatureUpdateSystem(
            worker.Prelude, combat, observation, combat.States, worker);
        sim.Tick();
        sim.Tick();
        Assert.Equal(20, observation.Experience);
    }

    private sealed class ObserveExperience : ICreatureUpdateStep
    {
        public int Experience;
        public string Name => "ObserveClaimExperience";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature) => Experience = creature.ExperiencePoints;
    }

    [Theory]
    [InlineData(TileWorkKind.Claim)]
    [InlineData(TileWorkKind.AttackTile)]
    [InlineData(TileWorkKind.ConvertRoom)]
    public void ARunningActionResumesIdenticallyAfterCopyAndSave(TileWorkKind kind)
    {
        GameSimulation sim = Start(kind, 2000);
        sim.Run(2);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new ImpTaskSystem()]);
        WorldState copiedWorld = WorldState.CopyOf(sim.World);
        var random = sim.Rng.SaveState();
        GameSimulation copied = GameSimulation.Resume(sim.Database, copiedWorld,
            SimRandom.Restore(random), [new ImpTaskSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(sim.StateHash(), copied.StateHash());
        for (int i = 0; i < 30; i++)
        {
            sim.Tick(); restored.Tick(); copied.Tick();
            Assert.Equal(sim.StateHash(), restored.StateHash());
            Assert.Equal(sim.StateHash(), copied.StateHash());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryIndependentActionInputChangesTheStateHash(int field)
    {
        GameSimulation sim = Start(TileWorkKind.Claim);
        ulong before = sim.StateHash();
        CreatureState imp = sim.World.Creatures[0];
        switch (field)
        {
            case 0: imp.ClaimAction.Kind = ClaimActionKind.Claim; break;
            case 1: imp.ClaimAction.Target = new TilePos(1, 0); break;
            case 2: imp.ClaimAction.Target = new TilePos(0, 1); break;
            case 3: imp.ClaimAction.AnimationComplete = true; break;
            case 4: imp.ClaimAction.OwnsClip = true; break;
        }
        Assert.NotEqual(before, sim.StateHash());
    }

    private static GameSimulation Start(
        TileWorkKind kind, int phase = 0, ActionAnimationFlags flags = ActionAnimationFlags.Loop)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        var art = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        art[13] = new CreatureAnimationTiming
        {
            ResourceType = 6, Flags = CreatureAnimationFlags.Present,
            FrameCount = 11, FramesPerSecond = 20,
        };
        var db = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values,
            fixture.Creatures.Values.Select(c => c.CreatureId == 1 ? c with { ArtSlots = art } : c),
            fixture.Variables, objects: fixture.Objects.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, (byte)3, (byte)4]);
        if (kind == TileWorkKind.ConvertRoom)
        {
            for (int y = 2; y <= 4; y++)
                for (int x = 4; x <= 6; x++)
                    RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Lair), PlayerIds.Neutral);
        }
        else
        {
            ref TileState tile = ref world.Tile(Target);
            tile.TerrainId = kind == TileWorkKind.Claim ? Dk2TerrainFixture.DirtPath : Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = kind == TileWorkKind.Claim ? PlayerIds.Neutral : (byte)4;
            tile.Health = kind == TileWorkKind.Claim ? 1 : 660;
        }
        CreatureState imp = world.SpawnCreature(db, 1, 3, Target);
        var sim = new GameSimulation(db, world, 99, [new ImpTaskSystem()]);
        imp.TargetWork = kind;
        imp.TargetTileIndex = Target.Y * world.Width + Target.X;
        imp.Dk2StateId = ImpStates.For(kind).Work;
        imp.StateEnteredTick = -1;
        imp.TimerTicks = 2;
        imp.ActionArtSlot = 13;
        imp.ActionAnimationPhase = phase;
        imp.ActionAnimationRate = 1862;
        imp.ActionAnimationFlags = flags;
        return sim;
    }
}
