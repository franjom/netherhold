// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Save;

/// <summary>One tile of the ten-byte payload written from runtime tile +2.
/// See docs/research/sav-format.md. The high four bits of the room and owner
/// words stay in <see cref="Raw"/>.</summary>
/// <param name="TerrainId">Lane 8: MAP(100) terrain id.</param>
/// <param name="OwnerPlayerId">Lanes 6-7, low twelve bits: player id.</param>
/// <param name="RoomId">Lanes 0-1, low twelve bits: room instance id.</param>
/// <param name="Health">Lanes 4-5: tile health.</param>
/// <param name="Raw">The full ten-byte record.</param>
public readonly record struct SavedTile(
    byte TerrainId,
    byte OwnerPlayerId,
    ushort RoomId,
    ushort Health,
    ReadOnlyMemory<byte> Raw)
{
}
