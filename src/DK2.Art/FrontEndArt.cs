// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Art;

/// <summary>The world resource requests of 0x004DFAA0, behind logical front-end asset keys.</summary>
public static class FrontEndArt
{
    public static IReadOnlyList<ArtEntry> Entries(GameDatabase database)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        var entries = new List<ArtEntry>();
        foreach (FrontEndMapPiece piece in Enum.GetValues(typeof(FrontEndMapPiece)))
        {
            string name = piece <= FrontEndMapPiece.Level21
                ? "3dmap_" + piece.ToString().ToLowerInvariant()
                : "Secret_Level" + ((int)piece - (int)FrontEndMapPiece.Level21);
            entries.Add(new ArtEntry(FrontEndAssets.CampaignPiece(piece), ArtUse.Model,
                new Dk2ArtResource(name, 5), "front-end campaign land"));
        }
        foreach (PetDungeonMapPiece piece in Enum.GetValues(typeof(PetDungeonMapPiece)))
            entries.Add(new ArtEntry(FrontEndAssets.PetDungeonPiece(piece), ArtUse.Model,
                new Dk2ArtResource("MPD_map" + (int)piece, 5), "front-end pet dungeon land"));
        return entries;
    }
}
