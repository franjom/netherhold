// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Slapping a Horny (ADR-0776): <c>0x004918B0</c> admits it only while <c>+0x46F</c> bit 0 is
/// set, and <c>0x0048B8A0</c> sends it into its departure, state 242, with nothing else.</summary>
public sealed class HornySlapTests
{
    private const byte Keeper = PlayerIds.FirstKeeper, HornyType = 201;

    private static (GameDatabase Db, WorldState World, CreatureState Horny) Setup()
    {
        var (basis, world, at) = SummonHornyTests.Setup();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => (c.Flags & CreatureFlags.IsHorny) == CreatureFlags.IsHorny
                ? c with { Flags = c.Flags | CreatureFlags.CanBeSlapped } : c),
            basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values, shots: basis.Shots.Values);
        CreatureState horny = world.SpawnCreature(db, HornyType, Keeper, at);
        StatePlacement.Place(horny, Systems.CreatureJobSystem.HornyIdleState);
        return (db, world, horny);
    }

    [Fact]
    public void AnIdleHornySlappedDeparts_WithNoDamageAngerBonusOrCount_StronglyInferredFromBinary()
    {
        var (db, world, horny) = Setup();
        int health = horny.Health;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, seed: 7, [new HandSystem()]);
        sim.ObserveWith(sink);

        Assert.True(HandRules.CanSlap(db, horny, new PlayerId(Keeper)));
        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, horny.Id)));

        Assert.Equal(ReaperRules.DepartingState, horny.Dk2StateId);
        Assert.False(horny.ReaperCanAcquireFight);
        Assert.False(horny.ReaperIsAttackable);
        Assert.Equal(health, horny.Health);
        Assert.Equal(0, horny.Anger.Max);
        Assert.Equal(0, horny.WorkFasterTicks);
        Assert.Equal(0, world.Player(Keeper).SlapCount);
        Assert.Contains(sink.Events, e => e.Kind == "slap_dismissed");
        // Departing clears bit 0, so a second slap is refused.
        Assert.False(HandRules.CanSlap(db, horny, new PlayerId(Keeper)));
    }

    [Fact]
    public void AHornyWithItsFightBitClearOrInState76_CannotBeSlapped_StronglyInferredFromBinary()
    {
        var (db, _, horny) = Setup();
        horny.ReaperCanAcquireFight = false;
        Assert.False(HandRules.CanSlap(db, horny, new PlayerId(Keeper)));
        horny.ReaperCanAcquireFight = true;
        StatePlacement.Place(horny, 76);
        Assert.False(HandRules.CanSlap(db, horny, new PlayerId(Keeper)));
    }
}
