// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

public enum ChickenProcess : byte { None = 0, Prison = 3, Temple = 4, Store = 5, Hatchery = 6, Reaction = 8, Moving = 9, Loose = 10, Portal = 52 }

[Flags]
public enum ObjectMoveFlags : byte { None = 0, Turning = 1, Interruptible = 2 }

public readonly record struct ObjectMovePoint(Fp32 X, Fp32 Y);

/// <summary>The Chicken object's processes: the owned-room loop (6, 8, 9) and the loose roam (10,
/// <c>0x004A26D0</c>, ADR-0647); Prison process 3 remains unbuilt.</summary>
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
            Decay(context, chicken);
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
            case ChickenProcess.Loose:
                // A process that returns 0 is cleared, and the next update restarts it (0x0049EED0).
                if (!Roam(context, chicken)) SetProcess(chicken, ChickenProcess.None);
                break;
        }
        Decay(context, chicken);
    }

    /// <summary>The object update's tail after the process (<c>0x0049BD20</c>): a definition with
    /// <c>DieOverTime</c>, or <c>DieOverTimeIfNotInRoom</c> while no room holds the bird (<c>+0x138</c>,
    /// ours the Hatchery anchor), loses one health a turn; at none it asks for its definition's death
    /// effect (<c>+0x119</c>; <c>Object_Init</c> sets the <c>+0x146</c> bit that allows it) at its point,
    /// heading and owner, and the thing pass's second walk removes it the same turn (ADR-0650).</summary>
    private static void Decay(SimulationContext context, ObjectState chicken)
    {
        ObjectDef definition = context.Database.ObjectById(chicken.ObjectTypeId)!;
        bool decays = definition.Flags.HasFlag(ObjectFlags.DieOverTime)
            || (definition.Flags.HasFlag(ObjectFlags.DieOverTimeIfNotInRoom) && chicken.HatcheryRoomAnchorTileIndex < 0);
        if (decays && chicken.Health > 0) chicken.Health--;
        if (chicken.Health > 0) return;
        if (definition.DeathEffectId != 0)
            context.RequestEffect("chicken", new ViewEffectRequest(new EffectId(definition.DeathEffectId), new PlayerId(chicken.OwnerPlayerId),
                new FpPosition3(new Fp32(chicken.PosX), new Fp32(chicken.PosY), new Fp32(chicken.PosZ)), new Dk2Angle((ushort)chicken.Heading)));
        context.Emit("chicken", "chicken_died", $"object={chicken.Id}");
        context.World.RemoveObject(chicken.Id);
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

    /// <summary>Process 10, <c>0x004A26D0</c>. A bird a Hatchery holds (<c>+0x14A</c> clear) off any Hatchery
    /// tile, one whose owner has fallen (<c>+0x132</c>) or one on a terrain with any Damage, harmful or healing
    /// (terrain <c>+0x1A9</c>) dies (vtable <c>+0x20</c>); a scared one does nothing. Otherwise <c>random(4)</c> picks the first
    /// of the four sides tried in turn: a neighbour on the map whose navigation byte is not solid and equals
    /// the bird's own tile's (<c>0x00455000</c> reads the raw byte, so a Heart centre's 3 refuses floor; ADR-0649),
    /// owned by its owner, not a room on the first three tries, with no door, trap or obstacle (a store
    /// tile's gold pile among them). The point is
    /// <c>random(0x555) + 0x555</c> into it on each axis, x first, and the walk resumes 10; a walk that cannot
    /// start tries the next side. Returns false when no side takes it.</summary>
    private static bool Roam(SimulationContext context, ObjectState chicken)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        var current = new TilePos(chicken.PosX >> 12, chicken.PosY >> 12);
        if (!world.Contains(current)) throw new InvalidOperationException("Chicken movement position is outside the map.");
        TileState here = world.Tile(current);
        if (!chicken.ChickenCanEscape && database.RoomByTerrain(here.TerrainId)?.TypeId != RoomTypeId.Hatchery)
        {
            Kill(context, chicken, "held_off_hatchery");
            return true;
        }
        if (chicken.ChickenScared) return false;
        if (world.HasPlayer(chicken.OwnerPlayerId) && world.Player(chicken.OwnerPlayerId).KilledByPlayerId != 0)
        {
            Kill(context, chicken, "owner_fallen");
            return false;
        }
        if (database.TerrainById(here.TerrainId).Damage != 0)
        {
            Kill(context, chicken, "terrain_damage");
            return false;
        }

        int start = context.Rng.NextInt(4);
        context.Navigation.Refresh(); // one pass over the tiles when nothing changed
        byte navByte = context.Navigation.NavByte(current.X, current.Y);
        for (int i = 0; i < 4; i++)
        {
            int direction = ((start + i) & 3) * 2;
            var neighbour = new TilePos(current.X + Dx8[direction], current.Y + Dy8[direction]);
            if (!world.Contains(neighbour)) continue;
            TileState tile = world.Tile(neighbour);
            byte sideByte = context.Navigation.NavByte(neighbour.X, neighbour.Y);
            if (NavBytes.IsSolid(sideByte) || sideByte != navByte
                || tile.OwnerPlayerId != chicken.OwnerPlayerId) continue;
            if (i < 3 && database.RoomByTerrain(tile.TerrainId) is not null) continue;
            int index = neighbour.Y * world.Width + neighbour.X;
            if (HasDoorOrTrap(world, neighbour) || CasinoStep.HasObstacle(world, database, index)
                || HasStorePile(database, tile)) continue;
            var destination = new ObjectMovePoint(
                new Fp32((neighbour.X << 12) | (context.Rng.NextInt(0x555) + 0x555)),
                new Fp32((neighbour.Y << 12) | (context.Rng.NextInt(0x555) + 0x555)));
            if (StartMove(context, chicken, destination, ChickenProcess.Loose, default, interruptible: true)) return true;
        }
        return false;
    }

    /// <summary>A Treasury or Heart tile holding gold holds one Gold Pile 1 (<c>0x004F3C60</c>), an Obstacle
    /// by its definition, which <c>Tile_FirstObstacle</c> (<c>0x00453600</c>) finds; ours keeps that gold on
    /// the tile without the object (ADR-0649).</summary>
    private static bool HasStorePile(GameDatabase database, in TileState tile) =>
        tile.GoldHeld > 0
        && database.RoomByTerrain(tile.TerrainId)?.TypeId is RoomTypeId.Treasury or RoomTypeId.DungeonHeart
        && database.ObjectById(FloorGold.TreasuryPileObjectId)?.Flags.HasFlag(ObjectFlags.Obstacle) == true;

    private static bool HasDoorOrTrap(WorldState world, TilePos tile)
    {
        foreach (DoorState door in world.Doors)
            if (door.X == tile.X && door.Y == tile.Y) return true;
        foreach (TrapState trap in world.Traps)
            if (trap.X == tile.X && trap.Y == tile.Y) return true;
        return false;
    }

    /// <summary>Vtable <c>+0x20</c> (<c>0x004B2AD0</c>) only zeroes the bird's health; the same update's
    /// <see cref="Decay"/> asks for its death effect and removes it.</summary>
    private static void Kill(SimulationContext context, ObjectState chicken, string reason)
    {
        chicken.Health = 0;
        context.Emit("chicken", "loose_chicken_died", $"object={chicken.Id} reason={reason}");
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
        ChickenProcess resume, Fp32 extraSpeed, bool interruptible) =>
        ObjectMovementRules.StartMove(context, chicken, destination, new ObjectProcessId((byte)resume), extraSpeed, interruptible);

    public static short RuntimeSpeed(ObjectDef definition) => ObjectMovementRules.RuntimeSpeed(definition);

    private static void Move(SimulationContext context, ObjectState chicken) => ObjectMovementRules.StepMove(context, chicken);

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

    internal static bool TryAvoidCreature(SimulationContext context, ObjectState chicken)
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
