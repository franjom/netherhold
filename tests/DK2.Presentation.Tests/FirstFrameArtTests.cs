// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The keys decoded ahead of the first frame cover what that frame asks for, and reading
/// them leaves the simulation as it was.</summary>
public sealed class FirstFrameArtTests
{
    private static readonly PlayerId Viewer = new(1);

    [InstallFact]
    public void TheKeysCoverEveryAssetAPresentOfTheSameWorldPasses()
    {
        // The runner presents after the first tick, whose additions load on request; what the
        // keys promise is the world they were read from.
        (GameDatabase database, GameSimulation simulation) = Level8();
        IReadOnlyCollection<AssetRef> keys = FirstFrameArt.Keys(database, simulation.World, Viewer);

        IRenderTarget target = DispatchProxy.Create<IRenderTarget, Recorder>();
        new ScenePresenter(new WorldView(database, Viewer), PlaceholderProvider.Instance, target, new TorchLayout(database))
            .Present(simulation.World);

        var recorder = (Recorder)target;
        Assert.NotEmpty(recorder.Assets);
        AssertCovered(keys, recorder.Assets);
    }

    [InstallFact]
    public void TheTerrainKeysCoverEveryPieceSetAnyTileHandsOut()
    {
        (GameDatabase database, GameSimulation simulation) = Level8();
        var view = new WorldView(database, Viewer);
        var expected = new HashSet<AssetRef>();
        for (int y = 0; y < simulation.World.Height; y++)
            for (int x = 0; x < simulation.World.Width; x++)
            {
                TileView tile = view.TileAt(simulation.World, x, y);
                if (tile.PieceKeys is { } pieces) expected.UnionWith(pieces.All());
                if (tile.BedKeys is { } bed) expected.UnionWith(bed.All());
                if (tile.RoomKeys is { } room) expected.UnionWith(room.All());
                expected.Add(tile.SightFogOverlay);
            }
        expected.Remove(default);

        AssertCovered(new HashSet<AssetRef>(view.TerrainArtKeys(simulation.World)), expected);
    }

    [InstallFact]
    public void ReadingTheKeysLeavesTheSimulationAsItWas()
    {
        (GameDatabase database, GameSimulation read) = Level8();
        (_, GameSimulation untouched) = Level8();

        FirstFrameArt.Keys(database, read.World, Viewer);
        for (int tick = 0; tick < 40; tick++)
        {
            read.Tick();
            untouched.Tick();
            Assert.Equal(untouched.StateHash(), read.StateHash());
        }
    }

    private static void AssertCovered(IReadOnlyCollection<AssetRef> keys, IEnumerable<AssetRef> wanted)
    {
        string[] missing = wanted.Where(key => !keys.Contains(key)).Select(key => key.ToString()).ToArray();
        Assert.True(missing.Length == 0, "not listed: " + string.Join(", ", missing));
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

    /// <summary>Every asset passed to a create call, except a tile's ground and furniture, which the
    /// classic terrain draws from its piece keys instead (checked by the terrain test).</summary>
    public class Recorder : DispatchProxy
    {
        public HashSet<AssetRef> Assets { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name.StartsWith("Create", StringComparison.Ordinal) && method.Name != nameof(IRenderTarget.CreateTile))
                foreach (object? arg in args!)
                    if (arg is ResolvedAsset asset && !asset.Reference.IsEmpty) Assets.Add(asset.Reference);
            return method.ReturnType == typeof(void) ? null : Activator.CreateInstance(method.ReturnType);
        }
    }
}
