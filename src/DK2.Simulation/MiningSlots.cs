// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The twelve places a miner can stand to work a wall: four faces, three slots a
/// third of a tile wide along each. DK2's chooser <c>0x00469AF0</c> (and <c>0x0046A550</c>)
/// walks them, skips a slot another worker holds or whose cell's floor pile holds more than
/// <c>MaxGoldPileOutsideTreasury</c>, and takes the first left; the miner's overflow then lands
/// in that slot's cell (<c>0x004971D0</c> → <c>0x004510C0</c>). gold-on-the-floor.md §10.
/// Deviates: faces are numbered here by geometry, not by DK2's direction table, and the
/// slot order runs along the axis; neither changes which cells exist.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/gold-on-the-floor.md")]
public static class MiningSlots
{
    /// <summary>No slot held.</summary>
    public const byte None = 0xFF;

    /// <summary>A floor pile with no cell: placed by a path that has no sub-tile position.</summary>
    public const byte AnyCell = 0xFF;

    /// <summary>Slots per face, the dig handler's three workers.</summary>
    public const int PerFace = 3;

    /// <summary>The face a worker standing at <paramref name="standing"/> works of the wall at
    /// <paramref name="target"/>: 0 wall above, 1 wall left, 2 wall right, 3 wall below; -1 if
    /// the two are not orthogonal neighbours.</summary>
    public static int Face(TilePos standing, TilePos target) =>
        (target.X - standing.X, target.Y - standing.Y) switch
        {
            (0, -1) => 0,
            (-1, 0) => 1,
            (1, 0) => 2,
            (0, 1) => 3,
            _ => -1,
        };

    /// <summary>The cell of the standing tile a slot covers, <c>cy × 3 + cx</c>: the strip of
    /// thirds along the wall's edge, the slot counting along it.</summary>
    public static byte Cell(byte slot)
    {
        int face = slot / PerFace, along = slot % PerFace;
        (int cx, int cy) = face switch
        {
            0 => (along, 0),
            1 => (0, along),
            2 => (2, along),
            _ => (along, 2),
        };
        return (byte)(cy * 3 + cx);
    }

    /// <summary>The first free slot on this face for a miner, or <see cref="None"/>. A slot is
    /// taken when another creature mining the same wall holds it, and closed when the floor pile
    /// in its cell holds more than <see cref="FloorGold.MaxPile"/>.</summary>
    public static byte FreeSlot(
        WorldState world, GameDatabase database, int targetIndex, TilePos standing, TilePos target,
        int askingCreatureId)
    {
        int face = Face(standing, target);
        if (face < 0) return None;
        int standingIndex = standing.Y * world.Width + standing.X;
        int limit = FloorGold.MaxPile(database);
        for (int along = 0; along < PerFace; along++)
        {
            byte slot = (byte)(face * PerFace + along);
            if (Held(world, targetIndex, slot, askingCreatureId)) continue;
            if (FloorGold.PileInCell(world, standingIndex, Cell(slot)) > limit) continue;
            return slot;
        }
        return None;
    }

    private static bool Held(WorldState world, int targetIndex, byte slot, int askingCreatureId) =>
        Holder(world, targetIndex, slot, askingCreatureId) is not null;

    /// <summary>The worker digging or mining this wall from this slot, other than the asker.
    /// Digs hold slots as mines do: the dig handler walks the same twelve (ADR-0289).</summary>
    public static CreatureState? Holder(WorldState world, int targetIndex, byte slot, int askingCreatureId)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        foreach (CreatureState other in world.Creatures)
            if (other.Id != askingCreatureId && other.TargetTileIndex == targetIndex && other.WorkSlot == slot
                && other.TargetWork is TileWorkKind.Dig or TileWorkKind.MineGold
                    or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall)
                return other;
        return null;
    }

    /// <summary>Where a worker stands in a slot, in the 4096-a-tile fixed point (<c>0x00698A80</c>):
    /// 682/4096 in from the edge that touches the wall, at 682, 2047 and 3412 along it (Q492).</summary>
    public static (long X, long Y) StancePoint(TilePos standing, byte slot)
    {
        int along = StanceAlong[slot % PerFace];
        (int ox, int oy) = (slot / PerFace) switch
        {
            0 => (along, StanceNear),
            1 => (StanceNear, along),
            2 => (StanceFar, along),
            _ => (along, StanceFar),
        };
        return ((standing.X * 4096L) + ox, (standing.Y * 4096L) + oy);
    }

    private const int StanceNear = 682, StanceFar = 3412;
    private static readonly int[] StanceAlong = [682, 2047, 3412];

    /// <summary>The side of a wall DK2's dig and reinforce handlers try first, and which way they
    /// turn (<c>0x004805F0</c>), from the worker's tile minus the wall's: sides 0 north, 1 east,
    /// 2 south, 3 west; the step is +1 clockwise or -1, toward the worker's second side.</summary>
    public static (int Side, int Step) StartSide(int dx, int dy)
    {
        if (dx == 0) return (dy > 0 ? 2 : 0, 1);
        if (dx < 0)
        {
            if (dy == 0) return (3, 1);
            if (dy > 0) return -dx < dy ? (2, 1) : (3, -1);
            return dx != dy && -dx <= -dy ? (0, -1) : (3, 1);
        }
        if (dy == 0) return (1, 1);
        if (dy > 0) return dx < dy ? (2, -1) : (1, 1);
        return -dx != dy && dx <= -dy ? (0, 1) : (1, -1);
    }
}
