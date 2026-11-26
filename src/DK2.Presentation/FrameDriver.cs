// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>The once-a-turn possession pose, put first among the commands handed to a turn (<see cref="PossessionSession"/>).</summary>
public interface ITurnPoseSource
{
    /// <summary>The pose for the turn about to run, or null when the viewer is not possessing.</summary>
    PlayerCommand? TurnPose(PlayerId viewer);
}

/// <summary>What the scripted view gave the frame's input step: whether a camera path was playing as the step began (it
/// holds the options key back), and the latest action 26 glide target.</summary>
public readonly record struct FrameScriptInput(bool PathWasPlaying, TilePos? Glide)
{
    public bool AllowOptionsKey => !PathWasPlaying;
}

/// <summary>The frame's ticks: how many, the input of the first (every queued command, or none), and the real time the
/// pacer accrued (none while the tick gate is shut, which <see cref="Accrued"/> tells).</summary>
public readonly record struct FrameTicks(int Count, InputFrame FirstInput, bool Accrued, double Advance)
{
    /// <summary>Tick <paramref name="index"/>'s input: the first carries the frame's commands, the rest none.</summary>
    public InputFrame InputFor(int index) => TickPacer.InputFor(index, FirstInput);
}

/// <summary>
/// The steps of a level frame around the adapter's own
/// camera, HUD and world-input steps. Per frame the host calls <see cref="UpdateScript"/>, then runs the level end (which
/// may end the frame), <see cref="AdvancePickups"/>, <see cref="TakeScriptedInput"/>, its own HUD keys (with
/// <see cref="FrameScriptInput.AllowOptionsKey"/>), possession, camera and <see cref="WorldPointerInput"/> (a session
/// switch ends the frame there), then <see cref="TakeTicks"/>, runs the ticks (inline or on a worker), and calls
/// <see cref="AfterTicks"/>. Main thread only; nothing here allocates per frame but the handed-off <see cref="InputFrame"/>.
/// </summary>
public sealed class FrameDriver
{
    private readonly List<PlayerCommand> _pending;
    private readonly WorldView _view;
    private readonly TickPacer _pacer;
    private readonly FrameJournalSession? _journal;
    private PlayerId? _pickupViewer;

    /// <param name="pending">The host's command queue: everything posted since the last hand-off.</param>
    /// <param name="journal">The session's journal, or null for none.</param>
    /// <remarks>The scripted view is passed per call, not kept: a host (or a probe) may replace its director.</remarks>
    public FrameDriver(List<PlayerCommand> pending, WorldView view, TickPacer pacer, FrameJournalSession? journal)
    {
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _journal = journal;
    }

    /// <summary>The scripted view's step: placed by the last tick run, the ticks started and not yet presented, and the
    /// debt toward the next (at most one), the clock condition 72 counts, so the script's next path follows it.</summary>
    public void UpdateScript(ScriptedViewDirector scriptedView, double nowMilliseconds, WorldState world, PlayerId viewer,
        int ticksPending)
    {
        if (scriptedView is null) throw new ArgumentNullException(nameof(scriptedView));
        if (world is null) throw new ArgumentNullException(nameof(world));
        scriptedView.Update(nowMilliseconds, world.CurrentTick.Value + ticksPending + Math.Min(_pacer.Debt, 1d),
            world.Player(viewer).PossessedCreatureId != 0 ? CameraModeId.Possession : CameraModeId.Dungeon);
    }

    /// <summary>The local pickups: dropped when the viewer changes, then advanced to <paramref name="milliseconds"/>.</summary>
    public void AdvancePickups(WorldState world, PlayerId viewer, long milliseconds)
    {
        if (_pickupViewer != viewer)
        {
            _view.LocalPickups.Reset();
            _pickupViewer = viewer;
        }
        _view.LocalPickups.Advance(world, milliseconds);
    }

    /// <summary>The scripted path's input: with a path playing, <paramref name="skipKey"/> (Escape or Space pressed this
    /// frame) ends it and the simulation hears it next tick; then the latest glide target, if any.</summary>
    public FrameScriptInput TakeScriptedInput(ScriptedViewDirector scriptedView, PlayerId viewer, bool skipKey)
    {
        if (scriptedView is null) throw new ArgumentNullException(nameof(scriptedView));
        bool pathWasPlaying = scriptedView.PathPlaying;
        if (pathWasPlaying && skipKey && scriptedView.Skip()) _pending.Add(new SkipCameraPathCommand(viewer));
        return new FrameScriptInput(pathWasPlaying, scriptedView.TryTakeCameraMove(out TilePos glide) ? glide : null);
    }

    /// <summary>After the frame's input: posts the drops whose pickup has landed, then, with the gate open, accrues
    /// <paramref name="elapsedSeconds"/> and takes up to <paramref name="maxTicksPerFrame"/> ticks. With a tick to run,
    /// <paramref name="poses"/>' pose goes first in the queue, and a non-empty queue is handed to the first tick: the
    /// journal sees it, then it is cleared.</summary>
    public FrameTicks TakeTicks(WorldState world, PlayerId viewer, double elapsedSeconds, float speed, int gameSpeed,
        bool ticksPaused, int maxTicksPerFrame, ITurnPoseSource? poses)
    {
        _view.LocalPickups.DrainReadyDrops(world, _pending);
        if (!(speed > 0f) || ticksPaused) return new FrameTicks(0, InputFrame.Empty, false, 0);
        double advance = _pacer.Accrue(elapsedSeconds, speed, gameSpeed);
        int ticks = _pacer.Take(maxTicksPerFrame);
        if (ticks > 0 && poses?.TurnPose(viewer) is PlayerCommand pose) _pending.Insert(0, pose);
        InputFrame first = TickPacer.FirstInput(ticks, _pending);
        if (first.Commands != null)
        {
            _journal?.HandOff(_pending);
            _pending.Clear();
        }
        return new FrameTicks(ticks, first, true, advance);
    }

    /// <summary>After the frame's ticks ran, or on a worker started: the tag previews are kept until their command's tick
    /// has completed, and the terrain highlights advance to <paramref name="milliseconds"/>.</summary>
    public void AfterTicks(WorldState world, long milliseconds)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        _view.AcknowledgeTagPreviews(world.CurrentTick);
        _view.AdvanceTerrainHighlights(new EffectTime(milliseconds));
    }
}
