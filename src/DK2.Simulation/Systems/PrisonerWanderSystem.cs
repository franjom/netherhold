// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

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
/// <see cref="CreatureMovementSystem"/> carries no guard. Not modelled: state 116's
/// cosmetic flourishes; the narrower 10-in-100 step roll is Q188. Runs every tick,
/// after <see cref="PrisonSystem"/>.</remarks>
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
            if (!PrisonRules.IsImprisoned(creature)) continue;

            // In the keeper's hand it is out of the dungeon (ADR-0062); down,
            // it is not walking anywhere.
            if (HandRules.IsHeld(creature)) continue;
            if (CreatureDeathRules.IsDown(creature)) continue;

            // Already walking. The original rolls in state 116 only once the
            // walk it was given has finished, and this is that condition.
            if (creature.HasRoute) continue;

            // On its way to a chicken, or standing over one. That is states 117
            // and 118 rather than 116, and they do not roll for a step: the
            // original's decision state asks about hunger FIRST and wanders
            // only when the answer is no (PrisonerEatSystem, ADR-0074).
            if (creature.TargetObjectId >= 0) continue;

            if (context.Rng.NextInt(100) >= PrisonRules.WanderPercentPerTick) continue;

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

            context.Emit(SystemName, "prisoner_stepped",
                $"creature={creature.Id} player={creature.OwnerPlayerId} " +
                $"from=({creature.Tile.X},{creature.Tile.Y}) " +
                $"to=({step.X},{step.Y}) start={start}");
        }
    }
}
