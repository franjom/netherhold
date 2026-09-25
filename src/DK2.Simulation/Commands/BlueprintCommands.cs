// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>Places a door blueprint on one of this keeper's tiles (<c>0x004C33B0</c>, reached from
/// game action <c>0x00513210</c>; ADR-0231).</summary>
public sealed record PlaceDoorCommand(byte PlayerId, TilePos Tile, byte DoorId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tile.X);
        hash.Add(Tile.Y);
        hash.Add(DoorId);
    }
}

/// <summary>Places a trap blueprint on one of this keeper's tiles (<c>0x004C3860</c>; ADR-0231).</summary>
public sealed record PlaceTrapCommand(byte PlayerId, TilePos Tile, byte TrapId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tile.X);
        hash.Add(Tile.Y);
        hash.Add(TrapId);
    }
}
