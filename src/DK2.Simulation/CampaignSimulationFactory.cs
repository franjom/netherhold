// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>Starts a campaign from loaded records, including its player and party scripts.</summary>
public static class CampaignSimulationFactory
{
    public static GameSimulation Create(
        GameDatabase database, LevelMap map, LevelDef level,
        IReadOnlyList<PlayerDef> players, IReadOnlyList<ThingRecord> things,
        IReadOnlyList<TriggerRecord> triggers, ulong seed, bool fogOfWarEnabled = false)
    {
        WorldState world = WorldState.FromMap(database, map,
            players.Select(p => p.PlayerId), fogOfWarEnabled: fogOfWarEnabled);
        ThingProjection.Place(world, database, things,
            players.ToDictionary(p => p.PlayerId, p => p.StartingGold));
        var tree = new TriggerTree(triggers, level.TriggerId,
            things.OfType<HeroPartyThing>(),
            players.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
        return new GameSimulation(database, world, seed, SimulationSystems.Default(), tree);
    }
}
