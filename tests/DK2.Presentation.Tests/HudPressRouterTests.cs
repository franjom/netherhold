// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Unity's DungeonRunner.QueueHudInput as <see cref="HudPressRouter"/> (plan step 2c): full map, options, overview,
/// minimap, gold, heart, each taker ending the frame, with the host's calls in the order Unity made them.</summary>
public sealed class HudPressRouterTests
{
    private static readonly PlayerId Keeper = PressFixture.Keeper;

    private sealed class Host(ClassicHudControls controls) : IHudPressHost
    {
        public readonly List<string> Calls = [];
        public bool OverviewTakes;
        public CreatureOverviewRequest Request;
        public ClassicHudAction Minimap;
        public KeeperResourceKind Resource;
        public ClassicOptionsAction Option;

        public void ApplyHudAction(ClassicHudAction action) { Calls.Add("apply " + action); controls.Apply(action); }
        public void GoToFullMapTile() => Calls.Add("full map tile");
        public void PollGameOptionsKeyboard() => Calls.Add("poll keys");
        public ClassicOptionsAction OptionsActionAtPointer() { Calls.Add("options entry"); return Option; }
        public void RefreshSaves() => Calls.Add("refresh saves");
        public void StartSession(ClassicSessionAction session) => Calls.Add("session " + session);
        public bool TryPressOverview(in HudPress press, out CreatureOverviewRequest request)
        {
            Calls.Add("overview");
            request = Request;
            return OverviewTakes;
        }
        public void FocusCreature(FpPosition3 position) => Calls.Add("focus");
        public ClassicHudAction PressMinimap(in HudPress press) { Calls.Add("minimap"); return Minimap; }
        public KeeperResourceKind ResourceAtPointer() { Calls.Add("resource"); return Resource; }
    }

    private sealed class Rig
    {
        public readonly GameDatabase Database;
        public readonly WorldState World;
        public readonly ClassicHudControls Controls = new();
        public readonly HudToolState Tools = new();
        public readonly PlayerInputTranslator Input = new();
        public readonly BottomPanelView Panel;
        public readonly CreatureOverview Overview;
        public readonly WorldView View;
        public readonly PickupFlow Pickups;
        public readonly Host Host;
        public readonly HudPressRouter Router;
        public readonly List<PlayerCommand> Output = [];

        public Rig() : this(PressFixture.Database()) { }

        public Rig(GameDatabase database, WorldState? world = null)
        {
            Database = database;
            World = world ?? PressFixture.World(database);
            Panel = PressFixture.Panel(database);
            Overview = new CreatureOverview(database, Keeper, (_, name) => name);
            View = new WorldView(database, Keeper);
            Pickups = new PickupFlow(View, null);
            Host = new Host(Controls);
            Router = new HudPressRouter(Controls, Tools, Overview, Panel, Pickups, Keeper, Host);
        }

        public bool Route(HudPress press) => Router.Route(press, World, Database, 0, Output);
    }

    [Fact]
    public void TheOpenFullMapTakesEveryFrameAndAnyPressClosesIt()
    {
        var rig = new Rig();
        rig.Controls.Apply(ClassicHudAction.ToggleFullMap);

        Assert.True(rig.Route(new HudPress(false, false)));
        Assert.Equal(["apply None"], rig.Host.Calls);
        rig.Host.Calls.Clear();

        Assert.True(rig.Route(new HudPress(true, false)));
        Assert.Equal(["full map tile", "apply ToggleFullMap"], rig.Host.Calls);
        Assert.False(rig.Controls.FullMapVisible);

        rig.Controls.Apply(ClassicHudAction.ToggleFullMap);
        rig.Host.Calls.Clear();
        Assert.True(rig.Route(new HudPress(false, true)));
        Assert.Equal(["apply ToggleFullMap"], rig.Host.Calls);
    }

    [Fact]
    public void TheOpenOptionsMenuTakesEveryFrameAndActsOnlyOnALeftPress()
    {
        var rig = new Rig();
        rig.Controls.Apply(ClassicHudAction.ToggleOptions);

        Assert.True(rig.Route(new HudPress(false, true)));
        Assert.Empty(rig.Host.Calls);

        rig.Host.Option = ClassicOptionsAction.Save;
        Assert.True(rig.Route(new HudPress(true, false)));
        Assert.Equal(["options entry", "refresh saves", "apply OpenSave"], rig.Host.Calls);
    }

    [Fact]
    public void AConfirmedRestartIsTakenAfterItsActionAndHandedToTheHost()
    {
        var rig = new Rig();
        rig.Controls.Apply(ClassicHudAction.ToggleOptions);
        rig.Controls.Apply(ClassicHudAction.RestartLevel);
        rig.Host.Option = ClassicOptionsAction.Confirm;

        Assert.True(rig.Route(new HudPress(true, false)));

        Assert.Equal(["options entry", "apply Confirm", "session Restart"], rig.Host.Calls);
        Assert.Equal(ClassicSessionAction.None, rig.Controls.TakeSessionAction());
    }

