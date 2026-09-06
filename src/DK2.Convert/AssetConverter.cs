// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Security.Cryptography;
using System.Text;
using DK2.Assets;
using DK2.Convert.Gltf;
using DK2.Convert.Kmf;
using DK2.Convert.Textures;
using DK2.Data.Model;
using DK2.Data.Wad;

namespace DK2.Convert;

public sealed record AssetConversionResult(
    AssetRef Reference, bool Produced, string? OutputPath, string Detail);

/// <summary>Converts exactly the logical keys enumerated by AssetCatalogue.</summary>
public static class AssetConverter
{
    private const int CreatureWalk = 0;
    private const int CreatureMelee = 4;
    private const int CreatureDie = 6;
    private const int CreatureSleeping = 11;
    private const int CreatureIdle = 19;
    private const int CreatureManufacturing = 21;

    public static IReadOnlyList<AssetConversionResult> ConvertAll(
        string installDirectory, GameDatabase database, string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        string root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(installDirectory);
        using var meshes = WadArchive.Open(Path.Combine(installDirectory, "Data", "Meshes.WAD"));
        using var textures = EngineTextureArchive.OpenInstall(installDirectory);
        var results = new List<AssetConversionResult>();
        var writtenTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (TerrainDef terrain in database.Terrain.Values.OrderBy(value => value.TerrainId))
        {
            IReadOnlyList<Dk2ArtResource> resources = art.Terrains[terrain.TerrainId];
            if (terrain.IsSolid)
                ConvertKmf(AssetRef.ForTerrain(terrain.Name, AssetKind.Mesh),
                    First(resources[1], resources[0]), "terrain side/complete mesh");
            else
                ConvertTexture(AssetRef.ForTerrain(terrain.Name, AssetKind.Texture),
                    resources[0], "terrain complete mesh's first material texture");
            if (terrain.Damage != 0)
                Missing(AssetRef.ForTerrain(terrain.Name, AssetKind.Effect),
                    "effect conversion is UNKNOWN; no effect format is emitted");
        }

        foreach (RoomDef room in database.Rooms.Values.OrderBy(value => value.RoomId))
        {
            Dk2ArtResource complete = art.Rooms[room.RoomId][2];
            ConvertTexture(AssetRef.ForRoom(room.Name, AssetKind.Texture), complete,
                "room complete mesh's first material texture");
            ConvertKmf(AssetRef.ForRoom(room.Name, AssetKind.Mesh), complete,
                "room complete mesh");
        }

        foreach (CreatureDef creature in database.Creatures.Values.OrderBy(value => value.CreatureId))
        {
            IReadOnlyList<Dk2ArtResource> resources = art.Creatures[creature.CreatureId];
            ConvertKmf(AssetRef.ForCreature(creature.Name, AssetKind.Mesh),
                resources[CreatureIdle], "idle model");
            foreach (string animation in AssetCatalogue.CreatureAnimations)
            {
                Dk2ArtResource source = animation switch
                {
                    "idle" => resources[CreatureIdle],
                    "sleeping" when creature.Name.Equals("Imp", StringComparison.OrdinalIgnoreCase)
                        => new Dk2ArtResource("", 0), // That shipped slot is Imp_Claim1(Land), not sleep.
                    "sleeping" => resources[CreatureSleeping],
                    "dying" => resources[CreatureDie],
                    "working" when creature.Name.Equals("Imp", StringComparison.OrdinalIgnoreCase)
                        => resources[CreatureMelee], // The Imp's slot is named Imp_Dig in shipped data.
                    "working" => resources[CreatureManufacturing],
                    _ => resources[CreatureWalk],
                };
                ConvertKmf(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation),
                    source, "animation KMF");
            }
        }

        foreach (ObjectDef value in database.Objects.Values.OrderBy(value => value.ObjectId))
            ConvertKmf(AssetRef.ForObject(value.Name, AssetKind.Mesh),
                art.Objects[value.ObjectId][0], "object mesh");

        IReadOnlyList<AssetRef> expected = AssetCatalogue.ForDatabase(database).ToArray();
        if (results.Count != expected.Count || !results.Select(result => result.Reference).SequenceEqual(expected))
            throw new InvalidOperationException("Converter result order drifted from AssetCatalogue.ForDatabase.");
        return results;

