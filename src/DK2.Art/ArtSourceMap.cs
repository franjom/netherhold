// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Art;

/// <summary>What a key takes from its KMF: the model itself, or its first material texture.</summary>
public enum ArtUse
{
    Model,
    FirstMaterialTexture,
}

/// <summary>
/// Where one catalogue key's original art lives: a KWD art resource and what to take from it,
/// or why there is none. <see cref="Detail"/> says which resource it is, for reports.
/// </summary>
public sealed record ArtEntry(AssetRef Reference, ArtUse Use, Dk2ArtResource? Source, string Detail,
    string? MissingReason = null)
{
    public bool HasSource => MissingReason is null;
}

/// <summary>
/// Every key <see cref="AssetCatalogue.ForDatabase"/> lists, in its order, mapped to the original
/// art that answers it. The converter writes these entries out and the install provider reads
/// them in place (ADR-0408), so the mapping exists once.
/// </summary>
public static class ArtSourceMap
{
    private const int CreatureWalk = 0;
    private const int CreatureMelee = 4;
    private const int CreatureDie = 6;
    private const int CreatureSleeping = 11;
    private const int CreatureIdle = 19;
    private const int CreatureManufacturing = 21;

    public static IReadOnlyList<ArtEntry> ForDatabase(GameDatabase database, Dk2ArtCatalogue art)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (art is null) throw new ArgumentNullException(nameof(art));

        var entries = new List<ArtEntry>();
        AddTerrain(entries, database, art);
        AddRooms(entries, database, art);
        AddTorches(entries, database);
        AddCreatures(entries, database, art);
        AddObjects(entries, database, art);

