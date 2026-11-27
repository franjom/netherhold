// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The frame order moved out of Unity's runner (plan step 2h), where a recorded journal cannot reach it: the pose
/// first, the hand-off journalled before the clear, ready drops after the frame's input, the tick gate, the path skip and
/// the options key, the pickups' viewer, and the scripted view's clock and camera mode. A failure here changes the command
/// stream.</summary>
public sealed class FrameDriverTests
{
    private static readonly PlayerId Keeper = PressFixture.Keeper;
    private static readonly double OneTick = 1.0 / SimulationTiming.TicksPerSecond;

    private sealed class Poses(PlayerCommand? pose) : ITurnPoseSource
    {
        public int Asked;
        public PlayerCommand? TurnPose(PlayerId viewer) { Asked++; return pose; }
    }

    private sealed class Rig
    {
        public readonly GameDatabase Database;
        public readonly WorldState World;
        public readonly WorldView View;
        public readonly ScriptedViewDirector Script;
        public readonly TickPacer Pacer = new();
        public readonly List<PlayerCommand> Pending = [];
        public readonly FrameInputRecorder Recorder = new();
        public readonly FrameDriver Driver;
        public FrameJournalSession? Journal;

        public Rig(GameDatabase? database = null, WorldState? world = null, Func<int, IReadOnlyList<KcsFrame>?>? paths = null,
            bool journal = false)
        {
            Database = database ?? PressFixture.Database();
            World = world ?? PressFixture.World(Database);
            View = new WorldView(Database, Keeper);
            Script = new ScriptedViewDirector(Keeper, paths ?? (_ => null), _ => null);
            if (journal)
            {
                Journal = new FrameJournalSession(_ => { }, _ => { });
            }

            Driver = new FrameDriver(Pending, View, Pacer, Journal);
        }

        public FrameTicks Take(double elapsed, ITurnPoseSource? poses = null, float speed = 1f, bool paused = false)
        {
            return Driver.TakeTicks(World, Keeper, elapsed, speed, 4, paused, TickPacer.DefaultMaxTicksPerFrame, poses);
        }
    }

    private static readonly PlayerCommand Pose = new SlapCreatureCommand(Keeper, 9);
    private static readonly PlayerCommand Queued = new GuiButtonCommand(Keeper, default);

    [Fact]
    public void WithATickToRun_ThePoseGoesFirstAndTheQueueIsHandedToTheFirstTickAlone()
    {
        var rig = new Rig();
        rig.Pending.Add(Queued);
        var poses = new Poses(Pose);

        FrameTicks ticks = rig.Take(2 * OneTick, poses);

        Assert.Equal(2, ticks.Count);
        Assert.True(ticks.Accrued);
        Assert.Equal(2.0, ticks.Advance);
        Assert.Equal(new[] { Pose, Queued }, ticks.InputFor(0).All);
        Assert.Empty(ticks.InputFor(1).All);
        Assert.Empty(rig.Pending);
        Assert.Equal(1, poses.Asked);
    }

    [Fact]
    public void WithNoTickToRun_NoPoseIsAskedAndTheQueueWaits()
    {
        var rig = new Rig();
        rig.Pending.Add(Queued);
        var poses = new Poses(Pose);

        FrameTicks ticks = rig.Take(OneTick / 2, poses);

        Assert.Equal(0, ticks.Count);
        Assert.True(ticks.Accrued);
        Assert.Null(ticks.FirstInput.Commands);
        Assert.Equal(0, poses.Asked);
        Assert.Equal(new[] { Queued }, rig.Pending);
        Assert.Equal(0.5, rig.Pacer.Debt);
    }

    [Fact]
    public void TheShutGate_AccruesNothingTakesNothingAndAsksNoPose()
    {
        foreach ((float speed, bool paused) in new[] { (0f, false), (1f, true), (-1f, false), (float.NaN, false) })
        {
            var rig = new Rig();
            rig.Pending.Add(Queued);
            var poses = new Poses(Pose);

            FrameTicks ticks = rig.Take(OneTick, poses, speed, paused);

            Assert.Equal(new FrameTicks(0, InputFrame.Empty, false, 0), ticks);
            Assert.Equal(0, rig.Pacer.Debt);
            Assert.Equal(0, poses.Asked);
            Assert.Equal(new[] { Queued }, rig.Pending);
        }
    }

