// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>The tool an adapter holds between frames. Its values are Unity's Inspector field's, serialised in
/// <c>Dungeon.unity</c>, so they must not change; <see cref="HudToolState.ActiveTool"/> maps it to the translator's tool.</summary>
public enum DungeonToolMode : byte
{
    Hand = 0,
    BuildRoom = 2,
    SellRoom = 3,
    CreateImp = 6,
}

/// <summary>The HUD's tool state: the tool, the bottom panel's
/// choice, the query tool, the selected minimap corner and the room the build tool places. Each reset is its own
/// transition and resets the relevant state. The six differ, and the differences are an open point
/// (docs/plans/2026-11-16-godot-step2-audit.md, "Tool resets"), not settled here.</summary>
public sealed class HudToolState
{
    public DungeonToolMode Tool { get; private set; } = DungeonToolMode.Hand;
    public RoomTypeId SelectedRoomId { get; private set; } = RoomTypeId.Treasury;
    /// <summary>The bottom panel's chosen door, trap, spell or room, or null.</summary>
    public BottomPanelChoiceKey? PanelChoice { get; private set; }
    /// <summary>Query (DK2's mode 10), which runs under the hand tool.</summary>
    public bool QueryTool { get; private set; }
    /// <summary>The minimap corner drawn selected: <see cref="ClassicHudAction.SelectQuery"/>, <see cref="ClassicHudAction.SelectSell"/> or None.</summary>
    public ClassicHudAction SelectedCorner { get; private set; }

    /// <summary>The translator's tool for <see cref="Tool"/>; any other value is the hand.</summary>
    public PlayerToolMode ActiveTool
    {
        get
        {
            switch (Tool)
            {
                case DungeonToolMode.BuildRoom: return PlayerToolMode.BuildRoom;
                case DungeonToolMode.SellRoom: return PlayerToolMode.SellRoom;
                case DungeonToolMode.CreateImp: return PlayerToolMode.CreateImp;
                default: return PlayerToolMode.Hand;
            }
        }
    }

    /// <summary>A session starts with the configured tool, the hand unless it is one of the four,
    /// and the configured room. The other fields are left as they are.</summary>
    public void StartWith(DungeonToolMode tool, RoomTypeId room)
    {
        Tool = tool is DungeonToolMode.Hand or DungeonToolMode.BuildRoom or DungeonToolMode.SellRoom or DungeonToolMode.CreateImp
            ? tool : DungeonToolMode.Hand;
        SelectedRoomId = room;
    }

    /// <summary>Keeps <see cref="SelectedRoomId"/> if <paramref name="rooms"/> holds it, else takes the lowest id; an empty
    /// list changes nothing.</summary>
    public void SelectKnownRoom(IEnumerable<RoomTypeId> rooms)
    {
        if (rooms is null)
        {
            throw new ArgumentNullException(nameof(rooms));
        }

        bool any = false;
        RoomTypeId lowest = default;
        foreach (RoomTypeId room in rooms)
        {
            if (room == SelectedRoomId)
            {
                return;
            }

            if (!any || (byte)room < (byte)lowest)
            {
                lowest = room;
            }

            any = true;
        }
        if (any)
        {
            SelectedRoomId = lowest;
        }
    }

