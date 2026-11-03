// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The heart's beat sound, which DK2's per-tile presentation pass requests (<c>0x0043CE85</c>) where it
/// consumes a pending beat (<see cref="HeartPulseGate"/>): <c>0x150</c>, handle 0, in the category of the heart
/// room's terrain, at the heart object's position. It is asked for before the game-turn test that gates the
/// mana effect, so it sounds during the opening too (docs/research/sound-effects.md section 8).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/sound-effects.md#8-the-dungeon-heart")]
public static class HeartBeatSound
{
    public const uint EventId = 0x150;

    /// <summary>The category name, empty when the heart room has none.</summary>
    public static string CategoryOf(GameDatabase database) =>
        database.Rooms.TryGetValue(RoomTypeId.DungeonHeart, out RoomDef? room)
            ? database.TerrainById(room.TerrainId).SoundCategory : "";

    public static SoundRequest? For(string category, in ObjectView heart) =>
        category.Length == 0 ? null
            : new SoundRequest(new SoundCategory(category.ToLowerInvariant()), new SoundId(EventId),
                new Vector3(heart.PosX / 4096f, heart.Height / 4096f, -heart.PosY / 4096f));
}
