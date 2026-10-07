// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Data.Model;

namespace DK2.Assets;

/// <summary>
/// Every asset key a game database implies — goal 4's <c>Game Definition → Asset
/// Reference</c> arrow, made enumerable (ADR-0034). It feeds the converter its work
/// list and makes the "runs with no assets" promise (plan §7.2) testable:
/// <c>ProviderTests</c> and <c>AssetCatalogueTests</c> resolve every key against the
/// placeholder.
/// </summary>
/// <remarks>
/// A key appears only when a mechanic or a renderer needs it. DK2's records name far
/// more art (about thirty animations in a creature record alone); enumerating those
/// would list keys nothing requests, making placeholder completeness an invention.
/// </remarks>
public static class AssetCatalogue
{
    /// <summary>
    /// The creature animation variants the project can currently distinguish:
    /// names mirroring <c>DK2.Simulation.CreatureActivity</c> one for one, so no
    /// activity is undrawable and no key is unaskable. Deliberately a copy, not a
    /// reference — this assembly sits beside the simulation, not above it; the
    /// copy cannot drift because <c>AssetCatalogueTests</c> asserts the two agree.
    /// This is not DK2's animation set; it grows as the simulation learns states.
    /// </summary>
    public static readonly string[] CreatureAnimations =
    [
        "idle",
        "moving_to_job",
        "working",
        "delivering_gold",
        "going_to_sleep",
        "sleeping",
        "dying",
        "leaving",
        "fetching_gold",
        "seeking_lair",
        "dead",
        "fetching_body",
        "carrying_body",
        "imprisoned",
        "fetching_object",
        "carrying_object",
        "seeking_food",
        "fetching_chicken",
        "seeking_wages",
        "going_to_job",
        "at_work",
        "fighting",
        "fleeing",
        "withdrawing",
        "raising_alarm",
        "reporting",
        "frozen",
        "stunned",
        "celebrating",
        "going_to_body",
        "being_tortured",
        "rescuing",
        "pit_fighting",
        "heeding_call_to_arms",
        "being_lifted",
        "chicken",
        "jack_in_the_box",
        "unbuilt_handler",
        "reaper_arriving",
        "collecting_winnings",
        "slapped",
    ];

    /// <summary>
    /// Every key the database implies, ordered by record id within each category so
    /// that two runs produce the same list and a converter's output is reproducible.
    /// </summary>
    public static IEnumerable<AssetRef> ForDatabase(GameDatabase database)
    {
        foreach (AssetRef reference in ForTerrain(database)) yield return reference;
        foreach (AssetRef reference in ForRooms(database)) yield return reference;
        foreach (AssetRef reference in ForTorches(database)) yield return reference;
        foreach (AssetRef reference in ForCreatures(database)) yield return reference;
        foreach (AssetRef reference in ForObjects(database)) yield return reference;
    }

    /// <summary>The bridge's own torch art, which a wall torch over a floor with no room carries
    /// (wall-torches.md §4).</summary>
    public const string DefaultTorchArt = "Torch1";

    /// <summary>A wall torch's mesh, keyed by its art name so the rooms that share one share it.</summary>
    public static AssetRef TorchMesh(string artName) =>
        AssetRef.ForRoom("torch", AssetKind.Mesh, artName);

    /// <summary>
    /// One mesh per distinct torch art: the bridge default and every room's whose torch art flag
    /// is set, in ordinal name order; none for a database with no Torch-flagged terrain.
    /// </summary>
    public static IEnumerable<AssetRef> ForTorches(GameDatabase database)
    {
        foreach (string art in TorchArt(database)) yield return TorchMesh(art);
    }

