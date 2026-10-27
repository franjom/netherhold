// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>One statistics cell: a Text.str label and either a formatted value or a Text.str value.</summary>
public sealed record CampaignResultCell(uint LabelTextId, string Value, uint ValueTextId = 0);

/// <summary>One text on the statistics page: where (2560 × 1920 units), what, and in which of the front
/// end's colours.</summary>
public readonly record struct CampaignResultText(FrontEndRect Rect, uint TextId, string? Text, FrontEndTextColour Colour,
    FrontEndTextAlignment Alignment = FrontEndTextAlignment.Left);

/// <summary>The front end's text colours (<c>0x00533E90</c>, five bytes B, G, R, A, 0) and the two fixed ones
/// its pages pass.</summary>
public enum FrontEndTextColour : byte
{
    /// <summary><c>+0x3031E</c> (and <c>+0x5FF4</c>, its copy): copied from the palette object
    /// <c>0x00756F34</c>, whose constructor <c>0x00559820</c> makes it white (LIKELY,
    /// functions/00533E90.md).</summary>
    Label,
    /// <summary><c>+0x30323</c>: (255, 168, 0).</summary>
    Value,
    /// <summary><c>+0x3032D</c>: (191, 191, 191).</summary>
    Grey,
    /// <summary><c>+0x30332</c>: (255, 84, 0).</summary>
    Rank,
    /// <summary>The fixed (255, 0, 0) of <c>0x0053CAF0</c>'s score and page 18's entry row.</summary>
    Red,
}

/// <summary>The campaign statistics page's content from profile block 3 (<c>0x0053C520</c>,
/// <c>0x0053C830</c>, <c>0x0053CAF0</c>, <c>0x0053D270</c>; docs/research/level-statistics.md §3–5).</summary>
public sealed record CampaignResults(IReadOnlyList<CampaignResultCell> Cells, uint RankTitleTextId, uint Total)
{
    private const int StatisticsRow = PlayerIds.FirstKeeper;
    /// <summary><c>0x0053D270</c> skips the row whose label is Text.str 1508 (Enemy Keepers Vanquished).</summary>
    private const uint HiddenRowLabel = 1508;
    /// <summary>Rows <c>0x0053D270</c> lays out from the scroll bar's first row.</summary>
    public const int ListedRows = 7;

    /// <summary>The sum of every campaign slot's Total Evil Rating (<c>0x00561CC0</c>, slots 1–25), printed in
    /// mode 1 outside a secret level; null on a secret level.</summary>
    public uint? Overall { get; init; }
    /// <summary>Block bytes <c>+0x41</c> and <c>+0x41</c> + <c>+0x42</c>, read signed: "%d / %d".</summary>
    public string Specials { get; init; } = "";

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
        uint overall = 0;
        for (int slot = 0; slot < 25; slot++) overall += profile.TotalEvilRating[slot];
        int found = (sbyte)b.SpecialsFound, missed = (sbyte)b.SpecialsMissed;
        // In mode 1 the page prints the slot's Total Evil Rating (0x00561CC0), not its own sum.
        return new(cells, KeeperRank.TitleTextId(profile.PlayerValues[StatisticsRow]), profile.TotalEvilRating[played.Value - 1])
        {
            Overall = played.Value <= 25 ? overall : null,
            Specials = $"{found} / {found + missed}",
        };
    }

    /// <summary><c>0x0053CAF0</c>'s summary at its fixed 640 × 480 boxes (left, top, right, bottom): the rank
    /// title at (191, 110); "Total Evil Rating For Level" (1641) at (191, 148) with the score in red at
    /// (460, 148); outside a secret level "Overall Total Evil Rating" (1637) at (191, 172) with the
    /// sum at (460, 172); "Specials Found" (1638) at (191, 194) with "found / found + missed" at (460, 194).
    /// The layout's alignment is the style's default, taken as left and top.</summary>
    public IEnumerable<CampaignResultText> Summary()
    {
        yield return new(Box(191, 110, 640, 480), RankTitleTextId, null, FrontEndTextColour.Rank);
        yield return new(Box(191, 148, 459, 170), 1641, null, FrontEndTextColour.Label);
        yield return new(Box(460, 148, 640, 170), 0, Total.ToString(), FrontEndTextColour.Red);
        if (Overall is uint overall)
        {
            yield return new(Box(191, 172, 459, 194), 1637, null, FrontEndTextColour.Grey);
            yield return new(Box(460, 172, 640, 194), 0, overall.ToString(), FrontEndTextColour.Grey);
        }
        yield return new(Box(191, 194, 459, 216), 1638, null, FrontEndTextColour.Grey);
        yield return new(Box(460, 194, 640, 216), 0, Specials, FrontEndTextColour.Grey);
    }

    /// <summary><c>0x0053D270</c> in the list widget's box: seven cells from <paramref name="first"/>, the
    /// hidden 1508 row taking no line; each label at the box's left, its value 250 pixels in, right-aligned
    /// (alignment 1, LIKELY right), a line every 25 pixels, clipped to the box: a line partly past its bottom
    /// is cut to what is inside. DK2 advances a line only when the value is not empty; every cell has a
    /// value, so the rule is not modelled.</summary>
    public IEnumerable<CampaignResultText> Rows(FrontEndRect list, int first)
    {
        float y = list.Y, bottom = list.Y + list.Height;
        for (int i = Math.Max(0, first); i < Math.Min(Cells.Count, first + ListedRows); i++)
        {
            CampaignResultCell cell = Cells[i];
            if (cell.LabelTextId == HiddenRowLabel) continue;
            if (y >= bottom) yield break;
            float height = Math.Min(list.Height, bottom - y);
            yield return new(new(list.X, y, list.Width, height), cell.LabelTextId, null, FrontEndTextColour.Label);
            yield return new(new(list.X + 250 * 4, y, list.Width - 250 * 4, height), cell.ValueTextId,
                cell.ValueTextId != 0 ? null : cell.Value, FrontEndTextColour.Value, FrontEndTextAlignment.Right);
            y += 25 * 4;
        }
    }

    /// <summary>The last first row the list can scroll to. The scroll bar's range is unread; ours stops once
    /// the last cell's line lies wholly inside the box.</summary>
    public int LastFirstRow(FrontEndRect list) => Math.Max(0, Cells.Count - Math.Max(1, (int)(list.Height / (25 * 4))));

    private static FrontEndRect Box(float left, float top, float right, float bottom) =>
        new(left * 4, top * 4, (right - left) * 4, (bottom - top) * 4);
}
