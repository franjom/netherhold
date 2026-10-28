// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The ten-row hi-score table: qualification (<c>0x0053E240</c>), the row a score takes and
/// the shift below it (<c>0x0053E260</c>, <c>0x0053E610</c>), and the level label
/// (<c>0x0053E320</c>).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/campaign-map-and-progression.md")]
public static class HiScoreTable
{
    public const int Rows = 10;
    public const int MaxText = 31;

    public static IReadOnlyList<HiScoreEntry> Empty { get; } =
        Array.AsReadOnly(Enumerable.Range(0, Rows).Select(_ => new HiScoreEntry(0, "", "")).ToArray());

    /// <summary>A score qualifies when it is at least the tenth row's.</summary>
    public static bool Qualifies(IReadOnlyList<HiScoreEntry> rows, uint rating) => rows[Rows - 1].EvilRating <= rating;

    /// <summary>The first row whose score the new one meets or beats.</summary>
    public static int Position(IReadOnlyList<HiScoreEntry> rows, uint rating)
    {
        for (int i = 0; i < Rows; i++)
            if (rows[i].EvilRating <= rating) return i;
        return -1;
    }

    public static IReadOnlyList<HiScoreEntry> Insert(IReadOnlyList<HiScoreEntry> rows, int position, HiScoreEntry entry)
    {
        var next = rows.ToList();
        next.Insert(position, entry);
        next.RemoveAt(Rows);
        return next.AsReadOnly();
    }

    /// <summary>The label 0x0053E320 builds: the level number, the branch maps' own strings, or for a
    /// secret level its briefing string 1 (through 0x00553B90, unread, so truncation is ours).</summary>
    public static string Label(CampaignSlot slot, string secretName)
    {
        if (slot.IsSecret) return secretName.Length > MaxText ? secretName.Substring(0, MaxText) : secretName;
        return slot.Value switch
        {
            6 => "6a", 7 => "6b", 12 => "11a", 13 => "11b", 14 => "11c", 18 => "15a", 19 => "15b",
            >= 8 and <= 11 => (slot.Value - 1).ToString(),
            >= 15 and <= 17 => (slot.Value - 3).ToString(),
            >= 20 and <= 24 => (slot.Value - 4).ToString(),
            _ => slot.Value.ToString(),
        };
    }
}
