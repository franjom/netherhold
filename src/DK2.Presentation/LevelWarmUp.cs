// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Runs a level's first ticks on a throwaway simulation while the level loads, so the runtime has compiled the
/// code they use before the real simulation's first frame. Cold, Level6a's first tick took 90-150 ms and the dozen after
/// it 15-60 ms, on the frame thread in the middle of the opening camera path. The throwaway has no observer and nothing of
/// it is kept; the level's records and database are the real simulation's, which no simulation changes
/// (<c>LevelWarmUpTests</c> compares the state hash with and without it).</summary>
public static class LevelWarmUp
{
    /// <summary>Ticks of the opening that cover the first-use code of Level6a's first seconds; later ones repeat it.</summary>
    public const int Ticks = 40;

    /// <summary>Ticks <paramref name="ticks"/> turns of a fresh simulation of <paramref name="level"/>, as the adapter
    /// builds the real one, and returns the state hash it ends with.</summary>
    public static ulong Run(CampaignLevelData level, ulong seed, bool fogOfWarEnabled, int ticks = Ticks)
    {
        if (level is null)
        {
            throw new ArgumentNullException(nameof(level));
        }

        GameSimulation simulation = level.CreateSimulation(seed, fogOfWarEnabled);
        for (int i = 0; i < ticks; i++)
        {
            simulation.Tick(InputFrame.Empty);
        }

        return simulation.StateHash();
    }
}
