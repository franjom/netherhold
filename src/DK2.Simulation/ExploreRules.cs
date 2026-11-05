// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>Job 24, Explore, and its state 225 (ADR-0674). The arm (<c>0x0045A290</c>, Job_Dispatch case
/// 0x18) makes up to 20 map-wide tries, <c>random(width)</c> then <c>random(height)</c>, for a tile that is
/// in bounds, not solid, neutral-owned and reachable (word 0x80); it sets state word <c>+0xA7</c> to 10 and
/// walks there resuming 225. State 225 (<c>0x0045A3E0</c>) spends one step and makes up to 5 tries of
/// <c>random(5) - 2</c> per axis for an unowned tile, walking on; none, or no steps, idles the creature.
/// Only a Firefly reaches it here: the Imp's table also holds Explore, but its jobs are its worker
/// system's. Reachability rests on <c>Creature_CanRouteTo</c>'s return, a HYPOTHESIS record.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/neutral-ambient-effect.md")]
public static class ExploreRules
{
    /// <summary>State 225, exploring.</summary>
    public const int ExploringState = 225;

    /// <summary>The arm's tries (<c>0x0045A2A0</c>: 0x14).</summary>
    public const int ArmTries = 20;

    /// <summary>State 225's tries per step.</summary>
    public const int StepTries = 5;

    /// <summary>The steps the arm grants, state word <c>+0xA7</c>.</summary>
    public const int Steps = 10;

    private const Navigation.MoveOptions RouteOptions = (Navigation.MoveOptions)0x80;

    /// <summary>The arm, <c>0x0045A290</c>. False when no try passed or the walk could not start.</summary>
    public static bool Arm(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        context.Navigation.Refresh();   // DK2's grid is current when the arm reads it
        var scratch = new List<Fixed16Vector>();
        for (int i = 0; i < ArmTries; i++)
        {
            int x = context.Rng.NextInt(world.Width);
            int y = context.Rng.NextInt(world.Height);
            if (!Accept(context, creature, x, y, PlayerIds.Neutral, scratch)) continue;
            creature.ExploreStepsLeft = Steps;
            return Walk(context, creature, x, y);
        }
        return false;
    }

    /// <summary>State 225's handler, <c>0x0045A3E0</c>. False sends the creature to idle.</summary>
    public static bool Step(SimulationContext context, CreatureState creature)
    {
        if (creature.ExploreStepsLeft == 0) return false;
        creature.ExploreStepsLeft--;
        context.Navigation.Refresh();
        TilePos at = creature.Tile;
        var scratch = new List<Fixed16Vector>();
        for (int i = 0; i < StepTries; i++)
        {
            int x = context.Rng.NextInt(5) + at.X - 2;
            int y = context.Rng.NextInt(5) + at.Y - 2;
            if (Accept(context, creature, x, y, PlayerIds.Unowned, scratch)) return Walk(context, creature, x, y);
        }
        return false;
    }

    /// <summary>A keeper creature's turn in 225 or on its way there. True when it was one.</summary>
    public static bool KeeperTurn(SimulationContext context, string system, CreatureState creature)
    {
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.UtilityNewState != ExploringState) return false;
            if (creature.HasRoute) return true;
            creature.StopMoving();
            StateShadow.Arrive(context, system, creature);
            return true;
        }
        if (creature.Dk2StateId != ExploringState) return false;
        if (!Step(context, creature))
        {
            creature.CurrentJob = JobType.None;
            StateShadow.ToIdle(context, system, creature);
        }
        return true;
    }

    private static bool Accept(SimulationContext context, CreatureState creature, int x, int y, byte owner, List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        if (!world.Contains(new TilePos(x, y))) return false;                    // slot +0x1FC
        if ((context.Navigation.NavByte(x, y) & NavBytes.Solid) != 0) return false;   // slot +0x208
        if (world.Tile(new TilePos(x, y)).OwnerPlayerId != owner) return false;
        return Combat.CombatTargetRules.CanReach(context.Navigation, creature, context.Database.CreatureById(creature.CreatureTypeId),
            CreatureState.CentreOf(x), CreatureState.CentreOf(y), int.MaxValue, scratch, RouteOptions);
    }

    /// <summary><c>Travel_RouteToPoint(point, 225, 0x80)</c> to the tile's centre.</summary>
    private static bool Walk(SimulationContext context, CreatureState creature, int x, int y)
    {
        if (!context.Navigation.TryRouteTo(creature, CreatureState.CentreOf(x), CreatureState.CentreOf(y), RouteOptions))
            return false;
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, ExploringState);
        return true;
    }
}
