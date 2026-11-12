// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The Mentor's time-limit countdown, the countdown block of <c>0x004B9740</c>, and world
/// action 51 (<c>0x0051986C</c>), which switches it (ADR-0767).</summary>
public sealed class TimeLimitCountdownTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Theory]
    [InlineData(1800, 1)]
    [InlineData(1200, 2)]
    [InlineData(600, 3)]
    [InlineData(300, 4)]
    [InlineData(240, 5)]
    [InlineData(180, 6)]
    [InlineData(120, 7)]
    [InlineData(60, 8)]
    public void TheMinuteMarksSpeakLinesOneToEight_WithoutClearing(int seconds, int soundId) =>
        Assert.Equal((soundId, false), TimeLimitCountdown.LineFor(seconds * SimulationTiming.TicksPerSecond));

    [Theory]
    [InlineData(10, 9)]
    [InlineData(5, 14)]
    [InlineData(1, 18)]
    public void TheLastTenSecondsClearTheQueueAndCount(int seconds, int soundId) =>
        Assert.Equal((soundId, true), TimeLimitCountdown.LineFor(seconds * SimulationTiming.TicksPerSecond));

    /// <summary>Equality tests only: between the marks, and with no limit, nothing is said.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(41)]
    [InlineData(60 * 4 + 1)]
    [InlineData(3600 * 4)]
    public void NoLineBetweenTheMarks(int ticks) => Assert.Null(TimeLimitCountdown.LineFor(ticks));

    /// <summary>The limit counts down at the start of the turn (<c>0x0050A64E</c>); the player tick
    /// after the trigger walk then finds ten seconds left and asks once, for every viewer, for line 9,
    /// clearing the queue.</summary>
    [Fact]
    public void ATurnThatReachesTenSecondsAsksTheKeeperForLineNine()
    {
        (GameSimulation simulation, ListEventSink events) = Simulation(timeLeftTicks: (10 * SimulationTiming.TicksPerSecond) + 1);

        simulation.Tick();

        ScriptedViewRequest request = Assert.Single(events.Events, e => e.ScriptedView is not null).ScriptedView!.Value;
        Assert.Equal(new ScriptedViewRequest(ScriptedViewKind.MentorSpeech, PlayerIds.Unowned, 9, default, true), request);
    }

    /// <summary>Action 51 in the same turn's trigger walk clears the bit before the player tick asks.</summary>
    [Fact]
    public void NothingIsSaidOnceActionFiftyOneClearsTheBit()
    {
        (GameSimulation simulation, ListEventSink events) = Simulation(MinuteLeft, action51: 0);
        simulation.Tick();
        Assert.False(simulation.World.Triggers.TimerSpeechEnabled);
        Assert.DoesNotContain(events.Events, e => e.ScriptedView is not null);
    }

    [Fact]
    public void NothingIsSaidInMyPetDungeon()
    {
        (GameSimulation simulation, ListEventSink events) = Simulation(MinuteLeft, GameMode.MyPetDungeon);
        simulation.Tick();
        Assert.DoesNotContain(events.Events, e => e.ScriptedView is not null);
    }

    /// <summary>The whole function returns while the player's Status (<c>+0x19A</c>) is set, so with
    /// no player left playing nothing is said.</summary>
    [Fact]
    public void NothingIsSaidOnceNoPlayerIsPlaying()
    {
        (GameSimulation simulation, ListEventSink events) = Simulation(MinuteLeft);
        simulation.World.Player(Keeper).Status = (byte)PlayerStatus.Won;
        simulation.Tick();
        Assert.DoesNotContain(events.Events, e => e.ScriptedView is not null);
    }

    /// <summary>The queue is shared and has no local-player test: a keeper with a result still hears
    /// the line while another player plays, and hears it once.</summary>
    [Fact]
    public void AnotherPlayerStillPlayingKeepsTheCountdownGoing_Once()
    {
        (GameSimulation simulation, ListEventSink events) = Simulation(MinuteLeft, players: [Keeper, PlayerIds.Good]);
        simulation.World.Player(Keeper).Status = (byte)PlayerStatus.Lost;
        simulation.Tick();
        ScriptedViewRequest request = Assert.Single(events.Events, e => e.ScriptedView is not null).ScriptedView!.Value;
        Assert.Equal(new ScriptedViewRequest(ScriptedViewKind.MentorSpeech, PlayerIds.Unowned, 8, default, false), request);
    }

    /// <summary>Action 51: byte 0 equal to 1 sets the bit, anything else clears it; it starts on
    /// (<c>0x00509CE5</c>).</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void ActionFiftyOneSetsTheBitOnlyForOne(byte available, bool enabled)
    {
        (GameSimulation simulation, _) = Simulation(0, action51: available);
        Assert.True(simulation.World.Triggers.TimerSpeechEnabled);

        simulation.Tick();

        Assert.Equal(enabled, simulation.World.Triggers.TimerSpeechEnabled);
        Assert.DoesNotContain(simulation.LastTriggerRun!.Partial, x => x.Type == 51);
        Assert.DoesNotContain(simulation.LastTriggerRun!.Unhandled, x => x.Type == 51);
    }

    private const int MinuteLeft = (60 * SimulationTiming.TicksPerSecond) + 1;

    private static (GameSimulation Simulation, ListEventSink Events) Simulation(
        int timeLeftTicks, GameMode gameMode = GameMode.Campaign, byte? action51 = null, byte[]? players = null)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), players ?? [Keeper], gameMode: gameMode);
        world.Triggers.LevelTimeLimit = timeLeftTicks;
        TriggerTree? tree = null;
        if (action51 is byte available)
        {
            TriggerRecord action = new ActionTriggerRecord(new byte[16])
            {
                Body = new byte[8], TriggerId = 0xFFFF, Type = 51,
                Parameters = new Dictionary<string, long>
                {
                    ["Available"] = available, ["Unreached_0001"] = 0, ["Unreached_0002"] = 0, ["Unreached_0004"] = 0,
                },
            };
            tree = new TriggerTree([action], action.TriggerId);
        }
        var simulation = new GameSimulation(database, world, seed: 42, [], tree);
        var events = new ListEventSink();
        simulation.ObserveWith(events);
        return (simulation, events);
    }
}
