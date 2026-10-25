// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Assets;

public enum FrontEndMapPiece : byte
{
    Level1 = 1, Level2, Level3, Level4, Level5, Level6a, Level6b, Level7, Level8, Level9, Level10,
    Level11a, Level11b, Level11c, Level12, Level13, Level14, Level15a, Level15b, Level16, Level17,
    Level18, Level19, Level20, Level21, Secret1, Secret2, Secret3, Secret4, Secret5,
}

public enum PetDungeonMapPiece : byte { First = 1, Second, Third, Fourth, Fifth, Sixth, Seventh }

public static class FrontEndAssets
{
    public static AssetRef CampaignPiece(FrontEndMapPiece piece) => Enum.IsDefined(typeof(FrontEndMapPiece), piece)
        ? AssetRef.Create(AssetRef.UiCategory, "campaign_map", AssetKind.Mesh, piece.ToString())
        : throw new ArgumentOutOfRangeException(nameof(piece));

    public static AssetRef CampaignMarker(CampaignSlot slot) => slot.IsCampaign
        ? AssetRef.Create(AssetRef.UiCategory, "campaign_marker", AssetKind.Mesh, slot.LevelName!)
        : throw new ArgumentOutOfRangeException(nameof(slot));

    public static AssetRef PetDungeonPiece(PetDungeonMapPiece piece) => Enum.IsDefined(typeof(PetDungeonMapPiece), piece)
        ? AssetRef.Create(AssetRef.UiCategory, "pet_dungeon_map", AssetKind.Mesh, piece.ToString())
        : throw new ArgumentOutOfRangeException(nameof(piece));

    public static AssetRef EntranceCap(string roomName) => AssetRef.ForRoom(roomName, AssetKind.Mesh, "entrance_cap");
}
