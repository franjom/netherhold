// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

/// <summary>DungeonRunner.ApplyHudAction as <see cref="HudActionRouter"/>: the gate, the two cancels, then one branch, with
/// the host's calls in the order Unity made them.</summary>
public sealed class HudActionRouterTests
{
    private static readonly PlayerId Keeper = PlayerIds.FirstKeeper;

    private sealed class Host : IHudActionHost, IPointerGestureHolder
    {
        public readonly List<string> Calls = [];
        public void NavigatePortal()
        {
            Calls.Add("portal");
        }

        public void NavigateHeart()
        {
            Calls.Add("heart");
        }

        public void NavigateFight()
        {
            Calls.Add("fight");
        }

        public void ZoomMinimap(bool zoomIn, bool zoomOut)
        {
            Calls.Add($"zoom {zoomIn} {zoomOut}");
        }

        public void PostGuiButton(GuiButtonTarget target)
        {
            Calls.Add($"post {target}");
        }

        public void CancelPointerGesture()
        {
            Calls.Add("panel cancel");
        }
    }

    private sealed class Rig
    {
        public readonly ClassicHudControls Controls = new();
        public readonly HudToolState Tools = new();
        public readonly PlayerInputTranslator Input = new();
        public readonly Host Host = new();
        public readonly BottomPanelView Panel = new(new GameDatabase([], [], [], new GlobalVariables()), Keeper, (_, name) => name);
        public readonly HudActionRouter Router;

        public Rig()
        {
            Router = new HudActionRouter(Controls, Tools, Input, Host);
            Drag();
        }

        public void Drag()
        {
            Input.Translate(new PlayerIntent(Keeper, PlayerToolMode.BuildRoom, new TilePos(1, 1),
            PointerButtonTransition.Pressed, RoomTypeId.Treasury), new PlayerInputSnapshot(false));
        }

        public bool Dragging
        {
            get
            {
                return Input.DragFrom(PlayerToolMode.BuildRoom) != null;
            }
        }

        public void Apply(ClassicHudAction action)
        {
            Router.Apply(action, Host, Panel);
        }
    }

    [Theory]
    [InlineData(ClassicHudAction.NavigatePortal, "portal")]
    [InlineData(ClassicHudAction.NavigateHeart, "heart")]
    [InlineData(ClassicHudAction.NavigateFight, "fight")]
    public void NavigationCancelsBothGesturesAndGoesToTheHost(ClassicHudAction action, string call)
    {
        var rig = new Rig();
        rig.Apply(action);
        Assert.Equal(["panel cancel", call], rig.Host.Calls);
        Assert.False(rig.Dragging);
    }

    [Fact]
    public void ZoomPostsItsGuiButtonThenZooms()
    {
        var rig = new Rig();
        rig.Apply(ClassicHudAction.ZoomIn);
        rig.Apply(ClassicHudAction.ZoomOut);
        string post = $"post {GuiButtonTarget.MinimapZoom}";
        Assert.Equal(["panel cancel", post, "zoom True False", "panel cancel", post, "zoom False True"], rig.Host.Calls);
    }

    [Fact]
    public void ACornerPostsItsGuiButtonThenSetsTheTool()
    {
        var rig = new Rig();
        rig.Apply(ClassicHudAction.SelectSell);
        Assert.Equal(["panel cancel", $"post {GuiButtonTarget.Sell}"], rig.Host.Calls);
        Assert.False(rig.Dragging);
        Assert.Equal(DungeonToolMode.SellRoom, rig.Tools.Tool);
        Assert.Equal(ClassicHudAction.SelectSell, rig.Tools.SelectedCorner);
        Assert.Equal(PlayerToolMode.SellRoom, rig.Panel.Selection.Tool);
        rig.Apply(ClassicHudAction.SelectQuery);
        Assert.Equal($"post {GuiButtonTarget.Query}", rig.Host.Calls[^1]);
        Assert.True(rig.Tools.QueryTool);
        Assert.Equal(ClassicHudAction.SelectQuery, rig.Tools.SelectedCorner);
        Assert.Equal(PlayerToolMode.Hand, rig.Panel.Selection.Tool);
    }

    [Fact]
    public void OtherActionsAreTheControlsAfterTheCancels()
    {
        var rig = new Rig();
        rig.Apply(ClassicHudAction.ToggleOptions);
        Assert.True(rig.Controls.OptionsVisible);
        Assert.Equal(["panel cancel"], rig.Host.Calls);
        Assert.False(rig.Dragging);
    }

    [Fact]
    public void ARefusedActionDoesNothing()
    {
        var rig = new Rig();
        rig.Apply(ClassicHudAction.None);
        rig.Apply(ClassicHudAction.ToggleOptions);   // modal from here
        rig.Host.Calls.Clear();
        rig.Drag();
        rig.Apply(ClassicHudAction.SelectQuery);
        rig.Apply(ClassicHudAction.NavigateHeart);
        rig.Apply(ClassicHudAction.ZoomIn);
        Assert.Empty(rig.Host.Calls);
        Assert.True(rig.Dragging);
        Assert.Equal(new HudToolState().SelectedCorner, rig.Tools.SelectedCorner);
        Assert.False(rig.Tools.QueryTool);
    }

    [Fact]
    public void WithNoBottomPanelOnlyTheDragIsCancelled()
    {
        var rig = new Rig();
        rig.Router.Apply(ClassicHudAction.NavigatePortal, null, null);
        Assert.Equal(["portal"], rig.Host.Calls);
        Assert.False(rig.Dragging);
    }
}
