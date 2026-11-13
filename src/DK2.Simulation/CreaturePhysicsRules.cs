// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;

namespace DK2.Simulation;

/// <summary>The persistent horizontal vector of the physical pass (<c>0x004B2030</c>, the arm DK2 runs
/// for every creature each update, between the state handler and the steering step), spent as
/// docs/research/door-push.md describes: sub-steps of a sixteenth of the vector, a wall or door body
/// reflecting and halving the component it stops, a commit that drops
/// <see cref="CreatureDef.AirFriction"/> of the horizontal vector. Gravity (<c>0x004B2069</c>-<c>0x004B20A5</c>):
/// a creature that is not flying (<c>vtable+0x88</c>) and stands above its floor gains
/// <see cref="VariableTypeId.GravityConstant"/> x 256 downward on the vertical vector, which no friction
/// damps; the commit's position setter (<c>0x004B5020</c>) clamps the height to the floor
/// (<see cref="FloorHeightRules"/>) and zeroes that vector, and every commit, a fall alone included, puts
/// the steering body back on the entity (Q795). Not built: the temporary vector
/// (<see cref="Combat.FearRules.Slide"/> spends its own); the imp's creation fall, a party's arrival and a
/// hand drop's landing keep their own height rules.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/door-push.md")]
public static class CreaturePhysicsRules
{
    /// <summary>The navigation byte of a portal's centre hole, which the pass lets a creature leave but not enter.</summary>
    private const byte PortalHole = NavBytes.PortalHole;

    /// <summary>A vector whose |x| + |y| is below this, but not zero, stops the creature instead of moving it
    /// (<c>0x004B25F0</c>–<c>0x004B2620</c>).</summary>
    private const int MinimumMagnitude = 0x80;

    public static void Step(SimulationContext context, CreatureState creature)
    {
        if (!creature.TileLink.IsLinked) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);

        bool ownsHeight = OwnsHeight(creature);
        int velocityZ = 0;
        if (ownsHeight)
        {
            int floor = FloorHeightRules.RelativeAt(world, database, creature.PosX, creature.PosY);
            // Native raises a creature onto a higher floor when its position is set; ours' other position
            // writers do not ask the floor, so the pass catches it up first.
            if (creature.PosZ < floor) SetHeight(world, database, creature, creature.PosZ);
            if (creature.PosZ > floor && !WaterSpeedRules.IsFlying(definition, creature))
                creature.CreationVelocityZ -= unchecked((byte)database[VariableTypeId.GravityConstant]) * 0x100;
            // 0x004B2740: on or below the floor the vertical vector is spent.
            if (creature.PosZ <= floor) creature.CreationVelocityZ = 0;
            velocityZ = creature.CreationVelocityZ;
        }

        int velocityX = creature.PhysicalVelocityX, velocityY = creature.PhysicalVelocityY;
        if (velocityX == 0 && velocityY == 0 && velocityZ == 0) return;

        WorldSteering navigation = context.Navigation;
        int startX = creature.PosX, startY = creature.PosY, z = creature.PosZ;
        byte startNav = navigation.NavByte(startX >> 12, startY >> 12);
        // Set while the creature is inside a door body already: it may stay in one, but not enter another.
        bool insideDoor = DoorRules.BlocksAt(context, startX, startY, z, out _);

        int steps = Math.Min(16, (IntegerMath.Length(velocityX, velocityY, velocityZ) >> 14) + 1);
        int baseX = startX, baseY = startY, lastX = startX, lastY = startY, baseZ = z, lastZ = z;
        int step = 1;
        do
        {
            int deltaX = (int)((long)step * velocityX / steps) / 16, deltaY = (int)((long)step * velocityY / steps) / 16;
            int nextX = baseX + deltaX, nextY = baseY + deltaY;
            int nextZ = baseZ + ((int)((long)step * velocityZ / steps) / 16);
            bool blocked = Blocked(context, navigation, startNav, nextX, nextY, nextZ, ref insideDoor);
            if (!blocked)
            {
                lastX = nextX;
                lastY = nextY;
                lastZ = nextZ;
                step++;
                continue;
            }

            // The reflect arm (the creature's vtable +0x60 answers 1, +0x84 answers 0): a move along y alone
            // from where it stands decides which component turns back, half as strong.
            int testY = startY + deltaY;
            if (!Allows(world, navigation, startX, testY)
                || (!insideDoor && DoorRules.BlocksAt(context, startX, testY, z, out _)))
            {
                creature.PhysicalVelocityY = -(creature.PhysicalVelocityY / 2);
                velocityY = -(velocityY / 2);
            }
            else
            {
                creature.PhysicalVelocityX = -(creature.PhysicalVelocityX / 2);
                velocityX = -(velocityX / 2);
            }

            baseX = lastX;
            baseY = lastY;
            baseZ = lastZ;
            steps -= step;
            step = 1;
        }
        while (step <= steps);

