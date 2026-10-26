// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>What the front end plays on the way back from a won campaign map, before the statistics
/// page (<c>0x00534F70</c> → <c>0x0053B980</c>): a title card with the cutscene's name, then
/// <c>CutSceneLevel&lt;N&gt;.tgq</c>; Level 20 plays <c>Outro.tgq</c> and <c>BullfrogOutro.tgq</c> instead.</summary>
/// <param name="TitleTextId">Speech.str (file 1090) id of the title, 0 for none.</param>
/// <param name="SpeechId">The voiced line played under the title and waited for (sound, F8).</param>
public sealed record CampaignCutscene(uint TitleTextId, uint SpeechId, IReadOnlyList<string> Movies)
{
    /// <summary>Speech.str title ids of the 9-byte rows at <c>0x006B0DD0</c>, indexed by level number;
    /// row n's movie number is n. Row 0 (Intro) and row 21 are not reached from a win.</summary>
    private static readonly uint[] TitleIds =
    {
        0x4D, 0x589, 0x59F, 0x59B, 0x5A5, 0x594, 0x592, 0x596, 0x598, 0x5A1, 0x597,
        0x599, 0x58B, 0x586, 0x59D, 0x588, 0x58C, 0x58D, 0x5A3, 0x58E, 0xB1B,
    };

    /// <summary>The waits around the voiced line (<c>0x0053BA9B</c>, <c>0x0053BB72</c>), in milliseconds.</summary>
    public const int PauseMilliseconds = 250;

    /// <summary><c>0x0053C320</c>: map slot to level number (6a/6b → 6, 11a–c → 11, 15a/b → 15); 0 otherwise.</summary>
    public static int LevelNumber(CampaignSlot slot) => slot.Value switch
    {
        >= 1 and <= 6 => slot.Value,
        7 => 6,
        >= 8 and <= 12 => slot.Value - 1,
        13 or 14 => 11,
        >= 15 and <= 18 => slot.Value - 3,
        19 => 15,
        >= 20 and <= 24 => slot.Value - 4,
        _ => 0,
    };

    /// <summary>The cutscene <c>0x00534F70</c> plays on re-entering the front end: only after a
    /// campaign session (saved mode 1), not a secret level, with the played slot (below 25) won.
    /// Not mirrored: <c>0x0053B980</c> skips level 13 for languages 7 and 0x12; ours has no language.</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/campaign-map-and-progression.md")]
    public static CampaignCutscene? AfterLevel(CampaignProfile profile)
    {
        if (profile.SecretLevelNumber != 0) return null;
        int slot = profile.LevelNumber;
        if (slot is < 1 or > 24 || profile.PlayerLevelStatus[slot - 1] != CampaignLevelStatus.Won) return null;
        int level = LevelNumber(new CampaignSlot(slot));
        if (level == 20) return new(0, 0, new[] { "Data/Movies/Outro.tgq", "Data/Movies/BullfrogOutro.tgq" });
        uint title = TitleIds[level];
        return new(title, title + 1, new[] { $"Data/Movies/CutSceneLevel{level}.tgq" });
    }
}
