// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>What <see cref="WorldClickCommands"/> asks of the adapter: the picking, which is camera-bound, and the camera's
/// turn after a query. Each is asked only on the path that needs it, as Unity's runner asked.</summary>
public interface IWorldClickHost
{
    /// <summary>Whether the pointer is over the HUD (<see cref="HudPointerMap"/>).</summary>
    bool PointerOverHud();
    /// <summary>The tile under the pointer, null over the HUD or off the map; null always for a host without picking.</summary>
    TilePos? PointerTile();
    /// <summary>What the pointer hovers on <paramref name="tile"/>, a tile the host's <see cref="PointerTile"/> gave.</summary>
    WorldHoverHit Hover(TilePos tile);
    /// <summary>A click queried a creature: the camera's glide stops and it turns to follow it
    /// (<see cref="ClassicCreatureQueryRules.FollowHeading"/>).</summary>
    void FollowQueriedCreature();
}

/// <summary>Unity's DungeonRunner.QueuePanelWorldAction, moved out of the runner (migration plan step 2c): what a press in
/// the world does while the query tool or a bottom panel choice is held, before the hand's own press is translated.
/// <list type="bullet">
/// <item>The query tool takes the frame: a left press on a creature with nothing queried queries it, and a right press
/// cancels the tool (<see cref="HudToolState.CancelQueryByRightClick"/>).</item>
/// <item>A right press with another tool or a choice held drops the world drag, or with none cancels the tool
/// (<see cref="HudToolState.CancelToolByRightClick"/>).</item>
/// <item>A door, trap or spell choice (not a room, not Create Imp's spell 4) takes the frame: a left press on a tile places
/// the door or trap (none without a Workshop, 0x0040849F, 0x00408586) or casts the spell on the hovered creature when it
/// targets a thing, else at the tile's centre.</item>
/// </list></summary>
public sealed class WorldClickCommands
{
    private readonly HudToolState _tools;
    private readonly PlayerInputTranslator _input;
    private readonly CreatureQueryView _query;
    private readonly BottomPanelView _panel;
    private readonly PlayerId _viewer;
    private readonly IWorldClickHost _host;

    public WorldClickCommands(HudToolState tools, PlayerInputTranslator input, CreatureQueryView query, BottomPanelView panel,
        PlayerId viewer, IWorldClickHost host)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _viewer = viewer;
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Applies this frame's <paramref name="press"/>; true when the frame is taken, so the hand's press is not
    /// translated. Commands are added to <paramref name="output"/>.</summary>
    public bool Queue(in HudPress press, GameDatabase database, List<PlayerCommand> output)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (_tools.QueryTool)
        {
            if (_query.Target == CreatureQueryTarget.None && press.Primary && !_host.PointerOverHud()
                && _host.PointerTile() is TilePos queryTile)
            {
                WorldHoverHit hit = _host.Hover(queryTile);
                if (hit.Kind == WorldHoverKind.Creature)
                {
                    _query.Query(new CreatureQueryTarget(hit.EntityId));
                    _host.FollowQueriedCreature();
                }
            }
            if (press.Secondary) _tools.CancelQueryByRightClick(_query);
            return true;
        }
        if (press.Secondary && (_tools.Tool != DungeonToolMode.Hand || _tools.PanelChoice != null))
        {
            if (_input.DragFrom(_tools.ActiveTool) != null) _input.CancelPointerGesture();
            else _tools.CancelToolByRightClick(_panel);
            return true;
        }
        if (_tools.PanelChoice is not BottomPanelChoiceKey choice || choice.Kind == BottomPanelChoiceKind.Room
            || choice.Kind == BottomPanelChoiceKind.Spell && choice.Value == 4) return false;
        if (!press.Primary || _host.PointerOverHud()) return true;
        if (_host.PointerTile() is not TilePos tile) return true;
        // Without a Workshop DK2's hand places no door or trap (0x0040849F, 0x00408586).
        if (_panel.Choice(choice)?.NeedsWorkshop == true) return true;
        if (choice.Kind == BottomPanelChoiceKind.Door) output.Add(new PlaceDoorCommand(_viewer, tile, choice.Value));
        else if (choice.Kind == BottomPanelChoiceKind.Trap) output.Add(new PlaceTrapCommand(_viewer, tile, choice.Value));
        else
        {
            KeeperSpellDef? spell = database.KeeperSpellById(choice.Value);
            if (spell == null) return true;
            if (KeeperSpellRules.TargetsAThing(spell.TargetSelector))
            {
                WorldHoverHit hit = _host.Hover(tile);
                if (hit.Kind == WorldHoverKind.Creature)
                    output.Add(new CastSpellOnCreatureCommand(_viewer, choice.Value, hit.EntityId));
            }
            else output.Add(new CastSpellAtPlaceCommand(_viewer, choice.Value,
                CreatureState.CentreOf(tile.X), CreatureState.CentreOf(tile.Y)));
        }
        return true;
    }
}
