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
        run.ConvertTerrain(database, art);
        run.ConvertRooms(database, art);
        run.ConvertTorches(database);
        run.ConvertCreatures(database, art);
        run.ConvertObjects(database, art);

        IReadOnlyList<AssetRef> expected = AssetCatalogue.ForDatabase(database).ToArray();
        if (run.Results.Count != expected.Count
            || !run.Results.Select(result => result.Reference).SequenceEqual(expected))
            throw new InvalidOperationException("Converter result order drifted from AssetCatalogue.ForDatabase.");
        return run.Results;
    }

    private static Dk2ArtResource CreatureAnimationSource(
        CreatureDef creature, IReadOnlyList<Dk2ArtResource> resources, string animation) => animation switch
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

    private static string NoArtRecord(string category, byte id) => $"KWD has no {category} art record for id {id}";

    private static Dk2ArtResource First(params Dk2ArtResource[] resources) =>
        resources.FirstOrDefault(resource => !resource.IsEmpty) ?? resources[0];

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

        public void ConvertTerrain(GameDatabase database, Dk2ArtCatalogue art)
        {
            foreach (TerrainDef terrain in database.Terrain.Values.OrderBy(value => value.TerrainId))
            {
                if (!art.Terrains.TryGetValue(terrain.TerrainId, out IReadOnlyList<Dk2ArtResource>? resources))
                    Missing(AssetRef.ForTerrain(terrain.Name, terrain.IsSolid ? AssetKind.Mesh : AssetKind.Texture),
                        NoArtRecord("terrain", terrain.TerrainId));
                else if (terrain.IsSolid)
                    ConvertKmf(AssetRef.ForTerrain(terrain.Name, AssetKind.Mesh),
                        First(resources[1], resources[0]), "terrain side/complete mesh");
                else
                    ConvertTexture(AssetRef.ForTerrain(terrain.Name, AssetKind.Texture),
                        resources[0], "terrain complete mesh's first material texture");
                if (terrain.Damage != 0)
                    Missing(AssetRef.ForTerrain(terrain.Name, AssetKind.Effect),
                        "effect conversion is UNKNOWN; no effect format is emitted");
            }
        }

        public void ConvertRooms(GameDatabase database, Dk2ArtCatalogue art)
        {
            foreach (RoomDef room in database.Rooms.Values.OrderBy(value => value.RoomId))
            {
                if (!art.Rooms.TryGetValue(room.RoomId, out IReadOnlyList<Dk2ArtResource>? resources))
                {
                    string reason = NoArtRecord("room", room.RoomId);
                    Missing(AssetRef.ForRoom(room.Name, AssetKind.Texture), reason);
                    Missing(AssetRef.ForRoom(room.Name, AssetKind.Mesh), reason);
                    continue;
                }
                Dk2ArtResource complete = resources[2];
                ConvertTexture(AssetRef.ForRoom(room.Name, AssetKind.Texture), complete,
                    "room complete mesh's first material texture");
                ConvertKmf(AssetRef.ForRoom(room.Name, AssetKind.Mesh), complete,
                    "room complete mesh");
            }
        }

        public void ConvertCreatures(GameDatabase database, Dk2ArtCatalogue art)
        {
            foreach (CreatureDef creature in database.Creatures.Values.OrderBy(value => value.CreatureId))
            {
                if (!art.Creatures.TryGetValue(creature.CreatureId, out IReadOnlyList<Dk2ArtResource>? resources))
                {
                    string reason = NoArtRecord("creature", creature.CreatureId);
                    Missing(AssetRef.ForCreature(creature.Name, AssetKind.Mesh), reason);
                    foreach (string animation in AssetCatalogue.CreatureAnimations)
                        Missing(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation), reason);
                    continue;
                }
                ConvertKmf(AssetRef.ForCreature(creature.Name, AssetKind.Mesh),
                    resources[CreatureIdle], "idle model");
                foreach (string animation in AssetCatalogue.CreatureAnimations)
                {
                    Dk2ArtResource source = CreatureAnimationSource(creature, resources, animation);
                    ConvertKmf(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation),
                        source, "animation KMF");
                }
            }
        }

        /// <summary>One mesh per distinct torch art, in the catalogue's order.</summary>
        public void ConvertTorches(GameDatabase database)
        {
            foreach (string name in AssetCatalogue.TorchArt(database))
                ConvertKmf(AssetCatalogue.TorchMesh(name), new Dk2ArtResource(name, 0), "torch mesh");
        }

        public void ConvertObjects(GameDatabase database, Dk2ArtCatalogue art)
        {
            foreach (ObjectDef value in database.Objects.Values.OrderBy(value => value.ObjectId))
            {
                if (!art.Objects.TryGetValue(value.ObjectId, out IReadOnlyList<Dk2ArtResource>? resources))
                    Missing(AssetRef.ForObject(value.Name, AssetKind.Mesh), NoArtRecord("object", value.ObjectId));
                else
                    ConvertKmf(AssetRef.ForObject(value.Name, AssetKind.Mesh), resources[0], "object mesh");
            }
        }

        private void ConvertKmf(AssetRef reference, Dk2ArtResource source, string detail)
        {
            if (source.IsEmpty) { Missing(reference, $"KWD has no {detail} resource"); return; }
            if (!TryKmf(source.Name, allowCollectionMember: false, out int index, out string entryName))
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

        private void ConvertTexture(AssetRef reference, Dk2ArtResource source, string detail)
        {
            if (source.IsEmpty) { Missing(reference, $"KWD has no {detail} resource"); return; }
            if (!TryKmf(source.Name, allowCollectionMember: true, out int index, out string entryName))
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

        /// <summary>
        /// Finds a KMF entry; <paramref name="entryName"/> is the stored name when found,
        /// else the name looked up.
        /// </summary>
        private bool TryKmf(string sourceName, bool allowCollectionMember, out int index, out string entryName)
        {
            entryName = sourceName.EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)
                ? sourceName : sourceName + ".kmf";
            if (_meshes.TryFind(entryName, out index))
            {
                entryName = _meshes.EntryNames[index];
                return true;
            }
            if (allowCollectionMember)
            {
                IReadOnlyList<string> names = _meshes.EntryNames;
                index = Enumerable.Range(0, names.Count)
                    .Where(candidate => names[candidate].EndsWith(".kmf", StringComparison.OrdinalIgnoreCase)
                        && names[candidate].StartsWith(sourceName, StringComparison.OrdinalIgnoreCase)
                        && names[candidate].Length > sourceName.Length
                        && char.IsDigit(names[candidate][sourceName.Length]))
                    .OrderBy(candidate => names[candidate], StringComparer.OrdinalIgnoreCase)
                    .DefaultIfEmpty(-1)
                    .First();
                if (index >= 0)
                {
                    entryName = names[index];
                    return true;
                }
            }
            index = -1;
            return false;
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

        private void Missing(AssetRef reference, string reason)
        {
            // A repeat conversion must not leave a formerly-produced key
            // resolvable after its current source becomes unavailable.
            string stalePath = OutputPath(reference);
            if (File.Exists(stalePath)) File.Delete(stalePath);
            Results.Add(new AssetConversionResult(reference, false, null, reason));
        }
    }
}
