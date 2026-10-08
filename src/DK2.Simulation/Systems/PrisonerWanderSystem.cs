// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A prisoner shuffles about its cell — one tile at a time, orthogonally,
/// never out of the room, paced by a 20-in-100 roll per tick. CONFIRMED (owner, Q172),
/// ADR-0073; read out of DK2's states 114-116 (docs/research/prison.md §6).</summary>
/// <remarks>Those states are collapsed into one per-tick rule with the same observable
/// behaviour: a standing prisoner rolls
/// <see cref="PrisonRules.WanderPercentPerTick"/>, and on a hit steps by
/// <see cref="PrisonRules.TryWanderStep"/> — the hundred first, the direction only if
/// it moves, so the random stream is consumed the same way round. Nothing stops a
/// prisoner leaving its cell because nothing offers a tile outside one, so
/// <see cref="CreatureMovementSystem"/> carries no guard. A creature whose Angry animation
/// carries flag bit 0 plays it (action 41) on rolls 0..9 and stands while it runs (Q188); the
/// look-at turns are not modelled. Runs every tick, after <see cref="PrisonSystem"/>.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison.md")]
public sealed class PrisonerWanderSystem : ISimulationSystem
{
    private const string SystemName = "PrisonerWander";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;

        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.Possessed) continue;
            if (!PrisonRules.IsImprisoned(creature)) continue;
            if (TortureRules.IsBeingTortured(creature)) continue;   // on a device, not in a cell
            if (JailBreakRules.IsEscaping(creature)) continue;      // walking out, in 121 (ADR-0229)

            // A Prison rebuilt since the claim sends its captives to 126, which waits a turn and
            // goes back in (0x004E2B30, ADR-0400).
            if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Held)) continue;
            if (SimulationTiming.OnOriginalTick(context.Tick.Value) && RoomEvictRules.Evict(context, SystemName, creature, RoomTypeId.Prison)) continue;
            if (SlapRules.IsReaction(creature.Dk2StateId)) continue;   // 120's reaction (ADR-0401)
            if (creature.Dk2StateId == PrisonRules.ReturningState)
            {
                if (SimulationTiming.OnOriginalTick(context.Tick.Value)) PrisonRules.Return(context, SystemName, creature);
                continue;
            }

            // In the keeper's hand it is out of the dungeon (ADR-0062); down,
            // it is not walking anywhere.
            if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Held | CreatureGuards.Down)) continue;

            // Already walking. The original rolls in state 116 only once the
            // walk it was given has finished, and this is that condition.
            if (creature.HasRoute) continue;

            // On its way to a chicken, or standing over one. That is states 117
            // and 118 rather than 116, and they do not roll for a step: the
            // original's decision state asks about hunger FIRST and wanders
            // only when the answer is no (PrisonerEatSystem, ADR-0074).
            if (creature.TargetObjectId >= 0) continue;

            // Searching again in 117, or finishing a meal in 119: neither rolls for a step.
            if (creature.Dk2StateId is PrisonRules.SeekingFoodState or PrisonRules.AfterEatingState) continue;

            // 116 rolls only once no action runs (0x00496D00): a flourish holds it.
            long now = context.Tick.Value;
            if (now < creature.ActionUntilTick) continue;

            // HasArt(9) draws nothing; the hundred comes first, then the flourish's rand(3).
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            int roll = context.Rng.NextInt(100);
            if (PrisonRules.HasAngryFlourish(definition) && roll < PrisonRules.FlourishPercent)
            {
                int variant = 2 + context.Rng.NextInt(3);
                // Released the tick after the animation ends, as action 49 is (portal-creature-action.md).
                creature.ActionUntilTick = now + PrisonRules.FlourishTicks(definition, variant) + 1;
                context.Emit(SystemName, "prisoner_flourish",
                    $"creature={creature.Id} player={creature.OwnerPlayerId} variant={variant} " +
                    $"until={creature.ActionUntilTick}");
                continue;
            }

            if (roll >= PrisonRules.WanderPercentPerTick) continue;

            int start = context.Rng.NextInt(PrisonRules.WanderDirections);
            if (!PrisonRules.TryWanderStep(
                    world, context.Database, creature, start, out TilePos step))
            {
                // A one-tile cell, or a prisoner boxed in by its own room's
                // shape. It stands still and rolls again next tick, which is
                // what the original does when its scan comes back empty.
                context.Emit(SystemName, "nowhere_to_step",
                    $"creature={creature.Id} player={creature.OwnerPlayerId} " +
                    $"at=({creature.Tile.X},{creature.Tile.Y}) start={start}");
                continue;
            }

            if (!context.Navigation.TryRoute(creature, step, Navigation.MoveOptions.Loose)) continue;

            // 116 back to 115, which starts a walk in 6 resuming in 116 and then sets 116 itself
            // without waiting (0x004731F0): the prisoner walks in 116, holding resume 116 and
            // saved 115, which is what DK2's world log shows of every placed prisoner (Q422).
            if (CreatureStateMachine.HasState(creature))
            {
                if (creature.Dk2StateId == PrisonRules.InTheCellState)
                    StateShadow.Move(context, SystemName, creature, PrisonRules.DecidingState);
                CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, PrisonRules.InTheCellState);
                StateShadow.Move(context, SystemName, creature, PrisonRules.InTheCellState);
            }

            context.Emit(SystemName, "prisoner_stepped",
                $"creature={creature.Id} player={creature.OwnerPlayerId} " +
                $"from=({creature.Tile.X},{creature.Tile.Y}) " +
                $"to=({step.X},{step.Y}) start={start}");
        }
    }
}
