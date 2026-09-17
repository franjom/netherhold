// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>The mover's request and its five-point window (docs/research/steering-behaviours.md §7).</summary>
public class RouteWindowTests
{
    private static Fixed16Vector[] SevenPointsAlongX() =>
        Enumerable.Range(1, 7).Select(i => FakeSteeringWorld.Tiles(i + 0.5, 0.5)).ToArray();

    [Fact]
    public void LoadSplitsARouteIntoTheWindowAndTheTail()
    {
        var body = new SteeringBody { Pos = FakeSteeringWorld.Tiles(0.5, 0.5) };
        Fixed16Vector[] route = SevenPointsAlongX();

        RouteWindow.Load(body, route, route[^1], 0);

        Assert.Equal(7, body.PathTotalCount);
        Assert.Equal(5, body.PathBufferCount);
        Assert.Equal(112, body.PathTotalDist);
        Assert.Equal(64, body.PathSubpathDist);
        Assert.Equal(32, body.PathTailDist);
        Assert.Equal(new[] { 16, 16, 16, 16, 0 }, body.Segment);
        Assert.Equal(route[4], body.Subpath[4]);
        Assert.False(body.PathFinished);
        Assert.Equal(0, FollowPathRules.Progress(body));
    }

    [Fact]
    public void AnEmptyRouteFinishesThePathAtOnce()
    {
        var body = new SteeringBody { Pos = FakeSteeringWorld.Tiles(0.5, 0.5), PathTotalCount = 3, PathTotalDist = 40 };

        RouteWindow.Load(body, [], FakeSteeringWorld.Tiles(3.5, 0.5), 5);

        Assert.True(body.PathFinished);
        Assert.Equal(0, body.PathTotalCount);
        Assert.Equal(0, body.PathTotalDist);
        Assert.Equal(5, body.PathFlags);
        Assert.Equal(Fixed16.One, FollowPathRules.Progress(body));
    }

    [Fact]
    public void AdvanceDropsTheSegmentAndReportsTheWindowUsedUp()
    {
        var body = new SteeringBody { Pos = FakeSteeringWorld.Tiles(0.5, 0.5) };
        Fixed16Vector[] route = SevenPointsAlongX();
        RouteWindow.Load(body, route, route[^1], 0);

        for (int i = 0; i < 4; i++) Assert.False(RouteWindow.Advance(body));
        Assert.Equal(0, body.PathSubpathDist);
        Assert.True(RouteWindow.Advance(body));
        Assert.Equal(5, body.SubpathIndex);
    }

    /// <summary>Progress is what is behind the body over the whole route, in sixteenths.</summary>
    [Fact]
    public void ProgressIsOneMinusTheRemainingShare()
    {
        var body = new SteeringBody { Pos = FakeSteeringWorld.Tiles(0.5, 0.5) };
        Fixed16Vector[] route = SevenPointsAlongX();
        RouteWindow.Load(body, route, route[^1], 0);

        body.Pos = route[0];
        RouteWindow.Advance(body);
        body.Pos = route[1];

        Assert.Equal(Fixed16.One - Fixed16.Div(80, 112), FollowPathRules.Progress(body));
    }
}
