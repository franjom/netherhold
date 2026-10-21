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
    private const int CreatureTortured = 16;
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
        AddStructures(entries, database, art);
        foreach (EffectElementDef element in AssetCatalogue.MeshEffectElements(database))
            entries.Add(Model(AssetRef.ForEffectElement(element.Name),
                new Dk2ArtResource(element.ArtResource.Name, element.ArtResource.Type), "effect element mesh"));

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
            AddTerrainPieces(entries, terrain, resources);
        }
    }

    /// <summary>The terrain loader's arts (<c>0x004F66E0</c>): art 0 the whole floor, art 1 the
    /// face, art 2 the top, art 3 the tagged top; the player quarters are named per set
    /// (floor-and-terrain-pieces.md §5).</summary>
    private static void AddTerrainPieces(List<ArtEntry> entries, TerrainDef terrain,
        IReadOnlyList<Dk2ArtResource>? resources)
    {
        bool wall = (terrain.Flags & TerrainFlags.PlayerColouredWall) != 0;
        foreach (AssetRef reference in AssetCatalogue.ForTerrainPieces(terrain))
        {
            if (resources is null)
            {
                entries.Add(Missing(reference, NoArtRecord("terrain", terrain.TerrainId)));
                continue;
            }
            string variant = reference.Key.Substring(reference.Key.LastIndexOf('.') + 1);
            if (variant.StartsWith("bed", StringComparison.Ordinal))
            {
                string bed = resources[0].IsEmpty ? "" : resources[0].Name + variant.Substring(3);
                entries.Add(Model(reference, new Dk2ArtResource(bed, 0), "liquid bed piece"));
                continue;
            }
            if (variant.StartsWith("hero", StringComparison.Ordinal))
            {
                entries.Add(Model(reference, new Dk2ArtResource("Herowall_LevelDefaultPart" + variant.Substring(4), 0),
                    "hero wall face"));
                continue;
            }
            if (variant.StartsWith("quarter", StringComparison.Ordinal))
            {
                string name = (wall ? "Claimed Top" : "Claimed Floor") + variant[7] + "_" + variant[8];
                entries.Add(Model(reference, new Dk2ArtResource(name, 0), "claimed quarter"));
                continue;
            }
            int art = variant switch
            {
                AssetCatalogue.TerrainFloor => 0,
                AssetCatalogue.TerrainSide => 1,
                AssetCatalogue.TerrainTop => 2,
                AssetCatalogue.TerrainTopTagged => 3,
                _ => throw new InvalidOperationException($"Unknown terrain piece '{reference.Key}'."),
            };
            entries.Add(Model(reference, resources[art], $"terrain art {art}"));
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
                for (int piece = 0; piece < AssetCatalogue.RoomPieceCount(room); piece++)
                    entries.Add(Missing(AssetCatalogue.RoomPiece(room.Name, piece), reason));
                if (room.TypeId == RoomTypeId.DungeonHeart)
                    for (int piece = 0; piece < 6; piece++)
                        entries.Add(Missing(AssetCatalogue.DestroyedHeartPiece(room.Name, piece), reason));
                continue;
            }
            Dk2ArtResource complete = resources[2];
            entries.Add(Texture(AssetRef.ForRoom(room.Name, AssetKind.Texture), complete,
                "room complete mesh's first material texture"));
            entries.Add(Model(AssetRef.ForRoom(room.Name, AssetKind.Mesh), complete, "room complete mesh"));
            // The loader names each piece art 2's name plus its number (0x004EACC0).
            for (int piece = 0; piece < AssetCatalogue.RoomPieceCount(room); piece++)
                entries.Add(Model(AssetCatalogue.RoomPiece(room.Name, piece),
                    new Dk2ArtResource(complete.IsEmpty ? "" : complete.Name + piece, 0), "room piece"));
            if (room.TypeId == RoomTypeId.DungeonHeart)
                for (int piece = 0; piece < 6; piece++)
                    entries.Add(Model(AssetCatalogue.DestroyedHeartPiece(room.Name, piece),
                        new Dk2ArtResource("Dungeon_Destroyed" + piece, 0), "destroyed heart piece"));
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
                entries.Add(Missing(AssetRef.ForCreature(creature.Name, AssetKind.Mesh, "dead"), reason));
                foreach (string animation in AssetCatalogue.CreatureAnimations)
                    entries.Add(Missing(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation), reason));
                foreach (string animation in AssetCatalogue.CreatureActionAnimations)
                    entries.Add(Missing(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation), reason));
                continue;
            }
            entries.Add(Model(AssetRef.ForCreature(creature.Name, AssetKind.Mesh), resources[CreatureIdle],
                "idle model"));
            Dk2ArtResource dead = art.CreatureCorpses.TryGetValue(creature.CreatureId, out Dk2ArtResource? corpse)
                && corpse.Type != 0 ? corpse : resources[CreatureWalk];
            entries.Add(Model(AssetRef.ForCreature(creature.Name, AssetKind.Mesh, "dead"), dead, "corpse mesh"));
            foreach (string animation in AssetCatalogue.CreatureAnimations)
                entries.Add(Model(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation),
                    animation == "dead" ? dead : CreatureAnimationSource(creature, resources, animation),
                    "creature visual resource"));
            foreach (string animation in AssetCatalogue.CreatureActionAnimations)
                entries.Add(Model(AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation),
                    animation == "movement_idle"
                        ? art.CreatureStandingPoses.TryGetValue(creature.CreatureId, out Dk2ArtResource? standing)
                            && standing.Type != 0 ? standing : resources[CreatureWalk]
                        : CreatureAnimationSource(creature, resources, animation), "action animation KMF"));
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
        foreach (ObjectDef value in database.Objects.Values.OrderBy(value => value.ObjectId))
            foreach (int slot in AssetCatalogue.ObjectArtSlots(value.ObjectId))
            {
                AssetRef reference = AssetCatalogue.ObjectArt(value.Name, slot);
                entries.Add(art.Objects.TryGetValue(value.ObjectId, out IReadOnlyList<Dk2ArtResource>? resources)
                    ? Model(reference, resources[slot], "object art " + slot)
                    : Missing(reference, NoArtRecord("object", value.ObjectId)));
            }
    }

    private static void AddStructures(List<ArtEntry> entries, GameDatabase database, Dk2ArtCatalogue art)
    {
        foreach (DoorDef door in database.Doors.Values.OrderBy(value => value.DoorId))
        {
            foreach (bool open in new[] { false, true })
            {
                AssetRef reference = AssetRef.ForDoor(door.Name, open);
                if (!art.Doors.TryGetValue(door.DoorId, out IReadOnlyList<Dk2ArtResource>? resources))
                    entries.Add(Missing(reference, NoArtRecord("door", door.DoorId)));
                else
                    entries.Add(Model(reference, door.Flags.HasFlag(DoorFlags.Barricade)
                        ? resources[0] : resources[open ? 4 : 5], open ? "door opening" : "door closing"));
            }
            AssetRef lockReference = AssetRef.ForDoorLock(door.Name);
            entries.Add(art.DoorLocks.TryGetValue(door.DoorId, out Dk2ArtResource? lockResource)
                ? Model(lockReference, lockResource, "door lock object")
                : Missing(lockReference, "No lock object art for this door."));
        }
        foreach (TrapDef trap in database.Traps.Values.OrderBy(value => value.TrapId))
        {
            AssetRef reference = AssetRef.ForTrap(trap.Name);
            entries.Add(art.Traps.TryGetValue(trap.TrapId, out IReadOnlyList<Dk2ArtResource>? resources)
                ? Model(reference, resources[0], "trap model") : Missing(reference, NoArtRecord("trap", trap.TrapId)));
        }
    }

    private static Dk2ArtResource CreatureAnimationSource(
        CreatureDef creature, IReadOnlyList<Dk2ArtResource> resources, string animation) => animation switch
    {
        "idle" => resources[CreatureIdle],
        "entrance" => resources[29],
        "fighting" => resources[CreatureMelee],
        "casting" => resources[5].Type != 0 ? resources[5] : resources[CreatureMelee],
        "landing" => resources[23].Type != 0 ? resources[23] : resources[3],
        "stunned" => resources[23].Type != 0 ? resources[23] : resources[3],
        "getting_up" => resources[26].Type != 0 ? resources[26] : resources[0],
        "running" => resources[1].Type != 0 ? resources[1] : resources[CreatureWalk],
        "eating" => resources[12].Type != 0 ? resources[12] : resources[7],
        "claiming" when creature.Name.Equals("Imp", StringComparison.OrdinalIgnoreCase) => resources[12],
        "reinforcing" when creature.Name.Equals("Imp", StringComparison.OrdinalIgnoreCase) => resources[11],
        "claiming" or "reinforcing" => new Dk2ArtResource("", 0),
        "sleeping" when creature.Name.Equals("Imp", StringComparison.OrdinalIgnoreCase)
            => new Dk2ArtResource("", 0), // That shipped slot is Imp_Claim1(Land), not sleep.
        "sleeping" => resources[CreatureSleeping],
        "dying" => resources[CreatureDie],
        "being_tortured" => resources[CreatureTortured],
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
