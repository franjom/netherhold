// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class ThunderboltStunTests
{
    private static readonly PlayerId Keeper = PlayerIds.FirstKeeper;

    [Theory]
    [InlineData(116, false, 1200)]
    [InlineData(138, true, 1454)]
    [InlineData(199, true, 1454)]
    [InlineData(1, false, 1200)]
    public void ACaptiveBindingPreventsLightningStunAfterDamage(int state, bool pitFighter, int health)
    {
        var (sim, target, sink) = Start(Dk2TerrainFixture.Goblin());
        StatePlacement.Place(target, state);
        target.PrisonOwnerPlayerId = Keeper.Value;
        target.CombatPitFighter = pitFighter;
        Fire(sim, target);

        sim.Tick();

        Assert.Equal(health, target.Health);
        Assert.Equal(state, target.Dk2StateId);
        Assert.Equal(0, target.StunTicks);
        Assert.Equal(Keeper.Value, target.PrisonOwnerPlayerId);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "stunned");
    }

    [Theory]
    [InlineData(0, 257, 0)]
    [InlineData(1, 257, 0)]
    [InlineData(2, 255, 1)]
    public void LightningRequestsDazedOnlyWhileTheFirstStunHandlerStillHolds(int durationTicks, int state, int effects)
    {
        CreatureDef definition = Dk2TerrainFixture.Goblin() with
        {
            CreatureId = 60,
            StunDuration = Fp32.FromDouble(durationTicks / 4.0),
        };
        var (sim, target, sink) = Start(definition);
        Fire(sim, target);
        sim.Tick();
        Assert.Equal(255, target.Dk2StateId);
        Assert.Equal(durationTicks, target.StunTicks);

        sim.Tick();

        Assert.Equal(state, target.Dk2StateId);
        Assert.Equal(effects, sink.Events.Count(e => e.EffectRequest?.EffectId.Value == 101));
    }

    private static void Fire(GameSimulation sim, CreatureState target) =>
        ShotRules.Spawn(sim.World, sim.Database.Shots[Dk2TerrainFixture.LightningShotId], Keeper,
            target.PosX, target.PosY, 300, 0, 0, FightTargetKind.Creature, target.Id,
            0, -1, target.PosX, target.PosY);

    private static (GameSimulation Sim, CreatureState Target, ListEventSink Sink) Start(CreatureDef definition)
    {
        GameDatabase database = definition.CreatureId == Dk2TerrainFixture.GoblinCreatureId
            ? Dk2TerrainFixture.Database() : Dk2TerrainFixture.Database(definition);
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, Keeper.Value, (byte)(Keeper.Value + 1)]);
        CreatureState target = world.SpawnCreature(database, definition.CreatureId,
            new PlayerId((byte)(Keeper.Value + 1)), new TilePos(3, 3));
        var sim = new GameSimulation(database, world, 42, [new CreatureStatusSystem(), new ShotSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, target, sink);
    }
}