        void ConvertKmf(AssetRef reference, Dk2ArtResource source, string detail)
        {
            if (source.IsEmpty) { Missing(reference, $"KWD has no {detail} resource"); return; }
            if (!TryKmf(source.Name, allowCollectionMember: false,
                    out WadEntry entry, out string entryName))
            { Missing(reference, $"Meshes.WAD has no '{entryName}'"); return; }
            try
            {
                KmfModel model = KmfReader.Read(meshes.Extract(entry));
                if (model.Mesh is null)
                {
                    Missing(reference, $"'{entry.Name}' is a KMF group; group placement semantics are UNKNOWN");
                    return;
                }
                string path = OutputPath(reference);
                byte[] glb = GlbWriter.Write(model, textureName => TextureUri(path, textureName));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, glb);
                foreach (string textureName in model.Materials.SelectMany(MaterialTextures).Distinct(
                    StringComparer.OrdinalIgnoreCase))
                    WriteDependency(textureName);
                results.Add(new AssetConversionResult(reference, true, path,
                    $"{detail} from {entry.Name}"));
            }
            catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException
                or NotSupportedException)
            {
                Missing(reference, $"{entry.Name}: {exception.Message}");
            }
        }

        void ConvertTexture(AssetRef reference, Dk2ArtResource source, string detail)
        {
            if (source.IsEmpty) { Missing(reference, $"KWD has no {detail} resource"); return; }
            if (!TryKmf(source.Name, allowCollectionMember: true,
                    out WadEntry entry, out string entryName))
            { Missing(reference, $"Meshes.WAD has no '{entryName}'"); return; }
            try
            {
                KmfModel model = KmfReader.Read(meshes.Extract(entry));
                string? textureName = model.Materials.SelectMany(MaterialTextures).FirstOrDefault();
                if (textureName is null)
                {
                    Missing(reference, model.Kind == KmfKind.Group
                        ? $"'{entry.Name}' is a KMF group; group materials are UNKNOWN"
                        : $"'{entry.Name}' has no named material texture");
                    return;
                }
                string path = OutputPath(reference);
                TextureExtractor.WritePng(textures.Decode(textureName), path);
                results.Add(new AssetConversionResult(reference, true, path,
                    $"{detail} '{textureName}' from {entry.Name}"));
            }
            catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException)
            {
                Missing(reference, $"{entry.Name}: {exception.Message}");
            }
        }

        bool TryKmf(string sourceName, bool allowCollectionMember,
            out WadEntry entry, out string entryName)
        {
            entryName = sourceName.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)
                ? sourceName : sourceName + ".kmf";
            if (meshes.TryGetEntry(entryName, out entry!)) return true;
            if (allowCollectionMember)
            {
                entry = meshes.Entries
                    .Where(candidate => candidate.Name.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)
                        && candidate.Name.StartsWith(sourceName, StringComparison.OrdinalIgnoreCase)
                        && candidate.Name.Length > sourceName.Length
                        && char.IsDigit(candidate.Name[sourceName.Length]))
                    .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault()!;
                if (entry is not null) return true;
            }
            entry = null!;
            return false;
        }

        string OutputPath(AssetRef reference)
        {
            string extension = FileAssetProvider.ExtensionsFor(reference.Kind)[0];
            return Path.Combine(root, FileAssetProvider.RelativePathWithoutExtension(reference) + extension);
        }

        string TextureUri(string glbPath, string textureName)
        {
            EngineTextureEntry entry = textures.FindBest(textureName);
            string dependency = TexturePath(entry);
            string relative = Path.GetRelativePath(Path.GetDirectoryName(glbPath)!, dependency)
                .Replace(Path.DirectorySeparatorChar, '/');
            return string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
        }

        void WriteDependency(string textureName)
        {
            EngineTextureEntry entry = textures.FindBest(textureName);
            string path = TexturePath(entry);
            if (writtenTextures.ContainsKey(entry.Name)) return;
            TextureExtractor.WritePng(textures.Decode(entry), path);
            writtenTextures.Add(entry.Name, path);
        }

        string TexturePath(EngineTextureEntry entry)
        {
            string readable = new(entry.Name.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\'
                    ? '_' : character).ToArray());
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(entry.Name.ToUpperInvariant()));
            return Path.Combine(root, "_textures", $"{readable}-{System.Convert.ToHexString(hash, 0, 4)}.png");
        }

        void Missing(AssetRef reference, string reason)
        {
            // A repeat conversion must not leave a formerly-produced key
            // resolvable after its current source becomes unavailable.
            string stalePath = OutputPath(reference);
            if (File.Exists(stalePath)) File.Delete(stalePath);
            results.Add(new AssetConversionResult(reference, false, null, reason));
        }
    }

    private static Dk2ArtResource First(params Dk2ArtResource[] resources) =>
        resources.FirstOrDefault(resource => !resource.IsEmpty) ?? resources[0];

    private static IEnumerable<string> MaterialTextures(KmfMaterial material) =>
        material.TextureNames.Concat(string.IsNullOrEmpty(material.EnvironmentTextureName)
            ? [] : [material.EnvironmentTextureName]);
}
