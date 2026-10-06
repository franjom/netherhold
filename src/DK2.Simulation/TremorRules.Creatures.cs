// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

public static partial class TremorRules
{
    public const int KnockdownState = 210;
    public const int WaitingState = 263;
    public const int GettingUpState = 264;
    private const string CreatureSystem = "tremor-creatures";

    public static bool IsRecovering(CreatureState creature) =>
        creature.Dk2StateId is KnockdownState or WaitingState or GettingUpState;

    public static void RunRecovery(SimulationContext context)
    {
        context.TremorRecoveryCompletedThisTick.Clear();
        foreach (CreatureState creature in context.World.Creatures)
        {
            if (creature.Possessed) continue;
            if (!IsRecovering(creature)) continue;
            creature.TremorRoutePaused = true;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            switch (creature.Dk2StateId)
            {
                case KnockdownState:
                    creature.ActionUntilTick = context.Tick.Value + AnimationTimingRules.KnockdownHoldTicks(definition);
                    CreatureStateMachine.TrySetState(context.Hooks, creature, WaitingState, context.Tick.Value);
                    break;
                case WaitingState:
                    if (context.Tick.Value < creature.ActionUntilTick) break;
                    if (creature.ShouldDelayGetUp)
                    {
                        creature.ShouldDelayGetUp = false;
                        break;
                    }
                    creature.ActionUntilTick = context.Tick.Value + AnimationTimingRules.GetUpHoldTicks(definition);
                    if (!CreatureStateMachine.TrySetState(context.Hooks, creature, GettingUpState, context.Tick.Value))
                        FinishRecovery(context, creature);
                    break;
                case GettingUpState:
                    if (context.Tick.Value >= creature.ActionUntilTick) FinishRecovery(context, creature);
                    break;
            }
        }
    }

    private static void FinishRecovery(SimulationContext context, CreatureState creature)
    {
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        context.TremorRecoveryCompletedThisTick.Add(creature);
    }

    public static bool Respond(SimulationContext context, CreatureState creature, TilePos centre, Fp32 radius)
    {
        if (!CombatRules.IsAttackable(creature) || creature.PrisonOwnerPlayerId != PrisonRules.NoPrison) return false;
        // Unmapped legacy activities cannot satisfy the original state-table gates.
        if (!CreatureStateMachine.HasState(creature)) return false;
        CreatureStateDef state = CreatureStateTable.Get(creature.Dk2StateId);
        if (state.Category == CreatureStateCategory.Movement || state.Group == 1) return false;
        if (!creature.Fleeing)
        {
            if (IsRecovering(creature)) return true;
        }
        else if (context.Rng.NextInt(100) < 5)
        {
            creature.Fleeing = false;
            if (CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value))
            {
                CreatureStateMachine.TrySetState(context.Hooks, creature, KnockdownState, context.Tick.Value);
                return true;
            }
        }
        context.Navigation.Refresh();
        TilePos position = creature.Tile;
        int lowX, lowY, highX, highY;
        unchecked
        {
            int dx = position.X - centre.X, dy = position.Y - centre.Y;
            int distance = Math.Max(1, IntegerMath.SquareRoot((uint)(dx * dx + dy * dy)));
            int q = (dx << 12) / distance;
            int qq = (q << 12) / distance;
            int rawX = centre.X - 2 + (int)((uint)(q * (radius.Raw + 8192)) >> 12);
            int rawY = centre.Y - 2 + (int)((uint)(qq * (radius.Raw + 8192)) >> 12);
            lowX = Clamp(rawX, context.World.Width);
            highX = Clamp(rawX + 5, context.World.Width);
            lowY = Clamp(rawY, context.World.Height);
            highY = Clamp(rawY + 5, context.World.Height);
        }
        bool hasOwnedFloor = false;
        for (int y = lowY; y <= highY && !hasOwnedFloor; y++)
            for (int x = lowX; x < highX; x++)
                if (OwnedFloor(context, creature, new TilePos(x, y))) { hasOwnedFloor = true; break; }
        if (hasOwnedFloor)
        {
            int rejected = 0;
            while (rejected < 25)
            {
                var at = new TilePos(lowX + context.Rng.NextInt(5), lowY + context.Rng.NextInt(5));
                if (!context.World.Contains(at)) continue;
                if (OwnedFloor(context, creature, at)
                    && Route(context, creature, new EscapePoint(new Fp32(CreatureState.CentreOf(at.X)),
                        new Fp32(CreatureState.CentreOf(at.Y)))))
                {
                    creature.Fleeing = true;
                    return true;
                }
                rejected++;
            }
            return false;
        }
        int awayX = centre.X - position.X, awayY = centre.Y - position.Y;
        if (awayX == 0) awayX = context.Rng.NextInt(2) == 0 ? -1 : 1;
        if (awayY == 0) awayY = context.Rng.NextInt(2) == 0 ? -1 : 1;
        var sight = new WorldSight(context.Navigation, context.World, context.Database);
        for (int attempt = 0; attempt < 25; attempt++)
        {
            int stepX = context.Rng.NextInt(Math.Abs(unchecked(awayX * 4))) << 12;
            int stepY = context.Rng.NextInt(Math.Abs(unchecked(awayY * 4))) << 12;
            if (awayX > 0) stepX = unchecked(-stepX);
            if (awayY > 0) stepY = unchecked(-stepY);
            var target = new EscapePoint(new Fp32(unchecked(creature.PosX + stepX)),
                new Fp32(unchecked(creature.PosY + stepY)));
            if (context.World.Contains(target.Tile)
                && LineOfSight.Clear(sight, creature.PosX, creature.PosY, target.X.Raw, target.Y.Raw)
                && Route(context, creature, target))
            {
                creature.Fleeing = true;
                return true;
            }
        }
        return false;
    }

    private static int Clamp(int value, int size) => Math.Min(Math.Max(value, 0), size - 1);

    private static bool OwnedFloor(SimulationContext context, CreatureState creature, TilePos at) =>
        !NavBytes.IsSolid(context.Navigation.NavByte(at.X, at.Y))
        && context.World.Tile(at).OwnerPlayerId == creature.OwnerPlayerId;

    private static bool Route(SimulationContext context, CreatureState creature, EscapePoint target) =>
        TunnelRules.RouteOrTunnel(context, CreatureSystem, creature, x: target.X.Raw, y: target.Y.Raw,
            travelState: StateShadow.TravelToPoint, resume: CreatureStateMachine.Idle, options: Navigation.MoveOptions.Loose,
            tunnelX: target.Tile.X, tunnelY: target.Tile.Y);

    private readonly record struct EscapePoint(Fp32 X, Fp32 Y)
    {
        public TilePos Tile => new(X.Raw >> 12, Y.Raw >> 12);
    }
}
