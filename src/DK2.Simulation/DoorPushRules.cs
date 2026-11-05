// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

/// <summary>A closed, completed door pushes the creatures on its tile off it (door update
/// <c>0x004A55F0</c>, step 4, docs/research/door-push.md). A barricade reaches this step too: its
/// early return is after it. The push is not a move: it adds a vector to each creature's persistent
/// velocity (<c>0x004B1F20</c>), which <see cref="CreaturePhysicsRules"/> spends.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/door-push.md")]
public static class DoorPushRules
{
    /// <summary>The length of the push added each turn, <c>0x80</c> (<c>0x004A590A</c>).</summary>
    public const int PushLength = 0x80;

    /// <summary>The door's four neighbours in DK2's table <c>0x006BC350</c>: north, east, south, west.</summary>
    private static readonly (int X, int Y)[] Neighbours = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The door update's step 4: for each creature on the tile of a closed, built door, add
    /// <see cref="PushLength"/> along the direction from the push point to the creature. Every creature
    /// is pushed, whoever owns it, in any state.</summary>
    public static void PushCreaturesOff(SimulationContext context, DoorState door)
    {
        if (door.UnderConstruction || door.Open) return;
        WorldState world = context.World;
        WorldSteering navigation = context.Navigation;

        // A neighbour is open when it is not Solid in the navigation grid (world slot +0x208) and the
        // door test at its centre (0x004529A0, height 0) does not stop a creature there.
        Span<bool> open = stackalloc bool[4];
        for (int i = 0; i < 4; i++)
        {
            int nx = door.X + Neighbours[i].X, ny = door.Y + Neighbours[i].Y;
            open[i] = !NavBytes.IsSolid(navigation.NavByte(nx, ny))
                && !DoorRules.BlocksAt(context, CreatureState.CentreOf(nx), CreatureState.CentreOf(ny), 0, out _);
        }

        // The point the creatures are pushed away from: the tile's centre, moved half a tile toward every
        // closed side (0x004A5821-0x004A5864).
        int pointX = CreatureState.CentreOf(door.X), pointY = CreatureState.CentreOf(door.Y);
        if (!open[0]) pointY -= 0x800;
        if (!open[1]) pointX += 0x800;
        if (!open[2]) pointY += 0x800;
        if (!open[3]) pointX -= 0x800;
        bool onlyAlongX = !open[0] && !open[2];
        bool onlyAlongY = !open[1] && !open[3];

        int tileIndex = (door.Y * world.Width) + door.X;
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.TileLink.TileIndex != tileIndex) continue;
            int dx = creature.PosX - pointX, dy = creature.PosY - pointY;
            if (onlyAlongX) dy = 0; // walls north and south: the push runs along the passage
            if (onlyAlongY) dx = 0;
            int length = IntegerMath.SquareRoot(unchecked((uint)((dx * dx) + (dy * dy))));
            if (length != 0)
            {
                dx = (dx << 7) / length;
                dy = (dy << 7) / length;
            }
            else
            {
                // On the point itself: along its own heading (the sine tables, as the flee step back).
                dx = (int)(((long)PushLength * TrapRules.Sine(creature.Heading)) >> 16);
                dy = -(int)(((long)PushLength * TrapRules.Cosine(creature.Heading)) >> 16);
            }

            creature.PhysicalVelocityX += dx;
            creature.PhysicalVelocityY += dy;
        }
    }
}
