// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>The tool selected by a local player. These are interface choices, not DK2 behaviour.</summary>
public enum PlayerToolMode : byte
{
    TagTiles,
    UntagTiles,
    BuildRoom,
    SellRoom,
    Hand,
    Slap,
    CreateImp,
}

/// <summary>Edges observed for the primary pointer button during one rendered frame.</summary>
[Flags]
public enum PointerButtonTransition : byte
{
    None = 0,
    Pressed = 1,
    Released = 2,
}

/// <summary>
/// One renderer-independent sample of local input. A host converts its pointer
/// coordinates to a tile and records button edges; no camera or engine type crosses here.
/// </summary>
public readonly record struct PlayerIntent(
    PlayerId PlayerId,
    PlayerToolMode Tool,
    TilePos? PointerTile,
    PointerButtonTransition PrimaryButton,
    RoomTypeId RoomId = 0,
    PointerButtonTransition SecondaryButton = PointerButtonTransition.None,
    bool PointerOverHud = false);

public readonly record struct ExcavationTarget(TilePos Tile, bool IsTagged);

public enum PlayerInputTargetKind : byte
{
    Creature,
    Object,
}

/// <summary>An entity the player's current view permits the pointer to target.</summary>
public readonly record struct PlayerInputTarget(
    PlayerInputTargetKind Kind,
    int Id,
    TilePos Tile);

/// <summary>The small, immutable part of the presented world needed to interpret a click.</summary>
public sealed class PlayerInputSnapshot
{
    private static readonly PlayerInputTarget[] NoTargets = [];

    public PlayerInputSnapshot(bool isHolding, IReadOnlyList<PlayerInputTarget>? targets = null,
        ExcavationTarget? excavation = null)
    {
        IsHolding = isHolding;
        Targets = targets ?? NoTargets;
        Excavation = excavation;
    }

    public bool IsHolding { get; }
    public IReadOnlyList<PlayerInputTarget> Targets { get; }
    public ExcavationTarget? Excavation { get; }

    public PlayerInputSnapshot ForHover(WorldHoverHit hit)
    {
        PlayerInputTargetKind? kind = hit.Kind switch
        {
            WorldHoverKind.Creature => PlayerInputTargetKind.Creature,
            WorldHoverKind.Object => PlayerInputTargetKind.Object,
            _ => null,
        };
        foreach (PlayerInputTarget target in Targets)
            if (target.Kind == kind && target.Id == hit.EntityId)
                return new PlayerInputSnapshot(IsHolding,
                    [new PlayerInputTarget(target.Kind, target.Id, hit.Tile)], Excavation);
        return new PlayerInputSnapshot(IsHolding, NoTargets, Excavation);
    }

    /// <summary>
    /// Captures only entities visible through <paramref name="view"/>. Held and
    /// worker-carried things are outside the dungeon and cannot be targeted, and with a
    /// <paramref name="database"/> neither is gold off the keeper's own land
    /// (<see cref="HandRules.MayReachObject"/>).
    /// </summary>
    public static PlayerInputSnapshot Capture(
        WorldView view, WorldState world, PlayerId playerId, GameDatabase? database = null,
        TilePos? pointerTile = null)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (world is null) throw new ArgumentNullException(nameof(world));

        bool isHolding = world.HasPlayer(playerId) && world.Player(playerId).Hand.Count > 0;
        var targets = new List<PlayerInputTarget>();

        foreach (CreatureView creature in view.Creatures(world))
        {
            if (world.CreatureById(creature.Id)?.InHand != false) continue;
            targets.Add(new PlayerInputTarget(
                PlayerInputTargetKind.Creature, creature.Id, creature.Tile));
        }

        foreach (ObjectView instance in view.Objects(world))
        {
            ObjectState? thing = world.ObjectById(instance.Id);
            if (thing is null || thing.InHand || thing.CarriedByCreatureId >= 0) continue;
            if (database is not null && !HandRules.MayReachObject(world, database, thing, playerId)) continue;
            targets.Add(new PlayerInputTarget(
                PlayerInputTargetKind.Object, instance.Id, instance.Tile));
        }

        ExcavationTarget? excavation = null;
        if (pointerTile is TilePos at && world.Contains(at) && database is not null)
        {
            ref TileState tile = ref world.Tile(at);
            bool tagged = tile.IsTaggedBy(playerId);
            if (tagged || (world.FogOfWarEnabled && !tile.IsDiscoveredBy(playerId))
                || database.TerrainById(tile.TerrainId).IsTaggable)
                excavation = new ExcavationTarget(at, tagged);
        }
        return new PlayerInputSnapshot(isHolding, targets, excavation);
    }
}

/// <summary>
/// Turns pointer gestures into the command vocabulary accepted by the simulation.
/// It retains only an unfinished drag; all world-facing choices are supplied in a snapshot.
/// </summary>
public sealed class PlayerInputTranslator
{
    private DragStart? _drag;
    public void CancelPointerGesture() => _drag = null;

    public void SelectTool(PlayerId playerId, PlayerToolMode tool)
    {
        if (_drag is DragStart drag && (drag.PlayerId != playerId || drag.Tool != tool)) _drag = null;
    }

