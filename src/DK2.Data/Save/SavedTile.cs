// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Save;

/// <summary>One tile of a saved game's 10-byte runtime grid record (Q181,
/// docs/research/sav-format.md). Four lanes are identified, each settled against
/// evidence outside the file; the rest ride in <see cref="Raw"/> unguessed.</summary>
/// <param name="TerrainId">Lane 0: the same enum the MAP(100) record uses.</param>
/// <param name="OwnerPlayerId">Lane 8: a reserved id, 0-7 — see <c>PlayerIds</c>.</param>
/// <param name="RoomId">Lane 2: the room instance (0 for none), not the type. Tiles
/// sharing a value form one contiguous room whose tile set includes surrounding wall.</param>
/// <param name="Health">Lanes 6-7, little-endian u16, in the units <c>Terrain.kwd</c>
/// states; a pristine tile carries its terrain's <c>StartingHealth</c> exactly.</param>
/// <param name="Raw">All ten bytes, so a partial reading loses nothing. Unidentified:
/// lanes 1, 3 and 9 change across most of the map between any two saves (consistent
/// with visibility or fog), and lanes 4 and 5 are non-zero only on room tiles.</param>
public readonly record struct SavedTile(
    byte TerrainId,
    byte OwnerPlayerId,
    byte RoomId,
    ushort Health,
    ReadOnlyMemory<byte> Raw)
{
}
