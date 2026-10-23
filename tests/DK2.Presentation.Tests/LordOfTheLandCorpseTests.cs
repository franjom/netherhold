// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Q622: Level 1's Lord of the Land dies outright (type 29, <c>0x0048E110</c>) and DK2
/// leaves his armoured body, a class-6 thing drawn with creature art 41 (<c>0x004B0F20</c>),
/// <c>LOL-DiePose</c>. Ours keeps him as a corpse for <c>DeadBodyDiesAfterSeconds</c> and draws that
/// mesh, and the Portal Gem he carried lies on the floor as object 28 (Q623).</summary>
public class LordOfTheLandCorpseTests
{
    [InstallFact]
    public void LevelOnesLordLiesAsHisDiePoseForAMinuteAndDropsTheGem()
    {
        string install = Dk2InstallGate.Path;
        const string level = "level1";
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        GameSimulation simulation = CampaignSimulationFactory.Create(database,
            GameDatabaseLoader.LoadCampaignMap(install, level)!, GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!, GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, 1);
        simulation.Run(3);
        WorldState world = simulation.World;
        CreatureState lord = Assert.Single(world.Creatures,
            c => database.CreatureById(c.CreatureTypeId).Name == "Lord Of The Land");

        lord.Health = 0;
        simulation.Run(SimulationTiming.TicksPerSecond * 2);

        Assert.True(CreatureDeathRules.IsCorpse(lord));
        CreatureView body = Assert.Single(new WorldView(database).Creatures(world), c => c.Id == lord.Id);
        AssetRef dead = AssetRef.ForCreature("Lord Of The Land", AssetKind.Mesh, "dead");
        Assert.Equal(dead, body.Mesh);
        Assert.False(body.PlaysAnimation);
        using InstallArtProvider art = InstallArtProvider.Open(install, database);
        Assert.True(art.TryResolve(dead, out ResolvedAsset asset));
        Assert.Equal("LOL-DiePose.kmf", asset.InstallEntry, ignoreCase: true);

        ObjectState gem = Assert.Single(world.Objects, o => o.ObjectTypeId == TriggerExecutor.PortalGemObjectId);
        Assert.Equal(-1, gem.CarriedByCreatureId);
        // Loose, it runs state 50 (0x004A3C30): bobbing about 0x1400 and spinning 0x40 a turn.
        ObjectView floating = Assert.Single(new WorldView(database).Objects(world), o => o.Id == gem.Id);
        Assert.Equal(LevelGemIdle.Height(LevelGemIdle.Turn(world)), floating.Height);
        Assert.Equal(297, database.ObjectById(TriggerExecutor.PortalGemObjectId)!.IdleEffectId);

        // Its idle effect, 297, once a game second while it is not carried.
        var idle = new LevelGemIdleEffects(database);
        var requests = new List<ViewEffectRequest>();
        idle.Collect(world, requests);
        requests.Clear();
        simulation.Run(SimulationTiming.TicksPerSecond * 2);
        idle.Collect(world, requests);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal((ushort)297, r.EffectId.Value));

        // DeadBody_Construct seeds 60 s (0x004B0FAF); the body is still there just before.
        simulation.Run(SimulationTiming.TicksPerSecond * 54);
        Assert.NotNull(world.CreatureById(lord.Id));
    }
}