    /// <summary>The torch art names <see cref="ForTorches"/> lists, for a converter to follow.</summary>
    public static IReadOnlyList<string> TorchArt(GameDatabase database)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        bool torchWalls = false;
        foreach (TerrainDef terrain in database.Terrain.Values)
            torchWalls |= (terrain.Flags & TerrainFlags.Torch) != 0;
        if (!torchWalls) return new List<string>();
        names.Add(DefaultTorchArt);
        foreach (RoomDef room in database.Rooms.Values)
            if (room.Torch is { HasArt: true } torch && !string.IsNullOrWhiteSpace(torch.ArtName))
                names.Add(torch.ArtName);
        return new List<string>(names);
    }

    /// <summary>One visual per terrain type; a hazard terrain also gets an effect key.</summary>
    /// <remarks>
    /// <para>Solid terrain gets a mesh, walkable terrain a texture, keyed on the
    /// terrain's <c>IsSolid</c> flag and never its id, so a mod-authored terrain
    /// stays correct. <c>Damage != 0</c> (lava) adds an effect key; nothing draws it
    /// yet, so the placeholder for an effect is nothing rather than a box.</para>
    /// <para>Deviates: one mesh per solid terrain. DK2 draws a terrain as a face per open side
    /// and a top, with a position-hashed variant and, on Reinforced Wall, a damage frame
    /// (docs/research/floor-and-terrain-pieces.md, Q71); those pieces are read but not yet
    /// catalogued. Room walls are (<see cref="ForRooms"/>).</para>
    /// </remarks>
    public static IEnumerable<AssetRef> ForTerrain(GameDatabase database)
    {
        foreach (byte id in Sorted(database.Terrain.Keys))
        {
            TerrainDef terrain = database.Terrain[id];

            yield return terrain.IsSolid
                ? AssetRef.ForTerrain(terrain.Name, AssetKind.Mesh)
                : AssetRef.ForTerrain(terrain.Name, AssetKind.Texture);

            if (terrain.Damage != 0)
                yield return AssetRef.ForTerrain(terrain.Name, AssetKind.Effect);
        }
    }

    /// <summary>
    /// A floor texture and a mesh per room type — both for every room, because a DK2
    /// room is paved ground plus furniture standing on it, and the two have different
    /// lifetimes: selling a room keeps the floor (ADR-0029).
    /// </summary>
    public static IEnumerable<AssetRef> ForRooms(GameDatabase database)
    {
        foreach (byte id in Sorted(database.Rooms.Keys.Select(k => (byte)k)))
        {
            RoomDef room = database.Rooms[(RoomTypeId)id];
            yield return AssetRef.ForRoom(room.Name, AssetKind.Texture);
            yield return AssetRef.ForRoom(room.Name, AssetKind.Mesh);
            if (DrawsRoomWalls(room))
                for (int piece = FirstWallPiece; piece <= LastWallPiece; piece++)
                    yield return WallPiece(room.Name, piece);
        }
    }

    /// <summary>The room wall pieces 4 to 8 (wall-rendering.md, Q71): 4 and 5 the end halves, 6 a
    /// running half, 7 the whole straight wall, 8 its decorated form.</summary>
    public const int FirstWallPiece = 4, LastWallPiece = 8;

    /// <summary>A room's wall piece, built by DK2's loader from the name of the room's art 2 and
    /// the piece number (<c>0x004EACC0</c>).</summary>
    public static AssetRef WallPiece(string roomName, int piece) =>
        AssetRef.ForRoom(roomName, AssetKind.Mesh, "wall" + piece);

    /// <summary>Whether the presentation draws this room's walls: the Normal and DoubleQuad rooms,
    /// whose wall chooser is the general one (<c>0x00444F10</c>).</summary>
    public static bool DrawsRoomWalls(RoomDef room) =>
        room.Construction is TileConstruction.Normal or TileConstruction.DoubleQuad;

    /// <summary>
    /// A mesh and one animation per implemented activity, for every creature the
    /// database defines (46 in the shipped data) and not only the ones the simulation
    /// spawns. The animation variants are what today's mechanics can distinguish —
    /// see <see cref="CreatureAnimations"/>.
    /// </summary>
    public static IEnumerable<AssetRef> ForCreatures(GameDatabase database)
    {
        foreach (byte id in Sorted(database.Creatures.Keys))
        {
            CreatureDef creature = database.Creatures[id];
            yield return AssetRef.ForCreature(creature.Name, AssetKind.Mesh);

            foreach (string animation in CreatureAnimations)
                yield return AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation);
        }
    }

    /// <summary>
    /// One mesh per object definition — a bed, a coop, an egg, a pile of gold, and
    /// the hundred-odd pillars and arches that are the rest of the record. Every
    /// object, not only the four the simulation places (<see cref="ForCreatures"/>).
    /// </summary>
    /// <remarks>
    /// Not implemented: object animations. DK2 gives an object a state machine
    /// (<c>StateId</c>, <c>StateAge</c> — docs/research/objects.md §7) and a
    /// <c>MoveTo</c> block, but nothing here can tell one object state from another
    /// yet, so no variant list is invented.
    /// </remarks>
    public static IEnumerable<AssetRef> ForObjects(GameDatabase database)
    {
        foreach (byte id in Sorted(database.Objects.Keys))
            yield return AssetRef.ForObject(database.Objects[id].Name, AssetKind.Mesh);
    }

    private static List<byte> Sorted(IEnumerable<byte> ids)
    {
        var list = new List<byte>(ids);
        list.Sort();
        return list;
    }
}
