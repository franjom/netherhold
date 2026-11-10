// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class PossessedMeleeAnimationTests
{
    private sealed class Commands(Action<SimulationContext> run) : ISimulationSystem
    {
        public string Name => "test commands";
        public void Execute(SimulationContext context) => run(context);
    }

    private static (GameSimulation Sim, CreatureState Imp) Setup(Action<SimulationContext, CreatureState> run, uint flags = 0x8000, int hitDelayRaw = 0,
        Action<SimulationContext, CreatureState>? after = null, bool possession = false)
    {
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        slots[5] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 15, FramesPerSecond = 20,
            Flags = (CreatureAnimationFlags)flags };
        CreatureDef impDef = Dk2TerrainFixture.Imp() with { ArtSlots = slots, MeleeHitDelay = new Fp32(hitDelayRaw) };
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == impDef.CreatureId ? impDef : c),
            basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        CreatureState imp = world.SpawnCreature(db, impDef.CreatureId, PlayerIds.FirstKeeper, new TilePos(5, 5));
        imp.Possessed = true;
        var systems = new List<ISimulationSystem> { new Commands(c => run(c, imp)), new CreatureUpdateSystem() };
        if (possession) systems.Insert(0, new PossessionSystem());
        if (after is not null) systems.Add(new Commands(c => after(c, imp)));
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(1)), systems.ToArray());
        ((Dk2Lcg)sim.Rng.Algorithm).Reseed(1);
        return (sim, imp);
    }

    private static void Begin(SimulationContext c, CreatureState imp) =>
        FightRules.BeginPossessedAction(c, imp, SpellRules.MeleeSwing, 0, FightTargetKind.None, CombatRules.NoTarget);

    [Fact]
    public void PossessedMeleeInstallsTheClipAndConsumesTheFc00CoinWithoutAnAlternateFlag()
    {
        var (sim, imp) = Setup((c, i) => { if (c.Tick.Value == 0) Begin(c, i); });
        sim.Tick(InputFrame.Empty);
        Assert.Equal(1015568748u, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Equal(5, imp.ActionArtSlot);
        Assert.Equal(1365, imp.ActionAnimationPhase);
        Assert.Equal(1365, imp.ActionAnimationRate);
    }

    [Fact]
    public void AnUnflaggedClipDoesNotDraw()
    {
        var (sim, _) = Setup((c, i) => { if (c.Tick.Value == 0) Begin(c, i); }, flags: 0);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(1u, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
    }

    [Fact]
    public void ReplacingMeleeWithMeleeKeepsTheRunningResourceAndPhase()
    {
        var (sim, imp) = Setup((c, i) => Begin(c, i));
        sim.Tick(InputFrame.Empty);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(1015568748u, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Equal(2730, imp.ActionAnimationPhase);
    }

    [Fact]
    public void ImpactDoesNotReleaseTheActionUntilTheFollowingPollSeesAWrap()
    {
        var (sim, imp) = Setup((c, i) => { if (c.Tick.Value == 0) Begin(c, i); });
        int[] phases = [1365, 2730, 4095, 1364];
        foreach (int phase in phases)
        {
            sim.Tick(InputFrame.Empty);
            Assert.Equal(phase, imp.ActionAnimationPhase);
            Assert.True(CreatureCurrentAction.IsRunning(imp, sim.CurrentTick));
            Assert.Equal(17, CreatureCurrentAction.IndexOf(imp, sim.Database.CreatureById(imp.CreatureTypeId), sim.CurrentTick));
        }
        sim.Tick(InputFrame.Empty);
        Assert.Equal(0, CreatureCurrentAction.IndexOf(imp, sim.Database.CreatureById(imp.CreatureTypeId), sim.CurrentTick));
        Assert.Equal(0, imp.ActionArtSlot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASharedReplacementWithTheSameArtIsNotClearedOrAdvancedByTheOldOwner(bool beforeImpact)
    {
        CreatureState? target = null;
        var (sim, imp) = Setup((c, i) =>
        {
            if (c.Tick.Value == 0)
            {
                Begin(c, i);
                i.SwingTargetKind = FightTargetKind.Creature;
                i.SwingTargetId = target!.Id;
                i.TimeLastUsedMelee = c.Tick.Value;
            }
            if (c.Tick.Value == (beforeImpact ? 0 : 1))
            {
                ActionAnimationClock.BeginCounted(i, c.Database.CreatureById(i.CreatureTypeId), 5, 1, c.Rng);
                Assert.False(i.SwingInProgress);
                Assert.False(FightRules.MeleeActionIsRunning(i, c.Database.CreatureById(i.CreatureTypeId), c.Tick));
            }
        });
        target = sim.World.SpawnCreature(sim.Database, imp.CreatureTypeId, PlayerIds.Good, new TilePos(6, 5));
        sim.Tick(InputFrame.Empty);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(5, imp.ActionArtSlot);
        Assert.Equal(beforeImpact ? 0 : 1365, imp.ActionAnimationPhase);
        Assert.Equal(2, imp.ActionCount);
    }

    [Fact]
    public void FrozenAnimationDoesNotAdvance()
    {
        var (sim, imp) = Setup((c, i) =>
        {
            if (c.Tick.Value == 0) Begin(c, i);
            else i.FrozenTicks = 10;
        });
        sim.Tick(InputFrame.Empty);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(1365, imp.ActionAnimationPhase);
        Assert.True(imp.PossessedMelee.Active);
    }

    [Fact]
    public void AnimationCompletionIsStickyWhileTheHandlerStillWaits()
    {
        var (sim, imp) = Setup((c, i) =>
        {
            if (c.Tick.Value == 0)
            {
                Begin(c, i);
                i.Possessed = false;
                i.SwingStartTick = 100;
            }
            if (c.Tick.Value == 5) i.SwingStartTick = -100;
        });
        for (int i = 0; i < 5; i++) sim.Tick(InputFrame.Empty);
        Assert.True(imp.PossessedMelee.Active);
        Assert.True(imp.PossessedMelee.AnimationComplete);
        Assert.True(imp.SwingInProgress);
        sim.Tick(InputFrame.Empty);
        Assert.False(imp.PossessedMelee.Active);
        Assert.False(imp.SwingInProgress);
        Assert.Equal(0, imp.ActionArtSlot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryCompletionFieldAffectsTheHashAndSurvivesSaveAndCopy(int field)
    {
        var (sim, imp) = Setup((_, _) => { });
        ulong before = sim.StateHash();
        var state = new PossessedMeleeState { Active = field == 0, AnimationComplete = field == 1, OwnsClip = field == 2 };
        imp.PossessedMelee = state;
        Assert.NotEqual(before, sim.StateHash());
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database);
        Assert.Equal(state, restored.World.CreatureById(imp.Id)!.PossessedMelee);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var (copy, _) = Setup((_, _) => { });
        copy.World.CopyFrom(sim.World);
        Assert.Equal(state, copy.World.CreatureById(imp.Id)!.PossessedMelee);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOrdinaryReplacementRetainsItsOwnBusyGate(bool zeroWindup)
    {
        var (sim, _) = Setup((c, i) =>
        {
            if (c.Tick.Value == 0) Begin(c, i);
            if (c.Tick.Value == 1)
            {
                i.Possessed = false;
                i.FightTargetKind = FightTargetKind.Creature;
                i.FightTargetId = 999;
                FightRules.StartSwing(c, new WorldSight(c.Navigation, c.World, c.Database), i,
                    c.Database.CreatureById(i.CreatureTypeId), []);
                Assert.True(FightRules.MeleeActionIsRunning(i, c.Database.CreatureById(i.CreatureTypeId), c.Tick));
            }
        }, hitDelayRaw: zeroWindup ? 0 : 4 * Fp32.One);
        sim.Tick(InputFrame.Empty);
        sim.Tick(InputFrame.Empty);
    }
    [Fact]
    public void AnUntimedPossessedClaimReplacesTheMeleeTail()
    {
        var (sim, imp) = Setup((c, i) => { if (c.Tick.Value == 0) Begin(c, i); }, possession: true);
        for (int y = 4; y <= 6; y++)
        for (int x = 4; x <= 6; x++)
        {
            sim.World.Tile(x, y).TerrainId = x == 5 && y == 5 ? Dk2TerrainFixture.DirtPath : Dk2TerrainFixture.ClaimedPath;
            sim.World.Tile(x, y).OwnerPlayerId = x == 5 && y == 5 ? PlayerIds.Neutral : PlayerIds.FirstKeeper;
        }
        sim.World.Player(PlayerIds.FirstKeeper).PossessedCreatureId = imp.Id;
        sim.Tick(InputFrame.Empty);
        Assert.True(imp.PossessedMelee.Active);
        imp.TimeLastUsedMelee = -100;

        sim.Tick(InputFrame.Of(new ActPossessedCreatureCommand(PlayerIds.FirstKeeper,
            new(new(new(imp.PosX), new(imp.PosY), new(imp.PosZ)), new(0x200), new(0x200)), true, new(0))));

        Assert.NotEqual(-1, imp.ClaimActionTile);
        Assert.False(imp.PossessedMelee.Active);
        Assert.False(imp.PossessedMelee.OwnsClip);
        Assert.NotEqual(17, CreatureCurrentAction.IndexOf(imp, sim.Database.CreatureById(imp.CreatureTypeId), sim.CurrentTick));
    }

    [Fact]
    public void CompletionDoesNotResurrectTheLegacyHoldWithinTheSameTurn()
    {
        CreatureState? target = null;
        var (sim, imp) = Setup((c, i) =>
        {
            if (c.Tick.Value != 0) return;
            Begin(c, i);
            i.SwingTargetKind = FightTargetKind.Creature;
            i.SwingTargetId = target!.Id;
            i.TimeLastUsedMelee = c.Tick.Value;
        }, after: (c, i) =>
        {
            if (c.Tick.Value != 4) return;
            Assert.False(CreatureCurrentAction.IsRunning(i, c.Tick));
            Assert.Equal(0, CreatureCurrentAction.IndexOf(i, c.Database.CreatureById(i.CreatureTypeId), c.Tick));
            Assert.False(FightRules.MeleeActionIsRunning(i, c.Database.CreatureById(i.CreatureTypeId), c.Tick));
        });
        target = sim.World.SpawnCreature(sim.Database, imp.CreatureTypeId, PlayerIds.Good, new TilePos(6, 5));
        sim.Run(5);
    }
}
