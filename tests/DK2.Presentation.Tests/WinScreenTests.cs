// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class WinScreenTests
{
    private static readonly PlayerId Viewer = new(3);

    private static SimEvent View(ScriptedViewKind kind, int value = 0, bool flag = false, int value2 = 0) =>
        new(default, "t", "scripted_view", "")
        {
            ScriptedView = new ScriptedViewRequest(kind, Viewer, value, default, flag, Value2: value2),
        };

    [Fact]
    public void ARewardWithNoSpeechDoesNotPlayMentorEntryZero()
    {
        var screen = new WinScreen(new byte[] { 30, 0, 0, 0, 0 }, _ => "x");
        var spoken = new List<uint>(); screen.SpeechRequested += spoken.Add;
        screen.Start(0);
        Assert.Empty(spoken);
    }

    [Fact]
    public void ARewardWaitsForTheAudioChannelEvenAfterItsSubtitleAndTabletEnd()
    {
        var spoken = new List<uint>();
        var screen = new WinScreen(new byte[] { 12, 42, 0, 0, 0 }, _ => "x");
        screen.SpeechRequested += spoken.Add;
        screen.Start(0);
        for (double now = 0; now <= 5000; now += 10) screen.Update(now, false, false, speechPlaying: true);
        Assert.Equal(12, screen.Current!.Value.Id);
        Assert.Equal(new uint[] { 1350 }, spoken);
        screen.Update(5010, false, false, speechPlaying: false);
        Assert.Equal(42, screen.Current!.Value.Id);
        Assert.Equal(new uint[] { 1350, 1351 }, spoken);
    }

    [Fact]
    public void TheTableAnnouncesLevelOnesRewardsWithTheirSpeechLines()
    {
        Assert.Equal(52, WinScreen.Items.Count);
        Assert.Equal(("Dark_Library", 1350u), (WinScreen.Items[12].Name, WinScreen.Items[12].SpeechTextId));
        Assert.Equal(("Training", 1351u), (WinScreen.Items[42].Name, WinScreen.Items[42].SpeechTextId));
        for (int i = 0; i < WinScreen.Items.Count; i++) Assert.Equal(i, WinScreen.Items[i].Id);
    }

    private static void Run(WinScreen screen, double from, double to, bool cameraOnPath = false)
    {
        for (double t = from; t <= to; t += 10) screen.Update(t, cameraOnPath, continueRequested: false);
    }

    [Fact]
    public void EachRewardWaitsForItsLineAndItsTabletAtAStandInThirtyFramesASecondThenTheDoneScreenTakesAContinueEveryHalfSecond()
    {
        var screen = new WinScreen(new byte[] { 12, 42, 0, 0, 0 }, id => id == 1350 ? "abcd" : "ab");
        screen.Start(0);
        Assert.Equal(12, screen.Current!.Value.Id);
        Run(screen, 0, 3000);
        // The line ends at 384 ms (4 characters); the tablet reaches frame 35 at 1140 ms and holds 2 s.
        Assert.Equal(12, screen.Current!.Value.Id);
        Run(screen, 3010, 3200);
        Assert.Equal(42, screen.Current!.Value.Id);
        int frame = screen.Tablet.Frame;
        Run(screen, 3210, 20_000, cameraOnPath: true);
        Assert.Equal(42, screen.Current!.Value.Id);               // a camera path holds the screen
        Assert.Equal(frame, screen.Tablet.Frame);                   // and its tablet
        double done = 20_010;
        for (; !screen.Done; done += 10) screen.Update(done, false, false);
        Assert.InRange(done, 23_000, 23_300);                       // the second tablet: ~3.1 s more
        Assert.Null(screen.Current);
        Assert.False(screen.Update(done + 10, false, true));        // the 500 ms deadline arms
        Assert.False(screen.Update(done + 520, false, false));      // re-arms without a request
        Assert.True(screen.Update(done + 1030, false, true));
    }

    [Fact]
    public void NoRewardsIsDoneAtOnce()
    {
        var screen = new WinScreen(new byte[5], _ => null);
        screen.Start(0);
        Assert.True(screen.Done);
    }

    [Fact]
    public void TheDirectorPassesTheFinalesRequestsOn()
    {
        var director = new ScriptedViewDirector(Viewer, _ => null, _ => null);
        bool winScreen = false, guiShown = true;
        (int Zoom, int Ms) zoom = default;
        director.WinScreenRequested += () => winScreen = true;
        director.GuiVisibilityRequested += shown => guiShown = shown;
        director.ZoomRequested += (z, ms) => zoom = (z, ms);
        director.Emit(View(ScriptedViewKind.CameraZoom, 0x5800, value2: 1000));
        director.Emit(View(ScriptedViewKind.CameraFollow, 42));
        director.Emit(View(ScriptedViewKind.WinScreen));
        director.Emit(View(ScriptedViewKind.HideGui));
        director.Emit(View(ScriptedViewKind.Letterbox, flag: true));
        director.Emit(View(ScriptedViewKind.Letterbox, flag: true));
        director.Update(0, 0);
        Assert.Equal((0x5800, 1000), zoom);
        Assert.Equal(42, director.FollowedCreatureId);
        Assert.True(winScreen);
        Assert.Equal(2, director.LetterboxRequestCount);
        director.Emit(View(ScriptedViewKind.Letterbox, 2));
        director.Update(10, 0);
        Assert.Equal(0, director.LetterboxRequestCount);
        Assert.True(guiShown);
    }
}
