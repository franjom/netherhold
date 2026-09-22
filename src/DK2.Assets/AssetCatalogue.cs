// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
    /// The creature animation variants the project can currently distinguish: 21
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
    ];

    /// <summary>
    /// Every key the database implies, ordered by record id within each category so
    /// that two runs produce the same list and a converter's output is reproducible.
    /// </summary>
    public static IEnumerable<AssetRef> ForDatabase(GameDatabase database)
    {
        foreach (AssetRef reference in ForTerrain(database)) yield return reference;
        foreach (AssetRef reference in ForRooms(database)) yield return reference;
        foreach (AssetRef reference in ForCreatures(database)) yield return reference;
        foreach (AssetRef reference in ForObjects(database)) yield return reference;
    }

    /// <summary>One visual per terrain type; a hazard terrain also gets an effect key.</summary>
    /// <remarks>
    /// <para>Solid terrain gets a mesh, walkable terrain a texture, keyed on the
    /// terrain's <c>IsSolid</c> flag and never its id, so a mod-authored terrain
    /// stays correct. <c>Damage != 0</c> (lava) adds an effect key; nothing draws it
    /// yet, so the placeholder for an effect is nothing rather than a box.</para>
    /// <para>Deviates: one mesh per solid terrain. DK2 builds a wall from a set of
    /// pieces chosen by the room behind it and textures walls per damage state —
    /// CONFIRMED (docs/research/wall-rendering.md). <see cref="AssetRef"/> already
    /// carries a variant segment, but which pieces exist is open (Q71), so no
    /// variant list is invented here.</para>
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
        foreach (byte id in Sorted(database.Rooms.Keys))
        {
            RoomDef room = database.Rooms[id];
            yield return AssetRef.ForRoom(room.Name, AssetKind.Texture);
            yield return AssetRef.ForRoom(room.Name, AssetKind.Mesh);
        }
    }

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
