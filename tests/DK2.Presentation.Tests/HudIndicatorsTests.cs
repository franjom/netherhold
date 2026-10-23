// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The indicator row 0x00427BA0 packs above the bottom panel and the message panel
/// (menu40) it pushes up (message-tabs.md §6, §9).</summary>
public sealed class HudIndicatorsTests
{
    private static HudIndicatorSnapshot Shown(bool time = false, bool score = false, bool kills = false, bool next = false) =>
        new(time, 400, score, 0, kills, next);

    [Fact]
    public void ShownIndicatorsPackLeftToRightFromTwentyAndHiddenOnesTakeNoSlot()
    {
        var placed = HudIndicatorLayout.Place(Shown(time: true, kills: true, next: true), panelTop: 1460);
        Assert.Equal(new[]
        {
            (HudIndicator.TimeRemaining, 20, 1332),
            (HudIndicator.HeroesKilled, 464, 1332),
            (HudIndicator.NextRoom, 908, 1332),
        }, placed);
    }

    [Fact]
    public void OnlyTheFirstIndicatorKeepsY1332WhenThePanelTopMoves()
    {
        var placed = HudIndicatorLayout.Place(Shown(time: true, score: true), panelTop: 1800);
        Assert.Equal((HudIndicator.TimeRemaining, 20, 1332), placed[0]);
        Assert.Equal((HudIndicator.Score, 464, 1672), placed[1]);
    }

    [Fact]
    public void CameraClassOneStacksThemUpwardFrom1332()
    {
        var placed = HudIndicatorLayout.Place(Shown(time: true, score: true, kills: true), panelTop: 1460, stacked: true);
        Assert.Equal(new[] { 1332, 1204, 1076 }, placed.Select(p => p.Y));
        Assert.All(placed, p => Assert.Equal(20, p.X));
    }

    [Fact]
    public void TheMessagePanelSits40AboveTheStripOr168WhileAnIndicatorShows()
    {
        Assert.False(Shown().Any);
        Assert.True(Shown(kills: true).Any);
        Assert.Equal((1020, 1460 - 272 - 40, 1200, 272), HudIndicatorLayout.MessagePanel(1460, 100, indicators: false));
        Assert.Equal((1020, 1460 - 272 - 168, 1200, 272), HudIndicatorLayout.MessagePanel(1460, 100, indicators: true));
        // The fit is strict: text 288 tall overflows to 440.
        Assert.Equal(440, HudIndicatorLayout.MessagePanel(1460, 288, false).Height);
        Assert.Equal(1460 - 608, HudIndicatorLayout.MessagePanel(1460, 300, true).Y);
    }

    [Fact]
    public void TheTextBoxFrameInsetsTheMenuByTenPixels()
    {
        Assert.Equal((110f, 210f, 280f, 80f), HudIndicatorLayout.FrameInner(100, 200, 300, 100, pixel: 1));
        Assert.Equal((120f, 220f, 260f, 60f), HudIndicatorLayout.FrameInner(100, 200, 300, 100, pixel: 2));
    }

    [Theory]
    [InlineData(4, "1@10")]                       // 1 s: the units only
    [InlineData(1, "1@10")]                       // rounded up
    [InlineData(40, "0@10 1@9")]                  // 10 s
    [InlineData(240, "0@10 0@9 1@7")]             // 1:00, the seconds' tens shown once minutes are
    [InlineData(4 * 754, "4@10 3@9 2@7 1@6")]     // 12:34
    [InlineData(4 * 6000, "0@10 0@9 0@7 10@6")]   // 100:00, the tens run as one number
    public void TimeRemainingDigitsFillTheirSlots(int turns, string expected)
    {
        string actual = string.Join(" ", HudIndicatorLayout.TimeRemainingDigits(turns, 4).Select(p => $"{p.Digits}@{p.Slots}"));
        Assert.Equal(expected, actual);
        Assert.Equal(8, HudIndicatorLayout.AlignedStart(2, 10));
    }

    private static LevelDef Level(ushort flags) => new([])
    {
        Name = "fixture", Description = "", Author = "", Email = "", Information = "",
        Unknown_1184 = ReadOnlyMemory<byte>.Empty, Messages = [], SoundCategory = "",
        PreviousRewards = ReadOnlyMemory<byte>.Empty, NextRewards = ReadOnlyMemory<byte>.Empty,
        IntroductionOverrideObjects = ReadOnlyMemory<byte>.Empty, IntroductionOverrideTextIds = [],
        TerrainPath = "", Unknown_6403 = new byte[] { 0 }, Paths = [], UnknownPathTail = [], Flags = flags,
    };

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void NextRoomShowsForRoomTypes1To25(byte room, bool shown)
    {
        var triggers = new TriggerState { SpecialInfoNextRoomTypeId = room };
        HudIndicatorSnapshot snapshot = HudIndicatorSnapshot.Capture(triggers, null);
        Assert.Equal(shown, snapshot.NextRoom);
        Assert.Equal(shown, snapshot.Any);
    }

    [Theory]
    [InlineData(0x0080, true)]
    [InlineData(0xFF7F, false)]
    public void HeroesKilledShowsOnLevelFlag0x80Only(int flags, bool shown)
    {
        Assert.Equal(shown, HudIndicatorSnapshot.Capture(new TriggerState(), Level((ushort)flags)).HeroesKilled);
        Assert.False(HudIndicatorSnapshot.Capture(new TriggerState(), null).HeroesKilled);
    }

    [Fact]
    public void TimeRemainingShowsWhileTheLimitIsNonZero()
    {
        Assert.False(HudIndicatorSnapshot.Capture(new TriggerState(), null).TimeRemaining);
        HudIndicatorSnapshot running = HudIndicatorSnapshot.Capture(new TriggerState { LevelTimeLimit = 37 }, null);
        Assert.True(running.TimeRemaining);
        Assert.Equal(37, running.TimeLimitTurns);
    }

    [Fact]
    public void ScoreShowsOnceIdOneTwentyEightIsWrittenEvenAtZero()
    {
        Assert.False(HudIndicatorSnapshot.Capture(new TriggerState(), null).Score);
        var triggers = new TriggerState();
        triggers.Write(TriggerState.SpecialFlagId, FlagWriteMode.Assign, 0);
        HudIndicatorSnapshot snapshot = HudIndicatorSnapshot.Capture(triggers, null);
        Assert.True(snapshot.Score);
        Assert.Equal(0, snapshot.ScoreValue);
        Assert.True(snapshot.Any);
    }
}
