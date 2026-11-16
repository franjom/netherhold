// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>The hand's local pickup, moved out of Unity's runner (migration plan step 2c), where it was written twice: the
/// creature overview's press (DungeonRunner.QueueHudInput) and the world click's command loop (formerly
/// DungeonRunner.QueueInput, now <see cref="WorldPointerInput"/>).
/// <see cref="LocalPickupFeedback.TryQueue"/> decides whether a pickup is posted (dead, dying, already pending, not
/// pickable, hand full); a posted one also plays the held-thing cursor and queues the minimap's preview. A drop is
/// deferred onto a pending pickup (<see cref="LocalPickupFeedback.TryQueueDrop"/>) until
/// <see cref="LocalPickupFeedback.DrainReadyDrops"/>, which the host calls once per frame, posts it.</summary>
/// <remarks>The two sites keep their differences, which are an open point of the step-2 audit, not settled here: a refused
/// overview pickup still takes the press (<see cref="HudPressRouter"/> returns true), while a refused world pickup is only
/// skipped and the frame's other commands go on; the overview's cursor gets no pointer and the world's gets the pointed
/// position; and only the overview also queues its own preview (<see cref="CreatureOverview.QueuePickupPreview"/>).
/// Each method's <c>milliseconds</c> is the host's wall clock (Unity's <c>Time.unscaledTimeAsDouble</c> in milliseconds).</remarks>
public sealed class PickupFlow
{
    private readonly WorldView _view;
    private readonly HandCursorFeedback? _cursor;

    /// <param name="view">The viewer's world view, whose <see cref="WorldView.LocalPickups"/> filter the pickups and whose
    /// minimap previews them.</param>
    /// <param name="cursor">The hand cursor's transient states; null for a host that draws no hand cursor.</param>
    public PickupFlow(WorldView view, HandCursorFeedback? cursor)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _cursor = cursor;
    }

    /// <summary>The creature overview's pickup of <paramref name="target"/> (a worker row or a column press that
    /// <see cref="CreatureOverview.Select"/> resolved to a pickup). Returns whether it was posted.</summary>
    public bool PickUpFromOverview(WorldState world, GameDatabase database, PlayerId viewer, CreatureQueryTarget target,
        CreatureOverview overview, long milliseconds, List<PlayerCommand> output)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (overview is null) throw new ArgumentNullException(nameof(overview));
        if (output is null) throw new ArgumentNullException(nameof(output));
        var command = new PickUpCreatureCommand(viewer, target.Value);
        if (!_view.LocalPickups.TryQueue(world, database, command.PlayerId, new CreatureId(command.CreatureId), milliseconds))
            return false;
        _cursor?.Play(HandCursorKind.HoldThing, milliseconds);
        output.Add(command);
        _view.QueueMinimapPickupPreview(command, world.CurrentTick.Next);
        overview.QueuePickupPreview(target, world.CurrentTick.Next);
        return true;
    }

    /// <summary>One command of a world click's frame (<see cref="InputFrame.All"/>, in its order): a pickup the local rules
    /// refuse is dropped, and a drop that a pending pickup takes is deferred; anything else is posted. A posted pickup also plays
    /// the held-thing cursor at <paramref name="pointed"/> and queues the minimap's preview. Returns whether
    /// <paramref name="command"/> was posted, so the host adds its own feedback (slap, cast, tag) only then.</summary>
    public bool PostWorldCommand(WorldState world, GameDatabase database, PlayerCommand command, long milliseconds,
        FpPosition2? pointed, List<PlayerCommand> output)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (command is PickUpCreatureCommand pickup)
        {
            if (!_view.LocalPickups.TryQueue(world, database, pickup.PlayerId, new CreatureId(pickup.CreatureId), milliseconds))
                return false;
            _cursor?.Play(HandCursorKind.HoldThing, milliseconds, pointed);
            output.Add(pickup);
            _view.QueueMinimapPickupPreview(pickup, world.CurrentTick.Next);
            return true;
        }
        if (command is DropCommand drop && _view.LocalPickups.TryQueueDrop(drop)) return false;
        output.Add(command);
        return true;
    }
}
