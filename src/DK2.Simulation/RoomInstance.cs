// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// One room as a <i>thing</i>, rather than as a scattering of tiles that happen
/// to share a terrain type.
///
/// <para><b>Why this has to exist.</b> Until now a room here was terrain, tile by
/// tile, and nothing could ask the one question most room rules turn out to
/// need: <i>how big is the room this tile belongs to, and where is this tile
/// within it?</i> Five separate findings sat unimplementable on that gap — a
/// dungeon heart storing 16000 rather than 25000 (Q52), a temple's capacity
/// being its edge tiles (Q60), a graveyard's graves being its interior ring
/// (Q60), a room needing a minimum size to function at all (Q45), and creature
/// attraction wanting a room's tile count (Q61).</para>
///
/// <para><b>Derived, never stored.</b> An instance is a connected group of tiles
/// with the same room terrain and the same owner, recomputed from the tiles
/// whenever it is asked for. That is a deliberate choice over keeping room
/// membership in <see cref="TileState"/>:</para>
/// <list type="bullet">
/// <item><b>It cannot drift.</b> Stored membership has to be maintained on every
/// path that changes a tile — built, sold, dug out, destroyed, claimed by a
/// rival — and the first one anybody forgets leaves a room that disagrees with
/// the map.</item>
/// <item><b>Splitting and merging come free.</b> Cut a treasury in half and it is
/// two treasuries on the next query, with no split logic to write and no
/// opportunity to write it wrong. Same for a tile that joins two rooms into
/// one.</item>
/// <item><b>It stays out of the determinism hash</b>, because it is a pure
/// function of state that is already hashed. No new field, no snapshot format
/// change, no <c>HashVersion</c> bump — and nothing that could make two machines
/// disagree.</item>
/// </list>
///
/// <para><b>Connectivity is orthogonal.</b> Two tiles joined only at a corner are
/// two rooms. STRONGLY INFERRED rather than observed: it is what the original
/// looks like, and it matches the tile work that builds rooms in the first place
/// (Q27, work is orthogonal). A diagonal-joined reading would make an L of
/// separate rooms into one and is the less likely of the two.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public sealed class RoomInstance
{
    internal RoomInstance(RoomDef definition, byte ownerPlayerId, int[] tileIndices,
                          int minX, int minY, int maxX, int maxY)
    {
        Definition = definition;
        OwnerPlayerId = ownerPlayerId;
        TileIndices = tileIndices;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    /// <summary>What kind of room this is.</summary>
    public RoomDef Definition { get; }

    public byte OwnerPlayerId { get; }

    /// <summary>
    /// Every tile in this room, in ascending tile-index order.
    ///
    /// The order is fixed so that anything iterating a room is reproducible,
    /// which matters the moment a rule has to pick <i>one</i> tile of several.
    /// </summary>
    public IReadOnlyList<int> TileIndices { get; }

    public int TileCount => TileIndices.Count;

    // The smallest axis-aligned box containing the room. A room need not fill
    // it — an L-shaped treasury has a box with empty corners — so a rule that
    // cares about shape must ask the tiles, not the box.
    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }

    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;

    /// <summary>Whether the room fills its own bounding box — i.e. it is a solid rectangle.</summary>
    public bool IsRectangular => TileCount == Width * Height;

    /// <summary>
    /// Whether this room is big enough to do anything.
    ///
    /// <para><b>STRONGLY INFERRED, and the field is badly named.</b>
    /// <see cref="RoomDef.RecommendedSizeX"/> does not recommend anything and
    /// does not stop a keeper building smaller — a 1x1 temple really is
    /// buildable (Q36) — it is the size below which the room does nothing. Two
    /// owner observations match it, made in different sittings and neither with
    /// the field in view: a graveyard needs 3x3 before a single grave appears,
    /// and the record says 3x3; a temple was flagged as needing 5x5, and the
    /// record says 5x5. See Q45.</para>
    ///
    /// <para>Measured against the bounding box rather than the tile count, so an
    /// L-shape wide enough in both directions counts. Which of those the original
    /// does is UNKNOWN and no observation distinguishes them.</para>
    /// </summary>
    public bool IsFunctional =>
        Width >= Definition.RecommendedSizeX && Height >= Definition.RecommendedSizeY;

    /// <summary>
    /// Tiles with at least one orthogonal neighbour outside the room — the rim.
    ///
    /// <para><b>This is a temple's capacity</b> (Q60), CONFIRMED against three
    /// sizes the owner read off the original's own tooltip: 3x3 holds 8, 4x4
    /// holds 12, 5x5 holds 16. For a solid rectangle the rim is
    /// <c>2(w+h) − 4</c>, which is exactly those three numbers, and counting the
    /// boundary rather than evaluating the formula means an L-shaped temple
    /// answers sensibly too. It also fits what the room is: a pool with a
    /// walkway, and creatures stand on the walkway.</para>
    /// </summary>
    public int EdgeTileCount { get; internal set; }

    /// <summary>Tiles wholly surrounded by the room — <see cref="TileCount"/> less the rim.</summary>
    public int InteriorTileCount => TileCount - EdgeTileCount;

    /// <summary>
    /// Whether a tile is one of the middle nine — the raised platform a dungeon
    /// heart's structure stands on.
    ///
    /// <para><b>This is what makes a heart hold 16000 and not 25000</b> (Q52).
    /// CONFIRMED by the owner: a keeper with no treasury tops out at 16000, and
    /// <c>MaxGoldPerDungeonHeartTile</c> is 1000, so sixteen of a heart's
    /// twenty-five tiles store gold. 25 − 16 = 9 = 3x3, and a screenshot of the
    /// original shows exactly that: a raised 3x3 centre with gold piles ringing
    /// it.</para>
    ///
    /// <para>Computed from the bounding box's centre rather than from a shape
    /// table, so it needs no notion of the heart's <c>FiveByFiveRotated</c>
    /// construction — which is a field we do not read. A room narrower than 3 in
    /// either direction has no middle to speak of and answers false throughout,
    /// which keeps a half-destroyed heart from swallowing its own storage.</para>
    /// </summary>
    public bool IsCentrePlatform(int x, int y)
    {
        if (Width < 3 || Height < 3) return false;

        int cx = (MinX + MaxX) / 2;
        int cy = (MinY + MaxY) / 2;
        return Math.Abs(x - cx) <= 1 && Math.Abs(y - cy) <= 1;
    }
}
