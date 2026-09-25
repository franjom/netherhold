// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Tests;

/// <summary>The (state, saved caller, resume) triples a creature passes through, one entry
/// each time the triple changes between ticks.</summary>
internal static class StateTrace
{
    public static List<(int State, int Saved, int Resume)> Of(
        GameSimulation sim, CreatureState creature, int ticks, Func<bool>? until = null)
    {
        var seen = new List<(int, int, int)>();
        (int, int, int) last = (creature.Dk2StateId, creature.StateBeforeUtilityState, creature.UtilityNewState);
        for (int t = 0; t < ticks && !(until?.Invoke() ?? false); t++)
        {
            sim.Tick();
            (int, int, int) now = (creature.Dk2StateId, creature.StateBeforeUtilityState, creature.UtilityNewState);
            if (now == last) continue;
            seen.Add(now);
            last = now;
        }

        return seen;
    }

    /// <summary>Only the state ids of <see cref="Of"/>.</summary>
    public static List<int> States(GameSimulation sim, CreatureState creature, int ticks, Func<bool>? until = null) =>
        Of(sim, creature, ticks, until).Select(s => s.State).ToList();
}
