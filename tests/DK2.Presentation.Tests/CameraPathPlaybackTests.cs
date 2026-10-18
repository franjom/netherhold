// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Camera;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Data.Wad;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Camera path playback, the letterbox and the subtitle queue (camera-paths.md,
/// ADR-0496).</summary>
public class CameraPathPlaybackTests
{
    private static readonly KcsRotation Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    private static KcsFrame[] Frames(int count, float z = -3f) =>
        [.. Enumerable.Range(0, count).Select(i => new KcsFrame(0, 0, z, Identity, 1f, 12000f))];

    [Fact]
    public void HudUsesNestedRequestsAndRestoresBeforeTheBarsFinishShrinking()
    {
        var viewer = new PlayerId(3);
        var director = new ScriptedViewDirector(viewer, _ => Frames(399), _ => null);
        SimEvent Letterbox(bool raised) => new(default, "t", "scripted_view", "")
        {
            ScriptedView = new ScriptedViewRequest(ScriptedViewKind.Letterbox, viewer, 0, default, raised),
        };
        director.Emit(Letterbox(true));
        director.Update(0, 0);
        Assert.False(director.PathPlaying);
        Assert.False(director.HudFor(ClassicCameraMode.Dungeon).ShowNormalHud);
        director.Emit(View(ScriptedViewKind.CameraPath, viewer, 1));
        director.Update(100, 1);
        Assert.Equal(2, director.LetterboxRequestCount);
        Assert.False(director.HudFor(ClassicCameraMode.Dungeon).AllowOrdinaryInput);
        director.Update(2000, 2);
        Assert.True(director.Skip());
        Assert.Equal(1, director.LetterboxRequestCount);
        Assert.False(director.HudFor(ClassicCameraMode.Dungeon).ShowNormalHud);
        director.Emit(Letterbox(false));
        director.Update(2100, 2);
        Assert.Equal(0, director.LetterboxRequestCount);
        Assert.True(director.HudFor(ClassicCameraMode.Dungeon).ShowNormalHud);
        Assert.True(director.HudFor(ClassicCameraMode.Dungeon).AllowOrdinaryInput);
        Assert.True(director.LetterboxBar > 0);
    }

    /// <summary>The path's native matrix goes through the same transpose and axis swap as the
    /// ordinary camera's, so the ordinary camera's own matrix must give its own axes.</summary>
    [Fact]
    public void ANativeMatrixGivesTheAxesTheOrdinaryCameraGivesIt()
    {
        var profile = new CameraThing(new byte[84])
        {
            CameraId = 3, Z = 4096, Zoom = 40960, MinZoom = 16384, MaxZoom = 53248,
            Lens = 8192, AnglePitch = 220, Flags = 0x26,
        };
        var ordinary = new ClassicCamera(profile, ClassicCamera.LevelStartYaw);
        float yaw = 256 * MathF.PI / 1024, pitch = 220 * MathF.PI / 1024;
        Matrix4x4 m = Matrix4x4.CreateRotationZ(-yaw) * Matrix4x4.CreateRotationX(-pitch);
        var rotation = new KcsRotation(m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33);

        CameraPathPose path = new CameraPathPlayback(0,
            [new KcsFrame(0, 0, -3, rotation, 1, 1), new KcsFrame(0, 0, -3, rotation, 1, 1)], new TilePos(0, 0)).Pose();
        ClassicCameraPose expected = ordinary.Pose(Vector3.Zero, new CameraViewport(640, 640, 480));

        Assert.True(Vector3.Distance(expected.Forward, path.Forward) < 1e-5f, $"{expected.Forward} vs {path.Forward}");
        Assert.True(Vector3.Distance(expected.Up, path.Up) < 1e-5f, $"{expected.Up} vs {path.Up}");
    }

    [Fact]
    public void TheEyeStandsOneMinusZTilesUpOverTheActionPointCentre()
    {
        CameraPathPose pose = new CameraPathPlayback(201, Frames(10), new TilePos(14, 28)).Pose();

        Assert.Equal(new Vector3(14.5f, 4f, -28.5f), pose.Position);
        Assert.Equal(24f, pose.ViewDistance, 4);
    }

    [Theory]
    [InlineData(52.99, false)]
    [InlineData(53, true)]
    public void TheHeartsPlugPathEndsAfterFiftyThreeTicks(double ticks, bool finished)
    {
        var path = new CameraPathPlayback(0, Frames(399), new TilePos(0, 0), startTick: 10);
        path.Seek(10 + 1 + ticks);
        Assert.Equal(finished, path.Finished);
    }

