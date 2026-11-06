// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The presented world's copy (ADR-0693) on a shipped level, where every state type is
/// populated: doors, traps, parties, generators, shots and a few hundred creatures.</summary>
public sealed class PresentedWorldOnRealDataTests
{
    private static ulong WorldHash(WorldState world)
    {
        Hash64 hash = Hash64.Start();
        world.HashInto(ref hash);
        return hash.Value;
    }

    [InstallFact]
    public void Level6a_CopyMatchesAtEveryCheckpoint_AndTicksOnLikeTheLive()
    {
        string install = Dk2InstallGate.Path;
        const string name = "Level6a";
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, name);
        LevelDef level = GameDatabaseLoader.LoadCampaignLevel(install, name)!;
        List<PlayerDef> players = GameDatabaseLoader.LoadCampaignPlayers(install, name)!;
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, name)!;
        List<TriggerRecord> triggers = GameDatabaseLoader.LoadCampaignTriggers(install, name)!;
        GameSimulation live = CampaignSimulationFactory.Create(database,
            GameDatabaseLoader.LoadCampaignMap(install, name)!, level, players, things, triggers, seed: 1);

        WorldState presented = WorldState.CopyOf(live.World);
        foreach (int ticks in new[] { 0, 300, 1200 })
        {
            live.Run(ticks);
            presented.CopyFrom(live.World);
            StateGraph.AssertDeepCopy(live.World, presented, StateGraph.WorldIgnored, StateGraph.WorldShared);
            Assert.Equal(WorldHash(live.World), WorldHash(presented));
        }

        GameSimulation twin = GameSimulation.Resume(database, WorldState.CopyOf(live.World),
            SimRandom.Restore(live.Rng.SaveState()), SimulationSystems.Default(),
            CampaignSimulationFactory.Triggers(level, players, things, triggers));
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) live.Navigation.Write(writer);
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            twin.Navigation.Read(reader);
        }
        Assert.Equal(live.StateHash(), twin.StateHash());
        for (int t = 0; t < 300; t++)
        {
            live.Tick();
            twin.Tick();
            Assert.True(live.StateHash() == twin.StateHash(), $"the copy diverged {t} ticks after it was taken");
        }
    }
}
