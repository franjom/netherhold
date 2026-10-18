// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The stationary, non-flying Create Imp subset of state 214 and the physical pass.
/// Heights use the simulation's zero floor; see imp-create-position.md.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-create-position.md")]
public static class ImpCreationRules
{
    public const int FallingState = 214;
    public const int BirthHeightAboveFloor = 0x800;

    public static void Begin(SimulationContext context, CreatureState creature)
    {
        creature.PosZ = creature.LastPosZ = BirthHeightAboveFloor;
        creature.CreationVelocityZ = 0;
        creature.JustDropped = false;
        StateShadow.Move(context, "ImpCreation", creature, FallingState);
    }

    internal static void Step(SimulationContext context, CreatureState creature)
    {
        creature.LastPosZ = creature.PosZ;
        if (creature.PosZ <= 0)
        {
            creature.PosZ = creature.CreationVelocityZ = 0;
            StateShadow.ToIdle(context, "ImpCreation", creature);
            creature.DroppedOnARoom = RoomIndex.RoomAt(context.World, context.Database,
                creature.Tile.Y * context.World.Width + creature.Tile.X) is not null;
            StateShadow.Move(context, "ImpCreation", creature, ImpStates.AfterDrop);
            return;
        }

        // State dispatch sees the old height; the physical pass follows it. The floor
        // clamp clears the persistent vertical vector, not state 214 itself.
        int gravity = unchecked((byte)context.Database[VariableTypeId.GravityConstant]);
        creature.CreationVelocityZ -= gravity * 0x100;
        creature.PosZ += creature.CreationVelocityZ / 16;
        if (creature.PosZ < 0) creature.PosZ = creature.CreationVelocityZ = 0;
    }

    internal static void Leave(CreatureState creature)
    {
        creature.PosZ = creature.LastPosZ = creature.CreationVelocityZ = 0;
    }
}