    [Fact]
    public void AnEmptyQueueHandsOffNothingAndAPoseAloneIsHandedOff()
    {
        var rig = new Rig();
        Assert.Null(rig.Take(OneTick).FirstInput.Commands);
        Assert.Equal(new[] { Pose }, rig.Take(OneTick, new Poses(Pose)).FirstInput.All);
        Assert.Null(rig.Take(OneTick, new Poses(null)).FirstInput.Commands);
    }

    [Fact]
    public void TheJournalSeesTheWholeQueueBeforeItIsCleared()
    {
        string root = Path.Combine(Path.GetTempPath(), "dk2-frame-driver-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rig = new Rig(journal: true);
            rig.Journal!.Open(root, "s", () => new Dictionary<string, string>());
            rig.Pending.Add(Queued);
            rig.Journal.BeginFrame(new FrameInputRecord(), rig.Pending);
            rig.Pending.Add(Queued);
            FrameTicks ticks = rig.Take(OneTick, new Poses(Pose));
            rig.Pending.Add(Queued);
            rig.Journal.Ticks(GameTick.Zero, ticks.Count);
            rig.Journal.EndFrame(rig.Pending, rig.Pacer.Debt);
            rig.Journal.Close("done", rig.Pending, 0, null);

            FrameInputRecord frame;
            using (FileStream stream = File.OpenRead(Path.Combine(root, "s", FrameJournalSession.FramesFile)))
            {
                frame = Assert.Single(FrameInputJournal.Read(stream).Frames);
            }
            // The first before the frame, then the pose (inserted first, after the recorder had seen index 0), then the rest.
            Assert.Equal(3, frame.CommandsHandedToTick);
            Assert.Equal(new[] { CommandQueuedWhen.BeforeUpdate, CommandQueuedWhen.DuringFrame, CommandQueuedWhen.DuringFrame,
                CommandQueuedWhen.AfterHandOff }, frame.Commands.Select(c => c.When));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [InstallFact]
    public void AReadyDropFollowsTheFramesInputIntoTheSameTick()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = PressFixture.Goblin();
        var rig = new Rig(db, world);
        var drop = new DropCommand(Keeper, new TilePos(1, 1));
        Assert.True(rig.View.LocalPickups.TryQueue(world, db, Keeper, new CreatureId(goblin.Id), 0));
        Assert.True(rig.View.LocalPickups.TryQueueDrop(drop));
        goblin.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(goblin));
        rig.Pending.Add(Queued);

        FrameTicks ticks = rig.Take(OneTick, new Poses(Pose));

        Assert.Equal(new[] { Pose, Queued, drop }, ticks.FirstInput.All);
        // Posted once: the next frame drains nothing.
        Assert.Null(rig.Take(OneTick).FirstInput.Commands);
    }

