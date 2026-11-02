// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation.Tests;

/// <summary>The sounds the hand's cursor requests from its state callbacks: the tag state's
/// <c>0x0040E300</c> (HAND 0xEC when the state is entered) and the slap state's <c>0x0040E520</c>
/// (HAND 0xC5 once the swing reaches frame 3), both at the pointer's world position, height 2.</summary>
public sealed class HandSoundTests
{
    private static FpPosition2 At(double x, double y) => new(new Fp32((int)(x * 4096)), new Fp32((int)(y * 4096)));

    private static SoundRequest Only(HandCursorFeedback feedback)
    {
        SoundRequest request = Assert.Single(feedback.Sounds.Pending);
        feedback.Sounds.Clear();
        return request;
    }

    [Fact]
    public void EnteringTheTagStateRequestsHandSoundECAtThePointerTwoTilesUp()
    {
        var feedback = new HandCursorFeedback();
        feedback.Play(HandCursorKind.Tag, 700, At(2.5, 3.5));
        SoundRequest request = Only(feedback);
        Assert.Equal("hand", request.Category.Name);
        Assert.Equal(0xECu, request.Id.Value);
        Assert.Equal(new Vector3(2.5f, 2f, -3.5f), request.World);   // DungeonScene.Ground's (x, height, -y)
        Assert.Equal(SoundVoiceId.None, request.Voice);              // handle 0: no voice to retain or replace
        Assert.False(request.Subtitle);
    }

    [Fact]
    public void OneTagSoundPerRequestAndNoneWhileTheStateRunsOnOrWhenOtherStatesPlay()
    {
        var feedback = new HandCursorFeedback();
        feedback.Play(HandCursorKind.Tag, 700, At(1, 1));
        Only(feedback);
        feedback.Resolve(HandCursorKind.Dig, false, false, true, 720, At(1, 1));
        feedback.Resolve(HandCursorKind.Dig, false, false, true, 800, At(1, 1));   // state 6, held
        feedback.Resolve(HandCursorKind.Dig, false, false, false, 900, At(1, 1));
        Assert.Empty(feedback.Sounds.Pending);

        feedback.Play(HandCursorKind.DropThing, 1000, At(1, 1));
        feedback.Play(HandCursorKind.DropGold, 1100, At(1, 1));
        Assert.Empty(feedback.Sounds.Pending);

        feedback.Play(HandCursorKind.Tag, 2000, At(1, 1));   // a second tag drag is a second request
        feedback.Play(HandCursorKind.Tag, 2500, At(1, 1));
        Assert.Equal(2, feedback.Sounds.Pending.Count);
    }

    [Fact]
    public void ATagWithNoPointerPositionPlaysNothing()
    {
        var feedback = new HandCursorFeedback();
        feedback.Play(HandCursorKind.Tag, 700);
        Assert.Empty(feedback.Sounds.Pending);
    }

    [Fact]
    public void TheSlapSoundsOnceWhenTheSwingReachesFrameThreeAtThePointerThen()
    {
        var feedback = new HandCursorFeedback();
        feedback.Play(HandCursorKind.Slap, 1000, At(1, 1));
        Assert.Empty(feedback.Sounds.Pending);   // entering the state is silent
        feedback.Advance(1099, At(2, 2));        // frame 2 at 30 a second
        Assert.Empty(feedback.Sounds.Pending);

        feedback.Advance(1100, At(5.25, 6.75));  // frame 3: the pointer has moved since the click
        SoundRequest request = Only(feedback);
        Assert.Equal("hand", request.Category.Name);
        Assert.Equal(0xC5u, request.Id.Value);
        Assert.Equal(new Vector3(5.25f, 2f, -6.75f), request.World);
        Assert.Equal(SoundVoiceId.None, request.Voice);

        feedback.Advance(1200, At(5, 5));
        feedback.Advance(1499, At(5, 5));
        Assert.Empty(feedback.Sounds.Pending);   // the latch holds until the swing completes
    }

    [Fact]
    public void ResolveStepsTheSlapToo()
    {
        var feedback = new HandCursorFeedback();
        feedback.Play(HandCursorKind.Slap, 0, At(1, 1));
        Assert.Equal(HandCursorKind.Slap, feedback.Resolve(HandCursorKind.Point, false, false, false, 120, At(1, 1)));
        Assert.Equal(0xC5u, Only(feedback).Id.Value);
    }

    [Fact]
    public void ASlapRequestedAgainBeforeFrameFourIsIgnoredAfterwardsItRestartsWithoutAnotherSound()
    {
        var feedback = new HandCursorFeedback();
        feedback.Play(HandCursorKind.Slap, 0, At(1, 1));
        feedback.Play(HandCursorKind.Slap, 100, At(1, 1));   // frame 3: still inside the first swing, ignored
        feedback.Advance(120, At(1, 1));                     // frame 3 of the first swing, not frame 0 of a restart
        Only(feedback);

        feedback.Play(HandCursorKind.Slap, 400, At(1, 1));   // frame 12: restarts, latch still set
        Assert.Equal(HandCursorKind.Slap, feedback.Resolve(HandCursorKind.Point, false, false, false, 700, At(1, 1)));
        feedback.Advance(700, At(1, 1));                     // frame 9 of the restart
        Assert.Empty(feedback.Sounds.Pending);

        Assert.Equal(HandCursorKind.Point, feedback.Resolve(HandCursorKind.Point, false, false, false, 900, At(1, 1)));  // completed: latch reset
        feedback.Play(HandCursorKind.Slap, 1000, At(1, 1));
        feedback.Advance(1100, At(1, 1));
        Assert.Equal(0xC5u, Only(feedback).Id.Value);
    }

    [Fact]
    public void AHitchedFrameStillSoundsTheSlapBeforeItCompletes()
    {
        var feedback = new HandCursorFeedback();
        feedback.Play(HandCursorKind.Slap, 0, At(1, 1));
        feedback.Resolve(HandCursorKind.Point, false, false, false, 5000, At(1, 1));   // callback 3 runs before completion
        Assert.Equal(0xC5u, Only(feedback).Id.Value);
    }
}
