// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/object-drawing.md#8-furniture-eggs-and-chickens")]
public static class EggLaunchRules
{
    public const byte LaunchProcess = 24;
    public const int HiddenTicks = 4 * SimulationTiming.OriginalTicksPerGameSecond;

    public static void Step(SimulationContext context, ObjectState egg)
    {
        WorldState world = context.World;
        egg.ProcessAgeTicks++;
        if (egg.ProcessAgeTicks <= HiddenTicks)
        {
            RoomInstance? room = RoomIndex.RoomAt(world, context.Database, egg.TileIndex);
            ObjectState? coop = world.Objects.Where(o => o.TileIndex == egg.TileIndex
                && o.ObjectTypeId == HatcheryRules.CoopObjectId && !o.InHand && o.CarriedByCreatureId < 0)
                .OrderByDescending(o => o.TileLink.Order).FirstOrDefault();
            if (room is null || room.Definition.TypeId != RoomTypeId.Hatchery || coop is null)
            {
                world.RemoveObject(egg.Id);
                return;
            }
            egg.PosX = CreatureState.CentreOf(coop.TileIndex % world.Width);
            egg.PosY = CreatureState.CentreOf(coop.TileIndex / world.Width);
            egg.PosZ = 0x13E8;
            int cancellation = context.Database[VariableTypeId.GravityConstant] * 0x100 - egg.PhysicalVelocityZ;
            if (egg.ProcessAgeTicks != HiddenTicks)
            {
                Move(context, egg, new Fp32(cancellation));
                return;
            }
            int heading = AutoFurnitureRules.Choice(coop.TileIndex % world.Width, coop.TileIndex / world.Width, 4) << 9;
            egg.Heading = (heading + context.Rng.NextInt(0x100) - 0x80) & Bearing.Mask;
            egg.PhysicalVelocityX += (int)((long)TrapRules.Sine(egg.Heading) * 0x6000 >> 16);
            egg.PhysicalVelocityY -= (int)((long)TrapRules.Cosine(egg.Heading) * 0x6000 >> 16);
            egg.PhysicalVelocityZ += 0x3000;
            Move(context, egg, new Fp32(cancellation));
            return;
        }

        if (Math.Abs(egg.PhysicalVelocityX) + Math.Abs(egg.PhysicalVelocityY) <= 0x1000)
        {
            // State 24 keeps the installed art when it stops; the art clock predates state 32.
            egg.ProcessState = 32;
            egg.ProcessAgeTicks = 0;
            egg.ProcessCount = 0;
            Move(context, egg, default);
            return;
        }
        egg.DrawPitch = (egg.DrawPitch + 0x40) & Bearing.Mask;
        egg.DrawYaw = (egg.DrawYaw + 0x40) & Bearing.Mask;
        egg.DrawRoll = (egg.DrawRoll + 0x40) & Bearing.Mask;
        // State 24 reinstalls floor art on every moving turn, resetting its normalized phase.
        egg.ProcessDeadline = context.Tick.Value;
        Move(context, egg, default);
    }

    internal static bool Move(SimulationContext context, ObjectState egg, Fp32 temporaryVelocityZ)
    {
        int ground = FloorHeight(context.World, context.Database, new Fp32(egg.PosX), new Fp32(egg.PosY));
        int temporaryZ = temporaryVelocityZ.Raw;
        if (egg.PosZ > ground && (short)(context.Database.ObjectById(egg.ObjectTypeId)?.Mass.Raw ?? 0) != 0)
            egg.PhysicalVelocityZ -= context.Database[VariableTypeId.GravityConstant] * 0x100;
        else if (egg.PosZ <= ground && Math.Abs(egg.PhysicalVelocityX) + Math.Abs(egg.PhysicalVelocityY) > 0x1000)
            temporaryZ += 0x3000;

        int vx = egg.PhysicalVelocityX, vy = egg.PhysicalVelocityY, vz = egg.PhysicalVelocityZ + temporaryZ;
        int norm = IntegerMath.Length(vx, vy, vz);
        if (norm == 0) return true;
        int steps = Math.Min(16, (norm >> 14) + 1);
        int baseX = egg.PosX, baseY = egg.PosY, baseZ = egg.PosZ;
        int lastX = baseX, lastY = baseY, lastZ = baseZ;
        for (int step = 1; step <= steps; step++)
        {
            int nextX = baseX + (vx * step / steps) / 16;
            int nextY = baseY + (vy * step / steps) / 16;
            int nextZ = baseZ + (vz * step / steps) / 16;
            bool blockedY = !Passable(context, new Fp32(baseX), new Fp32(nextY));
            if (!Passable(context, new Fp32(nextX), new Fp32(nextY)))
            {
                if (context.Rng.NextInt(2) == 0)
                {
                    context.RequestEffect("Egg", new ViewEffectRequest(new EffectId(301), new PlayerId(egg.OwnerPlayerId),
                        new FpPosition3(new Fp32(egg.PosX), new Fp32(egg.PosY), new Fp32(egg.PosZ)),
                        new Dk2Angle((ushort)egg.Heading)));
                    egg.PhysicalVelocityX = egg.PhysicalVelocityY = egg.PhysicalVelocityZ = 0;
                    egg.Health = 0;
                    context.World.RemoveObject(egg.Id);
                    return false;
                }
                if (blockedY)
                {
                    egg.PhysicalVelocityY = -(egg.PhysicalVelocityY / 2);
                    vy = -(vy / 2);
                }
                else
                {
                    egg.PhysicalVelocityX = -(egg.PhysicalVelocityX / 2);
                    vx = -(vx / 2);
                }
                (baseX, baseY, baseZ) = (lastX, lastY, lastZ);
                steps -= step;
                step = 0;
                continue;
            }
            (lastX, lastY, lastZ) = (nextX, nextY, nextZ);
        }
        egg.PosX = lastX;
        egg.PosY = lastY;
        egg.PosZ = lastZ;
        egg.TileIndex = (lastY >> 12) * context.World.Width + (lastX >> 12);
        ground = FloorHeight(context.World, context.Database, new Fp32(lastX), new Fp32(lastY));
        if (egg.PosZ < ground)
        {
            egg.PosZ = ground;
            egg.PhysicalVelocityZ = 0;
        }
        else if (egg.PosZ > 0x3000) egg.PosZ = 0x2000;
        int friction = 4096 - (ushort)(context.Database.ObjectById(egg.ObjectTypeId)?.AirFriction.Raw ?? 0);
        if (friction < 0) friction = 2048;
        egg.PhysicalVelocityX = unchecked(egg.PhysicalVelocityX * friction) / 4096;
        egg.PhysicalVelocityY = unchecked(egg.PhysicalVelocityY * friction) / 4096;
        int horizontal = Math.Abs(egg.PhysicalVelocityX) + Math.Abs(egg.PhysicalVelocityY);
        if (horizontal > 0 && horizontal < 128) egg.PhysicalVelocityX = egg.PhysicalVelocityY = 0;
        return true;
    }

    private static bool Passable(SimulationContext context, Fp32 x, Fp32 y)
    {
        var tile = new TilePos(x.Raw >> 12, y.Raw >> 12);
        return context.World.Contains(tile) && !context.Database.TerrainById(context.World.Tile(tile).TerrainId).IsSolid;
    }

    /// <summary>The floor height under the egg (<c>0x004545C0</c>, <see cref="FloorHeightRules"/>).</summary>
    internal static int FloorHeight(WorldState world, GameDatabase database, Fp32 posX, Fp32 posY) =>
        FloorHeightRules.At(world, database, posX.Raw, posY.Raw);
}
