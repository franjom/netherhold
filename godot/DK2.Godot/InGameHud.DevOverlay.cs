// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using DK2.Core;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The developer overlay's host (<see cref="DevOverlay"/>) and the frame's part timings it and the
/// <c>--record-frames</c> recorder share: a part is timed only while one of them is there, so neither costs a timestamp
/// otherwise. Also the <c>--fail-tick &lt;n&gt;</c> check, which makes turn n throw to show the turn failure screen.</summary>
public partial class InGameHud : IDevOverlayHost
{
    private DevOverlay? _devOverlay;
    /// <summary>The overlay's model while it shows, taken at the frame's start; null while it is off.</summary>
    private DevOverlayModel? _devTiming;
    /// <summary>The tile under the pointer this frame, kept only while the overlay shows.</summary>
    private TilePos? _devHoverTile;
    private long _failTick = -1;

    private void OpenDevOverlay(string[] args, CampaignLevelData level)
    {
        if (long.TryParse(LaunchArguments.Value(args, "--fail-tick"), NumberStyles.None, CultureInfo.InvariantCulture, out long fail))
            _failTick = fail;
        _devOverlay = DevOverlay.Attach(this, this, args, PlayerNames.From(level.Players));
    }

    private long PartStamp() => _recorder != null || _devTiming != null ? Stopwatch.GetTimestamp() : 0;

    private void PartSpent(FrameTelemetryPart part, long started)
    {
        if (_recorder == null && _devTiming == null) return;
        _recorder?.Spent(part, started);
        _devTiming?.AddPart(part, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    void IDevOverlayHost.FillDevOverlayFacts(ref DevOverlayFacts facts)
    {
        facts.Scene = DevOverlayScene.InGame;
        facts.Level = _levelName;
        facts.GameSpeed = _settings.GameSpeed;
        facts.Paused = _hudControls.TicksPaused;
        facts.CameraPath = _scriptedView?.PathPlaying == true;
        if (_simulation == null) return;
        facts.Turn = _simulation.World.CurrentTick;
        facts.World = _simulation.World;
        facts.Database = _simulation.Database;
        facts.HoverTile = _devHoverTile;
    }

    bool IDevOverlayHost.TryStateHash(out ulong hash, out GameTick turn)
    {
        hash = 0;
        turn = default;
        if (_simulation == null || _failed) return false;
        hash = _simulation.StateHash();
        turn = _simulation.World.CurrentTick;
        return true;
    }
}
