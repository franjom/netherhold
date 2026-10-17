// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>A level script's camera path and its view requests (camera-paths.md, ADR-0496):
/// action 31 starts a path the keeper's condition 72 then waits for, the skip ends it, and
/// actions 24, 31 and 38 tell the view what to play.</summary>
public class CameraPathTests
{
    private const byte Keeper = 3;
    private const byte PointId = 1;

    [Theory]
    [InlineData(0, 399, 53)]     // the Heart's plug path: 13.25 s against DK2's ~13.25 s
    [InlineData(201, 500, 67)]   // Level 1's entrance
    [InlineData(56, 150, 19)]    // a creature intro starts at frame 7
    [InlineData(274, 1, 0)]      // a one-frame front-end pose ends at once
    public void APathLastsItsFramesAtThirtyASecondInNominal250MsTicks(int pathId, int frames, int ticks) =>
        Assert.Equal(ticks, CameraPathRules.DurationTicks(pathId, frames));

    [Fact]
    public void ConditionSeventyTwoIsFalseWhileAPathPlaysAndAnAssumptionAfterwards()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        world.Player(Keeper).CameraPathEndTick = 10;
        TriggerRecord[] records = [Type72(5, child: 0xFFFF), SetFlag(1, flagId: 3)];
        var executor = new TriggerExecutor(
            new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]), world.Triggers, world, db);

        TriggerRunResult playing = executor.Run(1);
        Assert.Equal(0, world.Triggers.Read(3));
        Assert.Empty(playing.Assumed);

        world.Player(Keeper).CameraPathEndTick = world.CurrentTick.Value;
        TriggerRunResult still = executor.Run(2);
        Assert.Equal(1, world.Triggers.Read(3));
        Assert.Equal([new BlockedTrigger(213, 72, 5, TriggerContext.Player)], still.Assumed);
    }

    [Fact]
    public void ActionThirtyOneStartsThePathFromTheActionPointCentreAndTellsTheView()
    {
        (GameSimulation sim, ListEventSink events) = Simulation(PathAction(pathId: 201, available: 1));

        sim.Tick();

        Assert.Equal(67, sim.World.Player(Keeper).CameraPathEndTick);
        ScriptedViewRequest request = Assert.Single(Requests(events));
        Assert.Equal(new ScriptedViewRequest(ScriptedViewKind.CameraPath, new PlayerId(Keeper), 201,
            new TilePos(3, 4), true), request);
    }

    [Theory]
    [InlineData(true, false, 201)]   // a path already plays
    [InlineData(false, true, 201)]   // the keeper possesses
    [InlineData(false, false, 202)]  // no EnginePath202.kcs ships
    public void ARefusedStartChangesNothingAndAsksTheViewForNothing(bool playing, bool possessing, int pathId)
    {
        (GameSimulation sim, ListEventSink events) = Simulation(PathAction(pathId, available: 0));
        if (playing) sim.World.Player(Keeper).CameraPathEndTick = 5;
        if (possessing) sim.World.Player(Keeper).PossessedCreatureId = 1;

        sim.Tick();

        Assert.Equal(playing ? 5 : 0, sim.World.Player(Keeper).CameraPathEndTick);
        Assert.Empty(Requests(events));
    }

    [Fact]
    public void TheSkipEndsThePathBeforeTheTurnsWalkAndTellsTheView()
    {
        (GameSimulation sim, ListEventSink events) = Simulation(PathAction(pathId: 201, available: 1));
        sim.Tick();
        events.Clear();

        sim.Tick(new InputFrame([new SkipCameraPathCommand(new PlayerId(Keeper))]));

        // The root action runs on every walk, so the turn's walk, which comes after the skip,
        // finds the camera free and starts the path again from tick 1.
        Assert.Equal(1 + 67, sim.World.Player(Keeper).CameraPathEndTick);
        Assert.Equal([ScriptedViewKind.CameraPathSkipped, ScriptedViewKind.CameraPath],
            Requests(events).Select(r => r.Kind));
    }

    [Fact]
    public void ASkipWithNoPathPlayingDoesNothing()
    {
        (GameSimulation sim, ListEventSink events) = Simulation(PathAction(pathId: 202, available: 0));

        sim.Tick(new InputFrame([new SkipCameraPathCommand(new PlayerId(Keeper))]));

        Assert.Equal(0, sim.World.Player(Keeper).CameraPathEndTick);
        Assert.Empty(Requests(events));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]   // 0x004C8955 tests Available == 1
    public void ActionThirtyEightAsksForTheLetterboxOnlyForOne(byte available, bool on)
    {
        (GameSimulation sim, ListEventSink events) = Simulation(
            Action(38, new Dictionary<string, long> { ["Available"] = available }));

        sim.Tick();

        ScriptedViewRequest request = Assert.Single(Requests(events));
        Assert.Equal((ScriptedViewKind.Letterbox, on), (request.Kind, request.Flag));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]   // a keeper with a result: Message_Post returns before the speech
    public void ActionTwentyFourAsksForItsLineUnlessTheKeeperHasAResult(byte status, bool spoken)
    {
        (GameSimulation sim, ListEventSink events) = Simulation(
            Action(24, new Dictionary<string, long> { ["SpeechId"] = 3, ["SpeakOnly"] = 0 }));
        sim.World.Player(Keeper).Status = status;

        sim.Tick();

        Assert.Equal(spoken ? [new ScriptedViewRequest(ScriptedViewKind.Speech, new PlayerId(Keeper), 3, default, false)] : [],
            Requests(events));
        Assert.Contains(sim.LastTriggerRun!.Partial, b => b.Type == 24);
    }

    [Fact]
    public void ActionTwentySixAsksTheViewToGlideToThePointCentre()
    {
        (GameSimulation sim, ListEventSink events) = Simulation(
            Action(26, new Dictionary<string, long> { ["ActionPointId"] = PointId }));

        sim.Tick();

        Assert.Equal(new ScriptedViewRequest(ScriptedViewKind.CameraMove, new PlayerId(Keeper), 0,
            new TilePos(3, 4), false), Assert.Single(Requests(events)));
        Assert.DoesNotContain(sim.LastTriggerRun!.Partial, b => b.Type == 26);
    }

    private static (GameSimulation, ListEventSink) Simulation(TriggerRecord root)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        GameDatabase db = new(fixture.Terrain.Values, fixture.Rooms.Values, fixture.Creatures.Values,
            fixture.Variables, fixture.KeeperSpells.Values, fixture.Objects.Values, fixture.Doors.Values,
            fixture.Traps.Values, fixture.CreatureSpells.Values, fixture.Shots.Values, fixture.Effects.Values,
            fixture.EffectElements.Values, fixture.Level, new Dictionary<int, int> { [201] = 500 });
        WorldState world = World(db);
        ThingProjection.Place(world, db,
        [new ActionPointThing(new byte[56])
        {
            StartX = 2, StartY = 3, EndX = 5, EndY = 6, ActionPointId = PointId, Name = "camera",
        }]);
        var tree = new TriggerTree([root], 0, playerRoots: [new TriggerPlayerRoot(Keeper, root.TriggerId)]);
        var sim = new GameSimulation(db, world, seed: 1, triggers: tree);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, events);
    }

    private static WorldState World(GameDatabase database) =>
        WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static ScriptedViewRequest[] Requests(ListEventSink events) =>
        [.. events.Events.Where(e => e.ScriptedView is not null).Select(e => e.ScriptedView!.Value)];

    private static TriggerRecord PathAction(int pathId, byte available) => Action(31, new Dictionary<string, long>
    {
        ["PathId"] = pathId, ["ActionPointId"] = PointId, ["Available"] = available,
    });

    private static TriggerRecord Action(byte type, Dictionary<string, long> parameters) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = type, Parameters = parameters,
        };

    private static TriggerRecord Type72(ushort id, ushort child) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 72, RepeatCount = 1,
            ChildTriggerId = child, NextTriggerId = 0, Parameters = new Dictionary<string, long>(),
        };

    private static TriggerRecord SetFlag(ushort id, byte flagId) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 7,
            Parameters = new Dictionary<string, long> { ["FlagId"] = flagId, ["Flag"] = 10, ["Value"] = 1 },
        };
}
