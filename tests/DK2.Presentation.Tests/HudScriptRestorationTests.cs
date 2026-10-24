// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class HudScriptRestorationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptShowForcesGuiVisibleAndPreservesAnOpenModal(bool options)
    {
        var controls = new ClassicHudControls();
        if (options) controls.Apply(ClassicHudAction.ToggleOptions);
        else controls.Apply(ClassicHudAction.ToggleGui);
        var director = Director(controls);
        Letterbox(director, true);
        director.Update(0, 0);
        Assert.False(controls.GuiVisible);
        Letterbox(director, false);
        director.Update(100, 0);
        Assert.True(controls.GuiVisible);
        Assert.Equal(options, controls.OptionsVisible);
        Assert.True(controls.Visibility(director.HudFor(ClassicCameraMode.Dungeon)).ShowNormalHud);
    }

    [Fact]
    public void EndingAPathDoesNotRestoreAManuallyHiddenGui()
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleGui);
        var director = Director(controls);
        StartPath(director);
        director.Update(0, 0);
        Assert.True(director.Skip());
        Assert.Equal(0, director.LetterboxRequestCount);
        Assert.False(controls.GuiVisible);
        Assert.False(controls.Visibility(director.HudFor(ClassicCameraMode.Dungeon)).ShowNormalHud);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedScriptAndPathRestoreOnlyAfterBothRelease(bool scriptEndsFirst)
    {
        var controls = new ClassicHudControls();
        controls.Apply(ClassicHudAction.ToggleOptions);
        var director = Director(controls);
        Letterbox(director, true);
        StartPath(director);
        director.Update(0, 0);
        Assert.Equal(2, director.LetterboxRequestCount);
        if (scriptEndsFirst)
        {
            Letterbox(director, false);
            director.Update(100, 0);
        }
        else Assert.True(director.Skip());
        Assert.Equal(1, director.LetterboxRequestCount);
        Assert.False(controls.Visibility(director.HudFor(ClassicCameraMode.Dungeon)).ShowNormalHud);
        if (scriptEndsFirst) Assert.True(director.Skip());
        else
        {
            Letterbox(director, false);
            director.Update(100, 0);
        }
        Assert.True(controls.OptionsVisible);
        Assert.True(controls.Visibility(director.HudFor(ClassicCameraMode.Dungeon)).ShowNormalHud);
    }

    [Fact]
    public void OtherPlayersLetterboxCannotChangeTheViewersGui()
    {
        var controls = new ClassicHudControls();
        var director = Director(controls);
        Letterbox(director, true, new PlayerId(4));
        director.Update(0, 0);
        Assert.True(controls.GuiVisible);
        Assert.Equal(0, director.LetterboxRequestCount);
    }

    private static ScriptedViewDirector Director(ClassicHudControls controls)
    {
        var rotation = new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1);
        KcsFrame[] frames = [new(0, 0, -3, rotation, 1, 12000), new(0, 0, -3, rotation, 1, 12000)];
        var director = new ScriptedViewDirector(PlayerIds.FirstKeeper, _ => frames, _ => null);
        director.GuiVisibilityRequested += controls.SetGuiVisible;
        return director;
    }

    private static void StartPath(ScriptedViewDirector director) => director.Emit(
        new SimEvent(default, "test", "path", "")
        {
            ScriptedView = new ScriptedViewRequest(ScriptedViewKind.CameraPath,
                PlayerIds.FirstKeeper, 1, default, false),
        });

    private static void Letterbox(ScriptedViewDirector director, bool enabled, PlayerId? player = null)
    {
        var request = new ScriptedViewRequest(ScriptedViewKind.Letterbox,
            player ?? PlayerIds.FirstKeeper, 0, default, enabled);
        director.Emit(new SimEvent(default, "test", "letterbox", "") { ScriptedView = request });
    }
}