    [InstallFact]
    public void AReadyDropIsQueuedEvenWhileTheGateIsShut()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = PressFixture.Goblin();
        var rig = new Rig(db, world);
        var drop = new DropCommand(Keeper, new TilePos(1, 1));
        Assert.True(rig.View.LocalPickups.TryQueue(world, db, Keeper, new CreatureId(goblin.Id), 0));
        Assert.True(rig.View.LocalPickups.TryQueueDrop(drop));
        goblin.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(goblin));

        rig.Take(OneTick, paused: true);

        Assert.Equal(new PlayerCommand[] { drop }, rig.Pending);
    }

    [InstallFact]
    public void ANewViewerDropsThePendingPickupsAndTheSameViewerKeepsThem()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = PressFixture.Goblin();
        var rig = new Rig(db, world);
        rig.Driver.AdvancePickups(world, Keeper, 0);
        Assert.True(rig.View.LocalPickups.TryQueue(world, db, Keeper, new CreatureId(goblin.Id), 0));
        rig.Driver.AdvancePickups(world, Keeper, 10);
        Assert.Equal(1, rig.View.LocalPickups.Count);
        rig.Driver.AdvancePickups(world, PlayerIds.Neutral, 20);
        Assert.Equal(0, rig.View.LocalPickups.Count);
    }

    [InstallFact]
    public void TheFirstFrameStartsThePickupsOver()
    {
        (GameDatabase db, WorldState world, CreatureState goblin) = PressFixture.Goblin();
        var rig = new Rig(db, world);
        Assert.True(rig.View.LocalPickups.TryQueue(world, db, Keeper, new CreatureId(goblin.Id), 0));
        rig.Driver.AdvancePickups(world, Keeper, 0);
        Assert.Equal(0, rig.View.LocalPickups.Count);
    }

    [Fact]
    public void WithoutAPath_TheSkipKeyPostsNothingAndTheOptionsKeyIsAllowed()
    {
        var rig = new Rig();
        FrameScriptInput input = rig.Driver.TakeScriptedInput(rig.Script, Keeper, skipKey: true);
        Assert.False(input.PathWasPlaying);
        Assert.True(input.AllowOptionsKey);
        Assert.Null(input.Glide);
        Assert.Empty(rig.Pending);
    }

    [Fact]
    public void APlayingPath_EndsOnTheSkipKeyAndPostsTheSkipOnce_AndHoldsTheOptionsKeyThatFrame()
    {
        Rig rig = PathRig();
        FrameScriptInput held = rig.Driver.TakeScriptedInput(rig.Script, Keeper, skipKey: false);
        Assert.True(held.PathWasPlaying);
        Assert.Empty(rig.Pending);
        Assert.True(rig.Script.PathPlaying);

        FrameScriptInput skipped = rig.Driver.TakeScriptedInput(rig.Script, Keeper, skipKey: true);

        Assert.True(skipped.PathWasPlaying);
        Assert.False(skipped.AllowOptionsKey);
        Assert.False(rig.Script.PathPlaying);
        Assert.Equal(new PlayerCommand[] { new SkipCameraPathCommand(Keeper) }, rig.Pending);
        Assert.True(rig.Driver.TakeScriptedInput(rig.Script, Keeper, skipKey: true).AllowOptionsKey);
        Assert.Single(rig.Pending);
    }

    [Fact]
    public void TheScriptsGlideIsTakenOnce()
    {
        var rig = new Rig();
        rig.Script.Emit(View(ScriptedViewKind.CameraMove, 0, new TilePos(4, 5)));
        rig.Driver.UpdateScript(rig.Script, 0, rig.World, Keeper, ticksPending: 0);
        Assert.Equal(new TilePos(4, 5), rig.Driver.TakeScriptedInput(rig.Script, Keeper, skipKey: false).Glide);
        Assert.Null(rig.Driver.TakeScriptedInput(rig.Script, Keeper, skipKey: false).Glide);
    }

    [Fact]
    public void AfterTicks_DropsOnlyTheTagPreviewsWhoseTickHasRun()
    {
        var rig = new Rig();
        rig.View.QueueTagPreview(new TagTilesCommand(Keeper, [new TilePos(1, 1)], true), rig.World.CurrentTick.Next);
        int queued = rig.View.PreviewVersion;
        rig.Driver.AfterTicks(rig.World, 0);
        Assert.Equal(queued, rig.View.PreviewVersion);
        rig.View.QueueTagPreview(new TagTilesCommand(Keeper, [new TilePos(2, 2)], true), rig.World.CurrentTick);
        rig.Driver.AfterTicks(rig.World, 0);
        Assert.Equal(queued + 2, rig.View.PreviewVersion);
    }

    [Fact]
    public void APossessingViewer_PutsTheScriptInThePossessionModeWhichHoldsTheGlide()
    {
        var rig = new Rig();
        rig.World.Player(Keeper).PossessedCreatureId = 5;
        rig.Script.Emit(View(ScriptedViewKind.CameraMove, 0, new TilePos(4, 5)));
        rig.Driver.UpdateScript(rig.Script, 0, rig.World, Keeper, ticksPending: 0);
        Assert.Null(rig.Driver.TakeScriptedInput(rig.Script, Keeper, skipKey: false).Glide);

        rig.World.Player(Keeper).PossessedCreatureId = 0;
        rig.Script.Emit(View(ScriptedViewKind.CameraMove, 0, new TilePos(4, 5)));
        rig.Driver.UpdateScript(rig.Script, 0, rig.World, Keeper, ticksPending: 0);
        Assert.Equal(new TilePos(4, 5), rig.Driver.TakeScriptedInput(rig.Script, Keeper, skipKey: false).Glide);
    }

    [Theory]
    [InlineData(0.5, 2, 2.5)]   // two ticks started and not presented, half a tick owed
    [InlineData(3.75, 2, 3.0)]  // more than a tick owed (the cap held two back): the debt counts as one
    public void TheScriptedViewIsUpdatedOnTheDrawnTickPlusThePendingTicksAndAtMostOneTickOfDebt(double owed, int pending,
        double renderTick)
    {
        // Each path frame's x is its index, so the pose tells which render tick the path was sought to.
        var rig = new Rig(paths: _ => IndexedFrames());
        rig.Take(owed * OneTick);
        Assert.Equal(owed - (owed >= 2 ? 2 : 0), rig.Pacer.Debt, 9);
        rig.Script.Emit(View(ScriptedViewKind.CameraPath, 1, default));
        rig.Driver.UpdateScript(rig.Script, 0, rig.World, Keeper, ticksPending: pending);

        var expected = new ScriptedViewDirector(Keeper, _ => IndexedFrames(), _ => null);
        expected.Emit(View(ScriptedViewKind.CameraPath, 1, default));
        expected.Update(0, renderTick, CameraModeId.Dungeon);
        Assert.Equal(expected.Pose, rig.Script.Pose);
        // The frames differ, so another render tick would give another pose.
        var other = new ScriptedViewDirector(Keeper, _ => IndexedFrames(), _ => null);
        other.Emit(View(ScriptedViewKind.CameraPath, 1, default));
        other.Update(0, renderTick + 0.5, CameraModeId.Dungeon);
        Assert.NotEqual(other.Pose, rig.Script.Pose);
    }

    [Fact]
    public void AfterTicks_AdvancesTheTerrainHighlightsOnTheFramesClock()
    {
        var rig = new Rig();
        Assert.False(rig.View.TerrainHighlightPhase);
        rig.Driver.AfterTicks(rig.World, 201);
        Assert.True(rig.View.TerrainHighlightPhase);
        rig.Driver.AfterTicks(rig.World, 300);
        Assert.True(rig.View.TerrainHighlightPhase);
        rig.Driver.AfterTicks(rig.World, 402);
        Assert.False(rig.View.TerrainHighlightPhase);
    }

    [Fact]
    public void AReplacedDirector_IsTheOneTheDriverUpdatesAndReads()
    {
        var rig = new Rig();
        var replacement = new ScriptedViewDirector(Keeper, _ => null, _ => null);
        rig.Script.Emit(View(ScriptedViewKind.CameraMove, 0, new TilePos(1, 1)));
        replacement.Emit(View(ScriptedViewKind.CameraMove, 0, new TilePos(6, 6)));
        rig.Driver.UpdateScript(replacement, 0, rig.World, Keeper, ticksPending: 0);
        Assert.Equal(new TilePos(6, 6), rig.Driver.TakeScriptedInput(replacement, Keeper, skipKey: false).Glide);
        Assert.False(rig.Script.TryTakeCameraMove(out _));
    }

    private static KcsFrame[] IndexedFrames()
    {
        return [.. Enumerable.Range(0, 399).Select(i => new KcsFrame(i, 0, -3f, Identity, 1f, 12000f))];
    }

    private static Rig PathRig()
    {
        var rig = new Rig(paths: _ => [.. Enumerable.Range(0, 399).Select(_ => new KcsFrame(0, 0, -3f, Identity, 1f, 12000f))]);
        rig.Script.Emit(View(ScriptedViewKind.CameraPath, 1, default));
        rig.Driver.UpdateScript(rig.Script, 0, rig.World, Keeper, ticksPending: 0);
        Assert.True(rig.Script.PathPlaying);
        return rig;
    }

    private static readonly KcsRotation Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    private static SimEvent View(ScriptedViewKind kind, int value, TilePos tile)
    {
        return new(default, "t", "scripted_view", "") { ScriptedView = new ScriptedViewRequest(kind, Keeper, value, tile, false) };
    }
}
