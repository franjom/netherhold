// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a level script asked the player's view to do. Observation only: the view
/// plays it, the simulation never reads one back (<see cref="SimEvent"/>).</summary>
public enum ScriptedViewKind : byte
{
    /// <summary>Action 31: camera path <see cref="ScriptedViewRequest.Value"/> from the centre
    /// of <see cref="ScriptedViewRequest.Tile"/>; <see cref="ScriptedViewRequest.Flag"/> is the
    /// action's <c>Available</c>.</summary>
    CameraPath = 1,

    /// <summary>The player skipped the path in flight (<c>0x00405362</c>).</summary>
    CameraPathSkipped = 2,

    /// <summary>Action 38 and game action <c>0x74</c>: letterbox on (<see cref="ScriptedViewRequest.Flag"/>)
    /// or off; a <see cref="ScriptedViewRequest.Value"/> of 2 forces it off (<c>0x004426F0</c>).</summary>
    Letterbox = 3,

    /// <summary>A level Mentor line: <see cref="ScriptedViewRequest.Value"/> is one-based;
    /// its subtitle is the level text entry Value - 1. Flag replaces preceding speech for
    /// a Portal introduction (type 14).</summary>
    Speech = 4,

    /// <summary>Action 26: the keeper camera glides to the centre of
    /// <see cref="ScriptedViewRequest.Tile"/> (game action 4, <c>0x00442650</c>).</summary>
    CameraMove = 5,

    /// <summary>World action 9: replace or clear the shared HUD FlashButton descriptor.</summary>
    FlashButton = 6,

    /// <summary>Game action 15 (<c>0x00442780</c>): the camera asked for mode
    /// <see cref="ScriptedViewRequest.Value"/>, its yaw zeroed first from mode 7.</summary>
    CameraMode = 7,

    /// <summary>Game action 6 (<c>0x00442290</c>): zoom to <see cref="ScriptedViewRequest.Value"/>
    /// over <see cref="ScriptedViewRequest.Value2"/> milliseconds.</summary>
    CameraZoom = 8,

    /// <summary>Game action 17 (<c>0x004427D0</c>): follow creature <see cref="ScriptedViewRequest.Value"/>,
    /// or stop following with 0.</summary>
    CameraFollow = 9,

    /// <summary>Game action <c>0x84</c> (<c>0x00443030</c>): view <c>+0x1221 |= 0x10</c>, meaning unread.</summary>
    ViewFlag84 = 10,

    /// <summary>Interface slot <c>+0x40</c> (<c>0x0042BE40</c>): the win screen starts.</summary>
    WinScreen = 11,

    /// <summary>Interface slot <c>+0x38</c>: the GUI hidden for the celebration (hud-visibility.md).</summary>
    HideGui = 12,
}

/// <param name="Tick">For <see cref="ScriptedViewKind.CameraPath"/>, the tick the path started
/// on, so the view plays it on the simulation's clock and ends it on the tick
/// <see cref="CameraPathRules.DurationTicks"/> gives.</param>
/// <param name="Value2">A second argument where the kind has one (<see cref="ScriptedViewKind.CameraZoom"/>).</param>
public readonly record struct ScriptedViewRequest(
    ScriptedViewKind Kind, PlayerId Player, int Value, TilePos Tile, bool Flag, long Tick = 0,
    HudFlashRequest? FlashButton = null, int Value2 = 0);

public readonly record struct HudFlashRequest(HudFlashTarget Target, uint DurationSeconds, bool Enabled);

/// <summary>A level script's camera path, as far as the trigger walk can see it: condition 72
/// is false while one plays (camera-paths.md, ADR-0496).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/camera-paths.md")]
public static class CameraPathRules
{
    /// <summary><c>0x0044A370</c>: paths 50-99, the creature intros, start at frame 7.</summary>
    public static int FirstFrame(int pathId) => pathId is >= 50 and <= 99 ? 7 : 0;

    /// <summary>Game ticks a path of <paramref name="frames"/> frames plays at the nominal
    /// 250 ms a tick. <c>Camera_Update</c> adds 0.03 frames a wall-clock millisecond to a
    /// remainder that starts at 0.5 and ends the path once the frame reaches
    /// <c>frames - 1</c>, so it lasts <c>(frames - first - 1.5) / 0.03</c> ms, which is
    /// <c>(2 (frames - first) - 3) / 15</c> ticks, rounded up here so the walk that first
    /// sees the camera still is the one after it stopped. The original measures wall clock,
    /// ours game ticks; they agree at normal speed (INFERRED).</summary>
    public static int DurationTicks(int pathId, int frames)
    {
        int halfFrames = 2 * (frames - FirstFrame(pathId)) - 3;
        return halfFrames <= 0 ? 0 : (halfFrames + 14) / 15;
    }

    public static bool Playing(in PlayerState player, GameTick now) => player.CameraPathEndTick > now.Value;

    /// <summary>Action 31 for <paramref name="player"/>. <c>0x0044A370</c> refuses while a path
    /// plays or the keeper possesses, and when the file does not load; a refused start changes
    /// nothing. Returns whether the path started.</summary>
    public static bool TryStart(GameDatabase database, ref PlayerState player, bool possessing, int pathId, GameTick now)
    {
        if (Playing(player, now) || possessing) return false;
        if (!database.CameraPathFrames.TryGetValue(pathId, out int frames)) return false;
        player.CameraPathEndTick = now.Value + DurationTicks(pathId, frames);
        return true;
    }

    /// <summary>The skip (<c>0x00405362</c>): with the camera on a path, end it now.</summary>
    public static bool Skip(ref PlayerState player, GameTick now)
    {
        if (!Playing(player, now)) return false;
        player.CameraPathEndTick = now.Value;
        return true;
    }
}
