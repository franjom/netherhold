// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The keeper rank <c>0x004C0280</c> computes at a won or lost level's end (0..100) and the
/// title the statistics page shows for it (docs/research/level-statistics.md §4). DK2 works in
/// x87 floating point and truncates with <c>__ftol</c>; the x87 precision mode in play is UNKNOWN,
/// so a quotient that lands on an integer may truncate differently. Kept out of the integer-only
/// simulation because nothing in play reads it.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/level-statistics.md")]
public static class KeeperRank
{
    private static readonly int[] HeartBonus = { 0, 6, 8, 9, 10 };
    private static readonly uint[] TitleLimits = { 17, 33, 50, 67, 82 };

    /// <param name="nativeTurn">The verdict's game turn as DK2 counts it.</param>
    /// <param name="averageTimeMinutes">Variable 242, <c>AverageTime</c>.</param>
    /// <param name="levelRating">Variable 241, <c>LevelRating</c>, applied as × 2^-12.</param>
    public static uint Compute(LevelStatisticsBlock block, long nativeTurn, int ticksPerSecond,
        int averageTimeMinutes, int levelRating)
    {
        // Every constant is a double in the image (0x0066D9A8..0x0066DA00); the training factor is 10/9.
        const double TenNinths = 10.0 / 9.0;
        int level = (int)block.CreatureTraining >> 12;
        int hearts = block.DungeonHeartsDestroyed < HeartBonus.Length ? HeartBonus[block.DungeonHeartsDestroyed] : 0;
        int kills = Trunc(133.0 / (block.EnemyCreaturesKilled + (double)block.HeroesDestroyed + 13.3));
        int s = Trunc(-83.0 / (block.CreaturesCommanded + 8.3)) - kills;
        s -= Trunc(33.0 / (block.RoomsTakenOver + 3.3));
        s -= Trunc(67.0 / (block.ItemsManufactured + 6.7));
        s += Trunc(level * TenNinths - TenNinths) + hearts + 40;
        if (block.LevelWon) s += 10;
        s += Trunc(90.0 / ((int)block.OwnCreaturesKilled + 9.0)); // the one field loaded signed
        // DK2 multiplies the turn by TicksPerSecond here where TimeTaken divides by it (UNKNOWN whether a slip).
        uint time = (uint)(nativeTurn * ticksPerSecond), average = (uint)averageTimeMinutes * 60;
        if (time <= average / 3) s += 10;
        else if (time < 2 * average) s += 12 - (int)(6 * time / average);
        s -= Trunc(33.0 / (block.CreaturesConverted + 3.3));
        s += 10;
        // s and the rating load as unsigned dwords; only the low dword of the product is compared.
        uint rank = (uint)(long)((double)(uint)s * (uint)levelRating * (1.0 / 4096));
        return rank < 100 ? rank : 100;
    }

    /// <summary>Text.str id of the rank's title, 2895 Sniveller .. 2900 Immortal (<c>0x0053CAF0</c>).</summary>
    public static uint TitleTextId(uint rank)
    {
        for (int i = 0; i < TitleLimits.Length; i++)
            if (rank <= TitleLimits[i]) return 2895u + (uint)i;
        return 2900;
    }

    private static int Trunc(double value) => (int)value;
}
