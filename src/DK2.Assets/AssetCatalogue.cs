// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Data.Model;

namespace DK2.Assets;

/// <summary>
/// Every asset key a game database implies: the
/// <c>Game Definition → Asset Reference</c> arrow of goal 4, made enumerable.
///
/// <para>Two things need this. A <b>converter</b> needs the list of what to
/// produce, and it should come from the database rather than a hand-kept
/// manifest that can fall behind it. And the <b>"runs with no assets" promise</b>
/// (plan §7.2) is only testable if the set of keys is knowable — which is what
/// <c>PlaceholderProviderTests</c> uses to check the placeholder answers for
/// every last one.</para>
///
/// <para><b>The catalogue claims only what the game asks for.</b> DK2's records
/// carry far more art than this: a creature record alone names about thirty
/// animations. Enumerating all of them would produce a list of keys nothing
/// requests, and the placeholder-completeness test would then be checking
/// coverage of an invention. So the rule here is the same one
/// <c>TileState</c> and <c>CreatureState</c> follow — <b>a key appears when a
/// mechanic or a renderer needs it</b>, and the rest arrive with the mechanics
/// that read them.</para>
/// </summary>
public static class AssetCatalogue
{
    /// <summary>
    /// The creature animation variants the project can currently distinguish.
    ///
    /// <para><b>This mirrors <c>DK2.Simulation.CreatureActivity</c> exactly</b>,
    /// and it has to: a renderer picks an animation from a creature's activity,
    /// so an activity with no key would be undrawable and a key with no activity
    /// would be an asset nobody can ever ask for. It is a copy rather than a
    /// reference because this assembly sits beside the simulation, not above it —
    /// a renderer depends on both. <c>AssetCatalogueTests</c> asserts the two
    /// lists agree, so the copy cannot drift.</para>
    ///
    /// <para>Six names is not DK2's animation set; it is ours, and it grows as
    /// the simulation learns states.</para>
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
    ];

    /// <summary>
    /// Every key the database implies, in a stable order.
    ///
    /// <para>Order is by record id within each category, so two runs produce the
    /// same list and a converter's output is reproducible — the same reason the
    /// simulation never sorts its work queue on the fly.</para>
    /// </summary>
    public static IEnumerable<AssetRef> ForDatabase(GameDatabase database)
    {
        foreach (AssetRef reference in ForTerrain(database)) yield return reference;
        foreach (AssetRef reference in ForRooms(database)) yield return reference;
        foreach (AssetRef reference in ForCreatures(database)) yield return reference;
        foreach (AssetRef reference in ForObjects(database)) yield return reference;
    }

    /// <summary>
    /// One visual per terrain type, and which visual depends on whether the
    /// terrain fills its tile.
    ///
    /// <para><b>A solid terrain gets a mesh; a walkable one gets a texture.</b>
    /// That is not a rendering preference, it is what the two are: a wall is a
    /// volume you cannot enter and a floor is a surface you stand on. Asking the
    /// terrain's <c>IsSolid</c> flag rather than its id keeps this correct for a
    /// mod-authored terrain, the same rule the tile work rules follow.</para>
    ///
    /// <para>A terrain that damages what stands on it (lava) also gets an
    /// effect key, because its hazard is the thing a player must see. Nothing
    /// draws it yet, which is why the placeholder for an effect is honestly
    /// nothing at all rather than a box.</para>
    ///
    /// <para><b>One mesh per solid terrain is a known simplification, not the
    /// truth.</b> DK2 draws a wall from a <i>set</i> of pieces chosen by the
    /// size and shape of the room behind it, and gives walls separate textures
    /// per damage state — both CONFIRMED by Simon Schreibt's wireframe and
    /// mesh-extraction work (see <c>docs/research/wall-rendering.md</c>). So a
    /// faithful renderer will need variants here.</para>
    ///
    /// <para>They are not enumerated <i>yet</i> for the reason this file exists:
    /// <see cref="AssetRef"/> already carries a variant segment, so the shape is
    /// right and only the list is missing — and the list is missing because
    /// nobody has established what the pieces are. "Depends on the size of the
    /// room" is an observation, not a rule (Q71). Inventing a variant set to
    /// look complete would put keys in a converter's work list that correspond
    /// to nothing.</para>
    /// </summary>
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
    /// A floor texture and a mesh per room type.
    ///
    /// <para>Both, for every room, because a room is both things in DK2: paved
    /// ground you walk on and furniture standing on it. A treasury's floor and
    /// its gold piles are not one asset, and a keeper who sells the room keeps
    /// the floor (ADR-0029), so the two have different lifetimes as well as
    /// different shapes.</para>
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
    /// A mesh and one animation per implemented activity, for every creature.
    ///
    /// <para>Every creature, not only the two the simulation spawns: a keeper
    /// database defines 46 and the converter's job is to produce what the
    /// database defines, not what today's mechanics happen to place. The
    /// animation <i>variants</i>, by contrast, are exactly what today's
    /// mechanics can distinguish — see
    /// <see cref="CreatureAnimations"/>.</para>
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
    /// One mesh per object definition — a bed, a coop, an egg, a pile of gold,
    /// and the hundred-odd pillars and arches that are the rest of the record.
    ///
    /// <para><b>A mesh and nothing else</b>, for now. DK2 gives an object a
    /// state machine (<c>StateId</c> and <c>StateAge</c>, objects.md §7) and a
    /// <c>MoveTo</c> block, so a chicken pecks and a boulder rolls and both will
    /// want animations — but this project cannot yet distinguish one object
    /// state from another, and a variant list invented here would put keys in a
    /// converter's work list that correspond to nothing. The same restraint the
    /// wall variants get.</para>
    ///
    /// <para>Every object, not only the four the simulation places, for the
    /// reason <see cref="ForCreatures"/> gives: the converter's job is what the
    /// database defines.</para>
    /// </summary>
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
