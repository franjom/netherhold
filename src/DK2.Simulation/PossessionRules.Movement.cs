// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

public static partial class PossessionRules
{
    /// <summary>Action 25 (<c>0x005146B0</c>). A nonzero packed word (height's low 16 bits, heading
    /// in the high 16) installs the pose through the position setter (<c>0x004B5020</c>: a height
    /// above 0x3000 becomes 0x2000) and the heading masked to 0x7FF. A zero word, the Creature
    /// Teleport form, installs the point at height 0, copies it to the previous position and leaves
    /// the heading. Both go through the setter, which relinks the tile chain on a tile change. Its
    /// floor raise has no counterpart: this simulation's floor is height 0 (M9/Q398). Fog follows
    /// from the new position in FogOfWarSystem.</summary>
    public static void Move(SimulationContext context, CreatureState creature, PossessionPose pose)
    {
        uint packed = unchecked((uint)(ushort)pose.Position.Z.Raw | ((uint)pose.Heading.Raw << 16));
        int beforeX = creature.PosX >> 12, beforeY = creature.PosY >> 12;
        if (packed == 0)
        {
            creature.PosX = creature.LastPosX = pose.Position.X.Raw;
            creature.PosY = creature.LastPosY = pose.Position.Y.Raw;
            creature.PosZ = creature.LastPosZ = 0;
        }
        else
        {
            creature.PosX = pose.Position.X.Raw;
            creature.PosY = pose.Position.Y.Raw;
            int height = unchecked((ushort)pose.Position.Z.Raw);
            creature.PosZ = height > 0x3000 ? 0x2000 : height;
            creature.Heading = pose.Heading.Value;
        }

        // DK2 compares the coordinates' bits 12 and up, an arithmetic shift, not a truncation.
        if (creature.PosX >> 12 == beforeX && creature.PosY >> 12 == beforeY) return;
        WorldState world = context.World;
        creature.ChainTileIndex = world.Contains(creature.Tile)
            ? creature.Tile.Y * world.Width + creature.Tile.X : -1;
        creature.ChainOrder = creature.ChainTileIndex < 0 ? 0 : ++world.NextChainOrder;
    }
}