    /// <summary>The camera reaches its last frame in the last tick the script counts, so the
    /// path the script starts when condition 72 turns true finds the screen free.</summary>
    [Theory]
    [InlineData(1, 399)]
    [InlineData(201, 500)]
    [InlineData(56, 120)]
    [InlineData(230, 150)]
    [InlineData(221, 1401)]
    public void APathEndsInTheLastTickTheScriptCounts(int pathId, int frames)
    {
        int ticks = CameraPathRules.DurationTicks(pathId, frames);
        var path = new CameraPathPlayback(pathId, Frames(frames), new TilePos(0, 0), startTick: 100);

        path.Seek(100 + ticks);
        Assert.False(path.Finished);
        path.Seek(100 + 1 + ticks);
        Assert.True(path.Finished);
    }

    [Fact]
    public void ASlowFrameDoesNotSkipThePathsOpening()
    {
        KcsFrame[] frames = [.. Enumerable.Range(0, 399).Select(i => new KcsFrame(i, 0, -3, Identity, 1, 1))];
        var director = new ScriptedViewDirector(new PlayerId(3), _ => frames, _ => null);
        director.Emit(new SimEvent(default, "t", "scripted_view", "")
        {
            ScriptedView = new ScriptedViewRequest(ScriptedViewKind.CameraPath, new PlayerId(3), 1, default, false, Tick: 0),
        });

        director.Update(0, 1);
        // Five seconds of wall clock, a tenth of a tick of game: 0.75 frames on, not 150.
        director.Update(5000, 1.1);

        Assert.Equal(0.5f + 0.5f + 0.75f, director.Pose!.Value.Position.X, 4);
        director.SeekCamera(1.2);
        Assert.Equal(0.5f + 0.5f + 1.5f, director.Pose!.Value.Position.X, 4);
    }

    [Fact]
    public void TheNextPathTheScriptStartsFollowsTheOneThatEnded()
    {
        var viewer = new PlayerId(3);
        var director = new ScriptedViewDirector(viewer, id => Frames(id == 1 ? 399 : 500), _ => null);
        director.Emit(PathStart(viewer, 1, tick: 0));
        director.Update(0, 1);
        director.Update(13_000, 53.9);
        Assert.Equal(1, director.PathId);

        // Condition 72 sees the camera stopped on tick 53 and starts path 201 on it.
        director.Emit(PathStart(viewer, 201, tick: 53));
        director.Update(13_250, 54);
        Assert.Equal(201, director.PathId);
    }

