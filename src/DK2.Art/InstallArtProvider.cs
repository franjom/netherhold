// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Art;

/// <summary>
/// The converted tier read in place (ADR-0408): answers a key whose original KMF is in the
/// player's <c>Meshes.WAD</c>, and loads that model and its textures on request. Nothing it
/// reads is written anywhere. Models and images are decoded once and kept.
/// </summary>
public sealed class InstallArtProvider : IAssetProvider, IDisposable
{
    private readonly IMeshSource _meshes;
    private readonly ITextureSource _textures;
    private readonly IDisposable? _owned;
    private readonly Dictionary<AssetRef, (string Entry, ArtUse Use)> _entries = new();
    private readonly Dictionary<string, KmfModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RgbaImage> _images = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Over the given sources, which the caller owns.</summary>
    public InstallArtProvider(IReadOnlyList<ArtEntry> entries, IMeshSource meshes, ITextureSource textures)
        : this(entries, meshes, textures, owned: null)
    {
    }

    private InstallArtProvider(IReadOnlyList<ArtEntry> entries, IMeshSource meshes, ITextureSource textures,
        IDisposable? owned)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        _owned = owned;
        foreach (ArtEntry entry in entries)
        {
            if (!entry.HasSource) continue;
            bool collection = entry.Use == ArtUse.FirstMaterialTexture;
            if (_meshes.TryFind(entry.Source!.Name, collection, out _, out string entryName))
                _entries[entry.Reference] = (entryName, entry.Use);
        }
    }

    /// <summary>Opens the install's <c>Meshes.WAD</c>, <c>EngineTextures</c> and KWD art records
    /// for the keys <paramref name="database"/> implies.</summary>
    public static InstallArtProvider Open(string installDirectory, GameDatabase database)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
            throw new ArgumentException("An install directory is required.", nameof(installDirectory));
        if (database is null) throw new ArgumentNullException(nameof(database));

        IReadOnlyList<ArtEntry> entries = ArtSourceMap.ForDatabase(database, Dk2ArtCatalogue.Load(installDirectory));
        WadMeshSource meshes = WadMeshSource.OpenInstall(installDirectory);
        try
        {
            EngineTextureArchive textures = EngineTextureArchive.OpenInstall(installDirectory);
            return new InstallArtProvider(entries, meshes, textures, new Owned(meshes, textures));
        }
        catch
        {
            meshes.Dispose();
            throw;
        }
    }

    public string Name => "install";

    /// <summary>How many keys have an entry in the install.</summary>
    public int Count => _entries.Count;

    public bool TryResolve(AssetRef reference, out ResolvedAsset asset)
    {
        if (reference.IsEmpty || !_entries.TryGetValue(reference, out (string Entry, ArtUse Use) found))
        {
            asset = default;
            return false;
        }
        asset = new ResolvedAsset(reference, AssetSource.InstallEntry, filePath: null, look: default,
            providerName: Name, installEntry: found.Entry);
        return true;
    }

    /// <summary>
    /// The model a key resolved to. False for a KMF group, whose placement is UNKNOWN
    /// (<c>src/DK2.Convert/README.md</c>), so the caller draws its placeholder instead.
    /// </summary>
    public bool TryLoadModel(in ResolvedAsset asset, out KmfModel model)
    {
        model = LoadEntry(EntryOf(asset));
        return model.Mesh is not null;
    }

    /// <summary>The image a texture key resolved to: its KMF's first material texture.</summary>
    public bool TryLoadKeyTexture(in ResolvedAsset asset, out RgbaImage image)
    {
        KmfModel model = LoadEntry(EntryOf(asset));
        string? name = model.Materials.SelectMany(material => material.TextureNames.Concat(
            string.IsNullOrEmpty(material.EnvironmentTextureName) ? [] : [material.EnvironmentTextureName]))
            .FirstOrDefault();
        if (name is null)
        {
            image = null!;
            return false;
        }
        image = LoadTexture(name);
        return true;
    }

    /// <summary>A material's texture by the name the KMF gives it.</summary>
    public RgbaImage LoadTexture(string textureName)
    {
        if (string.IsNullOrEmpty(textureName)) throw new ArgumentException("A texture name is required.", nameof(textureName));
        string stored = _textures.Resolve(textureName);
        if (!_images.TryGetValue(stored, out RgbaImage? image))
        {
            image = _textures.Read(stored);
            _images[stored] = image;
        }
        return image;
    }

    public void Dispose() => _owned?.Dispose();

    private string EntryOf(in ResolvedAsset asset)
    {
        if (asset.Source != AssetSource.InstallEntry || asset.InstallEntry is null || asset.ProviderName != Name)
            throw new ArgumentException($"'{asset.Reference}' was not resolved by {Name}.", nameof(asset));
        return asset.InstallEntry;
    }

    private KmfModel LoadEntry(string entryName)
    {
        if (!_models.TryGetValue(entryName, out KmfModel? model))
        {
            if (!_meshes.TryFind(entryName, out int index))
                throw new FileNotFoundException($"Meshes.WAD has no '{entryName}'.");
            model = KmfReader.Read(_meshes.Read(index));
            _models[entryName] = model;
        }
        return model;
    }

    private sealed class Owned : IDisposable
    {
        private readonly IDisposable _meshes;
        private readonly IDisposable _textures;

        public Owned(IDisposable meshes, IDisposable textures)
        {
            _meshes = meshes;
            _textures = textures;
        }

        public void Dispose()
        {
            _textures.Dispose();
            _meshes.Dispose();
        }
    }
}
