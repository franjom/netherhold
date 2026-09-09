// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
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
    byte PlayerId,
    PlayerToolMode Tool,
    TilePos? PointerTile,
    PointerButtonTransition PrimaryButton,
    byte RoomId = 0);

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

    public PlayerInputSnapshot(bool isHolding, IReadOnlyList<PlayerInputTarget>? targets = null)
    {
        IsHolding = isHolding;
        Targets = targets ?? NoTargets;
    }

    public bool IsHolding { get; }
    public IReadOnlyList<PlayerInputTarget> Targets { get; }

    /// <summary>
    /// Captures only entities visible through <paramref name="view"/>. Held and
    /// worker-carried things are outside the dungeon and cannot be targeted.
    /// </summary>
    public static PlayerInputSnapshot Capture(WorldView view, WorldState world, byte playerId)
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
            targets.Add(new PlayerInputTarget(
                PlayerInputTargetKind.Object, instance.Id, instance.Tile));
        }

        return new PlayerInputSnapshot(isHolding, targets);
    }
}

/// <summary>
/// Turns pointer gestures into the command vocabulary accepted by the simulation.
/// It retains only an unfinished drag; all world-facing choices are supplied in a snapshot.
/// </summary>
public sealed class PlayerInputTranslator
{
    private DragStart? _drag;

    public InputFrame Translate(in PlayerIntent intent, PlayerInputSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        PlayerCommand? command = null;

        if ((intent.PrimaryButton & PointerButtonTransition.Pressed) != 0)
        {
            if (IsDragTool(intent.Tool) && intent.PointerTile is TilePos start)
            {
                _drag = new DragStart(intent.PlayerId, intent.Tool, start, intent.RoomId);
            }
            else
            {
                _drag = null;
                command = Click(intent, snapshot);
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

    private static bool IsDragTool(PlayerToolMode tool) =>
        tool is PlayerToolMode.TagTiles or PlayerToolMode.UntagTiles or
            PlayerToolMode.BuildRoom or PlayerToolMode.SellRoom;

    private static PlayerCommand? Drag(in DragStart drag, TilePos end) => drag.Tool switch
    {
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
            if (snapshot.IsHolding) return new DropCommand(intent.PlayerId, tile);

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
        byte PlayerId,
        PlayerToolMode Tool,
        TilePos Tile,
        byte RoomId);
}
