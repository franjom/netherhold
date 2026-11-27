// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>Unity's DungeonRunner.QueueInput as <see cref="WorldPointerInput"/> (plan step 2h): the gates in order, each
/// ending the frame, and the host's calls in the order Unity made them.</summary>
public sealed class WorldPointerInputTests
{
    private static readonly PlayerId Keeper = PressFixture.Keeper;
    private static readonly TilePos Tile = new(2, 2);

    private sealed class Host(ClassicHudControls controls) : IWorldPointerHost, IHudPressHost
    {
        public readonly List<string> Calls = [];
        public TilePos? Tile;
        public Func<string>? AtMinimap;

        public void MinimapPress(in HudPress press)
        {
            Calls.Add("minimap " + press.Primary + AtMinimap?.Invoke());
        }

        public FpPosition2? PointerPosition(TilePos tile) { Calls.Add("position"); return null; }
        public bool PointerOverHud() { Calls.Add("over hud"); return false; }
        public TilePos? PointerTile() { Calls.Add("tile"); return Tile; }
        public WorldHoverHit Hover(TilePos tile) { Calls.Add("hover"); return new WorldHoverHit(WorldHoverKind.Tile, tile); }
        public void FollowQueriedCreature()
        {
            Calls.Add("follow");
        }

        public void ApplyHudAction(ClassicHudAction action) { Calls.Add("apply " + action); controls.Apply(action); }
        public void GoToFullMapTile()
        {
            Calls.Add("full map tile");
        }

        public void PollGameOptionsKeyboard()
        {
            Calls.Add("poll keys");
        }

        public ClassicOptionsAction OptionsActionAtPointer() { Calls.Add("options entry"); return ClassicOptionsAction.None; }
        public void RefreshSaves()
        {
            Calls.Add("refresh saves");
        }

        public void StartSession(ClassicSessionAction session)
        {
            Calls.Add("session " + session);
        }

        public bool TryPressOverview(in HudPress press, out CreatureOverviewRequest request)
        {
            Calls.Add("overview");
            request = default;
            return false;
        }
        public void FocusCreature(FpPosition3 position)
        {
            Calls.Add("focus");
        }

        public ClassicHudAction PressMinimap(in HudPress press) { Calls.Add("hud minimap"); return ClassicHudAction.None; }
        public KeeperResourceKind ResourceAtPointer() { Calls.Add("resource"); return KeeperResourceKind.None; }
    }

    private sealed class Rig
    {
        public readonly GameDatabase Database = PressFixture.Database();
        public readonly WorldState World;
        public readonly ClassicHudControls Controls = new();
        public readonly HudToolState Tools = new();
        public readonly PlayerInputTranslator Input = new();
        public readonly WorldView View;
        public readonly Host Host;
        public readonly BottomPanelView Panel;
        public readonly WorldPointerInput Pointer;
        public readonly List<PlayerCommand> Pending = [];

        public Rig(FrameJournalSession? journal = null)
        {
            World = PressFixture.World(Database);
            View = new WorldView(Database, Keeper);
            Host = new Host(Controls);
            Panel = PressFixture.Panel(Database);
            BottomPanelView panel = Panel;
            var pickups = new PickupFlow(View, null);
            var presses = new HudPressRouter(Controls, Tools, new CreatureOverview(Database, Keeper, (_, name) => name), panel, pickups,
                Keeper, Host);
            var clicks = new WorldClickCommands(Tools, Input, PressFixture.Query(Database), panel, Keeper, Host);
            Pointer = new WorldPointerInput(Input, Tools, presses, clicks, pickups, View, null, null, journal, Host);
        }

        /// <summary>A sell drag in flight, which only a cancel or a press drops.</summary>
        public void StartDrag()
        {
            Input.Translate(new PlayerIntent(Keeper, PlayerToolMode.SellRoom, Tile, PointerButtonTransition.Pressed, default),
                PlayerInputSnapshot.Capture(View, World, Keeper));
            Assert.Equal(Tile, Input.DragFrom(PlayerToolMode.SellRoom));
        }

        public void Queue(bool ordinary = true, bool possessing = false, bool modal = false, bool primary = false,
            bool secondary = false, bool primaryUp = false)
        {
            Pointer.Queue(new WorldPointerFrame(Keeper, ordinary, possessing, modal, new HudPress(primary, secondary), primaryUp,
                false, 0), World, Database, Pending);
        }
    }

    [Fact]
    public void WithoutOrdinaryInput_TheWorldDragIsDroppedAndNothingElseRuns()
    {
        var rig = new Rig();
        rig.StartDrag();
        rig.Queue(ordinary: false, primary: true);
        Assert.Null(rig.Input.DragFrom(PlayerToolMode.SellRoom));
        Assert.Empty(rig.Host.Calls);
        Assert.Empty(rig.Pending);
    }