    /// <summary>A bottom panel choice (BottomPanelHud.Selected): the world drag and any pending special are dropped, the
    /// query is cleared and the corner deselected, and the selection's tool is taken (its room with the build tool).</summary>
    public void SelectPanelChoice(in BottomPanelSelection selection, PlayerInputTranslator input, CreatureQueryView? query)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        input.CancelPointerGesture();
        input.CancelTool();
        PanelChoice = selection.Choice;
        query?.Clear();
        QueryTool = false; SelectedCorner = ClassicHudAction.None;
        Tool = selection.Tool == PlayerToolMode.BuildRoom ? DungeonToolMode.BuildRoom
            : selection.Tool == PlayerToolMode.CreateImp ? DungeonToolMode.CreateImp : DungeonToolMode.Hand;
        if (selection.Tool == PlayerToolMode.BuildRoom)
        {
            SelectedRoomId = selection.Room;
        }
    }

    /// <summary>A stored special's slot (<see cref="PlayerInputTranslator.ChooseStoredSpecial(PlayerId, int, byte, BottomPanelView)"/>):
    /// action 88 is returned for the host to post and nothing here changes; or targeting began, and the choice, the query
    /// tool and the corner are dropped and the tool returns to the hand. No <see cref="PlayerInputTranslator.CancelTool"/>.</summary>
    public PlayerCommand? ActivateStoredSpecial(PlayerId viewer, int objectId, byte typeId, PlayerInputTranslator input, BottomPanelView panel)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (input.ChooseStoredSpecial(viewer, objectId, typeId, panel) is PlayerCommand command)
        {
            return command;
        }

        PanelChoice = null;
        QueryTool = false; SelectedCorner = ClassicHudAction.None;
        Tool = DungeonToolMode.Hand;
        return null;
    }

    /// <inheritdoc cref="ActivateStoredSpecial(PlayerId, int, byte, PlayerInputTranslator, BottomPanelView)"/>
    public PlayerCommand? ActivateStoredSpecial(PlayerId viewer, BottomPanelChoice special, PlayerInputTranslator input, BottomPanelView panel)
    {
        if (special is null)
        {
            throw new ArgumentNullException(nameof(special));
        }

        return ActivateStoredSpecial(viewer, special.ObjectId, special.Key.Value, input, panel);
    }

    /// <summary>The minimap's query or sell corner (the host posts its GUI button first): the choice is dropped, the corner
    /// selected, a pending special cancelled, and the hand (with query) or the sell tool taken, which the panel shows.
    /// The world drag is the router's to cancel, and the query target is kept.</summary>
    public void SelectCorner(ClassicHudAction corner, PlayerInputTranslator input, BottomPanelView panel)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (panel is null)
        {
            throw new ArgumentNullException(nameof(panel));
        }

        PanelChoice = null; SelectedCorner = corner;
        input.CancelTool();
        QueryTool = corner == ClassicHudAction.SelectQuery;
        Tool = QueryTool ? DungeonToolMode.Hand : DungeonToolMode.SellRoom;
        panel.SelectTool(ActiveTool, SelectedRoomId);
    }

    /// <summary>After a left click on the gold withdraws gold (the host posts it first): the choice and the query tool are
    /// dropped and the hand taken, which the panel shows. The corner stays selected, and a pending special stays.</summary>
    public void ResetAfterGoldWithdraw(BottomPanelView panel)
    {
        if (panel is null)
        {
            throw new ArgumentNullException(nameof(panel));
        }

        PanelChoice = null; QueryTool = false; Tool = DungeonToolMode.Hand;
        panel.SelectTool(PlayerToolMode.Hand);
    }

    /// <summary>A right click while the query tool is on: the query target is cleared, the query tool and the corner
    /// dropped and the hand taken. The choice, the panel and a pending special are not touched.</summary>
    public void CancelQueryByRightClick(CreatureQueryView query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        query.Clear(); QueryTool = false;
        SelectedCorner = ClassicHudAction.None; Tool = DungeonToolMode.Hand;
    }

    /// <summary>A right click that cancels a tool or a choice with no drag in progress: the choice and the corner are
    /// dropped and the hand taken, which the panel shows. The query tool and a pending special are kept.</summary>
    public void CancelToolByRightClick(BottomPanelView panel)
    {
        if (panel is null)
        {
            throw new ArgumentNullException(nameof(panel));
        }

        PanelChoice = null; Tool = DungeonToolMode.Hand; SelectedCorner = ClassicHudAction.None;
        panel.SelectTool(PlayerToolMode.Hand);
    }

    /// <summary>The keeper spell a posted command casts, for the cast cursor: Create Imp's 4, a spell's id, else null.</summary>
    public static byte? CastSpellId(PlayerCommand command)
    {
        return command switch
        {
            CreateImpCommand => 4,
            CastSpellAtPlaceCommand place => place.SpellId,
            CastSpellOnCreatureCommand creature => creature.SpellId,
            _ => null,
        };
    }

    /// <summary>A keeper spell is the selected tool, DK2's mode 8: Create Imp's own tool or a spell
    /// chosen from the panel.</summary>
    public bool SpellArmed(PlayerInputTranslator input)
    {
        return ActiveTool == PlayerToolMode.CreateImp
        || ActiveTool == PlayerToolMode.Hand && !QueryTool && input.Targeting is null
            && PanelChoice is BottomPanelChoiceKey { Kind: BottomPanelChoiceKind.Spell };
    }

    /// <summary>The active tool as 0x004096B0 sees it; null for tools DK2's cursor has no mode for.</summary>
    public HoverCageTool? CageTool
    {
        get
        {
            switch (ActiveTool)
            {
                case PlayerToolMode.BuildRoom: return HoverCageTool.Room;
                case PlayerToolMode.SellRoom: return HoverCageTool.Sell;
                // Create Imp is a keeper spell, DK2's mode 8.
                case PlayerToolMode.CreateImp: return HoverCageTool.SpellOrQuery;
                case PlayerToolMode.Hand:
                    if (QueryTool)
                    {
                        return HoverCageTool.SpellOrQuery;
                    }

                    if (PanelChoice is not BottomPanelChoiceKey choice)
                    {
                        return HoverCageTool.Hand;
                    }

                    return choice.Kind == BottomPanelChoiceKind.Door ? HoverCageTool.Door
                        : choice.Kind == BottomPanelChoiceKind.Trap ? HoverCageTool.Trap
                        : choice.Kind == BottomPanelChoiceKind.Spell ? HoverCageTool.SpellOrQuery : HoverCageTool.Hand;
                default: return null;
            }
        }
    }

    /// <summary>The placement cursor of the selected tool over <paramref name="tile"/> (null: over the HUD), whose hover
    /// target is <paramref name="hit"/>; <paramref name="gold"/> is the resource strip's, <paramref name="navigation"/>
    /// the simulation's navigation bytes as its last turn left them (the cursor never refreshes them).</summary>
    public PlacementFeedback Placement(WorldState world, GameDatabase database, INavByteGrid navigation, PlayerId viewer,
        TilePos? tile, WorldHoverHit hit, int gold, PlayerInputTranslator input, BottomPanelView panel)
    {
        if (database is null)
        {
            throw new ArgumentNullException(nameof(database));
        }

        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        PlayerToolMode active = ActiveTool;
        if (active == PlayerToolMode.BuildRoom)
        {
            if (tile is not TilePos at || !database.Rooms.TryGetValue(SelectedRoomId, out RoomDef? room))
            {
                return tile is null ? new PlacementFeedback(null, false, 0, PlacementIcon.PlaceRoom, true, true) : PlacementFeedback.None;
            }

            return PlacementCursor.Room(world, database, viewer, room, at, input.DragFrom(PlayerToolMode.BuildRoom), gold);
        }
        if (active == PlayerToolMode.SellRoom)
        {
            if (tile is not TilePos at)
            {
                return new PlacementFeedback(null, false, 0, PlacementIcon.Sell, true, true);
            }

            return PlacementCursor.Sell(world, database, viewer, at, input.DragFrom(PlayerToolMode.SellRoom));
        }
        if (active == PlayerToolMode.CreateImp)
        {
            return PlacementCursor.CreateImp(world, database, navigation, viewer, 4, tile);
        }

        if (active != PlayerToolMode.Hand)
        {
            return PlacementFeedback.None;
        }

        if (input.Targeting is SpecialTargeting targeting)
        {
            return PlacementCursor.Targeting(targeting);
        }

        if (QueryTool)
        {
            return PlacementCursor.Query();
        }

        if (PanelChoice is not BottomPanelChoiceKey choice)
        {
            return PlacementFeedback.None;
        }

        if (choice.Kind == BottomPanelChoiceKind.Spell)
        {
            return PlacementCursor.Spell(world, database, navigation, viewer, choice.Value, tile, hit);
        }

        if (panel is null)
        {
            throw new ArgumentNullException(nameof(panel));
        }

        bool workshop = panel.Choice(choice)?.NeedsWorkshop != true;
        if (choice.Kind == BottomPanelChoiceKind.Door && database.Doors.TryGetValue(choice.Value, out DoorDef? door))
        {
            return PlacementCursor.Door(world, database, viewer, door, tile, workshop, gold);
        }

        if (choice.Kind == BottomPanelChoiceKind.Trap && database.Traps.TryGetValue(choice.Value, out TrapDef? trap))
        {
            return PlacementCursor.Trap(world, database, viewer, trap, tile, workshop, gold);
        }

        return PlacementFeedback.None;
    }
}
