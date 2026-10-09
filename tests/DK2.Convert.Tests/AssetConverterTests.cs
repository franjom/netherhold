// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Data.Model;
using static DK2.Convert.Tests.ArtTestData;

namespace DK2.Convert.Tests;

public sealed class AssetConverterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dk2-convert-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ObjectsConvertThroughFakeSourcesInCatalogueOrder()
    {
        var meshes = new FakeMeshes(("Gold Mesh.kmf", AnimatedKmfFixture.TwoFrames()));
        var textures = new FakeTextures("creature0");
        Dk2ArtCatalogue art = Catalogue(objects: new() { [1] = [new("Gold Mesh", 0)], [2] = [new("Lantern", 0)] });
        GameDatabase database = Database(objects: [Object(2, "Lantern"), Object(1, "Gold Pile"), Object(3, "Barrel")]);

        IReadOnlyList<AssetConversionResult> results =
            AssetConverter.ConvertAll(art, meshes, textures, database, _root);

        Assert.Equal(["object.gold_pile.mesh", "object.lantern.mesh", "object.barrel.mesh"],
            results.Select(result => result.Reference.Key));
        AssetConversionResult gold = results[0];
        Assert.True(gold.Produced);
        Assert.Equal("object mesh from Gold Mesh.kmf", gold.Detail);
        Assert.Equal(Path.Combine(_root, "object", "gold_pile", "mesh.glb"), gold.OutputPath);
        Assert.True(File.Exists(gold.OutputPath));
        Assert.Equal(["creature0"], textures.Reads);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "_textures"), "creature0-*.png"));
        Assert.Equal((false, "Meshes.WAD has no 'Lantern.kmf'"), (results[1].Produced, results[1].Detail));
        Assert.Equal((false, "KWD has no object art record for id 3"), (results[2].Produced, results[2].Detail));
    }

    [Fact]
    public void AMissingSourceDeletesTheKeysEarlierOutput()
    {
        string stale = Path.Combine(_root, "object", "lantern", "mesh.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllBytes(stale, [1, 2, 3]);
        Dk2ArtCatalogue art = Catalogue(objects: new() { [2] = [new("Lantern", 0)] });

        AssetConversionResult result = Assert.Single(AssetConverter.ConvertAll(
            art, new FakeMeshes(), new FakeTextures(), Database(objects: [Object(2, "Lantern")]), _root));

        Assert.False(result.Produced);
        Assert.Null(result.OutputPath);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void AMissingEngineTextureReportsTheMeshAsMissing()
    {
        var meshes = new FakeMeshes(("Gold Mesh.kmf", AnimatedKmfFixture.TwoFrames()));
        Dk2ArtCatalogue art = Catalogue(objects: new() { [1] = [new("Gold Mesh", 0)] });

        AssetConversionResult result = Assert.Single(AssetConverter.ConvertAll(
            art, meshes, new FakeTextures(), Database(objects: [Object(1, "Gold Pile")]), _root));

        Assert.False(result.Produced);
        Assert.Equal("Gold Mesh.kmf: Engine texture 'creature0' was not found.", result.Detail);
        Assert.False(File.Exists(Path.Combine(_root, "object", "gold_pile", "mesh.glb")));
    }

    [Fact]
    public void WalkableTerrainTakesTheFirstCollectionMemberAndHazardsReportNoEffect()
    {
        var meshes = new FakeMeshes(
            ("Floor2.kmf", AnimatedKmfFixture.TwoFrames()), ("Floor1.kmf", AnimatedKmfFixture.TwoFrames()));
        Dk2ArtCatalogue art = Catalogue(terrains: new() { [1] = [new("Floor", 0)] });
        GameDatabase database = Database(terrain: [Terrain(1, "Lava", damage: 4), Terrain(2, "Rock", solid: true)]);

        IReadOnlyList<AssetConversionResult> results =
            AssetConverter.ConvertAll(art, meshes, new FakeTextures("creature0"), database, _root);

        // The plain terrain pieces follow each terrain's own keys (AssetCatalogue.ForTerrainPieces);
        // this fake lava has no ConstructionTypeWater flag, so it takes a floor.
        Assert.Equal(["terrain.lava.texture", "terrain.lava.effect", "terrain.lava.mesh.floor",
            "terrain.rock.mesh", "terrain.rock.mesh.side", "terrain.rock.mesh.top"],
            results.Select(result => result.Reference.Key));
        Assert.Equal("terrain complete mesh's first material texture 'creature0' from Floor1.kmf",
            results[0].Detail);
        Assert.True(File.Exists(Path.Combine(_root, "terrain", "lava", "texture.png")));
        Assert.Equal("effect conversion is UNKNOWN; no effect format is emitted", results[1].Detail);
        Assert.Equal("KWD has no terrain art record for id 2", results[3].Detail);
    }
}
