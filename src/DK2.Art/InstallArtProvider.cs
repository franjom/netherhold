// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using System.Diagnostics;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Art;

/// <summary>
/// The converted tier read in place (ADR-0408): answers a key whose original KMF is in the
/// player's <c>Meshes.WAD</c>, and loads that model and its textures on request. Nothing it
/// reads is written anywhere. Models and images are decoded once and kept. Loads are safe from
/// several threads: the caches are locked, decoding is not, and the first model or image kept for
/// a name is the one every caller gets.
/// </summary>
public sealed class InstallArtProvider : IAssetProvider, IDisposable
{
    private readonly IMeshSource _meshes;
    private readonly ITextureSource _textures;
    private readonly IDisposable? _owned;
    private readonly Dictionary<AssetRef, (string Entry, ArtUse Use, Dk2ArtResource Resource)> _entries = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, KmfModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RgbaImage> _images = new(StringComparer.OrdinalIgnoreCase);
    private long _decodeTicks;

    /// <summary>Models decoded and kept so far.</summary>
    public int ModelsDecoded
    {
        get { lock (_gate) return _models.Count; }
    }

    /// <summary>Images decoded and kept so far.</summary>
    public int ImagesDecoded
    {
        get { lock (_gate) return _images.Count; }
    }

    /// <summary>Time spent decoding models and images on request, cache hits and
    /// <see cref="Prefetch"/> excluded: what a caller waited for.</summary>
    public TimeSpan DecodeTime =>
        TimeSpan.FromSeconds(Interlocked.Read(ref _decodeTicks) / (double)Stopwatch.Frequency);

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
                _entries[entry.Reference] = (entryName, entry.Use, entry.Source);
        }
    }

    /// <summary>Opens the install's <c>Meshes.WAD</c>, <c>EngineTextures</c> and KWD art records
    /// for the keys <paramref name="database"/> implies.</summary>
    public static InstallArtProvider Open(string installDirectory, GameDatabase database)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
            throw new ArgumentException("An install directory is required.", nameof(installDirectory));
        if (database is null) throw new ArgumentNullException(nameof(database));

        Dk2ArtCatalogue catalogue = Dk2ArtCatalogue.Load(installDirectory);
        IReadOnlyList<ArtEntry> entries = ArtSourceMap.ForDatabase(database, catalogue);
        WadMeshSource meshes = WadMeshSource.OpenInstall(installDirectory);
        try
        {
            EngineTextureArchive textures = EngineTextureArchive.OpenInstall(installDirectory);
            return new InstallArtProvider(entries, meshes, textures, new Owned(meshes, textures)) { Catalogue = catalogue };
        }
        catch
        {
            meshes.Dispose();
            throw;
        }
    }

    public string Name => "install";

    /// <summary>The install's KWD art records, when <see cref="Open"/> read them; null otherwise.</summary>
    public Dk2ArtCatalogue? Catalogue { get; private init; }

    /// <summary>How many keys have an entry in the install.</summary>
    public int Count => _entries.Count;

    public bool TryResolve(AssetRef reference, out ResolvedAsset asset)
    {
        if (reference.IsEmpty || !_entries.TryGetValue(reference, out var found))
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

    /// <summary>A KMF group's members by the group's entry name (with or without <c>.kmf</c>): the
    /// parts an effect of art type 8 flings. False when there is no such entry or it is not a group.</summary>
    public bool TryLoadGroup(string groupName, out KmfGroup group)
    {
        group = null!;
        if (!_meshes.TryFind(groupName, allowCollectionMember: false, out _, out string entryName)) return false;
        KmfModel model = LoadEntry(entryName);
        if (model.Group is null) return false;
        group = model.Group;
        return true;
    }

    /// <summary>A mesh entry of Meshes.WAD by its own name, as an asset this provider answers, for a
    /// model the catalogue has no key for (a group's member). False when the entry does not exist.</summary>
    public bool TryResolveEntry(string entryName, out ResolvedAsset asset)
    {
        if (!_meshes.TryFind(entryName, allowCollectionMember: false, out _, out string found))
        {
            asset = default;
            return false;
        }
        asset = new ResolvedAsset(AssetRef.Create(AssetRef.MeshEntryCategory,
                Path.GetFileNameWithoutExtension(found), AssetKind.Mesh), AssetSource.InstallEntry,
            filePath: null, look: default, providerName: Name, installEntry: found);
        return true;
    }

    public bool TryGetResource(in ResolvedAsset asset, out Dk2ArtResource resource)
    {
        if (asset.Source == AssetSource.InstallEntry && asset.ProviderName == Name
            && _entries.TryGetValue(asset.Reference, out var entry))
        {
            resource = entry.Resource;
            return true;
        }
        resource = null!;
        return false;
    }

    public bool TryResolveSegment(in ResolvedAsset asset, AnimationSegment segment, out ResolvedAsset resolved)
    {
        resolved = default;
        if (!TryGetResource(asset, out Dk2ArtResource resource)) return false;
        if (segment == AnimationSegment.Main) { resolved = asset; return true; }
        CreatureAnimationFlags flag = segment switch
        {
            AnimationSegment.Start => CreatureAnimationFlags.StartSegment,
            AnimationSegment.End => CreatureAnimationFlags.EndSegment,
            _ => throw new ArgumentOutOfRangeException(nameof(segment)),
        };
        if ((resource.AnimationFlags & flag) == 0) return false;
        string name = resource.Name + (segment == AnimationSegment.Start ? "Start" : "End");
        if (!_meshes.TryFind(name, false, out _, out string entry)) return false;
        resolved = new ResolvedAsset(asset.Reference, AssetSource.InstallEntry, filePath: null,
            look: default, providerName: Name, installEntry: entry);
        return true;
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
    public RgbaImage LoadTexture(string textureName) => LoadTexture(textureName, counted: true);

    /// <summary>
    /// Decodes ahead what <paramref name="keys"/> will load, on up to <paramref name="workers"/>
    /// threads: each key's model, its animation's start and end segments, then the first and last
    /// texture of every material (the two a material is drawn with) or its environment texture.
    /// A key this provider does not answer is skipped, as is anything that fails to decode: a later
    /// load reports that as it always has. Safe alongside other loads.
    /// </summary>
    public void Prefetch(IEnumerable<AssetRef> keys, int workers)
    {
        if (keys is null) throw new ArgumentNullException(nameof(keys));
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetRef key in keys)
        {
            if (!TryResolve(key, out ResolvedAsset asset)) continue;
            entries.Add(asset.InstallEntry!);
            if (TryResolveSegment(asset, AnimationSegment.Start, out ResolvedAsset start)) entries.Add(start.InstallEntry!);
            if (TryResolveSegment(asset, AnimationSegment.End, out ResolvedAsset end)) entries.Add(end.InstallEntry!);
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, workers) };
        var textures = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(entries, options, entry =>
        {
            if (!TryPrefetch(() => LoadEntry(entry, counted: false), out KmfModel model)) return;
            foreach (KmfMaterial material in model.Materials)
            {
                if (material.TextureNames.Count > 0)
                {
                    textures.TryAdd(material.TextureNames[0], true);
                    textures.TryAdd(material.TextureNames[material.TextureNames.Count - 1], true);
                }
                else if (!string.IsNullOrEmpty(material.EnvironmentTextureName))
                    textures.TryAdd(material.EnvironmentTextureName, true);
            }
        });
        Parallel.ForEach(textures.Keys, options, name => TryPrefetch(() => LoadTexture(name, counted: false), out _));
    }

    // A failure here is left for the load that needs the art, which reports it as it always has.
    private static bool TryPrefetch<T>(Func<T> load, out T value)
    {
        try
        {
            value = load();
            return true;
        }
        catch (Exception)
        {
            value = default!;
            return false;
        }
    }

    private RgbaImage LoadTexture(string textureName, bool counted)
    {
        if (string.IsNullOrEmpty(textureName)) throw new ArgumentException("A texture name is required.", nameof(textureName));
        string stored = _textures.Resolve(textureName);
        lock (_gate)
            if (_images.TryGetValue(stored, out RgbaImage? kept)) return kept;
        long start = Stopwatch.GetTimestamp();
        RgbaImage image = _textures.Read(stored);
        if (counted) Interlocked.Add(ref _decodeTicks, Stopwatch.GetTimestamp() - start);
        return Keep(_images, stored, image);
    }

    private T Keep<T>(Dictionary<string, T> cache, string name, T value)
    {
        lock (_gate)
        {
            if (cache.TryGetValue(name, out T? kept)) return kept;
            cache[name] = value;
            return value;
        }
    }

    public void Dispose() => _owned?.Dispose();

    private string EntryOf(in ResolvedAsset asset)
    {
        if (asset.Source != AssetSource.InstallEntry || asset.InstallEntry is null || asset.ProviderName != Name)
            throw new ArgumentException($"'{asset.Reference}' was not resolved by {Name}.", nameof(asset));
        return asset.InstallEntry;
    }

    private KmfModel LoadEntry(string entryName, bool counted = true)
    {
        lock (_gate)
            if (_models.TryGetValue(entryName, out KmfModel? kept)) return kept;
        if (!_meshes.TryFind(entryName, out int index))
            throw new FileNotFoundException($"Meshes.WAD has no '{entryName}'.");
        long start = Stopwatch.GetTimestamp();
        KmfModel model = KmfReader.Read(_meshes.Read(index));
        if (counted) Interlocked.Add(ref _decodeTicks, Stopwatch.GetTimestamp() - start);
        return Keep(_models, entryName, model);
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
