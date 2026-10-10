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
        foreach (byte id in Sorted(database.Doors.Keys))
        {
            yield return AssetRef.ForDoor(database.Doors[id].Name);
            yield return AssetRef.ForDoor(database.Doors[id].Name, open: true);
        }
        foreach (byte id in Sorted(database.Traps.Keys))
            yield return AssetRef.ForTrap(database.Traps[id].Name);
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
    /// <para>Then the pieces DK2 builds a plain terrain from (<c>0x0043E860</c>,
    /// floor-and-terrain-pieces.md §5, Q71; <see cref="ForTerrainPieces"/>), which the classic
    /// look draws. The single key above stays for the placeholder and the converter.</para>
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

            foreach (AssetRef piece in ForTerrainPieces(terrain)) yield return piece;
        }
    }

    /// <summary>The player sets of claimed quarters, "Claimed Floor{set}_{piece}" and
    /// "Claimed Top{set}_{piece}": set = player id − 1, so 0 the heroes and 1 neutral (STRONGLY
    /// INFERRED from their textures, T_Claimed_Good and T_Claimed_Neutral).</summary>
    public const int ClaimedSets = 7, ClaimedPieces = 5;

    /// <summary>
    /// A plain terrain's pieces: a solid one's face (art 1), top (art 2) and tagged top (art 3,
    /// when taggable); an open one's whole floor (art 0); and the player quarters of a
    /// PlayerColouredPath floor or a PlayerColouredWall top. None for a room, whose pieces are its
    /// own. Water and lava have their bed pieces (<see cref="TerrainBed"/>); their surface, art 2, is
    /// an alpha texture rather than a mesh, and its drawer is unread (room-floors-and-liquids.md §3).
    /// </summary>
    public static IEnumerable<AssetRef> ForTerrainPieces(TerrainDef terrain)
    {
        if (terrain is null) throw new ArgumentNullException(nameof(terrain));
        TerrainFlags flags = terrain.Flags;
        if ((flags & TerrainFlags.Room) != 0) yield break;
        if ((flags & TerrainFlags.ConstructionTypeWater) != 0)
        {
            for (int k = 0; k < BedPieces; k++) yield return TerrainBed(terrain.Name, k);
            yield break;
        }

        if (terrain.IsSolid)
        {
            yield return TerrainPiece(terrain.Name, TerrainSide);
            if ((flags & TerrainFlags.PlayerColouredWall) == 0)
            {
                yield return TerrainPiece(terrain.Name, TerrainTop);
                if (terrain.IsTaggable) yield return TerrainPiece(terrain.Name, TerrainTopTagged);
            }
        }
        else if ((flags & TerrainFlags.PlayerColouredPath) == 0)
        {
            yield return TerrainPiece(terrain.Name, TerrainFloor);
        }

        if ((flags & (TerrainFlags.PlayerColouredPath | TerrainFlags.PlayerColouredWall)) != 0)
            for (int set = 0; set < ClaimedSets; set++)
                for (int piece = 0; piece < ClaimedPieces; piece++)
                    yield return TerrainQuarter(terrain.Name, set, piece);
    }

    /// <summary>The variants <see cref="TerrainPiece"/> takes: art 1, 2, 3 and 0.</summary>
    public const string TerrainSide = "side", TerrainTop = "top", TerrainTopTagged = "toptagged",
        TerrainFloor = "floor";

    /// <summary>A plain terrain's whole piece: <see cref="TerrainSide"/>, <see cref="TerrainTop"/>,
    /// <see cref="TerrainTopTagged"/> or <see cref="TerrainFloor"/>.</summary>
    public static AssetRef TerrainPiece(string terrainName, string piece) =>
        AssetRef.ForTerrain(terrainName, AssetKind.Mesh, piece);

    /// <summary>The bed pieces of a water or lava terrain, "&lt;art 0&gt;0..7" (WATER BED0..7, LAVA
    /// BED0..7; the terrain loader <c>0x004F66E0</c>).</summary>
    public const int BedPieces = 8;

    /// <summary>Bed piece <paramref name="k"/> of a water or lava terrain.</summary>
    public static AssetRef TerrainBed(string terrainName, int k) =>
        AssetRef.ForTerrain(terrainName, AssetKind.Mesh, "bed" + k);

    /// <summary>A player-coloured quarter of a terrain: piece 0..4 of player set 0..6.</summary>
    public static AssetRef TerrainQuarter(string terrainName, int set, int piece) =>
        AssetRef.ForTerrain(terrainName, AssetKind.Mesh, $"quarter{set}{piece}");

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
            for (int piece = 0; piece < RoomPieceCount(room); piece++)
                yield return RoomPiece(room.Name, piece);
            if (room.TypeId == RoomTypeId.DungeonHeart)
                for (int piece = 0; piece < 6; piece++)
                    yield return DestroyedHeartPiece(room.Name, piece);
        }
    }

    /// <summary>The room wall pieces 4 to 8 (wall-rendering.md, Q71): 4 and 5 the end halves, 6 a
    /// running half, 7 the whole straight wall, 8 its decorated form.</summary>
    public const int FirstWallPiece = 4, LastWallPiece = 8;

    /// <summary>A room's wall piece: <see cref="RoomPiece"/> 4 to 8.</summary>
    public static AssetRef WallPiece(string roomName, int piece) => RoomPiece(roomName, piece);

    /// <summary>A room's piece k, built by DK2's loader from the name of the room's art 2 and the
    /// number (<c>0x004EACC0</c>, wall-pieces.md §1): floor quarters, wall pieces and centres alike.</summary>
    public static AssetRef RoomPiece(string roomName, int piece) =>
        AssetRef.ForRoom(roomName, AssetKind.Mesh, "piece" + piece);

    public static AssetRef DestroyedHeartPiece(string roomName, int piece) =>
        AssetRef.ForRoom(roomName, AssetKind.Mesh, "destroyedpiece" + piece);

    /// <summary>How many numbered pieces DK2's loader builds for the room's construction, k = 0
    /// up (wall-pieces.md §1): Quad 4, 3×3 9, 3×3 rotated 3, Normal 10, DoubleQuad 15, the Heart 4,
    /// the 2×2 hero gate 4 and the 3×1 hero gate 7. The Final, FrontEnd and Tile gates' pieces
    /// (numbered from 1, or the bare name) are not keyed yet. The destroyed Heart is keyed separately. The unused
    /// Mercenary Gate is a 2×2 gate too, but its art 2 is empty, so none of its pieces exist.</summary>
    public static int RoomPieceCount(RoomDef room) => room.Construction switch
    {
        TileConstruction.QuadConstruction => 4,
        TileConstruction.ThreeByThree => 9,
        TileConstruction.ThreeByThreeRotated => 3,
        TileConstruction.Normal => 10,
        TileConstruction.DoubleQuad => 15,
        TileConstruction.FiveByFiveRotated => 4,
        TileConstruction.HeroGate2By2 when room.TypeId != RoomTypeId.MercenaryGate => 4,
        TileConstruction.HeroGate3By1 => 7,
        _ => 0,
    };

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
    /// Then the other arts a view shows by state (<see cref="ObjectArtSlots"/>).
    /// </summary>
    /// <remarks>
    /// Not implemented: object animations by state (a coop's wobble, an egg's hatch, a
    /// dummy's recoil; object-drawing.md §8).
    /// </remarks>
    public static IEnumerable<AssetRef> ForObjects(GameDatabase database)
    {
        foreach (byte id in Sorted(database.Objects.Keys))
            yield return AssetRef.ForObject(database.Objects[id].Name, AssetKind.Mesh);
        foreach (byte id in Sorted(database.Objects.Keys))
            foreach (int slot in ObjectArtSlots(id))
                yield return ObjectArt(database.Objects[id].Name, slot);
    }

    /// <summary>An object's file art <paramref name="slot"/> (5 to 8, the definition's extra arts;
    /// object-drawing.md §2). Slot 0 is the object's <see cref="AssetKind.Mesh"/>.</summary>
    public static AssetRef ObjectArt(string objectName, int slot) =>
        AssetRef.ForObject(objectName, AssetKind.Mesh, "art" + slot);

    /// <summary>The extra arts a view switches an object to: Gold Pile 1's urns by fill
    /// (<c>0x004F4440</c>) and the Book Case's three variants (<c>0x004EE6A5</c>).</summary>
    public static IReadOnlyList<int> ObjectArtSlots(byte objectId) => objectId switch
    {
        3 => GoldPileSlots,
        64 => BookCaseSlots,
        _ => [],
    };

    private static readonly int[] GoldPileSlots = [5, 6];
    private static readonly int[] BookCaseSlots = [5, 6, 7];

    private static List<byte> Sorted(IEnumerable<byte> ids)
    {
        var list = new List<byte>(ids);
        list.Sort();
        return list;
    }
}
