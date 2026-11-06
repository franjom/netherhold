// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The draw clock's lag behind the tick clock on the worker-thread path (ADR-0687): one
/// frame at the rate the game is running at, so the frame that owes a tick still draws inside the
/// interval of poses it has instead of clamping at the boundary. A short average of the frame
/// interval, so a capped or vsynced game settles on it exactly; never more than one tick, so a
/// stall cannot hide a whole tick of motion.
/// <para>With the presented world (ADR-0694) the frame does not wait for the tick, so the poses of
/// a tick arrive a tick's wall time after it started; the lag is at least that time plus a frame,
/// or the clock reaches the boundary before the poses exist and clamps there. The tick time rises
/// at once to a longer batch and decays slowly, so one slow tick does not clamp the next.</para></summary>
public sealed class WorkerDrawLag
{
    private const double Smoothing = 0.2;
    private const double TickDecay = 0.05;
    private double _frameTicks;
    private double _tickTicks;

    /// <summary>One frame's advance of the tick clock, in ticks, as the runner adds it to the debt.</summary>
    public void Observe(double frameAdvanceTicks)
    {
        if (frameAdvanceTicks <= 0) return;
        _frameTicks = _frameTicks <= 0 ? frameAdvanceTicks : _frameTicks + Smoothing * (frameAdvanceTicks - _frameTicks);
    }

    /// <summary>A tick batch's wall time, in ticks, as the worker measured it.</summary>
    public void ObserveTick(double batchTicks)
    {
        if (batchTicks <= 0) return;
        _tickTicks = batchTicks > _tickTicks ? batchTicks : _tickTicks + TickDecay * (batchTicks - _tickTicks);
    }

    /// <summary>One frame's advance of the tick clock at the rate observed, in ticks.</summary>
    public double Frame => _frameTicks;

    /// <summary>The lag, in ticks: one frame at the rate observed, plus the tick's wall time when the
    /// frame runs beside the tick; at most one tick.</summary>
    public double Lag => Math.Min(_frameTicks + _tickTicks, 1);

    /// <summary>How far the drawn world is from its last presented tick toward the next, for the
    /// creature interpolation and the camera path: the debt after this frame's ticks were taken,
    /// plus the ticks started and not yet presented, less the lag, within the known interval.</summary>
    public double Fraction(double debt, int ticksPending) => Math.Clamp(debt + ticksPending - Lag, 0, 1);
}
