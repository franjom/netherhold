// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The gem carrier's sparkle pace. Render_MeshEffectPoints (0x005A3AA0) runs after each draw of a creature's
/// mesh, and a gem carrier gives off effect 257 at its point 10001 every draw (<see cref="CreatureView.GemDustSparkle"/>).
/// Per-draw work is paced by the 50 ms presentation quantum, as the wall flames are (ADR-0494): at most one per draw and
/// one per quantum of the wall clock, since retail DK2's frame rate is open (Q604).</summary>
public sealed class GemSparkleGate
{
    public const int QuantumMilliseconds = 50;

    private readonly Dictionary<CreatureId, long> _quanta = new();

    /// <summary>Whether this draw of <paramref name="creature"/> sparkles: true the first time and in each later quantum.</summary>
    public bool TryPass(CreatureId creature, EffectTime now)
    {
        long quantum = now.Milliseconds / QuantumMilliseconds;
        if (_quanta.TryGetValue(creature, out long last) && quantum <= last) return false;
        _quanta[creature] = quantum;
        return true;
    }

    public void Forget(CreatureId creature) => _quanta.Remove(creature);
}
