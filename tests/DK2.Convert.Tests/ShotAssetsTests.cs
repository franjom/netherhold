// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Assets;
using DK2.Data;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class ShotAssetsTests
{
    [InstallFact]
    public void EveryShippedMeshBodyLoadsThroughItsLogicalShotKey()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        using var art = InstallArtProvider.Open(Dk2InstallGate.Path, database);
        foreach (var shot in database.Shots.Values.Where(s => s.MeshResource.Type is 5 or 6))
        {
            Assert.True(art.TryResolve(AssetRef.ForShot(shot.Name), out ResolvedAsset asset), shot.Name);
            Assert.True(art.TryLoadModel(asset, out var model), shot.Name);
            Assert.NotNull(model.Mesh);
        }
    }

    [InstallFact]
    public void EveryShippedSpriteBodyLoadsThroughItsLogicalShotKey()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        using var art = InstallArtProvider.Open(Dk2InstallGate.Path, database);
        var sprites = new InstallEffectSpriteProvider(database, art.LoadTexture);
        foreach (var shot in database.Shots.Values.Where(s => s.MeshResource.Type is 1 or 2 or 3))
        {
            Assert.True(sprites.TryResolve(AssetRef.ForShot(shot.Name), out ResolvedAsset asset), shot.Name);
            Assert.True(sprites.TryGetSprite(asset, out _), shot.Name);
            Assert.True(sprites.TryLoadFrame(asset, 0, out _), shot.Name);
        }
    }
}
