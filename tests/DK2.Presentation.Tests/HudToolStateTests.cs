// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

/// <summary>The HUD tool state moved out of Unity's runner (plan step 2b). Each reset site is pinned as it was: which fields
/// change, which are kept, and which of the translator's, the panel's and the query's calls happen. The six sites differ on
/// purpose of this move (the step-2 audit's "Tool resets"); a test that fails here means a site's subset changed.</summary>
public sealed class HudToolStateTests
{
    private static readonly PlayerId Keeper = PlayerIds.FirstKeeper;
    private static readonly BottomPanelChoiceKey Lair = new(BottomPanelChoiceKind.Room, (byte)RoomTypeId.Lair);
    private static readonly BottomPanelChoiceKey Door = new(BottomPanelChoiceKind.Door, 2);
    private static readonly CreatureQueryTarget Queried = new(42);

    private readonly record struct Fields(DungeonToolMode Tool, RoomTypeId Room, BottomPanelChoiceKey? Choice, bool Query,
        ClassicHudAction Corner);

    private static Fields Of(HudToolState state)
    {
        return new(state.Tool, state.SelectedRoomId, state.PanelChoice, state.QueryTool, state.SelectedCorner);
    }

    private static BottomPanelView Panel()
    {
        return new(new GameDatabase([], [], [], new GlobalVariables()), Keeper, (_, name) => name);
    }

    private static CreatureQueryView Query()
    {
        var view = new CreatureQueryView(new GameDatabase([], [], [], new GlobalVariables()), _ => "", Keeper, new NoNames());
        view.Query(Queried);
        return view;
    }

    /// <summary>A translator with a build drag in progress: <see cref="PlayerInputTranslator.CancelPointerGesture"/> drops it.</summary>
    private static PlayerInputTranslator Dragging()
    {
        var input = new PlayerInputTranslator();
        input.Translate(new PlayerIntent(Keeper, PlayerToolMode.BuildRoom, new TilePos(1, 1), PointerButtonTransition.Pressed,
            RoomTypeId.Treasury), new PlayerInputSnapshot(false));
        Assert.NotNull(input.DragFrom(PlayerToolMode.BuildRoom));
        return input;
    }

    /// <summary>A translator waiting for a special's creature: <see cref="PlayerInputTranslator.CancelTool"/> drops it.</summary>
    private static PlayerInputTranslator Targeting()
    {
        var input = new PlayerInputTranslator();
        Assert.Null(input.ActivateStoredSpecial(Keeper, 8, SpecialTargeting.KillCreatures));
        return input;
    }

    /// <summary>The build tool on the Lair, chosen from the panel.</summary>
    private static HudToolState Building()
    {
        var state = new HudToolState();
        state.SelectPanelChoice(new BottomPanelSelection(Lair, PlayerToolMode.BuildRoom, RoomTypeId.Lair), new PlayerInputTranslator(), null);
        Assert.Equal(new Fields(DungeonToolMode.BuildRoom, RoomTypeId.Lair, Lair, false, ClassicHudAction.None), Of(state));
        return state;
    }

