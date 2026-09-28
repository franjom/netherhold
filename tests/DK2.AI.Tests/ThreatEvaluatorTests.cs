// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.AI.Tests;

public class ThreatEvaluatorTests
{
    [Theory]
    [InlineData(1, -100, 101)]
    [InlineData(2, 125, 126)]
    [InlineData(2, -125, -126)]
    public void ThreatUsesSignedPercentageExceptAtLevelOne(byte level, short percent, int expected)
        => Assert.Equal(expected, ThreatEvaluator.CreatureThreat(101, level, percent));

    [Fact]
    public void RadiusIsStrictAndCircular()
    {
        Assert.False(ThreatEvaluator.WithinRadius(3 * 4096, 4 * 4096, 0, 0, 5 * 4096));
        Assert.True(ThreatEvaluator.WithinRadius(3 * 4096 - 1, 4 * 4096, 0, 0, 5 * 4096));
        Assert.False(ThreatEvaluator.WithinRadius(4 * 4096, 4 * 4096, 0, 0, 5 * 4096));
    }

    [Fact]
    public void EachAxisIsTruncatedBeforeAdding()
    {
        // 46 squared on each axis sums to 4232, but both individual >>12 terms are zero.
        Assert.True(ThreatEvaluator.WithinRadius(46, 46, 0, 0, 64));
        Assert.False(ThreatEvaluator.WithinRadius(64, 0, 0, 0, 64));
    }

    [Fact]
    public void TwelveTileRadiusOverflowsBeforeArithmeticShiftAndUnsignedComparison()
    {
        Assert.True(ThreatEvaluator.WithinRadius(100 * 4096, 0, 0, 0, 12 * 4096));
        Assert.False(ThreatEvaluator.WithinRadius(100 * 4096, 0, 0, 0, 10 * 4096));
    }

    [Fact]
    public void LocalStrengthExcludesOffMapSourcesAndTrapDefinitionTwo()
    {
        KeeperThreatSource[] sources =
        [
            new(3, 0, 0, 10, 0), new(3, 0, 0, 20, 1), new(3, 0, 0, 40, 2),
            new(3, 0, 0, 80, 3), new(4, 0, 0, 160, 0),
            new(3, 0, 0, 320, 0, true, 3), new(3, 0, 0, 640, 0, true, 2),
            new(3, 0, 0, 1280, 2, true, 3), new(3, 0, 0, 2560, 0, true, 1)
        ];
        Assert.Equal(110, ThreatEvaluator.LocalStrength(sources, 3, 0, 0, 4096, false));
        Assert.Equal(2990, ThreatEvaluator.LocalStrength(sources, 3, 0, 0, 4096, true));
    }

    [Fact]
    public void MessageKindsUseDifferentRadiiAndRoomMessagesNeverIncludeTraps()
    {
        var target = new TilePos(20, 20);
        const int centre = 20 * 4096 + 2048;
        KeeperThreatSource[] sources =
        [
            new(1, centre + 6 * 4096, centre, 10, 0),
            new(2, centre, centre, 20, 0),
            new(3, centre + 4 * 4096, centre, 30, 0),
            new(4, centre, centre, 40, 0, true, 3),
            new(5, centre, centre, 50, 0, true, 4)
        ];
        byte[] order = [1, 2, 3, 4, 5];
        bool Allied(byte player) => player is 3 or 5;
        Assert.Equal(new KeeperThreatAssessment(70, 50, 4),
            ThreatEvaluator.Assess(sources, order, Allied, target, false, true));
        Assert.Equal(new KeeperThreatAssessment(20, 30, 2),
            ThreatEvaluator.Assess(sources, order, Allied, target, true, true));
        Assert.Equal(new KeeperThreatAssessment(30, 0, 2),
            ThreatEvaluator.Assess(sources, order, Allied, target, false, false));
    }

    [Fact]
    public void AttackerIsLastNonzeroEnemyInSuppliedPlayerOrder()
    {
        KeeperThreatSource[] sources = [new(1, 2048, 2048, 10, 0), new(2, 2048, 2048, 20, 0)];
        Assert.Equal((byte)1, ThreatEvaluator.Assess(sources, new byte[] { 2, 1, 4 },
            _ => false, new TilePos(0, 0), false, false).AttackingPlayer);
    }

    [Fact]
    public void DropSitePreservesRoomScoringBugAndInclusiveSearchEdge()
    {
        var target = new TilePos(20, 20);
        Assert.Equal(target, ThreatEvaluator.DropSite(target, false, _ => true));
        Assert.Equal(new TilePos(20, 25), ThreatEvaluator.DropSite(target, true, _ => true));
        Assert.Equal(new TilePos(25, 25), ThreatEvaluator.DropSite(target, false,
            p => p == new TilePos(25, 25) || p == new TilePos(26, 20)));
    }

    [Fact]
    public void EqualScoresKeepFirstRowThenColumnAndZeroCoordinateDoesNotRetry()
    {
        var target = new TilePos(20, 20);
        Assert.Equal(new TilePos(20, 19), ThreatEvaluator.DropSite(target, false,
            p => p == new TilePos(20, 19) || p == new TilePos(19, 20)));
        Assert.Equal(new TilePos(19, 20), ThreatEvaluator.DropSite(target, false,
            p => p == new TilePos(19, 20) || p == new TilePos(21, 20)));
        Assert.Null(ThreatEvaluator.DropSite(new TilePos(0, 20), false,
            p => p == new TilePos(0, 20) || p == new TilePos(1, 20)));
    }

    [Theory]
    [InlineData(100, 100, true, false, false, 0)]
    [InlineData(100, 101, true, false, false, null)]
    [InlineData(100, 101, true, false, true, 0)]
    [InlineData(100, 50, false, false, false, null)]
    [InlineData(100, 50, false, false, true, 0)]
    [InlineData(100, 50, false, true, false, -50)]
    [InlineData(100, 0, false, true, false, 0)]
    [InlineData(100, 100, false, true, false, null)]
    [InlineData(100, -1, false, true, false, null)]
    [InlineData(40000, 0, true, false, false, -25536)]
    public void ResponseDistinguishesCancelledZeroAndNarrowedResiduals(int enemy, int ally,
        bool hasDrop, bool remove, bool room, int? expected)
    {
        var target = new TilePos(20, 20);
        var drop = new TilePos(21, 20);
        KeeperThreatResponse? response = ThreatEvaluator.Response(target, hasDrop ? drop : null,
            enemy, ally, 100, remove, room);
        Assert.Equal(expected, response.HasValue ? (int?)response.Value.RemainingThreat : null);
        if (response.HasValue) Assert.Equal(hasDrop ? drop : target, response.Value.Position);
    }

    [Fact]
    public void SuperiorityDivisionPrecedesSubtractingAllies()
    {
        var tile = new TilePos(20, 20);
        Assert.Equal((short)1, ThreatEvaluator.Response(tile, tile, 3, 2, 125, false, false)!.Value.RemainingThreat);
    }
}
