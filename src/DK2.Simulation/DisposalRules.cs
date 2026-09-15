// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How a keeper throws a creature away. DK2 has <b>two</b> unwalkable targets and
/// they are exclusive: a portal's hole takes anything but a worker, and a dungeon heart's
/// platform takes a worker and nothing else (owner, 2026-09-13; structure-blocks.md §4).
/// Both bypass the walkability refusal in <c>HandSystem.Drop</c> — being undroppable is
/// what the tiles would otherwise be — and neither bypasses ownership.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/structure-blocks.md")]
public static class DisposalRules
{
    /// <summary>Whether dropping <paramref name="mover"/> here gets rid of it. Keyed on
    /// <see cref="CreatureFlags.IsWorker"/>, as <see cref="HandRules.CanDropCreatureOn"/> is
    /// and for the same reason: the owner said <i>imps</i>, the flag also admits the Dwarf,
    /// and the difference is unreachable today.</summary>
    public static bool Disposes(
        WorldState world, GameDatabase database, CreatureDef mover, TilePos at, byte playerId)
    {
        if (mover.Flags.HasFlag(CreatureFlags.IsWorker))
            return IsHeartPlatformTile(world, database, at, playerId);

        return PortalRules.IsHoleTile(world, database, at, playerId);
    }

    /// <summary>Whether this tile is the raised middle of a dungeon heart this player owns —
    /// the nine <see cref="RoomInstance.IsCentrePlatform"/> answers for, which is also the
    /// nine <see cref="StructureBlocks"/> makes unwalkable. Which of the nine DK2 accepts is
    /// UNKNOWN; all nine are the structure and taking the platform whole invents least.</summary>
    public static bool IsHeartPlatformTile(
        WorldState world, GameDatabase database, TilePos at, byte playerId)
    {
        if (!world.Contains(at)) return false;

        ref TileState tile = ref world.Tile(at.X, at.Y);
        if (tile.OwnerPlayerId != playerId) return false;
        if (tile.TerrainId != database.Room(RoomTypeId.DungeonHeart).TerrainId) return false;

        int index = (at.Y * world.Width) + at.X;
        RoomInstance? heart = RoomIndex.RoomAt(world, database, index);

        return heart is not null
            && heart.Definition.TypeId == RoomTypeId.DungeonHeart
            && heart.IsCentrePlatform(at.X, at.Y);
    }
}
