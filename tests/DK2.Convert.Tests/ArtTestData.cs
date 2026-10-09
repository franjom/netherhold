// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Art.Textures;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Convert.Tests;

/// <summary>In-memory art sources and minimal definitions, shared by the converter's and the
/// install provider's tests.</summary>
internal static class ArtTestData
{
    internal static Dk2ArtCatalogue Catalogue(
        Dictionary<byte, IReadOnlyList<Dk2ArtResource>>? terrains = null,
        Dictionary<byte, IReadOnlyList<Dk2ArtResource>>? objects = null) => new()
    {
        Terrains = terrains ?? new(),
        Rooms = new Dictionary<byte, IReadOnlyList<Dk2ArtResource>>(),
        Creatures = new Dictionary<byte, IReadOnlyList<Dk2ArtResource>>(),
        Objects = objects ?? new(),
    };

    internal static GameDatabase Database(TerrainDef[]? terrain = null, ObjectDef[]? objects = null) =>
        new(terrain ?? [], [], [], new GlobalVariables(), objects: objects);

    internal static ObjectDef Object(byte id, string name) => new()
    {
        Name = name, ObjectId = id, Flags = default, Hp = 0, ManaValue = 0, RoomCapacity = 0,
        StartState = 0, PickUpPriority = 0, Width = new Fp32(0), Height = new Fp32(0),
        Mass = new Fp32(0), Speed = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    internal static TerrainDef Terrain(byte id, string name, short damage = 0, bool solid = false) => new()
    {
        Name = name, TerrainId = id, Flags = solid ? TerrainFlags.Solid : default, StartingHealth = 0,
        MaxHealth = 0, GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = damage,
        MaxHealthTypeTerrainId = 0, DestroyedTypeTerrainId = 0, Depth = new Fp32(0),
        NameStringId = 0, TooltipStringId = 0,
    };

    internal sealed class FakeMeshes(params (string Name, byte[] Bytes)[] entries) : IMeshSource
    {
        public IReadOnlyList<string> EntryNames { get; } = entries.Select(entry => entry.Name).ToArray();

        public bool TryFind(string name, out int index)
        {
            index = Array.FindIndex(entries, entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0;
        }

        public byte[] Read(int index) => entries[index].Bytes;
    }

    internal sealed class FakeTextures(params string[] names) : ITextureSource
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
