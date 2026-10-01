// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>The place a creature holds in a room: DK2's room id at creature <c>+0x3B9</c>, written
/// by the claim <c>0x004E5A70</c> and cleared by the release <c>0x004E5E00</c> (ADR-0309).</summary>
/// <remarks>Ours has no room ids (<see cref="RoomInstance"/> is derived), so a place names the
/// tile the creature stood on when it claimed, and belongs to whichever room holds that tile.
/// <see cref="Units"/> is what the claim added to the room's <c>UsedCapacity</c>: the
/// definition's <c>+0x672</c>, or 0 for a Combat Pit fighter, which links in without it.</remarks>
public readonly record struct RoomPlace(int TileIndex, int Units)
{
    /// <summary>No place: DK2's <c>+0x3B9</c> of 0.</summary>
    public static readonly RoomPlace None = new(-1, 0);

    public bool IsHeld => TileIndex >= 0;

    /// <summary>Whether this place is one of <paramref name="room"/>'s.</summary>
    public bool In(RoomInstance room) => IsHeld && room.Contains(TileIndex);
}
