// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;
using static DK2.Convert.Tests.ArtTestData;

namespace DK2.Convert.Tests;

/// <summary>The converted tier read in place (ADR-0408).</summary>
public sealed class InstallArtProviderTests
{
    [Fact]
    public void AKeyResolvesToItsKmfInTheInstallAndOnlyWhenTheKmfIsThere()
    {
        var meshes = new FakeMeshes(("Gold Mesh.kmf", AnimatedKmfFixture.TwoFrames()));
        Dk2ArtCatalogue art = Catalogue(objects: new() { [1] = [new("Gold Mesh", 0)], [2] = [new("Lantern", 0)] });
        GameDatabase database = Database(objects: [Object(1, "Gold Pile"), Object(2, "Lantern"), Object(3, "Barrel")]);

        var provider = new InstallArtProvider(ArtSourceMap.ForDatabase(database, art), meshes, new FakeTextures());

        Assert.True(provider.TryResolve(AssetRef.ForObject("Gold Pile", AssetKind.Mesh), out ResolvedAsset gold));
        Assert.Equal(AssetSource.InstallEntry, gold.Source);
        Assert.Equal("Gold Mesh.kmf", gold.InstallEntry);
        Assert.Null(gold.FilePath);
        Assert.False(gold.IsPlaceholder);
        Assert.False(provider.TryResolve(AssetRef.ForObject("Lantern", AssetKind.Mesh), out _));
        Assert.False(provider.TryResolve(AssetRef.ForObject("Barrel", AssetKind.Mesh), out _));
        Assert.Equal(1, provider.Count);
    }

    [Fact]
    public void AModelAndATextureAreDecodedOnce()
    {
        var meshes = new FakeMeshes(("Gold Mesh.kmf", AnimatedKmfFixture.TwoFrames()));
        var textures = new FakeTextures("creature0");
        Dk2ArtCatalogue art = Catalogue(objects: new() { [1] = [new("Gold Mesh", 0)] });
        var provider = new InstallArtProvider(
            ArtSourceMap.ForDatabase(Database(objects: [Object(1, "Gold Pile")]), art), meshes, textures);
        Assert.True(provider.TryResolve(AssetRef.ForObject("Gold Pile", AssetKind.Mesh), out ResolvedAsset gold));

        Assert.True(provider.TryLoadModel(gold, out KmfModel first));
        Assert.True(provider.TryLoadModel(gold, out KmfModel second));
        string texture = first.Materials.SelectMany(material => material.TextureNames).First();
        RgbaImage image = provider.LoadTexture(texture);

        Assert.Same(first, second);
        Assert.Same(image, provider.LoadTexture(texture));
        Assert.Equal(["creature0"], textures.Reads);
    }

    [Fact]
    public void ATextureKeyTakesTheFirstCollectionMembersFirstTexture()
    {
        var meshes = new FakeMeshes(
            ("Floor2.kmf", AnimatedKmfFixture.TwoFrames()), ("Floor1.kmf", AnimatedKmfFixture.TwoFrames()));
        Dk2ArtCatalogue art = Catalogue(terrains: new() { [1] = [new("Floor", 0)] });
        var provider = new InstallArtProvider(
            ArtSourceMap.ForDatabase(Database(terrain: [Terrain(1, "Lava")]), art), meshes,
            new FakeTextures("creature0"));

        Assert.True(provider.TryResolve(AssetRef.ForTerrain("Lava", AssetKind.Texture), out ResolvedAsset lava));
        Assert.Equal("Floor1.kmf", lava.InstallEntry);
        Assert.True(provider.TryLoadKeyTexture(lava, out RgbaImage image));
        Assert.Equal((1, 1), (image.Width, image.Height));
    }

    [Fact]
    public void TheStandardChainAsksTheInstallBeforeThePlaceholder()
    {
        var meshes = new FakeMeshes(("Gold Mesh.kmf", AnimatedKmfFixture.TwoFrames()));
        Dk2ArtCatalogue art = Catalogue(objects: new() { [1] = [new("Gold Mesh", 0)] });
        var provider = new InstallArtProvider(
            ArtSourceMap.ForDatabase(Database(objects: [Object(1, "Gold Pile"), Object(3, "Barrel")]), art),
            meshes, new FakeTextures());
        AssetProviderChain chain = AssetProviderChain.Standard(install: provider);

        Assert.Equal(AssetSource.InstallEntry, chain.Resolve(AssetRef.ForObject("Gold Pile", AssetKind.Mesh)).Source);
        Assert.True(chain.Resolve(AssetRef.ForObject("Barrel", AssetKind.Mesh)).IsPlaceholder);
        Assert.Equal(["install", "placeholder"], chain.Providers.Select(link => link.Name));
    }

    [Fact]
    public void AnAssetAnotherProviderResolvedIsRefused()
    {
        var provider = new InstallArtProvider([], new FakeMeshes(), new FakeTextures());
        Assert.True(PlaceholderProvider.Instance.TryResolve(AssetRef.ForObject("Barrel", AssetKind.Mesh),
            out ResolvedAsset placeholder));

        Assert.Throws<ArgumentException>(() => provider.TryLoadModel(placeholder, out _));
    }

    /// <summary>The shipped install: every wall piece of a room that draws walls loads, with
    /// vertices and a decodable first texture.</summary>
    [InstallFact]
    public void TheInstallAnswersTheLairsWallPiecesWithModelsAndTextures()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        using InstallArtProvider provider = InstallArtProvider.Open(install, database);

        for (int piece = AssetCatalogue.FirstWallPiece; piece <= AssetCatalogue.LastWallPiece; piece++)
        {
            Assert.True(provider.TryResolve(AssetCatalogue.WallPiece("Lair", piece), out ResolvedAsset asset),
                $"Lair wall piece {piece}");
            Assert.True(provider.TryLoadModel(asset, out KmfModel model), asset.InstallEntry);
            Assert.NotEmpty(model.Mesh!.Primitives.SelectMany(primitive => primitive.Vertices));
            RgbaImage image = provider.LoadTexture(model.Materials.SelectMany(material => material.TextureNames).First());
            Assert.True(image.Width > 0 && image.Height > 0);
        }
        Assert.True(provider.Count > 500, $"only {provider.Count} keys resolved");
    }
}
