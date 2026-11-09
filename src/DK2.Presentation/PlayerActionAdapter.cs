// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;

namespace DK2.Presentation;

/// <summary>An admitted action's commands, or the public-safe reason it was not admitted.</summary>
public readonly record struct PlayerActionResolution
{
    private readonly IReadOnlyList<PlayerCommand>? _commands;

    private PlayerActionResolution(IReadOnlyList<PlayerCommand>? commands, PlayerRefusalReason refusal)
    {
        _commands = commands;
        Refusal = refusal;
    }

    public IReadOnlyList<PlayerCommand> Commands => _commands ?? [];
    public PlayerRefusalReason Refusal { get; }
    public bool Admitted => Refusal == PlayerRefusalReason.None;

    public static PlayerActionResolution Of(PlayerCommand command) => new([command], PlayerRefusalReason.None);

    public static PlayerActionResolution Refused(PlayerRefusalReason reason) =>
        reason == PlayerRefusalReason.None ? throw new ArgumentException("A refusal needs a reason.", nameof(reason)) : new(null, reason);
}

/// <summary>
/// Turns an admitted <see cref="PlayerAction"/> into the commands the human input path builds for
/// the same gesture (<see cref="PlayerInputTranslator"/>): a tag drag's rectangle, a room drag clamped
/// to the rubber band, a hand click on a target, a drop naming the creature under it, a panel press.
/// It reads only the capture the action was based on, never a world, so every refusal is decided from
/// what the player could see. With no pointer, positions are tile centres (the translator's fallback),
/// so a Create Imp is not hash-identical to a mouse click. The engine stays authoritative.
/// </summary>
public sealed class PlayerActionAdapter
{
    private readonly PlayerId _player;
    private readonly int _rubberBand;

    public PlayerActionAdapter(GameDatabase database, PlayerId player)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        _player = player;
        _rubberBand = PlacementCursor.RubberBandLimit(database);
    }

    public PlayerId Player => _player;

    public PlayerActionResolution Resolve(PlayerObservationCapture capture, PlayerAction action)
    {
        if (capture is null) throw new ArgumentNullException(nameof(capture));
        if (action is null) throw new ArgumentNullException(nameof(action));
        PlayerObservation observation = capture.Observation;
        if (observation.Stamp.Player != _player) throw new ArgumentException("The capture is another player's.", nameof(capture));

        if (observation.Phase == PlayerPhase.Ended) return PlayerActionResolution.Refused(PlayerRefusalReason.Terminal);
        bool intro = observation.Phase == PlayerPhase.Intro;
        if (intro != (action is PlayerAction.SkipIntro)) return PlayerActionResolution.Refused(PlayerRefusalReason.WrongPhase);
        if ((observation.Offers.Actions & action.Kind) == 0) return PlayerActionResolution.Refused(PlayerRefusalReason.NotOffered);

        PlayerTileGrid tiles = observation.Tiles;
        switch (action)
        {
            case PlayerAction.SkipIntro:
                return PlayerActionResolution.Of(new SkipCameraPathCommand(_player));
            case PlayerAction.TagArea tag:
                if (!tiles.Contains(tag.From) || !tiles.Contains(tag.To)) return OutOfBounds;
                return PlayerActionResolution.Of(TagTilesCommand.Rect(_player, tag.From.X, tag.From.Y, tag.To.X, tag.To.Y, !tag.Untag));
            case PlayerAction.BuildRoom build:
                if (!tiles.Contains(build.From) || !tiles.Contains(build.To)) return OutOfBounds;
                if (!Offered(observation.Offers.Rooms, build.Room)) return NotOffered;
                // The release pulls the far corner toward the anchor, as the dragged build does.
                TilePos end = PlacementCursor.ClampDrag(build.From, build.To, _rubberBand);
                return PlayerActionResolution.Of(BuildRoomCommand.Rect(_player, build.Room, build.From.X, build.From.Y, end.X, end.Y));
            case PlayerAction.SelectPanel panel:
                return Contains(observation.Offers.Panels, panel.Button)
                    ? PlayerActionResolution.Of(new GuiButtonCommand(_player, panel.Button)) : NotOffered;
            case PlayerAction.DropAt drop:
                if (!tiles.Contains(drop.Tile)) return OutOfBounds;
                // A drop onto a creature feeds or pays it (HandSystem.DropObject reads the id).
                int onto = capture.TryFindCreatureAt(drop.Tile, out PlayerInputTarget under) ? under.Id : 0;
                return PlayerActionResolution.Of(new DropCommand(_player, drop.Tile, onto));
            case PlayerAction.CreateImpAt imp:
                if (!tiles.Contains(imp.Tile)) return OutOfBounds;
                // A pointer with no ground point casts at the tile centre (PlayerInputTranslator).
                return PlayerActionResolution.Of(new CreateImpCommand(_player, new FpPosition2(
                    new Fp32(CreatureState.CentreOf(imp.Tile.X)), new Fp32(CreatureState.CentreOf(imp.Tile.Y)))));
            case PlayerAction.PickUp pick:
                return OnThing(capture, pick.Thing, PlayerThingActions.PickUp, target => target.Kind switch
                {
                    PlayerInputTargetKind.Creature => new PickUpCreatureCommand(_player, target.Id),
                    PlayerInputTargetKind.Object => new PickUpObjectCommand(_player, target.Id),
                    PlayerInputTargetKind.StoreGold => new PickUpStoreGoldCommand(_player, target.Origin),
                    _ => null,
                });
            case PlayerAction.Slap slap:
                return OnThing(capture, slap.Thing, PlayerThingActions.Slap, target =>
                    target.Kind == PlayerInputTargetKind.Creature ? new SlapCreatureCommand(_player, target.Id) : null);
            case PlayerAction.ActivateSpecial special:
                return OnThing(capture, special.Special, PlayerThingActions.Activate, target =>
                    target.Kind == PlayerInputTargetKind.Special ? new ActivateSpecialCommand(_player, target.Id) : null);
            default:
                throw new InvalidOperationException($"No resolution for {action.GetType().Name}.");
        }
    }

    private static readonly PlayerActionResolution OutOfBounds = PlayerActionResolution.Refused(PlayerRefusalReason.OutOfBounds),
        NotOffered = PlayerActionResolution.Refused(PlayerRefusalReason.NotOffered);

    private static PlayerActionResolution OnThing(PlayerObservationCapture capture, PlayerThingHandle handle,
        PlayerThingActions needed, Func<PlayerInputTarget, PlayerCommand?> command)
    {
        if (!capture.TryResolve(handle, out PlayerInputTarget target))
            return PlayerActionResolution.Refused(PlayerRefusalReason.UnknownHandle);
        if ((capture.Observation.Things[handle.Ordinal].Actions & needed) == 0) return NotOffered;
        return command(target) is PlayerCommand built ? PlayerActionResolution.Of(built) : NotOffered;
    }

    private static bool Offered(IReadOnlyList<PlayerRoomOffer> rooms, RoomTypeId room)
    {
        for (int i = 0; i < rooms.Count; i++) if (rooms[i].Room == room) return true;
        return false;
    }

    private static bool Contains(IReadOnlyList<GuiButtonTarget> panels, GuiButtonTarget button)
    {
        for (int i = 0; i < panels.Count; i++) if (panels[i] == button) return true;
        return false;
    }
}
