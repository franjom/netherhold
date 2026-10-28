// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
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
    [InstallFact]
    public void DiggingDebrisResolvesOriginalMeshesAndTexturesThroughEffectKeys()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        using InstallArtProvider provider = InstallArtProvider.Open(install, database);
        foreach (ushort id in new ushort[] { 7, 8, 9, 10, 11 })
        {
            EffectElementDef element = database.EffectElements[id];
            AssetRef key = AssetRef.ForEffectElement(element.Name);
            Assert.Contains(key, AssetCatalogue.ForDatabase(database));
            Assert.True(provider.TryResolve(key, out ResolvedAsset asset), element.Name);
            Assert.Equal(element.ArtResource.Name + ".kmf", asset.InstallEntry, ignoreCase: true);
            Assert.True(provider.TryGetResource(asset, out Dk2ArtResource resource));
            Assert.Equal(5, resource.Type);
            Assert.True(provider.TryLoadModel(asset, out KmfModel model));
            Assert.NotEmpty(RenderMesh.FromModel(model).Positions);
            Assert.Same(model, LoadAgain(provider, asset));
            string[] textures = model.Materials.SelectMany(material => material.TextureNames).ToArray();
            Assert.NotEmpty(textures);
            foreach (string texture in textures)
            {
                RgbaImage image = provider.LoadTexture(texture);
                Assert.True(image.Width > 0 && image.Height > 0);
            }
        }
        Assert.DoesNotContain(AssetRef.ForEffectElement(database.EffectElements[144].Name),
            AssetCatalogue.ForDatabase(database));
    }

    [InstallFact]
    public void EveryMemberOfTheHeartPlugsGroupResolvesByItsEntryAndLoads()
    {
        // Effect 215's group art: the nine pieces the plug breaks into (heart-intro.md section 2).
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        using InstallArtProvider provider = InstallArtProvider.Open(install, database);
        Assert.True(provider.TryLoadGroup("Group_plugbroke", out KmfGroup group));
        Assert.Equal(9, group.Elements.Count);
        foreach (KmfGroupElement member in group.Elements)
        {
            Assert.True(provider.TryResolveEntry(member.Name, out ResolvedAsset asset), member.Name);
            Assert.True(provider.TryLoadModel(asset, out KmfModel model), member.Name);
            Assert.NotEmpty(RenderMesh.FromModel(model).Positions);
        }
        Assert.False(provider.TryResolveEntry("NoSuchPlugPiece", out _));
    }

    private static KmfModel LoadAgain(InstallArtProvider provider, ResolvedAsset asset)
    {
        Assert.True(provider.TryLoadModel(asset, out KmfModel model));
        return model;
    }

    [InstallFact]
    public void AfterAPrefetchTheModelsAndTheirDrawnTexturesLoadWithoutDecoding()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        using InstallArtProvider provider = InstallArtProvider.Open(install, database);
        AssetRef[] keys = AssetCatalogue.ForDatabase(database).Where(key => provider.TryResolve(key, out _)).Take(80).ToArray();

        provider.Prefetch(keys, workers: 4);

        Assert.True(provider.ModelsDecoded >= keys.Length / 2);
        foreach (AssetRef key in keys)
        {
            Assert.True(provider.TryResolve(key, out ResolvedAsset asset));
            if (!provider.TryLoadModel(asset, out KmfModel model)) continue;
            foreach (KmfMaterial material in model.Materials.Where(material => material.TextureNames.Count > 0))
            {
                provider.LoadTexture(material.TextureNames[0]);
                provider.LoadTexture(material.TextureNames[^1]);
            }
        }
        Assert.Equal(TimeSpan.Zero, provider.DecodeTime);
    }

    [InstallFact]
    public void LoadsFromSeveralThreadsAllGetTheOneModelKept()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        using InstallArtProvider provider = InstallArtProvider.Open(install, database);
        ResolvedAsset[] assets = AssetCatalogue.ForDatabase(database)
            .Select(key => provider.TryResolve(key, out ResolvedAsset asset) ? asset : default)
            .Where(asset => asset.InstallEntry is not null).Take(20).ToArray();

        var loaded = new KmfModel?[assets.Length * 8];
        Parallel.For(0, loaded.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => loaded[i] = provider.TryLoadModel(assets[i % assets.Length], out KmfModel model) ? model : null);

        for (int i = 0; i < assets.Length; i++)
            for (int copy = 1; copy < 8; copy++)
                Assert.Same(loaded[i], loaded[i + (copy * assets.Length)]);

        // The locked reads keep each decode whole: the same as one thread's, model and texture.
        using InstallArtProvider serial = InstallArtProvider.Open(install, database);
        string[] textures = loaded.Take(assets.Length).OfType<KmfModel>()
            .SelectMany(model => model.Materials).SelectMany(material => material.TextureNames).Distinct().ToArray();
        var images = new RgbaImage[textures.Length];
        Parallel.For(0, textures.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => images[i] = provider.LoadTexture(textures[i]));
        for (int i = 0; i < assets.Length; i++)
        {
            if (loaded[i] is not KmfModel model) continue;
            Assert.True(serial.TryLoadModel(assets[i], out KmfModel alone));
            Assert.Equal(RenderMesh.FromModel(alone).Positions, RenderMesh.FromModel(model).Positions);
        }
        Assert.NotEmpty(textures);
        for (int i = 0; i < textures.Length; i++)
            Assert.Equal(serial.LoadTexture(textures[i]).Pixels, images[i].Pixels);
    }

    [InstallFact]
    public void TheDungeonHeartResourceContainsMovingVertices()
    {
        string install = Dk2InstallGate.Path;
        var database = GameDatabaseLoader.Load(install);
        using InstallArtProvider provider = InstallArtProvider.Open(install, database);
        AssetRef key = AssetRef.ForObject(database.ObjectById(13)!.Name, AssetKind.Mesh);
        Assert.True(provider.TryResolve(key, out ResolvedAsset asset));
        Assert.True(provider.TryGetResource(asset, out Dk2ArtResource resource));
        Assert.Equal(6, resource.Type);
        Assert.Equal(70, resource.FrameCount);
        Assert.True(provider.TryLoadModel(asset, out KmfModel model));
        var clip = new RenderAnimation(model);
        var first = new Vector3[clip.Mesh.Positions.Count];
        var middle = new Vector3[first.Length];
        clip.Sample(0f, true, first);
        clip.Sample(0.5f, true, middle);
        Assert.False(first.SequenceEqual(middle));
    }

    [Fact]
    public void SeparateSegmentResourcesKeepTheirOwnTopologyAndNames()
    {
        var resource = new Dk2ArtResource("clip", 6)
        {
            AnimationFlags = CreatureAnimationFlags.StartSegment | CreatureAnimationFlags.EndSegment,
            StartSegmentFrames = 2, EndSegmentFrames = 2,
        };
        AssetRef key = AssetRef.ForCreature("Imp", AssetKind.Animation, "idle");
        using var provider = new InstallArtProvider([new ArtEntry(key, ArtUse.Model, resource, "test")],
            new FakeMeshes(("clip.kmf", AnimatedKmfFixture.TwoFrames()),
                ("clipStart.kmf", AnimatedKmfFixture.TwoFrames()),
                ("clipEnd.kmf", AnimatedKmfFixture.TwoFrames())), new FakeTextures());
        Assert.True(provider.TryResolve(key, out var asset));
        Assert.True(provider.TryResolveSegment(asset, AnimationSegment.Start, out var start));
        Assert.Equal("clipStart.kmf", start.InstallEntry);
        Assert.True(provider.TryResolveSegment(asset, AnimationSegment.End, out var end));
        Assert.Equal("clipEnd.kmf", end.InstallEntry);
        Assert.True(provider.TryLoadModel(start, out _));
        Assert.True(provider.TryLoadModel(end, out _));
        Assert.True(provider.TryResolveSegment(asset, AnimationSegment.Main, out var main));
        Assert.Equal(asset.InstallEntry, main.InstallEntry);
    }

    [Fact]
    public void AMissingRequiredSegmentDoesNotResolveAsTheMainResource()
    {
        var resource = new Dk2ArtResource("clip", 6)
        {
            AnimationFlags = CreatureAnimationFlags.StartSegment, StartSegmentFrames = 2,
        };
        AssetRef key = AssetRef.ForCreature("Imp", AssetKind.Animation, "idle");
        using var provider = new InstallArtProvider([new ArtEntry(key, ArtUse.Model, resource, "test")],
            new FakeMeshes(("clip.kmf", AnimatedKmfFixture.TwoFrames())), new FakeTextures());
        Assert.True(provider.TryResolve(key, out var asset));
        Assert.False(provider.TryResolveSegment(asset, AnimationSegment.Start, out _));
        Assert.False(provider.TryResolveSegment(asset, AnimationSegment.End, out _));
    }

    [Fact]
    public void PlaybackUsesTheDescriptorCountInsteadOfTheDecodedMeshCount()
    {
        var resource = new Dk2ArtResource("clip", 6) { FrameCount = 7, FramesPerSecond = 12 };
        AssetRef key = AssetRef.ForCreature("Imp", AssetKind.Animation, "idle");
        using var provider = new InstallArtProvider([new ArtEntry(key, ArtUse.Model, resource, "test")],
            new FakeMeshes(("clip.kmf", AnimatedKmfFixture.TwoFrames())), new FakeTextures());
        Assert.True(provider.TryResolve(key, out var asset));
        Assert.True(provider.TryLoadModel(asset, out var model));
        Assert.Equal(2u, model.Animation!.FrameCount);
        Assert.True(provider.TryGetResource(asset, out var descriptor));
        Assert.Equal((ushort)7, descriptor.FrameCount);
        Assert.Equal((ushort)12, descriptor.FramesPerSecond);
        Assert.False(provider.TryGetResource(default, out _));
    }

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

    /// <summary>
    /// Every plain terrain piece of the shipped terrains loads, and sits where the chooser places it
    /// (RenderMesh's frame): a face on the tile's north edge from height 1 to 2, a top at 2, a whole
    /// floor at 1, and a quarter centred on the origin, a quarter tile each way.
    /// </summary>
    [InstallFact]
    public void EveryPlainTerrainPieceLoadsAndSitsWhereTheChooserPlacesIt()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        using InstallArtProvider provider = InstallArtProvider.Open(install, database);

        string[] terrains = ["Impenetrable Rock", "Rock", "Dirt Path", "Gold", "Gems", "Claimed Path", "Reinforced Wall"];
        int loaded = 0;
        foreach (string name in terrains)
        {
            TerrainDef terrain = database.Terrain.Values.Single(value => value.Name == name);
            foreach (AssetRef key in AssetCatalogue.ForTerrainPieces(terrain))
            {
                Assert.True(provider.TryResolve(key, out ResolvedAsset asset), key.Key);
                Assert.True(provider.TryLoadModel(asset, out KmfModel model), asset.InstallEntry);
                IReadOnlyList<Vector3> positions = RenderMesh.FromModel(model).Positions;
                string variant = key.Key.Substring(key.Key.LastIndexOf('.') + 1);
                // A hero wall's halves (4..6) are a quadrant wide on the quadrant's outer edge; its
                // wholes (7, 8) a tile wide on the tile's edge, like the plain face.
                bool heroHalf = variant is "hero4" or "hero5" or "hero6";
                bool heroWhole = variant is "hero7" or "hero8";
                (float minY, float maxY, float half) = variant switch
                {
                    AssetCatalogue.TerrainSide => (1f, 2f, 0.5f),
                    _ when heroHalf => (1f, 2f, 0.25f),
                    _ when heroWhole => (1f, 2f, 0.5f),
                    AssetCatalogue.TerrainFloor => (1f, 1f, 0.5f),
                    AssetCatalogue.TerrainTop or AssetCatalogue.TerrainTopTagged => (2f, 2f, 0.5f),
                    _ => terrain.IsSolid ? (2f, 2f, 0.25f) : (1f, 1f, 0.25f),
                };
                Assert.All(positions, p =>
                {
                    Assert.InRange(p.Y, minY - 0.001f, maxY + 0.001f);
                    Assert.InRange(p.X, -half - 0.001f, half + 0.001f);
                    if (variant == AssetCatalogue.TerrainSide) Assert.Equal(0.5f, p.Z, 3);
                    // Measured: the halves lie on z 0.25 (Part6's relief reaches 0.313), the wholes
                    // on z 0.5 (Part8's recess goes back to 0.4005), so each meets the tile edge.
                    else if (heroHalf) Assert.InRange(p.Z, 0.249f, 0.314f);
                    else if (heroWhole) Assert.InRange(p.Z, 0.400f, 0.501f);
                    else Assert.InRange(p.Z, -half - 0.001f, half + 0.001f);
                });
                loaded++;
            }
        }
        Assert.True(loaded > 70, $"only {loaded} pieces");
    }

    /// <summary>
    /// The shipped install: every numbered piece the loader builds for a room the classic look
    /// draws (wall-pieces.md §1), and Water's and Lava's bed pieces, load with vertices.
    /// </summary>
    [InstallFact]
    public void EveryRoomPieceAndLiquidPieceLoads()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        using InstallArtProvider provider = InstallArtProvider.Open(install, database);

        AssetRef[] keys =
        [
            .. database.Rooms.Values.SelectMany(room =>
                Enumerable.Range(0, AssetCatalogue.RoomPieceCount(room)).Select(k => AssetCatalogue.RoomPiece(room.Name, k))),
            .. AssetCatalogue.ForTerrainPieces(database.WaterTerrain!),
            .. AssetCatalogue.ForTerrainPieces(database.LavaTerrain!),
        ];
        foreach (AssetRef key in keys)
        {
            Assert.True(provider.TryResolve(key, out ResolvedAsset asset), key.Key);
            Assert.True(provider.TryLoadModel(asset, out KmfModel model), asset.InstallEntry);
            Assert.NotEmpty(RenderMesh.FromModel(model).Positions);
        }
        Assert.Equal(189 + 4 + 7 + 16 + 16, keys.Length);
    }
}
