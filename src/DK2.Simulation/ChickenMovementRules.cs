// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

public enum ChickenProcess : byte { None = 0, Prison = 3, Temple = 4, Store = 5, Hatchery = 6, Reaction = 8, Moving = 9, Loose = 10, Portal = 52 }

[Flags]
public enum ObjectMoveFlags : byte { None = 0, Turning = 1, Interruptible = 2 }

public readonly record struct ObjectMovePoint(Fp32 X, Fp32 Y);

/// <summary>The owned-room Chicken object loop; loose process 10 and Prison process 3 remain unbuilt.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hatchery-roaming.md")]
public static class ChickenMovementRules
{
    public const int TurnRate = 0x100;
    private static readonly int[] Dx8 = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] Dy8 = [-1, -1, 0, 1, 1, 1, 0, -1];

    public static void Tick(SimulationContext context, ObjectState chicken)
    {
        if (chicken.InHand || chicken.CarriedByCreatureId >= 0) return;
        ReconcileMembership(context, chicken);
        if (chicken.ProcessState == 0)
        {
            SetProcess(chicken, (ChickenProcess)context.Database.ObjectById(chicken.ObjectTypeId)!.StartState);
            return;
        }
        chicken.ProcessAgeTicks = unchecked(chicken.ProcessAgeTicks + 1);
        switch ((ChickenProcess)chicken.ProcessState)
        {
            case ChickenProcess.Hatchery:
                ChooseRoomPoint(context, chicken);
                break;
            case ChickenProcess.Moving:
                Move(context, chicken);
                break;
            case ChickenProcess.Reaction:
                React(context, chicken);
                break;
        }
    }

    public static void SetProcess(ObjectState chicken, ChickenProcess process)
    {
        chicken.ProcessState = (byte)process;
        chicken.ProcessAgeTicks = 0;
    }

    public static void Land(SimulationContext context, ObjectState chicken)
    {
        RoomTypeId? room = context.Database.RoomByTerrain(
            context.World.TileAtIndex(chicken.TileIndex).TerrainId)?.TypeId;
        // FallAndLand selects the room handler without checking the object's owner.
        SetProcess(chicken, room switch
        {
            RoomTypeId.Hatchery => ChickenProcess.Hatchery,
            RoomTypeId.Prison => ChickenProcess.Prison,
            RoomTypeId.Temple => ChickenProcess.Temple,
            RoomTypeId.Treasury or RoomTypeId.DungeonHeart => ChickenProcess.Store,
            RoomTypeId.Portal or RoomTypeId.HeroPortal => ChickenProcess.Portal,
            _ => ChickenProcess.Loose,
        });
    }

    public static void UnlinkHatchery(ObjectState chicken)
    {
        if (chicken.HatcheryRoomAnchorTileIndex < 0) return;
        chicken.HatcheryRoomAnchorTileIndex = -1;
        chicken.ChickenCanEscape = true;
    }

    private static void ReconcileMembership(SimulationContext context, ObjectState chicken)
    {
        int anchor = chicken.HatcheryRoomAnchorTileIndex;
        if (anchor < 0) return;
        if (anchor >= context.World.Width * context.World.Height
            || context.World.TileAtIndex(anchor).OwnerPlayerId != chicken.OwnerPlayerId
            || context.Database.RoomByTerrain(context.World.TileAtIndex(anchor).TerrainId)?.TypeId != RoomTypeId.Hatchery)
            UnlinkHatchery(chicken);
    }

    private static void RegisterHatchery(SimulationContext context, ObjectState chicken, TilePos tile)
    {
        if (chicken.HatcheryRoomAnchorTileIndex >= 0) return;
        RoomInstance room = RoomIndex.RoomAt(context.World, context.Database, tile.Y * context.World.Width + tile.X)!;
        int used = 0;
        foreach (ObjectState bird in context.World.Objects)
            if (bird.ObjectTypeId == HatcheryRules.ChickenObjectId
                && room.Contains(bird.HatcheryRoomAnchorTileIndex))
                used += context.Database.ObjectById(bird.ObjectTypeId)!.RoomCapacity;
        int required = context.Database.ObjectById(chicken.ObjectTypeId)!.RoomCapacity;
        if (used + required > HatcheryRules.FlockCapacity(context.Database, room)) return;
        chicken.HatcheryRoomAnchorTileIndex = room.TileIndices[0];
        chicken.ChickenCanEscape = false;
    }

    private static bool IsOwnedHatchery(SimulationContext context, ObjectState chicken, TilePos tile) =>
        context.World.Contains(tile)
        && context.World.Tile(tile).OwnerPlayerId == chicken.OwnerPlayerId
        && context.Database.RoomByTerrain(context.World.Tile(tile).TerrainId)?.TypeId == RoomTypeId.Hatchery;

    private static void ChooseRoomPoint(SimulationContext context, ObjectState chicken)
    {
        WorldState world = context.World;
        var current = new TilePos(chicken.PosX >> 12, chicken.PosY >> 12);
        if (!world.Contains(current)) throw new InvalidOperationException("Chicken movement position is outside the map.");
        if (!IsOwnedHatchery(context, chicken, current))
        {
            SetProcess(chicken, context.Database.RoomByTerrain(world.Tile(current).TerrainId) is null
                ? ChickenProcess.None : ChickenProcess.Loose);
            return;
        }
        RegisterHatchery(context, chicken, current);
        TilePos target = current;
        int start = context.Rng.NextInt(4);
        for (int i = 0; i < 4; i++)
        {
            int direction = ((start + i) & 3) * 2;
            var neighbour = new TilePos(current.X + Dx8[direction], current.Y + Dy8[direction]);
            if (!IsOwnedHatchery(context, chicken, neighbour)
                || CasinoStep.HasObstacle(world, context.Database, neighbour.Y * world.Width + neighbour.X)) continue;
            target = neighbour;
            break;
        }
        int mask = SameRoomMask(context, target);
        int missing = (~mask) & 0xFF;
        int lowerX = 0x100 + ((missing & 0xE0) != 0 ? 0x400 : 0);
        int upperX = 0xF00 - ((missing & 0x0E) != 0 ? 0x400 : 0);
        int lowerY = 0x100 + ((missing & 0xC1) != 0 ? 0x400 : 0);
        int upperY = 0xF00 - ((missing & 0x38) != 0 ? 0x400 : 0);
        var destination = new ObjectMovePoint(new Fp32((target.X << 12) + lowerX + context.Rng.NextInt(upperX - lowerX)),
            new Fp32((target.Y << 12) + lowerY + context.Rng.NextInt(upperY - lowerY)));
        if (!StartMove(context, chicken, destination, ChickenProcess.Reaction, default, interruptible: true))
            SetProcess(chicken, ChickenProcess.None);
    }

    private static int SameRoomMask(SimulationContext context, TilePos position)
    {
        int mask = 0;
        TileState tile = context.World.Tile(position);
        for (int i = 0; i < 8; i++)
        {
            var neighbour = new TilePos(position.X + Dx8[i], position.Y + Dy8[i]);
            if (!context.World.Contains(neighbour)) continue;
            TileState other = context.World.Tile(neighbour);
            if (other.TerrainId == tile.TerrainId && other.OwnerPlayerId == tile.OwnerPlayerId) mask |= 1 << i;
        }
        return mask;
    }

    public static bool StartMove(SimulationContext context, ObjectState chicken, ObjectMovePoint destination,
        ChickenProcess resume, Fp32 extraSpeed, bool interruptible)
    {
        if (!context.World.Contains(new TilePos(destination.X.Raw >> 12, destination.Y.Raw >> 12))) return false;
        SetProcess(chicken, ChickenProcess.Moving);
        chicken.MoveResumeState = (byte)resume;
        chicken.MoveDestinationX = destination.X.Raw;
        chicken.MoveDestinationY = destination.Y.Raw;
        chicken.MoveFlags = (chicken.MoveFlags & ~ObjectMoveFlags.Interruptible)
            | (interruptible ? ObjectMoveFlags.Interruptible : ObjectMoveFlags.None);
        ObjectDef definition = context.Database.ObjectById(chicken.ObjectTypeId)!;
        int speed = unchecked((short)(RuntimeSpeed(definition) + extraSpeed.Raw));
        if (speed == 0) return false;
        int dx = unchecked(destination.X.Raw - chicken.PosX), dy = unchecked(destination.Y.Raw - chicken.PosY);
        int heading = Bearing.Of(dx, dy);
        chicken.MoveVelocityX = unchecked(TrapRules.Sine(heading) * speed) >> 16;
        chicken.MoveVelocityY = unchecked(-TrapRules.Cosine(heading) * speed) >> 16;
        uint distanceSquare = unchecked((uint)((int)(((long)dx * dx) >> 12) + (int)(((long)dy * dy) >> 12)));
        uint speedSquare = unchecked((uint)((long)speed * speed >> 12));
        if (speedSquare == 0) throw new DivideByZeroException("Object movement speed has zero fixed-point square.");
        int count = Math.Max(1, IntegerMath.SquareRoot(distanceSquare / speedSquare) - 1)
            + (distanceSquare % speedSquare != 0 ? 1 : 0);
        chicken.MoveTicksRemaining = unchecked((ushort)count);
        chicken.MoveFlags |= ObjectMoveFlags.Turning;
        return true;
    }

    public static short RuntimeSpeed(ObjectDef definition)
    {
        short speed = unchecked((short)(definition.Speed.Raw / SimulationTiming.OriginalTicksPerGameSecond));
        return definition.Speed.Raw != 0 && speed == 0 ? (short)1 : speed;
    }

    private static void Move(SimulationContext context, ObjectState chicken)
    {
        if ((chicken.MoveFlags & ObjectMoveFlags.Turning) != 0)
        {
            int heading = chicken.Heading;
            int target = Bearing.Of(chicken.MoveDestinationX - chicken.PosX, chicken.MoveDestinationY - chicken.PosY);
            int remaining = Bearing.TurnToward(ref heading, target, TurnRate);
            chicken.Heading = heading;
            if (remaining != 0) return;
            chicken.MoveFlags &= ~ObjectMoveFlags.Turning;
        }
        chicken.MoveTicksRemaining = unchecked((ushort)(chicken.MoveTicksRemaining - 1));
        if (chicken.MoveTicksRemaining == 0)
        {
            SetPosition(context.World, chicken, new ObjectMovePoint(new Fp32(chicken.MoveDestinationX), new Fp32(chicken.MoveDestinationY)));
            SetProcess(chicken, (ChickenProcess)chicken.MoveResumeState);
        }
        else
        {
            SetPosition(context.World, chicken, new ObjectMovePoint(new Fp32(chicken.PosX + chicken.MoveVelocityX), new Fp32(chicken.PosY + chicken.MoveVelocityY)));
            TryAvoidCreature(context, chicken);
        }
    }

    private static void SetPosition(WorldState world, ObjectState chicken, ObjectMovePoint position)
    {
        chicken.PosX = position.X.Raw;
        chicken.PosY = position.Y.Raw;
        chicken.TileIndex = (position.Y.Raw >> 12) * world.Width + (position.X.Raw >> 12);
    }

    private static void React(SimulationContext context, ObjectState chicken)
    {
        if (chicken.ProcessAgeTicks == 1) chicken.ChickenReactionTicks = context.Rng.NextInt(16) + 8;
        if (chicken.ChickenReactionTicks != 0)
        {
            if (chicken.ChickenScared && chicken.ProcessAgeTicks == 1
                && context.Database.ObjectById(chicken.ObjectTypeId)!.Flags.HasFlag(ObjectFlags.TypeFood))
            {
                chicken.ChickenReactionTicks = 32;
                return;
            }
            if ((chicken.ChickenReactionTicks & 3) == 0)
            {
                int direction = context.Rng.NextInt(2) == 1 ? -1 : 1;
                chicken.Heading = (chicken.Heading + direction * unchecked((short)context.Database.ObjectById(chicken.ObjectTypeId)!.ReactionTurn)) & Bearing.Mask;
            }
            if (!TryAvoidCreature(context, chicken)) chicken.ChickenReactionTicks--;
            return;
        }
        chicken.ChickenScared = false;
        var tile = new TilePos(chicken.PosX >> 12, chicken.PosY >> 12);
        ChickenProcess next = IsOwnedHatchery(context, chicken, tile) ? ChickenProcess.Hatchery
            : context.World.Tile(tile).OwnerPlayerId == chicken.OwnerPlayerId
              && context.Database.RoomByTerrain(context.World.Tile(tile).TerrainId)?.TypeId == RoomTypeId.Prison
                ? ChickenProcess.Prison : ChickenProcess.Loose;
        SetProcess(chicken, next);
    }

    private static bool TryAvoidCreature(SimulationContext context, ObjectState chicken)
    {
        if (!chicken.ChickenCanEscape || chicken.ChickenScared
            || (chicken.MoveFlags & ObjectMoveFlags.Interruptible) == 0) return false;
        WorldState world = context.World;
        int x = chicken.PosX >> 12, y = chicken.PosY >> 12;
        for (int i = 0; i <= 8; i++)
        {
            int tx = x + (i == 8 ? 0 : Dx8[i]), ty = y + (i == 8 ? 0 : Dy8[i]);
            if (!world.Contains(tx, ty) || context.Database.TerrainById(world.Tile(tx, ty).TerrainId).IsSolid) continue;
            CreatureState? first = null;
            foreach (CreatureState creature in world.Creatures)
                if (creature.Activity != CreatureActivity.Dead && creature.TileLink.TileIndex == ty * world.Width + tx
                    && (first is null || creature.TileLink.Order > first.TileLink.Order)) first = creature;
            if (first is null) continue;
            CreatureDef definition = context.Database.CreatureById(first.CreatureTypeId);
            // Full ground-height physics is absent; supported flight and vertical position gate this scan.
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker) || WaterSpeedRules.IsFlying(definition, first)
                || first.PosZ > Fp32.One) continue;
            int heading = Bearing.Of(chicken.PosX - first.PosX, chicken.PosY - first.PosY);
            var target = new ObjectMovePoint(new Fp32(chicken.PosX + (unchecked(TrapRules.Sine(heading) << 11) >> 16)),
                new Fp32(chicken.PosY + (unchecked(TrapRules.Cosine(heading) * -0x800) >> 16)));
            var destinationTile = new TilePos(target.X.Raw >> 12, target.Y.Raw >> 12);
            if (!world.Contains(destinationTile) || context.Database.TerrainById(world.Tile(destinationTile).TerrainId).IsSolid) continue;
            StartMove(context, chicken, target, ChickenProcess.Reaction,
                new Fp32(RuntimeSpeed(context.Database.ObjectById(chicken.ObjectTypeId)!)), interruptible: true);
            return true;
        }
        return false;
    }
}
