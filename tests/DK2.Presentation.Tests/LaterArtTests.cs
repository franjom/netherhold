// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The keys decoded in the background after the first tick: every clip of each creature
/// type present, and what the first tick added, less what the first frame already decoded
/// (ADR-0688).</summary>
public sealed class LaterArtTests
{
    private static readonly PlayerId Viewer = new(1);

    [InstallFact]
    public void TheLaterKeysHoldEveryClipOfEachPresentCreatureTypeAndNothingOfTheFirstFrame()
    {
        (GameDatabase database, GameSimulation simulation) = Level8();
        IReadOnlyCollection<AssetRef> first = FirstFrameArt.Keys(database, simulation.World, Viewer);
        simulation.Tick(InputFrame.Empty);
        IReadOnlyCollection<AssetRef> later = FirstFrameArt.LaterKeys(database, simulation.World, Viewer, first);

        Assert.NotEmpty(later);
        Assert.Empty(later.Intersect(first));

        var view = new WorldView(database, Viewer);
        var presentPrefixes = new HashSet<string>(view.Creatures(simulation.World)
            .Where(c => !c.Mesh.IsEmpty).Select(c => FirstFrameArt.TypePrefix(c.Mesh)));
        Assert.NotEmpty(presentPrefixes);
        var laterSet = new HashSet<AssetRef>(later);
        int presentTypes = 0;
        foreach (CreatureDef creature in database.Creatures.Values)
        {
            bool present = presentPrefixes.Contains(FirstFrameArt.TypePrefix(AssetRef.ForCreature(creature.Name, AssetKind.Mesh)));
            if (present) presentTypes++;
            foreach (string animation in AssetCatalogue.CreatureAnimations.Concat(AssetCatalogue.CreatureActionAnimations))
            {
                AssetRef clip = AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation);
                if (present) Assert.True(laterSet.Contains(clip) || first.Contains(clip), $"{clip.Key} is not decoded ahead");
                else Assert.False(laterSet.Contains(clip), $"{clip.Key} belongs to a creature type not present");
            }
        }
        Assert.True(presentTypes > 0);
    }

    [InstallFact]
    public void ReadingTheLaterKeysLeavesTheSimulationAsItWas()
    {
        (GameDatabase database, GameSimulation read) = Level8();
        (_, GameSimulation untouched) = Level8();
        read.Tick(InputFrame.Empty);
        untouched.Tick(InputFrame.Empty);
        IReadOnlyCollection<AssetRef> first = FirstFrameArt.Keys(database, read.World, Viewer);
        FirstFrameArt.LaterKeys(database, read.World, Viewer, first);
        for (int i = 0; i < 20; i++)
        {
            read.Tick(InputFrame.Empty);
            untouched.Tick(InputFrame.Empty);
        }
        Assert.Equal(untouched.StateHash(), read.StateHash());
    }

    private static (GameDatabase, GameSimulation) Level8()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        GameSimulation simulation = CampaignSimulationFactory.Create(database,
            GameDatabaseLoader.LoadCampaignMap(install, "Level8")!, GameDatabaseLoader.LoadCampaignLevel(install, "Level8")!,
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level8")!, GameDatabaseLoader.LoadCampaignThings(install, "Level8")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level8")!, 1, fogOfWarEnabled: false);
        return (database, simulation);
    }
}