    public InputFrame Translate(in PlayerIntent intent, PlayerInputSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        SelectTool(intent.PlayerId, intent.Tool);
        if (intent.PointerOverHud)
        {
            _drag = null;
            return InputFrame.Empty;
        }

        PlayerCommand? command = null;

        if ((intent.SecondaryButton & PointerButtonTransition.Pressed) != 0)
        {
            _drag = null;
            command = SecondaryClick(intent, snapshot);
            return command is null ? InputFrame.Empty : InputFrame.Of(command);
        }

        if ((intent.PrimaryButton & PointerButtonTransition.Pressed) != 0)
        {
            if (IsDragTool(intent.Tool) && intent.PointerTile is TilePos start)
            {
                _drag = new DragStart(intent.PlayerId, intent.Tool, start, intent.RoomId,
                    intent.Tool != PlayerToolMode.UntagTiles);
            }
            else
            {
                _drag = null;
                command = Click(intent, snapshot);
                if (command is null && intent.Tool == PlayerToolMode.Hand
                    && snapshot.Excavation is ExcavationTarget excavation
                    && intent.PointerTile == excavation.Tile)
                    _drag = new DragStart(intent.PlayerId, intent.Tool, excavation.Tile,
                        intent.RoomId, !excavation.IsTagged);
            }
        }

        if ((intent.PrimaryButton & PointerButtonTransition.Released) != 0)
        {
            if (_drag is DragStart drag &&
                drag.PlayerId == intent.PlayerId &&
                drag.Tool == intent.Tool &&
                intent.PointerTile is TilePos end)
            {
                command = Drag(drag, end);
            }

            _drag = null;
        }

        return command is null ? InputFrame.Empty : InputFrame.Of(command);
    }

    /// <summary>Where an unfinished drag with <paramref name="tool"/> began; null with none.</summary>
    public TilePos? DragFrom(PlayerToolMode tool) => _drag is DragStart drag && drag.Tool == tool ? drag.Tile : null;

    public bool? ExcavationPreviewTagged => _drag is DragStart drag
        && drag.Tool is PlayerToolMode.Hand or PlayerToolMode.TagTiles or PlayerToolMode.UntagTiles
            ? drag.Tagged : null;

    private static bool IsDragTool(PlayerToolMode tool) =>
        tool is PlayerToolMode.TagTiles or PlayerToolMode.UntagTiles or
            PlayerToolMode.BuildRoom or PlayerToolMode.SellRoom;

    private static PlayerCommand? Drag(in DragStart drag, TilePos end) => drag.Tool switch
    {
        PlayerToolMode.Hand =>
            TagTilesCommand.Rect(drag.PlayerId, drag.Tile.X, drag.Tile.Y, end.X, end.Y, drag.Tagged),
        PlayerToolMode.TagTiles =>
            TagTilesCommand.Rect(drag.PlayerId, drag.Tile.X, drag.Tile.Y, end.X, end.Y),
        PlayerToolMode.UntagTiles =>
            TagTilesCommand.Rect(drag.PlayerId, drag.Tile.X, drag.Tile.Y, end.X, end.Y, tagged: false),
        PlayerToolMode.BuildRoom =>
            BuildRoomCommand.Rect(drag.PlayerId, drag.RoomId, drag.Tile.X, drag.Tile.Y, end.X, end.Y),
        PlayerToolMode.SellRoom =>
            SellRoomCommand.Rect(drag.PlayerId, drag.Tile.X, drag.Tile.Y, end.X, end.Y),
        _ => null,
    };

    private static PlayerCommand? Click(in PlayerIntent intent, PlayerInputSnapshot snapshot)
    {
        if (intent.Tool == PlayerToolMode.CreateImp)
            return new CreateImpCommand(intent.PlayerId);

        if (intent.PointerTile is not TilePos tile) return null;

        if (intent.Tool == PlayerToolMode.Hand)
        {
            PlayerInputTarget? creature = TargetAt(
                snapshot.Targets, tile, PlayerInputTargetKind.Creature);
            if (creature is PlayerInputTarget c)
                return new PickUpCreatureCommand(intent.PlayerId, c.Id);

            PlayerInputTarget? instance = TargetAt(
                snapshot.Targets, tile, PlayerInputTargetKind.Object);
            return instance is PlayerInputTarget o
                ? new PickUpObjectCommand(intent.PlayerId, o.Id)
                : null;
        }

        if (intent.Tool == PlayerToolMode.Slap)
        {
            PlayerInputTarget? creature = TargetAt(
                snapshot.Targets, tile, PlayerInputTargetKind.Creature);
            return creature is PlayerInputTarget c
                ? new SlapCreatureCommand(intent.PlayerId, c.Id)
                : null;
        }

        return null;
    }

    private static PlayerCommand? SecondaryClick(in PlayerIntent intent, PlayerInputSnapshot snapshot)
    {
        if (intent.Tool != PlayerToolMode.Hand || intent.PointerTile is not TilePos tile) return null;
        PlayerInputTarget? creature = TargetAt(snapshot.Targets, tile, PlayerInputTargetKind.Creature);
        if (snapshot.IsHolding)
            return new DropCommand(intent.PlayerId, tile, creature?.Id ?? 0);
        return creature is PlayerInputTarget c ? new SlapCreatureCommand(intent.PlayerId, c.Id) : null;
    }

    private static PlayerInputTarget? TargetAt(
        IReadOnlyList<PlayerInputTarget> targets,
        TilePos tile,
        PlayerInputTargetKind kind)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            PlayerInputTarget target = targets[i];
            if (target.Kind == kind && target.Tile == tile) return target;
        }

        return null;
    }

    private readonly record struct DragStart(
        PlayerId PlayerId,
        PlayerToolMode Tool,
        TilePos Tile,
        RoomTypeId RoomId,
        bool Tagged);
}