        int magnitude = Math.Abs(creature.PhysicalVelocityY) + Math.Abs(creature.PhysicalVelocityX);
        bool moves = (magnitude == 0 || magnitude >= MinimumMagnitude)
            && CommitAllowed(navigation, definition, creature, startNav, lastX, lastY);
        if (!moves)
        {
            creature.PhysicalVelocityX = creature.PhysicalVelocityY = 0;
            return;
        }

        creature.PosX = lastX;
        creature.PosY = lastY;
        if (ownsHeight)
        {
            // 0x004B267C: a sixteenth that truncates to no change moves the height by the whole vector.
            if (lastZ == z) lastZ = z + velocityZ;
            SetHeight(world, database, creature, lastZ);
        }
        int friction = 0x1000 - (ushort)definition.AirFriction;
        if (friction < 0) friction = 0x800;
        creature.PhysicalVelocityX = creature.PhysicalVelocityX * friction / 0x1000;
        creature.PhysicalVelocityY = creature.PhysicalVelocityY * friction / 0x1000;
        // The pass writes the body's position from the entity's (+0x1A8/+0x1AC, 0x004B2702).
        WorldSteering.SyncBody(creature);
    }

    /// <summary>The height half of the position setter (<c>0x004B5020</c>): below the floor it is raised to
    /// it and the vertical vector is zeroed; above 0x3000 (absolute) it drops to 0x2000.</summary>
    internal static void SetHeight(WorldState world, GameDatabase database, CreatureState creature, int z)
    {
        int floor = FloorHeightRules.RelativeAt(world, database, creature.PosX, creature.PosY);
        if (z < floor)
        {
            z = floor;
            creature.CreationVelocityZ = 0;
        }
        else if (z > 0x3000 - FloorHeightRules.Ordinary)
        {
            z = 0x2000 - FloorHeightRules.Ordinary;
        }
        creature.PosZ = z;
    }

    /// <summary>Whether the generic height rules own this creature's height: not while the imp's creation
    /// fall, a party's arrival, a hand drop's landing, the hand or possession move it by their own rules.</summary>
    public static bool OwnsHeight(CreatureState creature) =>
        !creature.ArrivalHeightActive && !creature.LandingPending && !creature.InHand && !creature.Possessed
        && creature.Dk2StateId != ImpCreationRules.FallingState;

    /// <summary>One sub-step's stop test (<c>0x004B2150</c>–<c>0x004B2340</c>): off the map, a creature not
    /// standing in a portal hole stepping into one, terrain with the Solid flag, or a door body entered
    /// from outside; a barricade's body never stops it here (the walk's solid test does). The door test
    /// takes the sub-step's point, its height included (<c>0x004B2250</c>-<c>0x004B231D</c>).</summary>
    private static bool Blocked(
        SimulationContext context, WorldSteering navigation, byte startNav, int x, int y, int z, ref bool insideDoor)
    {
        WorldState world = context.World;
        int tileX = x >> 12, tileY = y >> 12;
        if (!world.Contains(tileX, tileY)) return true;
        if (startNav != PortalHole && navigation.NavByte(tileX, tileY) == PortalHole) return true;

        bool solid = context.Database.TerrainById(world.Tile(tileX, tileY).TerrainId).IsSolid;
        if (!DoorRules.BlocksAt(context, x, y, z, out DoorState? door))
        {
            insideDoor = false;
            return solid;
        }

        if (context.Database.Doors.TryGetValue(door!.DoorId, out DoorDef? definition)
            && ((definition.Flags & DoorFlags.Barricade) == DoorFlags.Barricade)) return false;
        return !insideDoor || solid;
    }

    /// <summary>The thing-position test <c>0x004B35C0</c>: on the map and not Solid in the navigation grid.</summary>
    private static bool Allows(WorldState world, WorldSteering navigation, int x, int y) =>
        world.Contains(x >> 12, y >> 12) && !NavBytes.IsSolid(navigation.NavByte(x >> 12, y >> 12));

    /// <summary>The commit's last gate (<c>0x004B263D</c>, <c>0x004B265C</c>): the creature may step from its own
    /// tile to its own, or to the tile it ends on (<c>0x0048EBF0</c>, with option bits 1 and 2 clear and respect-doors
    /// from the creature's last route word). A creature that cannot even stay where it stands, inside a
    /// locked own door, is moved regardless.</summary>
    private static bool CommitAllowed(
        WorldSteering navigation, CreatureDef definition, CreatureState creature, byte startNav, int x, int y)
    {
        bool flies = WaterSpeedRules.IsFlying(definition, creature);
        MoverProfile profile = WorldSteering.ProfileOf(definition, creature.OwnerPlayerId, (MoveOptions)((int)creature.RouteOptions & 0x80));
        if (flies) profile = profile with { CanWalkOnWater = true, CanWalkOnLava = true };
        if (MeshRouter.TransitionCost(startNav, startNav, profile) == 0) return true;
        return MeshRouter.TransitionCost(navigation.NavByte(x >> 12, y >> 12), startNav, profile) != 0;
    }
}
