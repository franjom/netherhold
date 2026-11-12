// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Systems;

/// <summary>The travel states' handover (travel-states.md §5, Q724): states 6 (<c>0x00470C80</c>) and 9
/// through it stop the body (<c>Creature_StopMoving</c>) once the route's progress is exactly 1.0, which
/// counts the last point's distance in sixteenths of a tile; 7 (<c>0x00471460</c>) zeroes the velocity
/// then and stops on a room match (ours leaves the room check to its handler). 8 calls 6 only after its
/// route refresh, so <see cref="Combat.ApproachRules"/> makes that test itself and 8 is skipped here.
/// Not mirrored: 6's wait while a kind-4 action runs (<c>0x00470CA8</c>). That comes before Follow_Path's half-radius arrival for a small body, a walk with path flag bit 0,
/// or one still over the arrival speed. Ours stops the body here, after the fight scan and before the
/// dispatcher's needs routine and the state handlers, so every handler that waits for
/// <see cref="CreatureState.HasRoute"/> to clear hands over when DK2's would. An imp is left to its own
/// handler, which steps it and tests the progress itself after the mover (ADR-0297).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/travel-states.md")]
public sealed class TravelArrivalSystem : ICreatureUpdateStep
{
    private const int WaitingState = 88;
    private const int ImpTypeId = 1;
    private const int ApproachState = 8;

    public string Name => "travel arrival";

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState creature)
    {
        if (creature.CreatureTypeId == ImpTypeId) return;
        if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Sidelined)) return;
        if (!StateShadow.IsTravelling(creature) || creature.Dk2StateId is WaitingState or ApproachState) return;
        if (creature.Body.PathTotalCount == 0 || creature.Body.PathFinished
            || FollowPathRules.Progress(creature.Body) != Fixed16.One) return;
        creature.StopMoving();
    }
}
