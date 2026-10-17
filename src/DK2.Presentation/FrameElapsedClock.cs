// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>Elapsed presentation time sampled after worker synchronization, with a catchup cap.</summary>
public sealed class FrameElapsedClock
{
    private double? _previous;

    public double Sample(double realtimeSeconds, double fallbackDeltaSeconds, double maximumDeltaSeconds, double timeScale)
    {
        double delta = _previous.HasValue ? (realtimeSeconds - _previous.Value) * timeScale : fallbackDeltaSeconds;
        _previous = realtimeSeconds;
        return Math.Clamp(delta, 0, maximumDeltaSeconds);
    }
}
