// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

public readonly record struct KeeperTileView(byte TerrainId, byte OwnerPlayerId, PlayerMask TaggedBy, byte NavigationByte)
{
    internal TileState ToTileState() => new()
    {
        TerrainId = TerrainId, OwnerPlayerId = OwnerPlayerId, TaggedBy = TaggedBy,
    };

    public bool IsTaggedBy(PlayerId playerId) => TaggedBy.Contains(playerId);
    public bool IsTaggedByAnyone => !TaggedBy.IsEmpty;
}