    /// <summary>Level 1's opening as the Unity runner drives it, at a steady 60 frames a second
    /// and with a two-second first frame capped as Unity caps <c>Time.deltaTime</c>: the plug
    /// path plays to its end from its first frame and the Lord's fly-over follows it.</summary>
    [InstallTheory]
    [InlineData(0.0)]
    [InlineData(2.0)]
    public void LevelOnesPlugPathPlaysWholeAndTheFlyOverFollows(double firstFrameSeconds)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "level1");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level1")!;
        LevelDef level = GameDatabaseLoader.LoadCampaignLevel(install, "level1")!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "level1")!;
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, "level1")!;
        WorldState world = WorldState.FromMap(database, map, [.. players.Select(p => p.PlayerId)]);
        ThingProjection.Place(world, database, things);
        var tree = new TriggerTree(GameDatabaseLoader.LoadCampaignTriggers(install, "level1")!, level.TriggerId,
            things.OfType<HeroPartyThing>(), players.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
        var simulation = new GameSimulation(database, world, seed: 7, triggers: tree);

        using var wad = WadArchive.Open(Path.Combine(install, "Data", "Paths.WAD"));
        IReadOnlyList<KcsFrame>? Load(int id) => wad.Entries
            .Where(e => e.Name.Equals($"EnginePath{id}.kcs", StringComparison.OrdinalIgnoreCase))
            .Select(e => KcsCameraPath.Read(wad.Extract(e), e.Name).Frames).FirstOrDefault();
        var director = new ScriptedViewDirector(new PlayerId(3), Load, _ => null);
        simulation.ObserveWith(director);

        // DungeonRunner.Start runs the first tick, then each Update draws and owes ticks.
        simulation.Run(1);
        double now = 0, debt = 0;
        var drawn = new List<int?>();
        CameraPathPose? firstPose = null;
        for (int frame = 0; now < 40_000; frame++)
        {
            director.Update(now, world.CurrentTick.Value + Math.Min(debt, 1));
            firstPose ??= director.Pose;
            if (drawn.Count == 0 || drawn[^1] != director.PathId) drawn.Add(director.PathId);
            double seconds = frame == 0 ? Math.Max(firstFrameSeconds, 1 / 60d) : 1 / 60d;
            now += seconds * 1000;
            debt += Math.Min(seconds, 1 / 3d) * SimulationTiming.TicksPerSecond;
            int ticks = (int)Math.Min(debt, 2);
            debt -= ticks;
            simulation.Run(ticks);
        }

        // No frame of the keeper camera between the two paths, and the keeper camera after.
        Assert.Equal([1, 201, null], drawn);
        CameraPathPose start = new CameraPathPlayback(1, Load(1)!, new TilePos(14, 28)).Pose();
        Assert.Equal(start.Position, firstPose!.Value.Position);
    }

    private static SimEvent PathStart(PlayerId player, int path, long tick) =>
        new(default, "t", "scripted_view", "")
        {
            ScriptedView = new ScriptedViewRequest(ScriptedViewKind.CameraPath, player, path, default, false, tick),
        };

    [Fact]
    public void ACreatureIntroStartsAtFrameSeven()
    {
        KcsFrame[] frames = [.. Enumerable.Range(0, 20).Select(i => new KcsFrame(i, 0, -3, Identity, 1, 1))];

        // Frame 7 and 8 lerped by the starting remainder 0.5, plus the half-tile origin.
        Assert.Equal(8f, new CameraPathPlayback(56, frames, new TilePos(0, 0)).Pose().Position.X);
        Assert.Equal(0.5f + 0.5f, new CameraPathPlayback(201, frames, new TilePos(0, 0)).Pose().Position.X);
    }

    [Fact]
    public void TheLetterboxIsCountedAndBlendsOverTwoSeconds()
    {
        var letterbox = new CinematicLetterbox();
        letterbox.Raise(0);
        Assert.Equal(0.0625f, letterbox.Bar(1000));
        letterbox.Raise(1000);   // already heading there: no new blend
        Assert.Equal(0.125f, letterbox.Bar(2000));

        letterbox.Lower(3000);
        Assert.Equal(0.125f, letterbox.Bar(4000));
        letterbox.Lower(4000);
        Assert.Equal(0.0625f, letterbox.Bar(5000));
        Assert.Equal(0f, letterbox.Bar(6000));
    }

    [Fact]
    public void ALineShowsNinetySixMillisecondsACharacterAndTheNextWaitsASecond()
    {
        var subtitles = new SubtitleQueue();
        subtitles.Enqueue(2, characters: 10);
        subtitles.Enqueue(5, characters: 1);

        subtitles.Update(100);
        Assert.Equal(2, subtitles.Current);
        subtitles.Update(1060);
        Assert.Equal(2, subtitles.Current);
        subtitles.Update(1061);
        Assert.Equal(-1, subtitles.Current);
        subtitles.Update(2061);
        Assert.Equal(-1, subtitles.Current);
        subtitles.Update(2062);
        Assert.Equal(5, subtitles.Current);
    }

    [Fact]
    public void TheDirectorPlaysOnlyTheViewersRequestsAndShowsSubtitlesOnlyOnAPath()
    {
        var viewer = new PlayerId(3);
        var director = new ScriptedViewDirector(viewer,
            pathId => pathId == 1 ? Frames(399) : null,
            line => line == 2 ? "Welcome!" : null);
        director.Emit(new SimEvent(default, "t", "scripted_view", "")
        {
            ScriptedView = new ScriptedViewRequest(ScriptedViewKind.CameraPath, new PlayerId(4), 1, default, false),
        });
        director.Update(0, 0);
        Assert.False(director.PathPlaying);

        director.Emit(View(ScriptedViewKind.CameraPath, viewer, 1));
        director.Emit(View(ScriptedViewKind.Speech, viewer, 3));
        director.Update(10, 1);
        Assert.True(director.PathPlaying);
        Assert.Equal("Welcome!", director.Subtitle);

        Assert.True(director.Skip());
        Assert.False(director.PathPlaying);
        Assert.Null(director.Subtitle);
        Assert.False(director.Skip());
    }

    [Theory]
    [InlineData(1f, 0f, 25.6)]
    [InlineData(3f, 2f, 332.8)]
    [InlineData(10f, 0f, 500)]
    public void AGlideLastsTwentyFiveAndAHalfMillisecondsASquareTileAndAtMostHalfASecond(float x, float y, double ms) =>
        Assert.Equal(ms, ScriptedViewDirector.GlideMilliseconds(x, y), 3);

    [Fact]
    public void TheDirectorHandsOverTheViewersLatestGlideOnce()
    {
        var viewer = new PlayerId(3);
        var director = new ScriptedViewDirector(viewer, _ => null, _ => null);
        director.Emit(new SimEvent(default, "t", "scripted_view", "")
        {
            ScriptedView = new ScriptedViewRequest(ScriptedViewKind.CameraMove, viewer, 0, new TilePos(5, 6), false),
        });
        director.Update(0, 0);

        Assert.True(director.TryTakeCameraMove(out TilePos tile));
        Assert.Equal(new TilePos(5, 6), tile);
        Assert.False(director.TryTakeCameraMove(out _));
    }

    private static SimEvent View(ScriptedViewKind kind, PlayerId player, int value) =>
        new(default, "t", "scripted_view", "") { ScriptedView = new ScriptedViewRequest(kind, player, value, default, false) };
}
