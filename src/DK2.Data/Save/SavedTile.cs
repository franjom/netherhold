// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Save;

/// <summary>
/// One tile of a saved game's runtime grid — the 10-byte record, as far as it
/// has been read.
///
/// <para>Four of the ten lanes are identified and each was settled against
/// something outside the file: terrain and owner against the level's design
/// map, health against <c>Terrain.kwd</c>'s own starting values, and the room
/// id against room footprints in a save built for the purpose. The rest are
/// carried as <see cref="Raw"/> rather than guessed. See
/// docs/research/sav-format.md and Q181.</para>
/// </summary>
/// <param name="TerrainId">Lane 0. The same enum the MAP(100) record uses.</param>
/// <param name="OwnerPlayerId">Lane 8. A reserved id, 0-7 — see <c>PlayerIds</c>.</param>
/// <param name="RoomId">
/// Lane 2. The room instance this tile belongs to, or 0 for none.
///
/// <para><b>An instance, not a type</b> — the type is already in the terrain
/// id. Tiles sharing a value form one contiguous room, and the room's tile set
/// includes part of its surrounding wall, which is why a room's id covers wall
/// terrain as well as floor.</para>
/// </param>
/// <param name="Health">
/// Lanes 6-7 as a little-endian u16: the tile's current health, in the units
/// <c>Terrain.kwd</c> states it in. A pristine tile carries its terrain's
/// <c>StartingHealth</c> exactly.
/// </param>
/// <param name="Raw">
/// All ten bytes, so nothing is lost to a partial reading. Lanes 1, 3 and 9
/// change across most of the map between any two saves — consistent with
/// visibility or fog state — and lanes 4 and 5 are non-zero only on room tiles.
/// None of the five is identified.
/// </param>
public readonly record struct SavedTile(
    byte TerrainId,
    byte OwnerPlayerId,
    byte RoomId,
    ushort Health,
    ReadOnlyMemory<byte> Raw)
{
}
