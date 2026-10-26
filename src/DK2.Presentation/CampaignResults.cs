// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>One statistics cell: a Text.str label and either a formatted value or a Text.str value.</summary>
public sealed record CampaignResultCell(uint LabelTextId, string Value, uint ValueTextId = 0);

/// <summary>The campaign statistics page's content from profile block 3 (<c>0x0053C520</c>,
/// <c>0x0053C830</c>, <c>0x0053CAF0</c>; docs/research/level-statistics.md §3–5).</summary>
public sealed record CampaignResults(IReadOnlyList<CampaignResultCell> Cells, uint RankTitleTextId, uint Total)
{
    private const int StatisticsRow = PlayerIds.FirstKeeper;

    public static CampaignResults? Build(CampaignProfile profile, CampaignSlot played)
    {
        if (profile.PlayerStatisticsValid[StatisticsRow] == 0 || played == CampaignSlot.None) return null;
        LevelStatisticsBlock b = LevelStatisticsBlock.FromDwords(profile.PlayerStatistics, StatisticsRow);
        uint seconds = b.TimeTaken, hours = seconds / 3600;
        string time = hours != 0 ? $"{hours:00} : {seconds / 60 % 60:00}" : $"{seconds / 60:00} : {seconds % 60:00}";
        (uint whole, uint hundredths) = b.TrainingParts;
        uint attempts = profile.LevelAttempts[played.Value - 1] + (b.LevelWon ? 1u : 0u);
        var cells = new[]
        {
            new CampaignResultCell(1516, "", b.LevelWon ? 21u : 22u),
            new CampaignResultCell(1410, time),
            new CampaignResultCell(1508, b.DungeonHeartsDestroyed.ToString()),
            new CampaignResultCell(1413, b.EnemyCreaturesKilled.ToString()),
            new CampaignResultCell(1509, b.HeroesDestroyed.ToString()),
            new CampaignResultCell(1510, b.RoomsTakenOver.ToString()),
            new CampaignResultCell(1511, b.LandOwned.ToString()),
            new CampaignResultCell(1411, b.GoldMined.ToString()),
            new CampaignResultCell(1512, b.ManaStored.ToString()),
            new CampaignResultCell(1513, b.ItemsManufactured.ToString()),
            new CampaignResultCell(1514, b.CreaturesCommanded.ToString()),
            new CampaignResultCell(1429, b.CreaturesConverted.ToString()),
            new CampaignResultCell(1515, $"{whole}.{hundredths}"),
            new CampaignResultCell(1636, attempts.ToString()),
        };
        // In mode 1 the page prints the slot's Total Evil Rating (0x00561CC0), not its own sum.
        return new(cells, KeeperRank.TitleTextId(profile.PlayerValues[StatisticsRow]), profile.TotalEvilRating[played.Value - 1]);
    }

    /// <summary>Remake placeholder layout inside widget 179 (DK2's own positions, past <c>0x0053CE40</c>,
    /// are unread): a header line, then two columns of seven label/value pairs, in 2560 × 1920 units.</summary>
    public static (FrontEndRect Label, FrontEndRect Value) CellRect(FrontEndRect table, int index)
    {
        const float rows = 8;
        float columnWidth = table.Width / 2, rowHeight = table.Height / rows;
        int column = index / 7, row = index % 7 + 1;
        float x = table.X + column * columnWidth, y = table.Y + row * rowHeight;
        return (new(x, y, columnWidth * .7f, rowHeight), new(x + columnWidth * .7f, y, columnWidth * .25f, rowHeight));
    }

    public static (FrontEndRect Rank, FrontEndRect Total) HeaderRects(FrontEndRect table)
    {
        float rowHeight = table.Height / 8;
        return (new(table.X, table.Y, table.Width * .6f, rowHeight), new(table.X + table.Width * .6f, table.Y, table.Width * .35f, rowHeight));
    }
}
