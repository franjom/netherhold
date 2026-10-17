// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>Camera path playback, the letterbox and the subtitle queue (camera-paths.md,
/// ADR-0496).</summary>
public class CameraPathPlaybackTests
{
    private static readonly KcsRotation Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    private static KcsFrame[] Frames(int count, float z = -3f) =>
        [.. Enumerable.Range(0, count).Select(i => new KcsFrame(0, 0, z, Identity, 1f, 12000f))];

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
    [InlineData(13240, false)]
    [InlineData(13250, true)]
    public void TheHeartsPlugPathEndsAfterThirteenAndAQuarterSeconds(double milliseconds, bool finished)
    {
        var path = new CameraPathPlayback(0, Frames(399), new TilePos(0, 0));
        path.Advance(milliseconds);
        Assert.Equal(finished, path.Finished);
    }

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
        director.Update(0);
        Assert.False(director.PathPlaying);

        director.Emit(View(ScriptedViewKind.CameraPath, viewer, 1));
        director.Emit(View(ScriptedViewKind.Speech, viewer, 3));
        director.Update(10);
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
        director.Update(0);

        Assert.True(director.TryTakeCameraMove(out TilePos tile));
        Assert.Equal(new TilePos(5, 6), tile);
        Assert.False(director.TryTakeCameraMove(out _));
    }

    private static SimEvent View(ScriptedViewKind kind, PlayerId player, int value) =>
        new(default, "t", "scripted_view", "") { ScriptedView = new ScriptedViewRequest(kind, player, value, default, false) };
}
