// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The draw clock's lag behind the tick clock on the worker-thread path (ADR-0687).
/// A frame that owes a tick starts it at its end, so its draw cannot have the tick's poses: with
/// the draw clock at the tick clock the fraction clamps at the boundary and the next frame catches
/// up, one hitch per tick. Drawing one frame behind, the owing frame still lies inside the
/// interval it has, and the next frame, with the tick joined, continues from there. The lag is the
/// frame interval the game is running at, followed by a short average so a capped or vsynced game
/// settles on it exactly; a frame longer than the lag clamps as before, by its excess, and a frame
/// shorter holds by the difference, both bounded by the frame-time variation itself. Never more
/// than one tick, so a stall cannot hide a whole tick of motion.</summary>
public sealed class WorkerDrawLag
{
    private const double Smoothing = 0.2;
    private double _frameTicks;

    /// <summary>One frame's advance of the tick clock, in ticks, as the runner adds it to the debt.</summary>
    public void Observe(double frameAdvanceTicks)
    {
        if (frameAdvanceTicks <= 0) return;
        _frameTicks = _frameTicks <= 0 ? frameAdvanceTicks : _frameTicks + Smoothing * (frameAdvanceTicks - _frameTicks);
    }

    /// <summary>The lag, in ticks: one frame at the rate observed, at most one tick.</summary>
    public double Lag => Math.Min(_frameTicks, 1);

    /// <summary>How far the drawn world is from its last completed tick toward the next, for the
    /// creature interpolation and the camera path: the debt after this frame's ticks were taken,
    /// plus those ticks (not yet run), less the lag, within the known interval.</summary>
    public double Fraction(double debt, int ticksStarting) => Math.Clamp(debt + ticksStarting - Lag, 0, 1);
}
