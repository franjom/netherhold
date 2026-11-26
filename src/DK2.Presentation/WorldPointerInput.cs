// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>What <see cref="WorldPointerInput"/> asks of the adapter beyond <see cref="IWorldClickHost"/>'s picking.</summary>
public interface IWorldPointerHost : IWorldClickHost
{
    /// <summary>The press neither the HUD nor a held choice took: the host's minimap may start a camera glide.</summary>
    void MinimapPress(in HudPress press);
    /// <summary>The pointer's point on <paramref name="tile"/>, map coordinates, or null where the host cannot pick one.</summary>
    FpPosition2? PointerPosition(TilePos tile);
}

/// <summary>The input a frame's world pointer step reads: whether ordinary input reaches the world, the possession and
/// modal gates, the frame's presses and releases, and the clock.</summary>
public readonly record struct WorldPointerFrame(
    PlayerId Viewer,
    bool OrdinaryInput,
    bool Possessing,
    bool Modal,
    HudPress Press,
    bool PrimaryUp,
    bool SecondaryUp,
    long Milliseconds);

/// <summary>
/// The gates a frame's pointer passes
/// on its way to a world command, in order. Without ordinary input (a camera path, the letterbox, the result) the world
/// drag is dropped. Possessing, the pointer is the view's: only a modal panel takes its presses, and the drag is dropped.
/// The HUD's press (<see cref="HudPressRouter"/>) or a held choice's (<see cref="WorldClickCommands"/>) takes the frame.
/// Else the host's minimap may glide, the tool is selected, and a frame with no press or release ends. Last, the
/// <see cref="PlayerIntent"/> is built and journalled, translated over a snapshot of the world (with the hover for the
/// Hand and Slap), and each command posted through <see cref="PickupFlow"/> with its cursor, cast hand and tag preview.
/// </summary>
public sealed class WorldPointerInput
{
    private readonly PlayerInputTranslator _input;
    private readonly HudToolState _tools;
    private readonly HudPressRouter _presses;
    private readonly WorldClickCommands _clicks;
    private readonly PickupFlow _pickups;
    private readonly WorldView _view;
    private readonly HandCursorFeedback? _cursor;
    private readonly HoverCursorResolver? _hover;
    private readonly FrameJournalSession? _journal;
    private readonly IWorldPointerHost _host;

    /// <param name="cursor">The hand cursor's transient feedback, or null for a host that draws no hand cursor.</param>
    /// <param name="hover">The hover resolver for the cast hand, or null for a host without one.</param>
    /// <param name="journal">The session's journal, or null for none.</param>
    public WorldPointerInput(PlayerInputTranslator input, HudToolState tools, HudPressRouter presses, WorldClickCommands clicks,
        PickupFlow pickups, WorldView view, HandCursorFeedback? cursor, HoverCursorResolver? hover, FrameJournalSession? journal,
        IWorldPointerHost host)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _presses = presses ?? throw new ArgumentNullException(nameof(presses));
        _clicks = clicks ?? throw new ArgumentNullException(nameof(clicks));
        _pickups = pickups ?? throw new ArgumentNullException(nameof(pickups));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _cursor = cursor;
        _hover = hover;
        _journal = journal;
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Runs the frame's pointer through the gates; commands are added to <paramref name="pending"/>.</summary>
    public void Queue(in WorldPointerFrame frame, WorldState world, GameDatabase database, List<PlayerCommand> pending)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (pending is null) throw new ArgumentNullException(nameof(pending));
        if (!frame.OrdinaryInput)
        {
            _input.CancelPointerGesture();
            return;
        }
        // Possessing, the pointer is the view's: only a modal panel takes its clicks.
        if (frame.Possessing)
        {
            if (frame.Modal) _presses.Route(frame.Press, world, database, frame.Milliseconds, pending);
            _input.CancelPointerGesture();
            return;
        }
        if (_presses.Route(frame.Press, world, database, frame.Milliseconds, pending) || _clicks.Queue(frame.Press, database, pending)) return;
        _host.MinimapPress(frame.Press);
        PlayerToolMode tool = _tools.ActiveTool;
        _input.SelectTool(frame.Viewer, tool);
        PointerButtonTransition button = PointerButtonTransition.None;
        if (frame.Press.Primary) button |= PointerButtonTransition.Pressed;
        if (frame.PrimaryUp) button |= PointerButtonTransition.Released;

        PointerButtonTransition secondaryButton = PointerButtonTransition.None;
        if (frame.Press.Secondary) secondaryButton |= PointerButtonTransition.Pressed;
        if (frame.SecondaryUp) secondaryButton |= PointerButtonTransition.Released;

        if (button == PointerButtonTransition.None && secondaryButton == PointerButtonTransition.None) return;

        TilePos? tile = _host.PointerTile();
        var intent = new PlayerIntent(
            frame.Viewer, tool, tile, button, _tools.SelectedRoomId,
            SecondaryButton: secondaryButton,
            PointerOverHud: _host.PointerOverHud(),
            PointerPosition: tile is TilePos pointTile ? _host.PointerPosition(pointTile) : null);
        _journal?.Intent(intent);
        var snapshot = PlayerInputSnapshot.Capture(
            _view, world, frame.Viewer, database, pointerTile: tile, pendingCommands: pending);
        if (tile is TilePos at && (tool == PlayerToolMode.Hand || tool == PlayerToolMode.Slap))
            snapshot = snapshot.ForHover(_host.Hover(at));
        InputFrame translated = _input.Translate(intent, snapshot);
        long nowMs = frame.Milliseconds;
        FpPosition2? pointed = intent.PointerPosition ?? (tile is TilePos centred
            ? new FpPosition2(new Fp32(CreatureState.CentreOf(centred.X)), new Fp32(CreatureState.CentreOf(centred.Y)))
            : null);
        if ((button & PointerButtonTransition.Pressed) != 0 && _input.DragFrom(PlayerToolMode.Hand) != null)
            _cursor?.Play(HandCursorKind.Tag, nowMs, pointed);

        foreach (PlayerCommand command in translated.All)
        {
            // A refused pickup is skipped and a drop deferred onto a pending pickup; a posted pickup gets its held
            // cursor first and its minimap preview after (PickupFlow).
            if (!_pickups.PostWorldCommand(world, database, command, nowMs, pointed, pending)) continue;
            if (command is SlapCreatureCommand) _cursor?.Play(HandCursorKind.Slap, nowMs, pointed);
            // DK2's click runs the cursor's own permission tests and posts only a permitted cast.
            _hover?.CastPosted(command, nowMs);
            if (command is TagTilesCommand tag) _view.QueueTagPreview(tag, world.CurrentTick.Next);
        }
    }
}
