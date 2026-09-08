// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;

namespace DK2.Assets;

/// <summary>
/// Resolves asset keys to files under one root, answering only for files that exist.
/// The only type that turns a key into a path (plan §4.8: no filename literals outside
/// <c>DK2.Assets</c>), mechanically — <c>creature.imp.mesh</c> →
/// <c>creature/imp/mesh.&lt;ext&gt;</c> — so an art pack's layout follows from the key.
/// </summary>
/// <remarks>
/// Two roles, one class, differing only in root and reported
/// <see cref="AssetSource"/>: the converter's output (<c>build/converted</c>) and a
/// replacement pack; precedence is the chain's order (<see cref="AssetProviderChain"/>).
/// Nothing here reads a DK2 file: converting art is the user's build step (plan §7.1).
/// </remarks>
public sealed class FileAssetProvider : IAssetProvider
{
    private readonly string _root;
    private readonly AssetSource _source;
    private readonly IFileProbe _probe;

    /// <summary>
    /// Existence checking, behind an interface purely so the tests can prove the
    /// path mapping without writing files. Production has one implementation.
    /// </summary>
    public interface IFileProbe
    {
        bool Exists(string path);
    }

    private sealed class RealFileProbe : IFileProbe
    {
        public static readonly RealFileProbe Instance = new();
        public bool Exists(string path) => File.Exists(path);
    }

    public FileAssetProvider(string root, AssetSource source, string? name = null, IFileProbe? probe = null)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("An asset root cannot be blank.", nameof(root));
        if (source == AssetSource.Placeholder)
            throw new ArgumentException(
                "A file provider cannot report Placeholder; that is PlaceholderProvider's answer.",
                nameof(source));

        _root = root;
        _source = source;
        _probe = probe ?? RealFileProbe.Instance;
        Name = name ?? (source == AssetSource.ConvertedFile ? "converted" : "replacement");
    }

    /// <summary>The converter's output directory, as the plan names it (§7.1).</summary>
    public static FileAssetProvider Converted(string root, IFileProbe? probe = null) =>
        new(root, AssetSource.ConvertedFile, "converted", probe);

    /// <summary>A replacement art pack, which takes precedence over converted art.</summary>
    public static FileAssetProvider Replacement(string root, string? name = null, IFileProbe? probe = null) =>
        new(root, AssetSource.ReplacementFile, name ?? "replacement", probe);

    public string Name { get; }

    public bool TryResolve(AssetRef reference, out ResolvedAsset asset)
    {
        asset = default;
        if (reference.IsEmpty) return false;

        foreach (string extension in ExtensionsFor(reference.Kind))
        {
            string path = Path.Combine(_root, RelativePathWithoutExtension(reference) + extension);
            if (!_probe.Exists(path)) continue;

            asset = new ResolvedAsset(reference, _source, path, look: default, providerName: Name);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The key's path form, without an extension: <c>creature.imp.mesh</c> →
    /// <c>creature/imp/mesh</c>. Plain substitution is safe only because a key's
    /// segments are validated to <c>[a-z0-9_]</c> (<see cref="AssetRef"/>): no
    /// segment can be <c>..</c>, absolute, or hold a separator, so no key addresses
    /// anything outside the root. That validation is the traversal guard.
    /// </summary>
    public static string RelativePathWithoutExtension(AssetRef reference) =>
        reference.Key.Replace('.', Path.DirectorySeparatorChar);

    /// <summary>
    /// The extensions tried for a kind, in order of preference. Open formats only:
    /// the converter turns proprietary formats into these, so nothing downstream of
    /// it knows what a KMF is. Order is preference — glTF binary before glTF text,
    /// OGG before WAV.
    /// </summary>
    public static IReadOnlyList<string> ExtensionsFor(AssetKind kind) => kind switch
    {
        AssetKind.Mesh => [".glb", ".gltf", ".obj"],
        AssetKind.Texture => [".png", ".dds", ".tga"],
        AssetKind.Animation => [".glb", ".gltf"],
        AssetKind.Sound => [".ogg", ".wav"],
        AssetKind.Music => [".ogg", ".mp3", ".wav"],
        AssetKind.Video => [".webm", ".mp4"],
        AssetKind.Effect => [".json"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No extensions for this asset kind."),
    };
}
