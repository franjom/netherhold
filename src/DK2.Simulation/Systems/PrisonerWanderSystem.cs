// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>
/// A prisoner shuffles about its cell — one tile at a time, orthogonally, and
/// never out of the room (Q172).
///
/// <para><b>The original's loop, read state by state.</b> DK2 gives a prisoner
/// three states and they form a cycle:</para>
/// <list type="number">
/// <item><b>114</b> (<c>0x72</c>) — arrival. Finds the room of type <c>0xb</c>,
/// checks it with the same tile predicate the prison's capacity uses, records
/// the keeper whose prison it is, and enters 115.</item>
/// <item><b>115</b> (<c>0x73</c>) — the decision. If hunger clears a threshold
/// <i>and</i> a cooldown has elapsed it goes to state <b>117</b>, which is the
/// hunt for a chicken and not a meal on the spot
/// (<see cref="PrisonerEatSystem"/>, ADR-0074). Otherwise it asks
/// <c>Room.cpp:1717</c> for a neighbouring tile of the same room, issues a move
/// to it, and enters 116.</item>
/// <item><b>116</b> (<c>0x74</c>) — the walk, and the pause after it. Each tick
/// it rolls <c>random(100)</c> and returns to 115 when the roll is under 20.
/// </item>
/// </list>
///
/// <para><b>Collapsed into one per-tick rule here</b>, which gives the same
/// observable behaviour with none of the state machine: a prisoner that is
/// standing still rolls <see cref="PrisonRules.WanderPercentPerTick"/>, and on
/// a hit takes one step chosen by
/// <see cref="PrisonRules.TryWanderStep"/>. The two draws happen in the
/// original's order — the hundred first, the direction only if it moves — so
/// the random stream is consumed the same way round.</para>
///
/// <para><b>Why this is not a movement rule.</b> Nothing here stops a prisoner
/// leaving its cell, because nothing ever offers it a tile outside one: the
/// original's scan tests candidate tiles against the room instance the creature
/// is standing in. That is a better guarantee than a guard in the mover, which
/// is why <see cref="CreatureMovementSystem"/> no longer carries one.</para>
///
/// <para><b>What is deliberately not modelled.</b> The original's 116 also
/// plays idle flourishes on a second roll, and turns to look at something in
/// the room on a ten-tick cadence — both cosmetic, both gated on an animation
/// slot (<c>creature+0x3a9</c>) that this project has no counterpart for. The
/// narrower 10-in-100 variant of the step roll is Q188.</para>
///
/// <para>Runs every tick, after <see cref="PrisonSystem"/>: a prisoner freed or
/// turned to bone this second should not be handed a step it will never
/// take.</para>
/// </summary>
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
            if (creature.PathIndex < creature.Path.Count) continue;

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

            creature.Path.Clear();
            creature.Path.Add(step);
            creature.PathIndex = 0;

            context.Emit(SystemName, "prisoner_stepped",
                $"creature={creature.Id} player={creature.OwnerPlayerId} " +
                $"from=({creature.Tile.X},{creature.Tile.Y}) " +
                $"to=({step.X},{step.Y}) start={start}");
        }
    }
}
