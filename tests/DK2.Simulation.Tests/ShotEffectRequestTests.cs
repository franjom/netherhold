// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class ShotEffectRequestTests
{
    [Fact]
    public void AShotAlreadyDeadAtEntryDoesNotRequestADeathEffect()
    {
        var (sim, shot, events) = Setup();
        shot.Health = 0;
        sim.Tick();
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId == new EffectId(13));
        Assert.Empty(sim.World.Shots);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(251)]
    public void ATimedIdOfZeroSkipsTheDivision(byte delay)
    {
        var (sim, _, events) = Setup(timedId: 0, timedDelay: delay);
        sim.Run(2);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId == new EffectId(12));
    }

    [Fact]
    public void CreationFlag7AddsVelocityWithoutTyingTheCreationEffectToTheShot()
    {
        var (sim, _, events) = Setup(flags: 0x84);
        sim.Tick();
        ViewEffectRequest creation = Request(events, 11);
        Assert.Equal(new FpPosition3(new Fp32(13107), default, default), creation.CarriedVelocity);
        Assert.Equal(ShotId.None, creation.AttachedShotId);
        Assert.Equal(EffectVisibility.Gated, creation.Visibility);
    }

    [Fact]
    public void LifetimeDeathUsesThePositionAfterTheFinalMove()
    {
        var (sim, shot, events) = Setup();
        shot.Health = 1;
        sim.Tick();
        Assert.Equal(CreatureState.CentreOf(5), Request(events, 13).Position.X.Raw);
    }

    [Fact]
    public void AReturningOneInstantKillStillRequestsStandingHitDeathAndTimedEffects()
    {
        var (sim, shot, events) = Setup(timedDelay: 1, processType: 34);
        shot.TargetKind = FightTargetKind.Creature;
        shot.TargetId = 999;
        shot.VelX = shot.VelY = 0;
        sim.Tick();
        Assert.Equal(CreatureState.CentreOf(4), Request(events, 14).Position.X.Raw);
        Request(events, 13);
        Request(events, 12);
        Assert.Empty(sim.World.Shots);
    }

    [Fact]
    public void ARestoredShotUsesTheDefinedFloorAndVelocityHeadingFallbackInsteadOfAZeroPose()
    {
        var (sim, _, _) = Setup(timedDelay: 1);
        sim.Tick();
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database,
            [new PrepareNavigation(), new ShotSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var events = new ListEventSink();
        restored.ObserveWith(events);
        restored.Tick();
        ViewEffectRequest timed = Request(events, 12);
        Assert.Equal(new Fp32(Fp32.One), timed.Height);
        Assert.Equal(new Dk2Angle(512), timed.Heading);
    }

    [Fact]
    public void TrailUsesTheInitialHeadingAndLaunchVelocityWhileCreationUsesTheLaunchedHeading()
    {
        var (sim, shot, events) = Setup();
        sim.Tick();
        ViewEffectRequest trail = Request(events, 10);
        Assert.Equal(new Dk2Angle(256), trail.Heading);
        Assert.Equal(new ShotId(shot.Id), trail.AttachedShotId);
        Assert.Equal(EffectVisibility.Ungated, trail.Visibility);
        Assert.Equal(new FpPosition3(new Fp32(13107), new Fp32(0), new Fp32(0)), trail.CarriedVelocity);
        Assert.Equal(new Dk2Angle(512), Request(events, 11).Heading);
        Assert.Equal(new Fp32(3000), Request(events, 11).Height);
        Assert.Equal(new Fp32(CreatureState.CentreOf(4)), Request(events, 11).Position.X);
    }

    [Fact]
    public void TimedRequestsUseThePostMovePositionAndAimAtTheAuthoredCadence()
    {
        var (sim, shot, events) = Setup();
        sim.Run(3);
        ViewEffectRequest timed = Request(events, 12);
        Assert.Equal(CreatureState.CentreOf(6), timed.Position.X.Raw);
        Assert.Equal(new FpPosition3(new Fp32(shot.TargetX), new Fp32(shot.TargetY), new Fp32(3000)), timed.SecondPoint);
    }

    [Fact]
    public void AStandingShotRequestsHitSolidWithoutUsingDeadLiquidFields()
    {
        var (sim, shot, events) = Setup();
        shot.VelX = shot.VelY = 0;
        sim.Tick();
        Assert.Equal(shot.PosX, Request(events, 14).Position.X.Raw);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId is { Value: 90 or 91 });
    }

    [Fact]
    public void SolidKillsRequestTheStartOfTickHitAndSkipDeath()
    {
        var (sim, shot, events) = Setup(flags: 1);
        sim.World.Tile(5, 4).TerrainId = Dk2TerrainFixture.Rock;
        sim.Tick();
        Assert.Equal(CreatureState.CentreOf(4), Request(events, 14).Position.X.Raw);
        Assert.Empty(sim.World.Shots);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest?.EffectId == new EffectId(13));
    }

    [Fact]
    public void ACreatureHitAttachesTheHitEffectAndStillShowsDeath()
    {
        var (sim, shot, events) = Setup(flags: 2);
        CreatureState target = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, 4, new TilePos(4, 4));
        shot.TargetKind = FightTargetKind.Creature;
        shot.TargetId = target.Id;
        shot.ForcedCollision = true;
        sim.Tick();
        Assert.Equal(target.Id, Request(events, 15).AttachedCreatureId);
        Assert.Equal(shot.PosX, Request(events, 15).Position.X.Raw);
        Assert.Equal(shot.PosX, Request(events, 13).Position.X.Raw);
        Assert.Empty(sim.World.Shots);
    }

    [Fact]
    public void ObservationDoesNotChangeTheHashOrSimulationRandomState()
    {
        var (a, _, _) = Setup();
        var (b, _, _) = Setup(requestTrail: false);
        b.ObserveWith(NullEventSink.Instance);
        for (int tick = 0; tick < 4; tick++)
        {
            a.Tick(); b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
            Assert.Equal(a.Rng.SaveState(), b.Rng.SaveState());
        }
    }

    private static ViewEffectRequest Request(ListEventSink events, ushort id) =>
        Assert.Single(events.Events, e => e.EffectRequest?.EffectId == new EffectId(id)).EffectRequest!.Value;

    private static (GameSimulation Simulation, ShotState Shot, ListEventSink Events) Setup(
        uint flags = 4, ushort timedId = 12, byte timedDelay = 2, bool requestTrail = true, byte processType = 0)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        ShotDef definition = basis.Shots[Dk2TerrainFixture.FireballShotId] with
        {
            Flags = flags, Health = 4, Speed = 4096, CollideType = 0, DamageType = 0, ProcessType = processType,
            ShotProcessFlags = processType == 34 ? 2u : 0u, AirFriction = 0, GeneralEffectId = 10, CreationEffectId = 11,
            TimedEffectId = timedId, TimedDelay = timedDelay, DeathEffectId = 13, HitSolidEffectId = 14,
            HitThingEffectId = 15, HitLavaEffectId = 90, HitWaterEffectId = 91,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, shots: [definition]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 12), [(byte)0, (byte)1, (byte)2, (byte)3, (byte)4]);
        for (int y = 1; y < 11; y++)
            for (int x = 1; x < 19; x++) world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        ShotState shot = ShotRules.Spawn(world, definition, 3, CreatureState.CentreOf(4), CreatureState.CentreOf(4), 0, 0,
            0, FightTargetKind.None, -1, 0, -1, CreatureState.CentreOf(15), CreatureState.CentreOf(4));
        ShotRules.Launch(shot, definition, shot.TargetX, shot.TargetY);
        var sim = new GameSimulation(db, world, 1, [new InitialTrail(shot, requestTrail), new ShotSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, shot, events);
    }

    private sealed class InitialTrail(ShotState shot, bool requestTrail) : ISimulationSystem
    {
        public string Name => "test_trail";
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            if (requestTrail && shot.Age == 0) ShotRules.RequestTrail(context, shot, new Dk2Angle(256), new Fp32(3000));
        }
    }

    private sealed class PrepareNavigation : ISimulationSystem
    {
        public string Name => "test_navigation";
        public void Execute(SimulationContext context) => context.Navigation.Refresh();
    }
}
