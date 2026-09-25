// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A guard in its keeper's Guard Room: one turn of states 97, 98 and 99 and the walks
/// between them (ADR-0212, <c>guard-room.md</c>).</summary>
/// <remarks>97 walks to the nearest post, 98 walks the room's edge one straight leg at a time,
/// 99 waits a turn after a failed leg and starts again. Not built: the Guard Post a patrol
/// visits at its deadline (state 100), since traps are not built, so the deadline only
/// resets; the <c>+0x3E6</c> bits 22–25; the failed leg's sound.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/guard-room.md")]
public static class GuardStep
{
    /// <summary>One original turn. Returns false when the guard drops to idle, which ends the
    /// job.</summary>
    public static bool Turn(SimulationContext context, string system, CreatureState creature)
    {
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return true;
            creature.StopMoving();
            StateShadow.Arrive(context, system, creature);
        }

        return creature.Dk2StateId switch
        {
            GuardRules.ToThePostState => ToThePost(context, system, creature),
            GuardRules.PatrolState => Patrol(context, system, creature),
            GuardRules.LostState => Lost(context, system, creature),
            _ => false,
        };
    }

    private static bool ToThePost(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        TilePos at = creature.Tile;
        if (!GuardRules.IsOwnGuardRoom(world, database, at.X, at.Y, creature.OwnerPlayerId)) return false;

        TilePos post = GuardRules.NearestPost(world, database, at.X, at.Y, creature.OwnerPlayerId);
        int slot = GuardRules.Slot(world, database, post.X, post.Y, creature.OwnerPlayerId, out int facing);
        if (slot != GuardRules.NoSlot) creature.GuardFacing = facing;
        creature.GuardDeadlineTick = context.Tick.Value + 75 + Random(context, VariableTypeId.MaxTimeInGuardRoomBeforeWanderingToGuardPostSeconds);

        if (GuardRules.PostPoint(post, slot) is not (int x, int y) || !Walk(context, creature, x, y)) return false;
        context.Emit(system, "guard_to_post", $"creature={creature.Id} post=({post.X},{post.Y}) slot={slot} facing={creature.GuardFacing}");
        return true;
    }

    private static bool Patrol(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        // Past the deadline 0x00463190 looks for a Guard Post in the room; there are no traps.
        if (context.Tick.Value > creature.GuardDeadlineTick)
            creature.GuardDeadlineTick = context.Tick.Value + 75 + Random(context, VariableTypeId.ImpIdleDelayBeforeReevaluationSeconds);

        TilePos at = creature.Tile;
        if (!GuardRules.IsOwnGuardRoom(world, database, at.X, at.Y, creature.OwnerPlayerId)) return false;

        (TilePos to, int facing) = GuardRules.Leg(world, database, at.X, at.Y, creature.GuardFacing, creature.OwnerPlayerId);
        creature.GuardFacing = facing;
        (int x, int y) = GuardRules.LegPoint(to, facing);
        if (Walk(context, creature, x, y))
        {
            context.Emit(system, "guard_leg", $"creature={creature.Id} to=({to.X},{to.Y}) facing={facing}");
            return true;
        }

        StateShadow.Move(context, system, creature, GuardRules.LostState);
        creature.GuardLostTick = context.Tick.Value;
        return true;
    }

    /// <summary>99: <c>Age</c> at 1 or less holds; the dispatcher counts it up before the
    /// handler, so the first act is two turns after entry.</summary>
    private static bool Lost(SimulationContext context, string system, CreatureState creature)
    {
        if (context.Tick.Value - creature.GuardLostTick <= 1) return true;
        TilePos at = creature.Tile;
        if (!GuardRules.IsOwnGuardRoom(context.World, context.Database, at.X, at.Y, creature.OwnerPlayerId)) return false;
        StateShadow.Move(context, system, creature, GuardRules.ToThePostState);
        return true;
    }

    /// <summary><c>0x00471890(&amp;point, 98, 0x80)</c>: state 6 resuming 98. A point off the map
    /// is a failed route.</summary>
    private static bool Walk(SimulationContext context, CreatureState creature, int x, int y)
    {
        if (!context.World.Contains(new TilePos(x / Fp32.One, y / Fp32.One))) return false;
        if (!context.Navigation.TryRouteTo(creature, x, y, Navigation.MoveOptions.Loose)) return false;
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, GuardRules.PatrolState);
        return true;
    }

    /// <summary><c>random(v)</c> with the variable's raw value, which the loader stores unscaled.</summary>
    private static int Random(SimulationContext context, VariableTypeId variable)
    {
        int bound = context.Database[variable];
        return bound > 0 ? context.Rng.NextInt(bound) : 0;
    }
}
