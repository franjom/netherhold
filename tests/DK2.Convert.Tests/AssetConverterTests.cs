// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Art;
using DK2.Core;
using DK2.Data.Model;

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
        Dk2ArtCatalogue art = Art(objects: new() { [1] = [new("Gold Mesh", 0)], [2] = [new("Lantern", 0)] });
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
        Dk2ArtCatalogue art = Art(objects: new() { [2] = [new("Lantern", 0)] });

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
        Dk2ArtCatalogue art = Art(objects: new() { [1] = [new("Gold Mesh", 0)] });

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
        Dk2ArtCatalogue art = Art(terrains: new() { [1] = [new("Floor", 0)] });
        GameDatabase database = Database(terrain: [Terrain(1, "Lava", damage: 4), Terrain(2, "Rock", solid: true)]);

        IReadOnlyList<AssetConversionResult> results =
            AssetConverter.ConvertAll(art, meshes, new FakeTextures("creature0"), database, _root);

        Assert.Equal(["terrain.lava.texture", "terrain.lava.effect", "terrain.rock.mesh"],
            results.Select(result => result.Reference.Key));
        Assert.Equal("terrain complete mesh's first material texture 'creature0' from Floor1.kmf",
            results[0].Detail);
        Assert.True(File.Exists(Path.Combine(_root, "terrain", "lava", "texture.png")));
        Assert.Equal("effect conversion is UNKNOWN; no effect format is emitted", results[1].Detail);
        Assert.Equal("KWD has no terrain art record for id 2", results[2].Detail);
    }

    private static Dk2ArtCatalogue Art(
        Dictionary<byte, IReadOnlyList<Dk2ArtResource>>? terrains = null,
        Dictionary<byte, IReadOnlyList<Dk2ArtResource>>? objects = null) => new()
    {
        Terrains = terrains ?? new(),
        Rooms = new Dictionary<byte, IReadOnlyList<Dk2ArtResource>>(),
        Creatures = new Dictionary<byte, IReadOnlyList<Dk2ArtResource>>(),
        Objects = objects ?? new(),
    };

    private static GameDatabase Database(TerrainDef[]? terrain = null, ObjectDef[]? objects = null) =>
        new(terrain ?? [], [], [], new GlobalVariables(), objects: objects);

    private static ObjectDef Object(byte id, string name) => new()
    {
        Name = name, ObjectId = id, Flags = default, Hp = 0, ManaValue = 0, RoomCapacity = 0,
        StartState = 0, PickUpPriority = 0, Width = new Fp32(0), Height = new Fp32(0),
        Mass = new Fp32(0), Speed = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    private static TerrainDef Terrain(byte id, string name, short damage = 0, bool solid = false) => new()
    {
        Name = name, TerrainId = id, Flags = solid ? TerrainFlags.Solid : default, StartingHealth = 0,
        MaxHealth = 0, GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = damage,
        MaxHealthTypeTerrainId = 0, DestroyedTypeTerrainId = 0, Depth = new Fp32(0),
        NameStringId = 0, TooltipStringId = 0,
    };

    private sealed class FakeMeshes(params (string Name, byte[] Bytes)[] entries) : IMeshSource
    {
        public IReadOnlyList<string> EntryNames { get; } = entries.Select(entry => entry.Name).ToArray();

        public bool TryFind(string name, out int index)
        {
            index = Array.FindIndex(entries, entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0;
        }

        public byte[] Read(int index) => entries[index].Bytes;
    }

    private sealed class FakeTextures(params string[] names) : ITextureSource
    {
        public List<string> Reads { get; } = [];

        public string Resolve(string textureName) =>
            names.FirstOrDefault(name => name.Equals(textureName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Engine texture '{textureName}' was not found.");

        public RgbaImage Read(string storedName)
        {
            Reads.Add(storedName);
            return new RgbaImage(1, 1, [255, 0, 0, 255]);
        }
    }
}
