// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How a keeper throws a creature away. DK2 has <b>two</b> single unwalkable tiles
/// and they are exclusive: a portal's hole takes anything but a worker, a dungeon heart's
/// centre takes a worker and refuses everything else. Both bypass the walkability refusal in
/// <c>HandSystem.Drop</c> — being undroppable is what the tiles would otherwise be — and
/// neither bypasses ownership. CONFIRMED throughout (owner, 2026-09-13 and 2026-09-15 with
/// screenshots of both halves; structure-blocks.md §4).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/structure-blocks.md")]
public static class DisposalRules
{
    /// <summary>Whether dropping <paramref name="mover"/> here gets rid of it. Keyed on
    /// <see cref="CreatureFlags.IsWorker"/>, as <see cref="HandRules.CanDropCreatureOn"/> is
    /// and for the same reason: the owner said <i>imps</i>, the flag also admits the Dwarf,
    /// and the difference is unreachable today.</summary>
    public static bool Disposes(
        WorldState world, GameDatabase database, CreatureDef mover, TilePos at, PlayerId playerId)
    {
        if (mover.Flags.HasFlag(CreatureFlags.IsWorker))
            return IsHeartCentreTile(world, database, at, playerId);

        return PortalRules.IsHoleTile(world, database, at, playerId);
    }

    /// <summary>Whether this tile is the <b>single centre tile</b> of a dungeon heart this
    /// player owns — <b>the one tile that takes an imp</b>, and not the raised 3x3 around it
    /// that <see cref="StructureBlocks"/> makes unwalkable. CONFIRMED (owner, 2026-09-15, two
    /// screenshots), which also settles that a heart is <b>always 5x5</b>, never smaller.</summary>
    public static bool IsHeartCentreTile(
        WorldState world, GameDatabase database, TilePos at, PlayerId playerId)
    {
        if (!world.Contains(at)) return false;

        ref TileState tile = ref world.Tile(at.X, at.Y);
        if (tile.OwnerPlayerId != playerId) return false;
        if (tile.TerrainId != database.Room(RoomTypeId.DungeonHeart).TerrainId) return false;

        int index = (at.Y * world.Width) + at.X;
        RoomInstance? heart = RoomIndex.RoomAt(world, database, index);
        if (heart is null || heart.Definition.TypeId != RoomTypeId.DungeonHeart) return false;

        return at.X == (heart.MinX + heart.MaxX) / 2
            && at.Y == (heart.MinY + heart.MaxY) / 2;
    }
}
