// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

public sealed class HeartPulseGate
{
    private EffectTime? _lastConsumed;

    public bool ConsumePendingBeat(EffectTime now)
    {
        if (!_lastConsumed.HasValue) { _lastConsumed = now; return false; }
        if (now.Milliseconds - _lastConsumed.Value.Milliseconds <= 1000) return false;
        _lastConsumed = now;
        return true;
    }

    public static bool CanEmit(GameTick tick) => tick.Value > HeartIntroRules.SequenceSeconds * SimulationTiming.OriginalTicksPerGameSecond;
}
