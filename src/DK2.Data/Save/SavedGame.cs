// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Data.Save;

/// <summary>
/// The part of a saved game's header this project reads.
///
/// <para>The header runs to <b>0x134</b> and its layout is taken from DK2's own
/// save routine, not guessed from samples. Five fields are read; the two opaque
/// runs inside it, and the block between the header and the grid, are undecoded
/// (Q183). See docs/research/sav-format.md.</para>
/// </summary>
/// <param name="FormatWord">
/// The word the file opens with, always 220. <b>Not a length</b> — DK2's save
/// routine emits it as a literal, and the header runs to 0x134.
/// </param>
/// <param name="Version">The word after it, 1 in both samples. Meaning unproven.</param>
/// <param name="BuildStamp">
/// The <c>__DATE__ __TIME__</c> of the DKII.EXE that wrote the save. DK2 stores
/// it as a version guard and so does this reader.
/// </param>
/// <param name="LevelName">
/// The level the save belongs to, e.g. "mpd1" — the stem of its map files, with
/// no extension.
/// </param>
/// <param name="GameTick">
/// The tick the game was on when it saved, straight from the world's own tick
/// getter. 8480 and 2598 in the two reference saves.
/// </param>
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

/// <summary>
/// A loaded saved game: the dungeon as the player left it.
///
/// <para><b>Terrain and ownership only</b> — what follows the grid is a run of
/// typed sections whose entities each serialise themselves (Q182), so a save
/// carries no creatures, objects, traps or doors yet. A campaign level loads
/// with no occupants either, for the unrelated reason that parsed THINGS have
/// not yet been projected into <c>WorldState</c>, so this is not a new gap.</para>
/// </summary>
/// <param name="Header">What the save says about itself.</param>
/// <param name="Map">
/// The grid, in the same shape <c>GameDatabaseLoader.LoadCampaignMap</c>
/// returns, so anything that renders or simulates a level takes one unchanged.
/// </param>
/// <param name="TerrainAgreement">
/// The share of tiles whose terrain still matches the level's design map — the
/// read's own sanity check rather than a fact about the save. See
/// <see cref="Dk2SaveReader.AgreementWithDesign"/> for what a plausible value
/// looks like.
/// </param>
/// <param name="Tiles">
/// The same grid as <paramref name="Map"/> but with the lanes a
/// <c>LevelMap</c> has nowhere to put — room instance and health.
/// </param>
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
