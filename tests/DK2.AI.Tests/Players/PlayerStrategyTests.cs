// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The strategy the campaign player's routines read (campaign bot stage 3 plan, Task 1).</summary>
public class PlayerStrategyTests
{
    [Fact]
    public void TheDefaultsAreTodaysOptions()
    {
        var options = new CampaignPlayerOptions(new PlayerRunId("strategy-test"), new PlayerId(3), [new CreatureTypeId(1)]);
        PlayerStrategy fromOptions = options.Strategy, plain = new PlayerStrategy();

        foreach (PlayerStrategy s in new[] { fromOptions, plain })
        {
            Assert.Equal(options.Rooms, s.Rooms);
            Assert.Equal(options.RoomSide, s.RoomSide);
            Assert.Equal(options.MaxOutstandingTags, s.MaxOutstandingTags);
            Assert.Equal(options.AdvanceTicks, s.AdvanceTicks);
            Assert.Equal(DigMode.GoldFirst, s.Dig);
            Assert.Null(s.DigTarget);
            Assert.Equal(FightMode.Engage, s.Fight);
            Assert.True(s.UseSpecials);
            Assert.Empty(s.Validate());
        }
    }

    [Fact]
    public void OptionsCarryTheirValuesIntoTheStrategy()
    {
        var options = new CampaignPlayerOptions(new PlayerRunId("strategy-test"), new PlayerId(3), [new CreatureTypeId(1)])
        {
            Rooms = [RoomTypeId.Lair],
            RoomSide = 4,
            MaxOutstandingTags = 5,
            AdvanceTicks = 8,
        };
        Assert.Equal([RoomTypeId.Lair], options.Strategy.Rooms);
        Assert.Equal((4, 5, 8), (options.Strategy.RoomSide, options.Strategy.MaxOutstandingTags, options.Strategy.AdvanceTicks));
    }

    [Fact]
    public void ValidationNamesEveryProblem()
    {
        var bad = new PlayerStrategy
        {
            Rooms = [RoomTypeId.Lair, RoomTypeId.Lair],
            RoomSide = 0,
            MaxOutstandingTags = 0,
            AdvanceTicks = 41,
            Dig = DigMode.Toward,
        };
        IReadOnlyList<string> problems = bad.Validate();
        Assert.Equal(5, problems.Count);
        Assert.Contains(problems, p => p.Contains("RoomSide", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("MaxOutstandingTags", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("AdvanceTicks", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("twice", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("DigTarget", StringComparison.Ordinal));
    }

    [Fact]
    public void ATargetOffTheMapIsRefusedAndAnEmptyRoomListIsAllowed()
    {
        var toward = new PlayerStrategy { Dig = DigMode.Toward, DigTarget = new TilePos(20, 3) };
        Assert.Empty(toward.Validate());
        Assert.Single(toward.Validate(mapWidth: 11, mapHeight: 11));
        Assert.Empty(toward.Validate(mapWidth: 21, mapHeight: 11));
        Assert.Empty(new PlayerStrategy { Rooms = [] }.Validate());
        Assert.Single(new PlayerStrategy { DigTarget = new TilePos(1, 1) }.Validate()); // a target only goes with Toward
    }
}
