// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The wall-repair strike, action 8's handler (<c>0x00497AC0</c>, mode 3): DK2 applies it from
/// the common action poll that precedes the fight scan in the creature's update (<c>0x00488930</c> at
/// <c>0x00489A68</c>), not from state 24's handler (<c>0x00466710</c> only begins the action). So a
/// repairer the fight scan moves off the wall this turn has still struck this turn.</summary>
/// <remarks>The strike's cadence stays <see cref="WorkCadence"/>'s (first at arrival + 2, a gap,
/// then every tick), a fitted family default (WorkCadence) that the Level 5 capture fits (+3 a
/// tick from 2128) but does not prove; the clip itself is not modelled (repair clip length unread, see <see cref="ImpTaskSystem"/>).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/functions/00497AC0.md")]
internal static class RepairStrikeRules
{
    private static readonly TileWorkSystem Work = new();

    internal static void Poll(SimulationContext context, CreatureState creature)
    {
        if (creature.TargetWork != TileWorkKind.RepairWall || creature.TargetTileIndex < 0
            || creature.Dk2StateId != ImpStates.For(TileWorkKind.RepairWall).Work
            || context.Tick.Value <= creature.StateEnteredTick
            || !context.RepairStrikesPolledThisTick.Add(creature)) return;

        // Decrement first, then act on zero (ADR-0280).
        creature.TimerTicks--;
        if (creature.TimerTicks > 0) return;

        context.World.QueueWork(new TileWorkOrder(
            creature.TargetTileIndex, TileWorkKind.RepairWall, creature.OwnerPlayerId, creature.Id));
        creature.ActionHandlerDone = true;
        creature.TimerTicks = WorkCadence.AfterApplication(
            TileWorkKind.RepairWall, context.Tick.Value, creature.StateEnteredTick);
        Work.Execute(context);
    }
}
