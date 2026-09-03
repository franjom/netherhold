using System;
using System.Collections.Generic;
using System.IO;

namespace DK2.Assets;

/// <summary>
/// Resolves asset keys to files under one root directory, and answers only for
/// files that are actually there.
///
/// <para><b>This is the only type in the project that turns a key into a
/// path</b> (plan §4.8: "no filename literals outside <c>DK2.Assets</c>"). The
/// mapping is mechanical — <c>creature.imp.mesh</c> becomes
/// <c>creature/imp/mesh.&lt;ext&gt;</c> — so an art pack's layout is knowable
/// from the key alone, with no manifest to write and none to fall out of
/// date.</para>
///
/// <para>Two roles, one class, distinguished only by the root and the
/// <see cref="AssetSource"/> it reports: the converter's output
/// (<c>build/converted</c>, built by the user from their own copy) and a
/// replacement art pack. They behave identically, so giving them separate
/// classes would have been ceremony — the chain's order is what expresses the
/// precedence (see <see cref="AssetProviderChain"/>).</para>
///
/// <para><b>Nothing here reads a DK2 file.</b> Converting original art is a
/// build step the user runs against their own install (plan §7.1); this provider
/// only sees what came out of it. That is what keeps the promise that no
/// converted asset is ever committed or distributed — this code could not
/// distribute one, because it does not know how to make one.</para>
/// </summary>
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

    /// <summary>The root this provider looks under. Diagnostics and tests.</summary>
    public string Root => _root;

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
    /// The key's path form, without an extension:
    /// <c>creature.imp.mesh</c> → <c>creature/imp/mesh</c>.
    ///
    /// <para>A key's segments are already validated to <c>[a-z0-9_]</c>
    /// (<see cref="AssetRef"/>), which is what makes this safe to do by
    /// substitution: no segment can be <c>..</c>, absolute, or contain a
    /// separator, so a key cannot address anything outside the root. The
    /// validation in <c>AssetRef</c> is therefore not tidiness — it is what
    /// stops a key being a path traversal.</para>
    /// </summary>
    public static string RelativePathWithoutExtension(AssetRef reference) =>
        reference.Key.Replace('.', Path.DirectorySeparatorChar);

    /// <summary>
    /// The extensions tried for a kind, in order of preference.
    ///
    /// <para>Open formats only, and that is the point: the converter's job is to
    /// turn proprietary formats into these, so nothing downstream of it knows
    /// what a KMF is. Order is preference — glTF binary before glTF text
    /// because it loads faster, OGG before WAV because it is smaller.</para>
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