    /// <summary>Query from the minimap corner, after the Lair was built (so the room is not the default).</summary>
    private static HudToolState Querying()
    {
        HudToolState state = Building();
        state.SelectCorner(ClassicHudAction.SelectQuery, new PlayerInputTranslator(), Panel());
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, true, ClassicHudAction.SelectQuery), Of(state));
        return state;
    }

    [Fact]
    public void TheToolMapsToTheTranslatorsAndAnUnknownValueIsTheHand()
    {
        var state = new HudToolState();
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Treasury, null, false, ClassicHudAction.None), Of(state));
        Assert.Equal(PlayerToolMode.Hand, state.ActiveTool);
        state.StartWith(DungeonToolMode.BuildRoom, RoomTypeId.Lair);
        Assert.Equal(PlayerToolMode.BuildRoom, state.ActiveTool);
        state.StartWith(DungeonToolMode.SellRoom, RoomTypeId.Lair);
        Assert.Equal(PlayerToolMode.SellRoom, state.ActiveTool);
        state.StartWith(DungeonToolMode.CreateImp, RoomTypeId.Lair);
        Assert.Equal(PlayerToolMode.CreateImp, state.ActiveTool);
        // The start-up sanitiser: a serialised value that is none of the four is the hand; the room is taken as given.
        state.StartWith((DungeonToolMode)1, RoomTypeId.Portal);
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Portal, null, false, ClassicHudAction.None), Of(state));
    }

    [Fact]
    public void AnUnknownRoomFallsBackToTheLowestIdAndAKnownOrEmptyListKeepsIt()
    {
        var state = new HudToolState();
        state.SelectKnownRoom([RoomTypeId.Portal, RoomTypeId.Treasury]);
        Assert.Equal(RoomTypeId.Treasury, state.SelectedRoomId);
        state.SelectKnownRoom([RoomTypeId.Portal, RoomTypeId.Lair]);
        Assert.Equal(RoomTypeId.Lair, state.SelectedRoomId);
        state.SelectKnownRoom([]);
        Assert.Equal(RoomTypeId.Lair, state.SelectedRoomId);
    }

    [Fact]
    public void APanelChoiceCancelsTheDragAndTheSpecialClearsTheQueryAndTakesTheSelectionsTool()
    {
        HudToolState state = Querying();
        PlayerInputTranslator dragging = Dragging(), targeting = Targeting();
        CreatureQueryView query = Query();
        state.SelectPanelChoice(new BottomPanelSelection(Door, PlayerToolMode.Hand, RoomTypeId.Portal), dragging, query);
        // Every field but the room: the room changes only with the build tool.
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, Door, false, ClassicHudAction.None), Of(state));
        Assert.Null(dragging.DragFrom(PlayerToolMode.BuildRoom));
        Assert.Equal(CreatureQueryTarget.None, query.Target);
        state.SelectPanelChoice(new BottomPanelSelection(null, PlayerToolMode.CreateImp, RoomTypeId.Portal), targeting, null);
        Assert.Null(targeting.Targeting);
        Assert.Equal(new Fields(DungeonToolMode.CreateImp, RoomTypeId.Lair, null, false, ClassicHudAction.None), Of(state));
        state.SelectPanelChoice(new BottomPanelSelection(Lair, PlayerToolMode.BuildRoom, RoomTypeId.Portal), targeting, null);
        Assert.Equal(new Fields(DungeonToolMode.BuildRoom, RoomTypeId.Portal, Lair, false, ClassicHudAction.None), Of(state));
        // It takes no panel: the selection came from the panel, which needs no SelectTool.
    }

    [Fact]
    public void AStoredSpecialThatPostsChangesNothingAndOneThatTargetsResetsAllButTheRoomWithoutCancellingTheTool()
    {
        HudToolState state = Querying();
        BottomPanelView panel = Panel();
        panel.SelectTool(PlayerToolMode.SellRoom);
        PlayerInputTranslator input = Targeting();
        Fields before = Of(state);
        Assert.IsType<ActivateSpecialCommand>(state.ActivateStoredSpecial(Keeper, 7, 124, input, panel));
        Assert.Equal(before, Of(state));
        Assert.Equal(PlayerToolMode.SellRoom, panel.Selection.Tool);
        // No CancelTool on this path: the special already pending stays.
        Assert.Equal(new SpecialTargeting(8, SpecialTargeting.KillCreatures), input.Targeting);

        state = Building();
        Assert.Null(state.ActivateStoredSpecial(Keeper, 8, SpecialTargeting.KillCreatures, input, panel));
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, false, ClassicHudAction.None), Of(state));
        // The targeting the translator began stands: no CancelTool here. The panel is back on the hand.
        Assert.Equal(new SpecialTargeting(8, SpecialTargeting.KillCreatures), input.Targeting);
        Assert.Equal(PlayerToolMode.Hand, panel.Selection.Tool);
        state = Querying();
        Assert.Null(state.ActivateStoredSpecial(Keeper, 9, SpecialTargeting.TransferCreature, input, panel));
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, false, ClassicHudAction.None), Of(state));
    }

    [Fact]
    public void ACornerCancelsTheToolKeepsTheDragAndShowsItsToolOnThePanel()
    {
        HudToolState state = Building();
        PlayerInputTranslator dragging = Dragging(), targeting = Targeting();
        BottomPanelView panel = Panel();
        state.SelectCorner(ClassicHudAction.SelectSell, dragging, panel);
        Assert.Equal(new Fields(DungeonToolMode.SellRoom, RoomTypeId.Lair, null, false, ClassicHudAction.SelectSell), Of(state));
        Assert.NotNull(dragging.DragFrom(PlayerToolMode.BuildRoom));   // the router's to cancel
        Assert.Equal(new BottomPanelSelection(null, PlayerToolMode.SellRoom, RoomTypeId.Lair), panel.Selection);
        state.SelectCorner(ClassicHudAction.SelectQuery, targeting, panel);
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, true, ClassicHudAction.SelectQuery), Of(state));
        Assert.Null(targeting.Targeting);
        Assert.Equal(new BottomPanelSelection(null, PlayerToolMode.Hand, RoomTypeId.Lair), panel.Selection);
        // The query target is kept: SelectCorner takes no query view.
    }

    [Fact]
    public void AGoldWithdrawKeepsTheCornerAndPutsThePanelOnTheHand()
    {
        HudToolState state = Querying();
        BottomPanelView panel = Panel();
        panel.SelectTool(PlayerToolMode.SellRoom);
        // No translator is passed, so the pending special and the drag are kept by the signature.
        state.ResetAfterGoldWithdraw(panel);
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, false, ClassicHudAction.SelectQuery), Of(state));
        Assert.Equal(PlayerToolMode.Hand, panel.Selection.Tool);

        state = Building();
        state.ResetAfterGoldWithdraw(panel);
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, false, ClassicHudAction.None), Of(state));
    }

    [Fact]
    public void ARightClickUnderQueryClearsTheQueryTheQueryToolAndTheCorner()
    {
        HudToolState state = Querying();
        CreatureQueryView query = Query();
        // Only the query view is passed: the panel, the pending special and the drag are kept by the signature.
        state.CancelQueryByRightClick(query);
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, false, ClassicHudAction.None), Of(state));
        Assert.Equal(CreatureQueryTarget.None, query.Target);
    }

    [Fact]
    public void ARightClickCancelKeepsTheQueryToolAndPutsThePanelOnTheHand()
    {
        HudToolState state = Querying();
        BottomPanelView panel = Panel();
        panel.SelectTool(PlayerToolMode.SellRoom);
        // No translator is passed, so the pending special is kept by the signature.
        state.CancelToolByRightClick(panel);
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, true, ClassicHudAction.None), Of(state));
        Assert.Equal(PlayerToolMode.Hand, panel.Selection.Tool);

        state = Building();
        state.CancelToolByRightClick(panel);
        Assert.Equal(new Fields(DungeonToolMode.Hand, RoomTypeId.Lair, null, false, ClassicHudAction.None), Of(state));
    }

    [Fact]
    public void TheCageToolAndTheSpellArmFollowTheToolTheQueryAndTheChoice()
    {
        var state = new HudToolState();
        var input = new PlayerInputTranslator();
        Assert.Equal(HoverCageTool.Hand, state.CageTool);
        Assert.False(state.SpellArmed(input));
        state.SelectPanelChoice(new BottomPanelSelection(Door, PlayerToolMode.Hand, default), input, null);
        Assert.Equal(HoverCageTool.Door, state.CageTool);
        var trap = new BottomPanelChoiceKey(BottomPanelChoiceKind.Trap, 3);
        state.SelectPanelChoice(new BottomPanelSelection(trap, PlayerToolMode.Hand, default), input, null);
        Assert.Equal(HoverCageTool.Trap, state.CageTool);
        var spell = new BottomPanelChoiceKey(BottomPanelChoiceKind.Spell, 2);
        state.SelectPanelChoice(new BottomPanelSelection(spell, PlayerToolMode.Hand, default), input, null);
        Assert.Equal(HoverCageTool.SpellOrQuery, state.CageTool);
        Assert.True(state.SpellArmed(input));
        Assert.False(state.SpellArmed(Targeting()));
        state.SelectPanelChoice(new BottomPanelSelection(null, PlayerToolMode.CreateImp, default), input, null);
        Assert.Equal(HoverCageTool.SpellOrQuery, state.CageTool);
        Assert.True(state.SpellArmed(input));
        Assert.Equal(HoverCageTool.Room, Building().CageTool);
        HudToolState query = Querying();
        Assert.Equal(HoverCageTool.SpellOrQuery, query.CageTool);
        Assert.False(query.SpellArmed(input));
        query.SelectCorner(ClassicHudAction.SelectSell, input, Panel());
        Assert.Equal(HoverCageTool.Sell, query.CageTool);
    }

    [Fact]
    public void ACommandsCastSpellIsCreateImpsFourOrTheSpellsId()
    {
        Assert.Equal((byte)4, HudToolState.CastSpellId(new CreateImpCommand(Keeper)));
        Assert.Equal((byte)9, HudToolState.CastSpellId(new CastSpellAtPlaceCommand(Keeper, 9, 0, 0)));
        Assert.Equal((byte)11, HudToolState.CastSpellId(new CastSpellOnCreatureCommand(Keeper, 11, 5)));
        Assert.Null(HudToolState.CastSpellId(new RemoveCallToArmsCommand(Keeper)));
    }

    [Fact]
    public void OverTheHudThePlacementIsThePerToolIconOrNone()
    {
        var database = new GameDatabase([], [], [], new GlobalVariables());
        var input = new PlayerInputTranslator();
        BottomPanelView panel = Panel();
        PlacementFeedback Over(HudToolState state, PlayerInputTranslator translator)
        {
            return state.Placement(null!, database, null!, Keeper, null, default, 0, translator, panel);
        }

        var state = new HudToolState();
        Assert.Equal(PlacementFeedback.None, Over(state, input));
        Assert.Equal(PlacementCursor.Targeting(new SpecialTargeting(8, SpecialTargeting.KillCreatures)), Over(state, Targeting()));
        Assert.Equal(PlacementCursor.Query(), Over(Querying(), input));
        Assert.Equal(new PlacementFeedback(null, false, 0, PlacementIcon.PlaceRoom, true, true), Over(Building(), input));
        state.StartWith(DungeonToolMode.SellRoom, RoomTypeId.Treasury);
        Assert.Equal(new PlacementFeedback(null, false, 0, PlacementIcon.Sell, true, true), Over(state, input));
    }

    private sealed class NoNames : IClassicCreatureNames
    {
        public string Name(CreatureNameIndex index)
        {
            return "";
        }

        public string BloodGroup(CreatureBloodGroupIndex index)
        {
            return "";
        }
    }
}
