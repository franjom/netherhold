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

/// <summary>The keys decoded ahead of the first frame cover what the presenter then asks for.</summary>
public sealed class FirstFrameArtTests
{
    [InstallFact]
    public void TheKeysCoverEveryAssetTheFirstPresentPassesExceptBareTiles()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        GameSimulation simulation = CampaignSimulationFactory.Create(database,
            GameDatabaseLoader.LoadCampaignMap(install, "Level8")!, GameDatabaseLoader.LoadCampaignLevel(install, "Level8")!,
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level8")!, GameDatabaseLoader.LoadCampaignThings(install, "Level8")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level8")!, 1, fogOfWarEnabled: false);
        var viewer = new PlayerId(1);

        IReadOnlyCollection<AssetRef> keys = FirstFrameArt.Keys(database, simulation.World, viewer);

        IRenderTarget target = DispatchProxy.Create<IRenderTarget, Recorder>();
        new ScenePresenter(new WorldView(database, viewer), PlaceholderProvider.Instance, target, new TorchLayout(database))
            .Present(simulation.World);
        var recorder = (Recorder)target;
        Assert.NotEmpty(recorder.Assets);
        Assert.NotEmpty(recorder.TerrainKeys);
        string[] missing = recorder.Assets.Concat(recorder.TerrainKeys)
            .Where(key => !keys.Contains(key)).Select(key => key.ToString()).ToArray();
        Assert.True(missing.Length == 0, "not listed: " + string.Join(", ", missing));
    }

    /// <summary>Every asset passed to a create call, except a tile's ground and furniture, which the
    /// classic terrain draws from its piece keys instead; those keys are kept apart.</summary>
    public class Recorder : DispatchProxy
    {
        public HashSet<AssetRef> Assets { get; } = [];
        public HashSet<AssetRef> TerrainKeys { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IRenderTarget.CreateTile))
            {
                var tile = (TileView)args![1]!;
                if (tile.PieceKeys is { } pieces)
                {
                    TerrainKeys.Add(pieces.Side);
                    TerrainKeys.Add(pieces.Top);
                    TerrainKeys.Add(pieces.Floor);
                }
                if (tile.RoomKeys is { } room) TerrainKeys.Add(room.Piece(0));
                TerrainKeys.Remove(default);
            }
            else if (method.Name.StartsWith("Create", StringComparison.Ordinal))
            {
                foreach (object? arg in args!)
                    if (arg is ResolvedAsset asset && !asset.Reference.IsEmpty) Assets.Add(asset.Reference);
            }
            return method.ReturnType == typeof(void) ? null : Activator.CreateInstance(method.ReturnType);
        }
    }
}
