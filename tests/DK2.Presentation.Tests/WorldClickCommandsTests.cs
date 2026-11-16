// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

/// <summary>Unity's DungeonRunner.QueuePanelWorldAction as <see cref="WorldClickCommands"/> (plan step 2c): the query click,
/// the two right-click cancels (their reset subsets are <see cref="HudToolStateTests"/>'), and the door, trap and spell
/// commands, with the host's picking asked only where Unity asked it.</summary>
public sealed class WorldClickCommandsTests
{
    private static readonly PlayerId Keeper = PressFixture.Keeper;
    private static readonly TilePos At = new(3, 4);

    private sealed class Host : IWorldClickHost
    {
        public readonly List<string> Calls = [];
        public bool OverHud;
        public TilePos? Tile = At;
        public WorldHoverHit Hit = new(WorldHoverKind.Tile, At);

        public bool PointerOverHud() { Calls.Add("over hud"); return OverHud; }
        public TilePos? PointerTile() { Calls.Add("tile"); return Tile; }
        public WorldHoverHit Hover(TilePos tile) { Calls.Add("hover"); return Hit; }
        public void FollowQueriedCreature() => Calls.Add("follow");
    }

    private sealed class Rig
    {
        public readonly GameDatabase Database;
        public readonly HudToolState Tools = new();
        public readonly PlayerInputTranslator Input = new();
        public readonly CreatureQueryView Query;
        public readonly BottomPanelView Panel;
        public readonly Host Host = new();
        public readonly WorldClickCommands Commands;
        public readonly List<PlayerCommand> Output = [];

        public Rig(params KeeperSpellDef[] spells) : this(PressFixture.Database(spells)) { }

        public Rig(GameDatabase database)
        {
            Database = database;
            Query = PressFixture.Query(Database);
            Panel = PressFixture.Panel(Database);
            Commands = new WorldClickCommands(Tools, Input, Query, Panel, Keeper, Host);
        }

        public void Choose(BottomPanelChoiceKind kind, byte value) => Tools.SelectPanelChoice(
            new BottomPanelSelection(new BottomPanelChoiceKey(kind, value), PlayerToolMode.Hand, RoomTypeId.Portal), Input, Query);

        public void Drag() => Input.Translate(new PlayerIntent(Keeper, PlayerToolMode.BuildRoom, new TilePos(1, 1),
            PointerButtonTransition.Pressed, RoomTypeId.Treasury), new PlayerInputSnapshot(false));

        public bool Queue(bool primary, bool secondary) => Commands.Queue(new HudPress(primary, secondary), Database, Output);
    }

    [Fact]
    public void TheQueryToolQueriesTheClickedCreatureAndTakesEveryFrame()
    {
        var rig = new Rig();
        rig.Tools.SelectCorner(ClassicHudAction.SelectQuery, rig.Input, rig.Panel);
        Assert.True(rig.Queue(false, false));
        Assert.Empty(rig.Host.Calls);

        rig.Host.Hit = new WorldHoverHit(WorldHoverKind.Creature, At, 42);
        Assert.True(rig.Queue(true, false));

        Assert.Equal(["over hud", "tile", "hover", "follow"], rig.Host.Calls);
        Assert.Equal(new CreatureQueryTarget(42), rig.Query.Target);
        Assert.Empty(rig.Output);
        // With a creature queried, a left press picks nothing.
        rig.Host.Calls.Clear();
        Assert.True(rig.Queue(true, false));
        Assert.Empty(rig.Host.Calls);
    }

    [Fact]
    public void AQueryClickOnTheHudOrOnNoCreatureQueriesNothing()
    {
        var rig = new Rig();
        rig.Tools.SelectCorner(ClassicHudAction.SelectQuery, rig.Input, rig.Panel);
        rig.Host.OverHud = true;
        Assert.True(rig.Queue(true, false));
        Assert.Equal(["over hud"], rig.Host.Calls);

        rig.Host.OverHud = false;
        rig.Host.Calls.Clear();
        Assert.True(rig.Queue(true, false));
        Assert.Equal(["over hud", "tile", "hover"], rig.Host.Calls);
        Assert.Equal(CreatureQueryTarget.None, rig.Query.Target);
    }

    [Fact]
    public void ARightPressCancelsTheQueryTool()
    {
        var rig = new Rig();
        rig.Tools.SelectCorner(ClassicHudAction.SelectQuery, rig.Input, rig.Panel);
        rig.Query.Query(new CreatureQueryTarget(42));

        Assert.True(rig.Queue(false, true));

        Assert.False(rig.Tools.QueryTool);
        Assert.Equal(CreatureQueryTarget.None, rig.Query.Target);
        Assert.Equal(ClassicHudAction.None, rig.Tools.SelectedCorner);
    }

    [Fact]
    public void ARightPressWithAToolDropsTheDragFirstAndOnlyThenCancelsTheTool()
    {
        var rig = new Rig();
        rig.Tools.StartWith(DungeonToolMode.BuildRoom, RoomTypeId.Treasury);
        rig.Drag();
        Assert.NotNull(rig.Input.DragFrom(PlayerToolMode.BuildRoom));

        Assert.True(rig.Queue(false, true));
        Assert.Null(rig.Input.DragFrom(PlayerToolMode.BuildRoom));
        Assert.Equal(DungeonToolMode.BuildRoom, rig.Tools.Tool);

        Assert.True(rig.Queue(false, true));
        Assert.Equal(DungeonToolMode.Hand, rig.Tools.Tool);
        Assert.Empty(rig.Host.Calls);
    }

