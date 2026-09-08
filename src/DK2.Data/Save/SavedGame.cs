// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Data.Save;

/// <summary>The five header fields this project reads. The header runs to 0x134, its
/// layout STRONGLY INFERRED from DK2's own save routine, not guessed from samples; its
/// two opaque runs and the block before the grid are undecoded (Q183, sav-format.md).</summary>
/// <param name="FormatWord">Offset 0, always 220. Not a length — the save routine
/// emits it as a literal, and the header runs to 0x134.</param>
/// <param name="Version">Offset 4, 1 in both samples. Meaning unproven.</param>
/// <param name="BuildStamp">Offset 8: the <c>__DATE__ __TIME__</c> of the DKII.EXE
/// that wrote the save. DK2 uses it as a version guard and so does this reader.</param>
/// <param name="LevelName">Offset 0xC2: the stem of the level's map files with no
/// extension, e.g. "mpd1".</param>
/// <param name="GameTick">Offset 0x102: the tick the game was on when it saved, from
/// the world's own tick getter. 8480 and 2598 in the two reference saves.</param>
public sealed record Dk2SaveHeader(
    uint FormatWord,
    uint Version,
    string BuildStamp,
    string LevelName,
    uint GameTick)
{
    /// <summary>Whether the writing build is the one whose layout is proven.</summary>
    public bool IsSupportedBuild =>
        BuildStamp == Dk2SaveReader.SupportedBuildStamp;
}

/// <summary>A loaded saved game: the dungeon as the player left it, terrain and
/// ownership only. Not implemented: creatures, objects, traps and doors, because the
/// typed sections after the grid serialise each entity themselves (Q182).</summary>
/// <param name="Header">What the save says about itself.</param>
/// <param name="Map">The grid in the shape <c>GameDatabaseLoader.LoadCampaignMap</c>
/// returns, so a renderer or simulation takes one unchanged.</param>
/// <param name="TerrainAgreement">The share of tiles whose terrain still matches the
/// design map — the read's own sanity check, not a fact about the save; see
/// <see cref="Dk2SaveReader.AgreementWithDesign"/> for a plausible value.</param>
/// <param name="Tiles">The same grid as <paramref name="Map"/> plus the lanes
/// <c>LevelMap</c> has nowhere to put: room instance and health.</param>
public sealed record SavedGame(
    Dk2SaveHeader Header,
    LevelMap Map,
    SavedTile[] Tiles,
    double TerrainAgreement,
    int GridOffset)
{
    /// <summary>Whoever the saved grid says owns something — the players to build.</summary>
    public byte[] PlayerIds
    {
        get
        {
            var seen = new SortedSet<byte>();
            foreach (MapTile tile in Map.Tiles) seen.Add(tile.PlayerId);

            var players = new byte[seen.Count];
            seen.CopyTo(players);
            return players;
        }
    }
}
