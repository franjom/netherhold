// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Art;

public enum EffectSpriteBlend { Opaque, Alpha, Additive }

public sealed record EffectSpriteResource(int WidthRaw, int HeightRaw, int DeclaredFrameCount,
    int StoredFrameCount, EffectSpriteBlend Blend, uint ArtFlags);

public interface IEffectSpriteProvider : IAssetProvider
{
    bool TryGetSprite(in ResolvedAsset asset, out EffectSpriteResource sprite);
    bool TryLoadFrame(in ResolvedAsset asset, int frame, out RgbaImage image);
}

/// <summary>Original effect sprites behind logical keys; texture filenames stay in this provider.</summary>
public sealed class InstallEffectSpriteProvider : IEffectSpriteProvider
{
    private readonly Dictionary<AssetRef, EffectElementDef> _elements = new();
    private readonly Dictionary<AssetRef, LoadedSprite?> _loaded = new();
    private readonly Func<string, RgbaImage> _loadTexture;

    public InstallEffectSpriteProvider(GameDatabase database, Func<string, RgbaImage> loadTexture)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        _loadTexture = loadTexture ?? throw new ArgumentNullException(nameof(loadTexture));
        foreach (var group in database.EffectElements.Values
                     .Where(element => element.ArtResource.Type is >= 1 and <= 3
                         && !string.IsNullOrWhiteSpace(element.ArtResource.Name))
                     .GroupBy(element => AssetRef.ForEffectElement(element.Name)))
            if (group.Count() == 1) _elements.Add(group.Key, group.Single());
    }

    public string Name => "install_effect_sprites";

    public bool TryResolve(AssetRef reference, out ResolvedAsset asset)
    {
        if (reference.IsEmpty || Load(reference) is null)
        {
            asset = default;
            return false;
        }
        asset = new ResolvedAsset(reference, AssetSource.InstallEntry, null, default, Name,
            _elements[reference].ArtResource.Name);
        return true;
    }

    public bool TryGetSprite(in ResolvedAsset asset, out EffectSpriteResource sprite)
    {
        LoadedSprite? loaded = Owns(asset) ? Load(asset.Reference) : null;
        sprite = loaded?.Resource!;
        return loaded is not null;
    }

    public bool TryLoadFrame(in ResolvedAsset asset, int frame, out RgbaImage image)
    {
        LoadedSprite? loaded = Owns(asset) ? Load(asset.Reference) : null;
        if (loaded is null || frame < 0 || frame >= loaded.Frames.Count)
        {
            image = null!;
            return false;
        }
        image = loaded.Frames[frame];
        return true;
    }

    private bool Owns(in ResolvedAsset asset) => asset.Source == AssetSource.InstallEntry
        && asset.ProviderName == Name && !asset.Reference.IsEmpty
        && _elements.TryGetValue(asset.Reference, out var element)
        && asset.InstallEntry == element.ArtResource.Name;

    private LoadedSprite? Load(AssetRef reference)
    {
        if (_loaded.TryGetValue(reference, out LoadedSprite? cached)) return cached;
        if (!_elements.TryGetValue(reference, out var element)) return null;
        KwdArtResource art = element.ArtResource;
        ReadOnlySpan<byte> values = art.Unknown_0044.Span;
        if (values.Length != 12) throw new InvalidDataException("A sprite descriptor requires twelve value bytes.");
        int declaredFrames = (art.Flags & 4) != 0 ? Math.Max(1, (int)values[8]) : 1;
        bool animated = (art.Flags & 4) != 0;
        var frames = new List<RgbaImage>();
        for (int frame = 0; frame < declaredFrames; frame++)
        {
            try { frames.Add(_loadTexture(animated ? art.Name + frame : art.Name)); }
            catch (FileNotFoundException) { break; }
        }
        // Descriptor duration and material image count are separate (room-construction-effects.md section 4).
        var loaded = frames.Count == 0 ? null : new LoadedSprite(new EffectSpriteResource(
            BinaryPrimitives.ReadInt32LittleEndian(values),
            BinaryPrimitives.ReadInt32LittleEndian(values.Slice(4)), declaredFrames, frames.Count,
            art.Type == 3 ? EffectSpriteBlend.Additive : art.Type == 2 ? EffectSpriteBlend.Alpha : EffectSpriteBlend.Opaque,
            art.Flags), frames);
        _loaded.Add(reference, loaded);
        return loaded;
    }

    private sealed record LoadedSprite(EffectSpriteResource Resource, IReadOnlyList<RgbaImage> Frames);
}