    [Fact]
    public void ARightPressWithTheBareHandIsLeftToTheHand()
    {
        var rig = new Rig();

        Assert.False(rig.Queue(false, true));
        Assert.Empty(rig.Host.Calls);
    }

    [Fact]
    public void ADoorOrTrapChoiceTakesEveryFrameAndPlacesOnALeftPressOnATile()
    {
        var rig = new Rig();
        rig.Choose(BottomPanelChoiceKind.Door, 2);
        Assert.True(rig.Queue(false, false));
        Assert.Empty(rig.Host.Calls);

        Assert.True(rig.Queue(true, false));
        Assert.Equal(["over hud", "tile"], rig.Host.Calls);
        Assert.Equal(new PlaceDoorCommand(Keeper, At, 2), Assert.Single(rig.Output));

        rig.Choose(BottomPanelChoiceKind.Trap, 5);
        rig.Output.Clear();
        Assert.True(rig.Queue(true, false));
        Assert.Equal(new PlaceTrapCommand(Keeper, At, 5), Assert.Single(rig.Output));

        rig.Output.Clear();
        rig.Host.OverHud = true;
        Assert.True(rig.Queue(true, false));
        rig.Host.OverHud = false;
        rig.Host.Tile = null;
        Assert.True(rig.Queue(true, false));
        Assert.Empty(rig.Output);
    }

    [Fact]
    public void WithoutAWorkshopADoorOrTrapChoiceTakesTheLeftPressAndPlacesNothing()
    {
        GameDatabase db = PressFixture.Database([], [PressFixture.Door(2)], [PressFixture.Trap(5, 0)]);
        var rig = new Rig(db);
        var door = new BottomPanelChoiceKey(BottomPanelChoiceKind.Door, 2);
        var trap = new BottomPanelChoiceKey(BottomPanelChoiceKind.Trap, 5);
        rig.Choose(BottomPanelChoiceKind.Door, 2);
        // Before a capture the panel has not dimmed the Workshop: the door is placed.
        Assert.True(rig.Queue(true, false));
        Assert.Equal(new PlaceDoorCommand(Keeper, At, 2), Assert.Single(rig.Output));

        // The keeper owns no Workshop tile (0x0040849F, 0x00408586).
        rig.Panel.Capture(PressFixture.World(db));
        Assert.True(rig.Panel.Choice(door)!.NeedsWorkshop);
        Assert.True(rig.Panel.Choice(trap)!.NeedsWorkshop);
        rig.Output.Clear(); rig.Host.Calls.Clear();
        Assert.True(rig.Queue(true, false));
        Assert.Equal(["over hud", "tile"], rig.Host.Calls);
        Assert.Empty(rig.Output);
        rig.Choose(BottomPanelChoiceKind.Trap, 5);
        Assert.True(rig.Queue(true, false));
        Assert.Empty(rig.Output);
    }

    [Fact]
    public void ARoomChoiceAndCreateImpsSpellAreLeftToTheHand()
    {
        var rig = new Rig();
        rig.Choose(BottomPanelChoiceKind.Room, (byte)RoomTypeId.Lair);
        Assert.False(rig.Queue(true, false));
        rig.Choose(BottomPanelChoiceKind.Spell, 4);
        Assert.False(rig.Queue(true, false));
        Assert.Empty(rig.Host.Calls);
        Assert.Empty(rig.Output);
    }

    [Fact]
    public void ASpellIsCastAtTheTileCentreOrOnTheHoveredCreatureByItsTarget()
    {
        var rig = new Rig(PressFixture.Spell(9, targetSelector: 0), PressFixture.Spell(10, targetSelector: 1));
        rig.Choose(BottomPanelChoiceKind.Spell, 9);
        Assert.True(rig.Queue(true, false));
        Assert.Equal(new CastSpellAtPlaceCommand(Keeper, 9, CreatureState.CentreOf(At.X), CreatureState.CentreOf(At.Y)),
            Assert.Single(rig.Output));
        Assert.Equal(["over hud", "tile"], rig.Host.Calls);

        rig.Choose(BottomPanelChoiceKind.Spell, 10);
        rig.Output.Clear(); rig.Host.Calls.Clear();
        Assert.True(rig.Queue(true, false));
        Assert.Empty(rig.Output);
        Assert.Equal(["over hud", "tile", "hover"], rig.Host.Calls);

        rig.Host.Hit = new WorldHoverHit(WorldHoverKind.Creature, At, 42);
        Assert.True(rig.Queue(true, false));
        Assert.Equal(new CastSpellOnCreatureCommand(Keeper, 10, 42), Assert.Single(rig.Output));

        rig.Choose(BottomPanelChoiceKind.Spell, 11);
        rig.Output.Clear();
        Assert.True(rig.Queue(true, false));
        Assert.Empty(rig.Output);
    }
}
