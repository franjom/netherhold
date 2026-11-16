// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

/// <summary>The journal policy moved out of Unity's runner (plan step 2h): the opt-in, the session folder, the header and its
/// key table, the call order, the tick journal and the failure and close handling.</summary>
public sealed class FrameJournalSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dk2-journal-session-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _logs = [];
    private readonly List<string> _warnings = [];

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private FrameJournalSession Session() => new(_logs.Add, _warnings.Add);

    [Fact]
    public void TheHostsSettingWins_ElseTheVariable_ElseOff()
    {
        Func<string, string?> env = name => name == FrameJournalSession.Variable ? "from-env" : null;
        Assert.Equal("D:/own", FrameJournalSession.Root("D:/own", env));
        Assert.Equal("from-env", FrameJournalSession.Root("", env));
        Assert.Equal("from-env", FrameJournalSession.Root("  ", env));
        Assert.Null(FrameJournalSession.Root(null, _ => null));
    }

    [Fact]
    public void TheFolderIsNamedByTheUtcClockAndTheLevelOrTheFrontEnd()
    {
        var at = new DateTime(2026, 11, 16, 9, 8, 7, 654, DateTimeKind.Utc);
        Assert.Equal("session-20261116-090807-654-Level3", FrameJournalSession.FolderName(at, GameSessionMode.Campaign, "Level3"));
        Assert.Equal("session-20261116-090807-654-front-end", FrameJournalSession.FolderName(at, GameSessionMode.FrontEnd, "Level3"));
    }

    [Fact]
    public void TheHeaderNamesItsKeyTable()
    {
        Dictionary<string, string> header = FrameJournalSession.Header("godot", FrameJournalSession.GodotKeyTable,
            GameSessionMode.Campaign, "Level1", 7, new PlayerId(3), fogOfWar: true, tickOnWorker: false, 1024, 768);
        Assert.Equal(new Dictionary<string, string>
        {
            ["source"] = "godot", ["keys"] = "Godot.Key", ["session.mode"] = "Campaign", ["level"] = "Level1", ["seed"] = "7",
            ["viewing.player"] = "3", ["fog.of.war"] = "on", ["tick.on.worker"] = "off", ["screen"] = "1024x768",
            ["commit"] = "unknown", ["src.dirty"] = "unknown",
        }, header);
    }

    [Fact]
    public void WithoutARoot_ItResolvesAndRecordsNothing()
    {
        FrameJournalSession session = Session();
        bool built = false;
        session.Open(null, "s", () => { built = true; return new Dictionary<string, string>(); });
        Assert.True(session.Resolved);
        Assert.False(session.Recording);
        Assert.False(built);
        session.BeginFrame(new FrameInputRecord(), []);
        session.EndFrame([], 0);
        session.Close("done", [], 0, null);
        Assert.False(Directory.Exists(_root));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void AFolderThatCannotBeMade_WarnsAndLeavesTheJournalOff()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "taken"), "");
        FrameJournalSession session = Session();
        session.Open(Path.Combine(_root, "taken"), "s", () => new Dictionary<string, string>());
        Assert.True(session.Resolved);
        Assert.False(session.Recording);
        Assert.Single(_warnings, w => w.StartsWith("Input journal stopped: ", StringComparison.Ordinal));
    }

    [Fact]
    public void ASecondSessionInTheSameFolder_IsRefusedNotOverwritten()
    {
        FrameJournalSession first = Session();
        first.Open(_root, "s", () => new Dictionary<string, string>());
        FrameJournalSession second = Session();
        second.Open(_root, "s", () => new Dictionary<string, string>());
        Assert.False(second.Recording);
        Assert.Single(_warnings);
        first.Close("done", [], 0, null);
        Assert.Equal("done", Read().EndReason);
    }

    [Fact]
    public void FramesAreWrittenInOrderAndCloseWritesTheOpenFrameAndTheEnd()
    {
        FrameJournalSession session = Session();
        session.Open(_root, "s", () => new Dictionary<string, string> { ["k"] = "v" });
        Assert.Equal(new[] { "Input journal: " + Path.Combine(_root, "s") }, _logs);
        var pending = new List<PlayerCommand>();
        var intent = new PlayerIntent(new PlayerId(3), PlayerToolMode.Hand, new TilePos(1, 2), PointerButtonTransition.Pressed, default);
        session.BeginFrame(new FrameInputRecord { Frame = 1 }, pending);
        session.Intent(intent);
        session.EndFrame(pending, 0.25);
        session.BeginFrame(new FrameInputRecord { Frame = 2 }, pending);
        pending.Add(new GuiButtonCommand(new PlayerId(3), default));
        session.Close("destroyed", pending, 0.5, null);

        FrameInputJournal journal = Read();
        Assert.Equal("v", journal.Header["k"]);
        Assert.Equal([1L, 2L], journal.Frames.Select(f => f.Frame));
        Assert.Equal(intent, Assert.Single(journal.Frames[0].Intents));
        Assert.Equal(0.25, journal.Frames[0].TickDebtAfter);
        Assert.Single(journal.Frames[1].Commands);
        Assert.Equal(2, journal.FramesWritten);
        Assert.Equal("destroyed", journal.EndReason);
        Assert.False(File.Exists(Path.Combine(_root, "s", FrameJournalSession.TicksFile)));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void TheTickJournal_OpensOnTheFirstTickWithTheStartTickAndHashAndEndsAtClose()
    {
        GameDatabase database = PressFixture.Database();
        var simulation = new GameSimulation(database, PressFixture.World(database), seed: 7);
        FrameJournalSession session = Session();
        session.Open(_root, "s", () => new Dictionary<string, string> { [FrameJournalSession.KeyTableKey] = "t" });
        ulong start = simulation.StateHash();
        var frame = InputFrame.Of(new GuiButtonCommand(new PlayerId(3), default));
        session.BeforeTick(0, frame, simulation);
        simulation.Tick(frame);
        session.AfterTick(1, simulation);
        session.Close("destroyed", [], 0, simulation);

        InputJournal ticks;
        using (FileStream stream = File.OpenRead(Path.Combine(_root, "s", FrameJournalSession.TicksFile))) ticks = InputJournal.Read(stream);
        Assert.Equal("0", ticks.Header["world.start.tick"]);
        Assert.Equal("t", ticks.Header[FrameJournalSession.KeyTableKey]);
        Assert.Equal(start, ticks.Hashes[0]);
        Assert.Equal(simulation.StateHash(), ticks.Hashes[1]);
        Assert.Equal([0L], ticks.FrameTicks);
        Assert.Equal(1, ticks.TicksRun);
        Assert.Equal("destroyed", ticks.EndReason);
    }

    [Fact]
    public void AFailedTick_EndsTheTickJournalWithTheFailureAndCloseAddsNoSecondEnd()
    {
        GameDatabase database = PressFixture.Database();
        var simulation = new GameSimulation(database, PressFixture.World(database), seed: 7);
        FrameJournalSession session = Session();
        session.Open(_root, "s", () => new Dictionary<string, string>());
        session.BeforeTick(0, InputFrame.Empty, simulation);
        session.TickFailed(new InvalidOperationException("boom"), simulation);
        session.AfterTick(1, simulation);
        session.Close("destroyed", [], 0, simulation);

        InputJournal ticks;
        using (FileStream stream = File.OpenRead(Path.Combine(_root, "s", FrameJournalSession.TicksFile))) ticks = InputJournal.Read(stream);
        Assert.Equal("exception at tick 0: System.InvalidOperationException: boom", ticks.EndReason);
        Assert.Equal([0L], ticks.Hashes.Keys);
        Assert.Equal("destroyed", Read().EndReason);
    }

    [Fact]
    public void WithoutAFrameJournal_TheTickCallsWriteNothing()
    {
        GameDatabase database = PressFixture.Database();
        var simulation = new GameSimulation(database, PressFixture.World(database), seed: 7);
        FrameJournalSession session = Session();
        session.Open(null, "s", () => new Dictionary<string, string>());
        session.BeforeTick(0, InputFrame.Empty, simulation);
        session.AfterTick(1, simulation);
        session.TickFailed(new InvalidOperationException("boom"), simulation);
        session.Close("destroyed", [], 0, simulation);
        Assert.False(Directory.Exists(_root));
    }

    private FrameInputJournal Read()
    {
        using FileStream stream = File.OpenRead(Path.Combine(_root, "s", FrameJournalSession.FramesFile));
        return FrameInputJournal.Read(stream);
    }
}