    [Fact]
    public void Possessing_OnlyAModalPanelTakesThePressAndTheWorldDragIsDropped()
    {
        var rig = new Rig();
        rig.StartDrag();
        rig.Queue(possessing: true, primary: true);
        Assert.Null(rig.Input.DragFrom(PlayerToolMode.SellRoom));
        Assert.Empty(rig.Host.Calls);

        rig.Controls.Apply(ClassicHudAction.ToggleOptions);
        rig.Queue(possessing: true, modal: true, primary: true);
        Assert.Equal(["options entry", "apply None"], rig.Host.Calls);
    }

    [Fact]
    public void AHudPressTakesTheFrameBeforeTheMinimapAndTheWorld()
    {
        var rig = new Rig();
        rig.Controls.Apply(ClassicHudAction.ToggleFullMap);
        rig.Host.Calls.Clear();
        rig.Queue(primary: true);
        Assert.DoesNotContain(rig.Host.Calls, c => c.StartsWith("minimap ", StringComparison.Ordinal) || c == "tile");
    }

    [Fact]
    public void TheHudTakesThePressBeforeAHeldQueryTool()
    {
        var rig = new Rig();
        rig.Tools.SelectCorner(ClassicHudAction.SelectQuery, rig.Input, rig.Panel);
        rig.Controls.Apply(ClassicHudAction.ToggleFullMap);
        rig.Queue(secondary: true);
        // The full map took the right press and closed; the query tool is still held.
        Assert.False(rig.Controls.FullMapVisible);
        Assert.True(rig.Tools.QueryTool);
    }

    [Fact]
    public void TheMinimapIsAskedBeforeTheToolIsSelected()
    {
        var rig = new Rig();
        rig.StartDrag();
        rig.Host.AtMinimap = () => rig.Input.DragFrom(PlayerToolMode.SellRoom) is null ? " dropped" : " held";
        rig.Queue();
        Assert.Contains("minimap False held", rig.Host.Calls);
        Assert.Null(rig.Input.DragFrom(PlayerToolMode.SellRoom));
    }

    [Fact]
    public void AHeldQueryToolsRightPressTakesTheFrameBeforeTheMinimapAndTheWorld()
    {
        var rig = new Rig();
        rig.Tools.SelectCorner(ClassicHudAction.SelectQuery, rig.Input, rig.Panel);
        rig.Queue(secondary: true);
        Assert.False(rig.Tools.QueryTool);
        Assert.DoesNotContain(rig.Host.Calls, c => c.StartsWith("minimap ", StringComparison.Ordinal) || c == "tile");
    }

    [Fact]
    public void WithNoPressOrRelease_TheMinimapIsAskedAndTheToolSelectedButNoIntentIsBuilt()
    {
        var rig = new Rig();
        rig.StartDrag();
        rig.Queue();
        Assert.Equal(["overview", "hud minimap", "resource", "minimap False"], rig.Host.Calls);
        // The tool is the hand: selecting it drops the sell drag.
        Assert.Null(rig.Input.DragFrom(PlayerToolMode.SellRoom));
    }

    [Fact]
    public void APress_AsksThePickingInUnitysOrderAndJournalsTheIntent()
    {
        string root = Path.Combine(Path.GetTempPath(), "dk2-world-pointer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new FrameJournalSession(_ => { }, _ => { });
            journal.Open(root, "s", () => new Dictionary<string, string>());
            var rig = new Rig(journal);
            rig.Host.Tile = Tile;
            journal.BeginFrame(new FrameInputRecord(), rig.Pending);
            rig.Queue(primary: true, primaryUp: true);
            journal.Close("done", rig.Pending, 0, null);

            Assert.Equal(["overview", "hud minimap", "resource", "minimap True", "tile", "over hud", "position", "hover"], rig.Host.Calls);
            FrameInputJournal read;
            using (FileStream stream = File.OpenRead(Path.Combine(root, "s", FrameJournalSession.FramesFile)))
            {
                read = FrameInputJournal.Read(stream);
            }

            PlayerIntent intent = Assert.Single(Assert.Single(read.Frames).Intents);
            Assert.Equal(new PlayerIntent(Keeper, PlayerToolMode.Hand, Tile,
                PointerButtonTransition.Pressed | PointerButtonTransition.Released, rig.Tools.SelectedRoomId), intent);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void APressOffTheMap_BuildsAnIntentWithoutAPointAndAsksNoHover()
    {
        var rig = new Rig();
        rig.Queue(primary: true);
        Assert.Equal(["overview", "hud minimap", "resource", "minimap True", "tile", "over hud"], rig.Host.Calls);
        Assert.Empty(rig.Pending);
    }
}