    [Fact]
    public void TheGameOptionsPagesOnlyPollTheKeys()
    {
        var rig = new Rig();
        rig.Controls.Apply(ClassicHudAction.ToggleOptions);
        rig.Controls.Apply(ClassicHudAction.OpenGameOptions);
        Assert.True(rig.Controls.GameOptionsVisible);

        Assert.True(rig.Route(new HudPress(true, true)));

        Assert.Equal(["poll keys"], rig.Host.Calls);
    }

    [Fact]
    public void TheOverviewIsAskedFirstAndItsPressTakesTheFrameEvenWhenItSelectsNothing()
    {
        var rig = new Rig();
        rig.Host.OverviewTakes = true;
        rig.Host.Request = new CreatureOverviewRequest(new CreatureOverviewType(1), CreatureOverviewSelection.PickupNext);

        Assert.True(rig.Route(new HudPress(true, false)));

        Assert.Equal(["overview"], rig.Host.Calls);
        Assert.Empty(rig.Output);
    }

    [Fact]
    public void AMinimapActionTakesTheFrameBeforeTheResources()
    {
        var rig = new Rig();
        rig.Host.Minimap = ClassicHudAction.ToggleOptions;

        Assert.True(rig.Route(new HudPress(true, false)));

        Assert.Equal(["overview", "minimap", "apply ToggleOptions"], rig.Host.Calls);
    }

    [Theory]
    [InlineData(false, false, false, 100)]
    [InlineData(true, false, false, 1000)]
    [InlineData(false, true, false, 10000)]
    [InlineData(true, true, false, 100)]
    [InlineData(false, false, true, 100)]
    [InlineData(true, false, true, 100)]
    public void ALeftPressOnTheGoldWithdrawsByTheModifiersThenResetsTheToolKeepingTheCorner(bool shift, bool control, bool alt,
        int amount)
    {
        var rig = new Rig();
        rig.Tools.SelectCorner(ClassicHudAction.SelectQuery, rig.Input, rig.Panel);
        rig.Host.Resource = KeeperResourceKind.Gold;

        Assert.True(rig.Route(new HudPress(true, false, shift, control, alt)));

        Assert.Equal(["overview", "minimap", "resource"], rig.Host.Calls);
        Assert.Equal(new WithdrawCounterGoldCommand(Keeper, new GoldAmount(amount)), Assert.Single(rig.Output));
        Assert.False(rig.Tools.QueryTool);
        Assert.Equal(DungeonToolMode.Hand, rig.Tools.Tool);
        Assert.Equal(ClassicHudAction.SelectQuery, rig.Tools.SelectedCorner);
    }

    [Fact]
    public void ARightPressOnTheGoldFallsThroughAndARightPressOnTheHeartNavigates()
    {
        var rig = new Rig();
        rig.Host.Resource = KeeperResourceKind.Gold;
        Assert.False(rig.Route(new HudPress(false, true)));
        Assert.Empty(rig.Output);

        rig.Host.Resource = KeeperResourceKind.Heart;
        rig.Host.Calls.Clear();
        Assert.False(rig.Route(new HudPress(true, false)));
        Assert.Equal(["overview", "minimap", "resource"], rig.Host.Calls);

        rig.Host.Calls.Clear();
        Assert.True(rig.Route(new HudPress(false, true)));
        Assert.Equal(["overview", "minimap", "resource", "apply NavigateHeart"], rig.Host.Calls);
    }

    [Fact]
    public void AFrameNoPanelTakesIsLeftToTheWorld()
    {
        var rig = new Rig();

        Assert.False(rig.Route(new HudPress(false, false)));

        Assert.Equal(["overview", "minimap", "resource"], rig.Host.Calls);
        Assert.Empty(rig.Output);
    }

    [InstallFact]
    public void AnOverviewPickupIsPostedAndARefusedOneStillTakesThePress()
    {
        (GameDatabase? db, WorldState? world, CreatureState? goblin) = PressFixture.Goblin();
        var rig = new Rig(db, world);
        rig.Host.OverviewTakes = true;
        rig.Host.Request = new CreatureOverviewRequest(new CreatureOverviewType(goblin.CreatureTypeId),
            CreatureOverviewSelection.PickupNext);

        Assert.True(rig.Route(new HudPress(true, false)));
        Assert.Equal(new PickUpCreatureCommand(Keeper, goblin.Id), Assert.Single(rig.Output));

        // A world click's pickup is pending, which the overview does not see: it selects the Goblin again, the local
        // rules refuse it, and the press is still taken (the audit's open point).
        var refused = new Rig(db, world);
        refused.Pickups.PostWorldCommand(world, db, new PickUpCreatureCommand(Keeper, goblin.Id), 0, null, []);
        refused.Host.OverviewTakes = true;
        refused.Host.Request = rig.Host.Request;
        Assert.True(refused.Route(new HudPress(true, false)));
        Assert.Empty(refused.Output);
        Assert.Equal(["overview"], refused.Host.Calls);
    }
}
