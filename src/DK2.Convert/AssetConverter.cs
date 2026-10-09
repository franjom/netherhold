// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Security.Cryptography;
using System.Text;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Convert.Gltf;
using DK2.Convert.Textures;
using DK2.Data.Model;

namespace DK2.Convert;

public sealed record AssetConversionResult(
    AssetRef Reference, bool Produced, string? OutputPath, string Detail);

/// <summary>Converts exactly the logical keys enumerated by AssetCatalogue.</summary>
public static class AssetConverter
{
    public static IReadOnlyList<AssetConversionResult> ConvertAll(
        string installDirectory, GameDatabase database, string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        Directory.CreateDirectory(Path.GetFullPath(outputRoot));
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(installDirectory);
        using WadMeshSource meshes = WadMeshSource.OpenInstall(installDirectory);
        using EngineTextureArchive textures = EngineTextureArchive.OpenInstall(installDirectory);
        return ConvertAll(art, meshes, textures, database, outputRoot);
    }

    /// <summary>Converts from the given sources; the caller owns and disposes them.</summary>
    public static IReadOnlyList<AssetConversionResult> ConvertAll(
        Dk2ArtCatalogue art, IMeshSource meshes, ITextureSource textures,
        GameDatabase database, string outputRoot)
    {
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        string root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        var run = new Run(root, meshes, textures);
        foreach (ArtEntry entry in ArtSourceMap.ForDatabase(database, art))
        {
            if (!entry.HasSource) run.Missing(entry.Reference, entry.MissingReason!);
            else if (entry.Use == ArtUse.Model) run.ConvertKmf(entry.Reference, entry.Source!, entry.Detail);
            else run.ConvertTexture(entry.Reference, entry.Source!, entry.Detail);
        }
        return run.Results;
    }

    private static IEnumerable<string> MaterialTextures(KmfMaterial material) =>
        material.TextureNames.Concat(string.IsNullOrEmpty(material.EnvironmentTextureName)
            ? [] : [material.EnvironmentTextureName]);

    /// <summary>One conversion pass: its sources, output root and accumulated results.</summary>
    private sealed class Run
    {
        private readonly string _root;
        private readonly IMeshSource _meshes;
        private readonly ITextureSource _textures;
        private readonly HashSet<string> _writtenTextures = new(StringComparer.OrdinalIgnoreCase);

        public Run(string root, IMeshSource meshes, ITextureSource textures)
        {
            _root = root;
            _meshes = meshes;
            _textures = textures;
        }

        public List<AssetConversionResult> Results { get; } = [];

        public void ConvertKmf(AssetRef reference, Dk2ArtResource source, string detail)
        {
            if (source.IsEmpty) { Missing(reference, $"KWD has no {detail} resource"); return; }
            if (!_meshes.TryFind(source.Name, allowCollectionMember: false, out int index, out string entryName))
            { Missing(reference, $"Meshes.WAD has no '{entryName}'"); return; }
            try
            {
                KmfModel model = KmfReader.Read(_meshes.Read(index));
                if (model.Mesh is null)
                {
                    Missing(reference, $"'{entryName}' is a KMF group; group placement semantics are UNKNOWN");
                    return;
                }
                string path = OutputPath(reference);
                byte[] glb = GlbWriter.Write(model, textureName => TextureUri(path, textureName),
                    PassSeconds(source));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, glb);
                foreach (string textureName in model.Materials.SelectMany(MaterialTextures).Distinct(
                    StringComparer.OrdinalIgnoreCase))
                    WriteDependency(textureName);
                Results.Add(new AssetConversionResult(reference, true, path, $"{detail} from {entryName}"));
            }
            catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException
                or NotSupportedException)
            {
                Missing(reference, $"{entryName}: {exception.Message}");
            }
        }

        /// <summary>A type-6 clip's pass by the type-6 clock (<c>0x004B41E0</c>): the descriptor's
        /// frame count over its FPS, which is not always the KMF's own count (300 of 906 creature
        /// clips differ, mostly by one); 0 for a resource with no rate.</summary>
        private static float PassSeconds(Dk2ArtResource source) =>
            source.FramesPerSecond > 0 && source.FrameCount > 0
                ? source.FrameCount / (float)source.FramesPerSecond
                : 0f;

        public void ConvertTexture(AssetRef reference, Dk2ArtResource source, string detail)
        {
            if (source.IsEmpty) { Missing(reference, $"KWD has no {detail} resource"); return; }
            if (!_meshes.TryFind(source.Name, allowCollectionMember: true, out int index, out string entryName))
            { Missing(reference, $"Meshes.WAD has no '{entryName}'"); return; }
            try
            {
                KmfModel model = KmfReader.Read(_meshes.Read(index));
                string? textureName = model.Materials.SelectMany(MaterialTextures).FirstOrDefault();
                if (textureName is null)
                {
                    Missing(reference, model.Kind == KmfKind.Group
                        ? $"'{entryName}' is a KMF group; group materials are UNKNOWN"
                        : $"'{entryName}' has no named material texture");
                    return;
                }
                string path = OutputPath(reference);
                TextureExtractor.WritePng(_textures.Read(_textures.Resolve(textureName)), path);
                Results.Add(new AssetConversionResult(reference, true, path,
                    $"{detail} '{textureName}' from {entryName}"));
            }
            catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException)
            {
                Missing(reference, $"{entryName}: {exception.Message}");
            }
        }

        private string OutputPath(AssetRef reference)
        {
            string extension = FileAssetProvider.ExtensionsFor(reference.Kind)[0];
            return Path.Combine(_root, FileAssetProvider.RelativePathWithoutExtension(reference) + extension);
        }

        private string TextureUri(string glbPath, string textureName)
        {
            string dependency = TexturePath(_textures.Resolve(textureName));
            string relative = Path.GetRelativePath(Path.GetDirectoryName(glbPath)!, dependency)
                .Replace(Path.DirectorySeparatorChar, '/');
            return string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
        }

        private void WriteDependency(string textureName)
        {
            string storedName = _textures.Resolve(textureName);
            string path = TexturePath(storedName);
            if (_writtenTextures.Contains(storedName)) return;
            TextureExtractor.WritePng(_textures.Read(storedName), path);
            _writtenTextures.Add(storedName);
        }

        private string TexturePath(string storedName)
        {
            string readable = new(storedName.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\'
                    ? '_' : character).ToArray());
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(storedName.ToUpperInvariant()));
            return Path.Combine(_root, "_textures", $"{readable}-{System.Convert.ToHexString(hash, 0, 4)}.png");
        }

        public void Missing(AssetRef reference, string reason)
        {
            // A repeat conversion must not leave a formerly-produced key
            // resolvable after its current source becomes unavailable.
            string stalePath = OutputPath(reference);
            if (File.Exists(stalePath)) File.Delete(stalePath);
            Results.Add(new AssetConversionResult(reference, false, null, reason));
        }
    }
}
