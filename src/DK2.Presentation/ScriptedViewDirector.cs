// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Plays what the level script asks of the viewer's own view (actions 24, 31 and 38,
/// ADR-0496): the camera path on the drawn world's tick, which the script's condition 72 counts
/// too, and the letterbox and the Mentor's subtitles on the wall clock, as DK2's camera and
/// speech system run on theirs. Other keepers' requests are
/// dropped, as DK2 runs those arms for its local player only.</summary>
/// <remarks>Requests arrive during a tick, possibly off the main thread, and are applied by
/// <see cref="Update"/>.</remarks>
public sealed class ScriptedViewDirector(
    PlayerId viewer,
    Func<int, IReadOnlyList<KcsFrame>?> loadPath,
    Func<int, string?> subtitle,
    ISimulationEventSink? downstream = null) : ISimulationEventSink
{
    private readonly ConcurrentQueue<ScriptedViewRequest> _requests = new();
    private readonly CinematicLetterbox _letterbox = new();
    private readonly SubtitleQueue _subtitles = new();
    private CameraPathPlayback? _path;
    private TilePos? _move;
    private double _now;
    private double _renderTick;
    private HudFlashRequest? _flash;
    private uint _flashStartedAt;

    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.ScriptedView is { } request && (request.Player == viewer || request.Kind == ScriptedViewKind.FlashButton))
            _requests.Enqueue(request);
        downstream?.Emit(simEvent);
    }

    public bool PathPlaying => _path is not null;
    public int LetterboxRequestCount => _letterbox.RequestCount;
    public HudFlashTarget? ActiveFlashTarget => _flash?.Target;
    public KeeperResourceFlash ResourceFlash => _flash?.Target == HudFlashTarget.Mana ? KeeperResourceFlash.Mana
        : _flash?.Target == HudFlashTarget.Gold ? KeeperResourceFlash.Gold : KeeperResourceFlash.None;

    public void ClearFlash(HudFlashTarget target)
    {
        if (_flash?.Target == target) _flash = null;
    }
    public HudVisibility HudFor(ClassicCameraMode normalMode) => HudVisibilityRules.ForCamera(
        PathPlaying ? CameraModeId.ScriptedPath : new((ushort)normalMode), _letterbox.RequestCount);

    /// <summary>The path on screen, or null.</summary>
    public int? PathId => _path?.PathId;

    /// <summary>The path's camera this frame, or null when the ordinary camera has the view.</summary>
    public CameraPathPose? Pose => _path?.Pose();

    /// <summary>Each letterbox bar's height as a fraction of the screen's.</summary>
    public float LetterboxBar => _letterbox.Bar(_now);

    /// <summary>The subtitle to draw: DK2 draws one only while the camera is on a path
    /// (<c>0x00403FB0</c>), in the bottom bar.</summary>
    public string? Subtitle => _path is not null && _subtitles.Current >= 0 ? subtitle(_subtitles.Current) : null;

    /// <summary>Takes the latest action 26 glide target, if one came since the last call.</summary>
    public bool TryTakeCameraMove(out TilePos tile)
    {
        tile = _move.GetValueOrDefault();
        bool any = _move.HasValue;
        _move = null;
        return any;
    }

    /// <summary><c>0x0044A270(0)</c>: a move lasts a tenth of the summed squares of its
    /// quarter-unit offsets, 25.6 ms per square tile, and at most 500 ms.</summary>
    public static double GlideMilliseconds(float tilesX, float tilesY) =>
        Math.Min(25.6 * (tilesX * tilesX + tilesY * tilesY), 500);

    /// <param name="nowMilliseconds">The wall clock, for the letterbox and the subtitles.</param>
    /// <param name="renderTick">The drawn world's tick plus its fraction toward the next, for
    /// the camera path (<see cref="CameraPathPlayback.Seek"/>).</param>
    public void Update(double nowMilliseconds, double renderTick)
    {
        _now = nowMilliseconds;
        _renderTick = renderTick;
        if (_flash is { DurationSeconds: > 0 } flash
            && unchecked(_flashStartedAt + flash.DurationSeconds * 1000) < unchecked((uint)_now))
            _flash = null;
        // The path that has run out ends before the requests, so the next one the script
        // started on that tick follows it.
        SeekPath();
        while (_requests.TryDequeue(out ScriptedViewRequest request)) Apply(request);
        SeekPath();
        _subtitles.Update(_now);
    }

    private void SeekPath()
    {
        if (_path is null) return;
        _path.Seek(_renderTick);
        if (_path.Finished) EndPath();
    }

    /// <summary>Places the camera on the clock used to draw the creatures, without consuming requests.</summary>
    public void SeekCamera(double renderTick)
    {
        _renderTick = renderTick;
        SeekPath();
    }

    /// <summary>Escape or Space (<c>0x00405362</c>): with a path playing, end it. Returns whether
    /// there was one, so the caller tells the simulation. The skip's speech clear
    /// (<c>0x00568140</c>) returns at once with the speech object's <c>+4</c> zero, which this
    /// speech-off queue models, so the waiting subtitles stay (INFERRED, camera-paths.md §9).</summary>
    public bool Skip()
    {
        if (_path is null) return false;
        EndPath();
        return true;
    }

    private void Apply(ScriptedViewRequest request)
    {
        switch (request.Kind)
        {
            case ScriptedViewKind.FlashButton:
                _flash = request.FlashButton is { Enabled: true } active && active.Target.Category != byte.MaxValue
                    ? active : null;
                _flashStartedAt = unchecked((uint)_now);
                break;
            case ScriptedViewKind.CameraPath:
                // The simulation refuses a start while a path plays (CameraPathRules.TryStart), so
                // a request it sent is the path the script now has; one still drawn gives way.
                if (loadPath(request.Value) is not { Count: > 0 } frames) return;
                if (_path is not null) EndPath();
                _path = new CameraPathPlayback(request.Value, frames, request.Tile, request.Tick);
                // Every in-game start leaves camera flag 0x8 clear, so 0x0044A370 raises the letterbox.
                _letterbox.Raise(_now);
                break;
            case ScriptedViewKind.CameraPathSkipped:
                if (_path is not null) EndPath();
                break;
            case ScriptedViewKind.Letterbox:
                if (request.Flag) _letterbox.Raise(_now);
                else _letterbox.Lower(_now);
                break;
            case ScriptedViewKind.CameraMove:
                _move = request.Tile;
                break;
            case ScriptedViewKind.Speech:
                // The queue takes the sound event id and subtracts one for Speech.str.
                int line = request.Value - 1;
                if (subtitle(line) is { } text) _subtitles.Enqueue(line, text.Length);
                break;
        }
    }

    /// <summary><c>0x0044D8F0</c> without flag 0x8: the saved mode comes back, the keeper camera
    /// where it was, and the letterbox is lowered.</summary>
    private void EndPath()
    {
        _path = null;
        _letterbox.Lower(_now);
    }
}