        IReadOnlyList<AssetRef> expected = AssetCatalogue.ForDatabase(database).ToArray();
        if (entries.Count != expected.Count || !entries.Select(entry => entry.Reference).SequenceEqual(expected))
            throw new InvalidOperationException("Art source order drifted from AssetCatalogue.ForDatabase.");
        return entries;
    }

    private static void AddTerrain(List<ArtEntry> entries, GameDatabase database, Dk2ArtCatalogue art)
    {
        foreach (TerrainDef terrain in database.Terrain.Values.OrderBy(value => value.TerrainId))
        {
            if (!art.Terrains.TryGetValue(terrain.TerrainId, out IReadOnlyList<Dk2ArtResource>? resources))
                entries.Add(Missing(AssetRef.ForTerrain(terrain.Name, terrain.IsSolid ? AssetKind.Mesh : AssetKind.Texture),
                    NoArtRecord("terrain", terrain.TerrainId)));
            else if (terrain.IsSolid)
                entries.Add(Model(AssetRef.ForTerrain(terrain.Name, AssetKind.Mesh),
                    First(resources[1], resources[0]), "terrain side/complete mesh"));
            else
                entries.Add(Texture(AssetRef.ForTerrain(terrain.Name, AssetKind.Texture),
                    resources[0], "terrain complete mesh's first material texture"));
            if (terrain.Damage != 0)
                entries.Add(Missing(AssetRef.ForTerrain(terrain.Name, AssetKind.Effect),
                    "effect conversion is UNKNOWN; no effect format is emitted"));
        }
    }

    private static void AddRooms(List<ArtEntry> entries, GameDatabase database, Dk2ArtCatalogue art)
    {
        foreach (RoomDef room in database.Rooms.Values.OrderBy(value => value.RoomId))
        {
            if (!art.Rooms.TryGetValue(room.RoomId, out IReadOnlyList<Dk2ArtResource>? resources))
            {
                string reason = NoArtRecord("room", room.RoomId);
                entries.Add(Missing(AssetRef.ForRoom(room.Name, AssetKind.Texture), reason));
                entries.Add(Missing(AssetRef.ForRoom(room.Name, AssetKind.Mesh), reason));
                if (AssetCatalogue.DrawsRoomWalls(room))
                    for (int piece = AssetCatalogue.FirstWallPiece; piece <= AssetCatalogue.LastWallPiece; piece++)
                        entries.Add(Missing(AssetCatalogue.WallPiece(room.Name, piece), reason));
                continue;
            }
            Dk2ArtResource complete = resources[2];
            entries.Add(Texture(AssetRef.ForRoom(room.Name, AssetKind.Texture), complete,
                "room complete mesh's first material texture"));
            entries.Add(Model(AssetRef.ForRoom(room.Name, AssetKind.Mesh), complete, "room complete mesh"));
            // The loader names each piece art 2's name plus its number (0x004EACC0).
            if (AssetCatalogue.DrawsRoomWalls(room))
                for (int piece = AssetCatalogue.FirstWallPiece; piece <= AssetCatalogue.LastWallPiece; piece++)
                    entries.Add(Model(AssetCatalogue.WallPiece(room.Name, piece),
                        new Dk2ArtResource(complete.IsEmpty ? "" : complete.Name + piece, 0), "room wall piece"));
        }
    }

    /// <summary>One mesh per distinct torch art, in the catalogue's order.</summary>
    private static void AddTorches(List<ArtEntry> entries, GameDatabase database)
    {
        foreach (string name in AssetCatalogue.TorchArt(database))
            entries.Add(Model(AssetCatalogue.TorchMesh(name), new Dk2ArtResource(name, 0), "torch mesh"));
    }

    private static void AddCreatures(List<ArtEntry> entries, GameDatabase database, Dk2ArtCatalogue art)
    {
        foreach (CreatureDef creature in database.Creatures.Values.OrderBy(value => value.CreatureId))
        {
            if (!art.Creatures.TryGetValue(creature.CreatureId, out IReadOnlyList<Dk2ArtResource>? resources))
            {
                string reason = NoArtRecord("creature", creature.CreatureId);
                entries.Add(Missing(AssetRef.ForCreature(creature.Name, AssetKind.Mesh), reason));
                foreach (string animation in AssetCatalogue.CreatureAnimations)
                    entries.Add(Missing(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation), reason));
                continue;
            }
            entries.Add(Model(AssetRef.ForCreature(creature.Name, AssetKind.Mesh), resources[CreatureIdle],
                "idle model"));
            foreach (string animation in AssetCatalogue.CreatureAnimations)
                entries.Add(Model(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation),
                    CreatureAnimationSource(creature, resources, animation), "animation KMF"));
        }
    }

    private static void AddObjects(List<ArtEntry> entries, GameDatabase database, Dk2ArtCatalogue art)
    {
        foreach (ObjectDef value in database.Objects.Values.OrderBy(value => value.ObjectId))
        {
            if (!art.Objects.TryGetValue(value.ObjectId, out IReadOnlyList<Dk2ArtResource>? resources))
                entries.Add(Missing(AssetRef.ForObject(value.Name, AssetKind.Mesh), NoArtRecord("object", value.ObjectId)));
            else
                entries.Add(Model(AssetRef.ForObject(value.Name, AssetKind.Mesh), resources[0], "object mesh"));
        }
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

    private static ArtEntry Model(AssetRef reference, Dk2ArtResource source, string detail) =>
        source.IsEmpty
            ? Missing(reference, $"KWD has no {detail} resource")
            : new ArtEntry(reference, ArtUse.Model, source, detail);

    private static ArtEntry Texture(AssetRef reference, Dk2ArtResource source, string detail) =>
        source.IsEmpty
            ? Missing(reference, $"KWD has no {detail} resource")
            : new ArtEntry(reference, ArtUse.FirstMaterialTexture, source, detail);

    private static ArtEntry Missing(AssetRef reference, string reason) =>
        new(reference, reference.Kind == AssetKind.Texture ? ArtUse.FirstMaterialTexture : ArtUse.Model, null,
            "", reason);

    private static string NoArtRecord(string category, byte id) => $"KWD has no {category} art record for id {id}";

    private static Dk2ArtResource First(params Dk2ArtResource[] resources) =>
        resources.FirstOrDefault(resource => !resource.IsEmpty) ?? resources[0];
}
