// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;

namespace DK2.Assets;

/// <summary>Where a resolved asset actually comes from.</summary>
public enum AssetSource
{
    /// <summary>A file on disk, produced by the converter from the user's own copy.</summary>
    ConvertedFile,

    /// <summary>A file on disk from a replacement art pack.</summary>
    ReplacementFile,

    /// <summary>An entry in the player's own DK2 install, read in place (ADR-0408). The
    /// provider that answered also loads it; <see cref="ResolvedAsset.InstallEntry"/> names
    /// it.</summary>
    InstallEntry,

    /// <summary>Nothing on disk: a coloured box, a tone, a flat colour.</summary>
    Placeholder,
}

/// <summary>
/// What a provider hands back: a description a renderer can act on — a file path, or
/// a placeholder's shape and colour — and never an engine object. This assembly is
/// loaded by Unity and must not depend on it (ADR-0002), so turning the description
/// into a mesh is the renderer's job.
/// </summary>
public readonly struct ResolvedAsset
{
    public ResolvedAsset(AssetRef reference, AssetSource source, string? filePath, PlaceholderLook look,
        string providerName, string? installEntry = null)
    {
        Reference = reference;
        Source = source;
        FilePath = filePath;
        Look = look;
        ProviderName = providerName;
        InstallEntry = installEntry;
    }

    /// <summary>The key that was asked for.</summary>
    public AssetRef Reference { get; }

    /// <summary>Where it came from.</summary>
    public AssetSource Source { get; }

    /// <summary>
    /// The file to load, for a file-backed source; null for a placeholder. The only
    /// asset path in the project: producing one is a provider's job, and no caller
    /// may construct one.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// The entry inside the player's install, for <see cref="AssetSource.InstallEntry"/>; null
    /// otherwise. Not a path: only the provider that answered can open it.
    /// </summary>
    public string? InstallEntry { get; }

    /// <summary>How to draw it when there is no file. Meaningful for placeholders.</summary>
    public PlaceholderLook Look { get; }

    /// <summary>Which provider answered. Diagnostics only; never a switch.</summary>
    public string ProviderName { get; }

    /// <summary>True when a renderer must improvise rather than load a file.</summary>
    public bool IsPlaceholder => Source == AssetSource.Placeholder;

    public override string ToString() => Source switch
    {
        AssetSource.Placeholder => $"{Reference} → placeholder {Look} ({ProviderName})",
        AssetSource.InstallEntry => $"{Reference} → install {InstallEntry} ({ProviderName})",
        _ => $"{Reference} → {FilePath} ({ProviderName})",
    };
}

/// <summary>
/// How to draw an asset nobody supplied: a shape, a size and a colour — enough to
/// make a dungeon legible as boxes. The colour is derived from the asset key, so a
/// terrain is the same colour every run and on every machine
/// (<see cref="PlaceholderProvider"/>).
/// </summary>
public readonly struct PlaceholderLook
{
    public PlaceholderLook(PlaceholderShape shape, byte red, byte green, byte blue, ushort heightPermille)
    {
        Shape = shape;
        Red = red;
        Green = green;
        Blue = blue;
        HeightPermille = heightPermille;
    }

    public PlaceholderShape Shape { get; }
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }

    /// <summary>
    /// Height in thousandths of a tile width — an integer on purpose (ADR-0001);
    /// the renderer converts.
    /// </summary>
    public ushort HeightPermille { get; }

    public override string ToString() =>
        $"{Shape} #{Red:X2}{Green:X2}{Blue:X2} h={HeightPermille}‰";
}

/// <summary>The shapes a placeholder renderer has to be able to draw.</summary>
public enum PlaceholderShape
{
    /// <summary>Nothing is drawn. For assets whose absence is not a hole (music, effects).</summary>
    None,

    /// <summary>A tile-sized cube. Walls, rock, room floors at full height.</summary>
    Block,

    /// <summary>A flat quad on the ground. Floors, water, lava, textures.</summary>
    Slab,

    /// <summary>A small upright box. Creatures and objects.</summary>
    Figure,
}

/// <summary>
/// Goal 4's <c>Asset Provider</c> arrow. One rule: given a key, either answer or say
/// you cannot, and never throw for a key you do not have. Implementations are chained
/// (<see cref="AssetProviderChain"/>), so "cannot" is an ordinary answer and the last
/// link is <see cref="PlaceholderProvider"/>, which always answers.
/// </summary>
public interface IAssetProvider
{
    /// <summary>A short name, for diagnostics and for the chain's reporting.</summary>
    string Name { get; }

    /// <summary>
    /// Resolves a key, or returns false. Must not throw for an unknown key: a missing
    /// asset is normal in a project that has to run with no assets at all.
    /// </summary>
    bool TryResolve(AssetRef reference, out ResolvedAsset asset);
}
