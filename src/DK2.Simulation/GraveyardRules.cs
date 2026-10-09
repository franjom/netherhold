// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public readonly record struct GraveyardBurialResult(
    int DecomposeValue, int SpawnedCreatureId, byte SpawnedCreatureTypeId);

/// <summary>Per-room corpse intake and Vampire creation for Graveyards.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-death.md")]
public static class GraveyardRules
{
    /// <summary>Removes tallies whose anchor is no longer the anchor of a
    /// Graveyard owned by the same player. The identity policy is ADR-0151.</summary>
    public static void Reconcile(WorldState world, GameDatabase database)
    {
        var current = new HashSet<(int Anchor, byte Owner)>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId != RoomTypeId.Graveyard) continue;
            current.Add((room.TileIndices[0], room.OwnerPlayerId));
        }

        world.RawGraveyards.RemoveAll(
            state => !current.Contains((state.AnchorTileIndex, state.OwnerPlayerId)));
    }

    /// <summary>A burial (<c>0x004DD470</c>, graveyard.md §2): the corpse's <c>DecomposeValue</c>, above
    /// level 1 scaled by stat 19 and kept within 0 to 0xFFFE, is added first. Then, when the owner's
    /// gate passes (<see cref="HasRoomForAnother"/>) and the tally has reached
    /// <c>DecomposeValueNeededForVampire</c>, the tally is zeroed, the excess lost, and one creature is
    /// raised (<c>0x004DD820</c>): the room's <c>CreatedCreatureId</c> at a random tile of the room,
    /// one draw of its tile count along the chain (<c>0x004E4D90</c>). DK2 puts it on the room's last
    /// Graveyard Crypt (object 75) when one is listed, after a draw; no shipped level places one.
    /// Not modelled: effect <c>0x138</c>, state 198 and message 72 on the new creature.</summary>
    public static GraveyardBurialResult Bury(
        SimulationContext context, RoomInstance graveyard, CreatureDef corpse, int corpseLevel)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        Reconcile(world, database);
        GraveyardState state = world.GraveyardAt(graveyard.TileIndices[0], graveyard.OwnerPlayerId);
        int value = corpseLevel == 1
            ? corpse.DecomposeValue
            : Math.Clamp(corpse.DecomposeValue * CreatureLevelRules.Percent(database, CreatureStatId.DecomposeValue, corpseLevel) / 100, 0, 0xFFFE);
        state.Add(value);

        if (!HasRoomForAnother(context, graveyard.OwnerPlayerId)
            || (uint)state.DecomposeValue < (uint)database[VariableTypeId.DecomposeValueNeededForVampire])
            return new GraveyardBurialResult(state.DecomposeValue, -1, 0);

        state.Zero();
        byte creatureTypeId = graveyard.Definition.CreatedCreatureId;
        if (creatureTypeId == 0) return new GraveyardBurialResult(state.DecomposeValue, -1, 0);
        int[] chain = SlabChain.Order(world, graveyard);
        int tile = chain[context.Rng.NextInt(chain.Length)];
        CreatureState creature = world.SpawnCreature(
            database, creatureTypeId, graveyard.OwnerPlayerId, new(tile % world.Width, tile / world.Width));
        return new GraveyardBurialResult(state.DecomposeValue, creature.Id, creatureTypeId);
    }

    /// <summary>The corpse selections, at the first corpse met on the worker's keeper's tile: the
    /// per-tile <c>0x0046CF40</c> (the drop scan) and the search <c>0x0046D3C0</c>. No Graveyard posts
    /// 41 at availability 3 (once a level, player <c>+0xF4E</c>); none with a place
    /// (<see cref="HasPlace"/>) posts 42, the search always and the per-tile one at availability 3;
    /// otherwise <c>0x004C6860</c> draws one of those with a place, and the corpse goes to the nearest
    /// (<see cref="CarryRules.NearestGraveyardTile"/>). Returns that tile, or -1 and no carry.</summary>
    public static int ForCorpse(SimulationContext context, CreatureState worker, bool search)
    {
        WorldState world = context.World;
        PlayerId player = worker.OwnerPlayerId;
        bool available = world.Availability(player, AvailabilityKind.Room, (int)RoomTypeId.Graveyard) == RoomNeedRules.Available;
        var graveyards = RoomIndex.Build(world, context.Database)
            .Where(r => r.OwnerPlayerId == player && r.Definition.TypeId == RoomTypeId.Graveyard).ToList();
        if (graveyards.Count == 0)
        {
            if (available) RoomNeedRules.Post(context, player, PlayerMessageKind.RoomNeeded, RoomTypeId.Graveyard);
            return -1;
        }

        int placed = graveyards.Count(g => HasPlace(world, g));
        if (placed == 0)
        {
            if (search || available) RoomNeedRules.Post(context, player, PlayerMessageKind.RoomFull, RoomTypeId.Graveyard);
            return -1;
        }

        context.Rng.NextInt(placed);
        return CarryRules.NearestGraveyardTile(world, context.Database, worker);
    }

    /// <summary><c>Room_HasCapacity</c> for a corpse (one unit) in a Graveyard: its used count
    /// (<c>+0x6B</c>, the corpses lying in it, <c>0x004E6490</c>) within its floor slots. Ours buries a
    /// corpse on arrival, so none lies there and a Graveyard has a place while it has a floor slot
    /// (Q578).</summary>
    public static bool HasPlace(WorldState world, RoomInstance graveyard) =>
        AutoFurnitureRules.FloorSlotCount(world, graveyard) >= RoomCapacityRules.Units;

    /// <summary>The capacity gate <c>0x004DDCA0</c>: the owner's living creatures that can be
    /// resurrected must number fewer than the summed capacity of its Graveyards, each a Graveyard's
    /// floor slots (<c>Room_ComputeTotalCapacity</c>, type 14). Failing, it posts 42 for the
    /// Graveyard when the owner has one at all.</summary>
    public static bool HasRoomForAnother(SimulationContext context, PlayerId owner)
    {
        WorldState world = context.World;
        int capacity = 0;
        bool any = false;
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.OwnerPlayerId != owner || room.Definition.TypeId != RoomTypeId.Graveyard) continue;
            any = true;
            capacity += AutoFurnitureRules.FloorSlotCount(world, room);
        }

        int resurrectable = 0;
        foreach (CreatureState creature in world.Creatures)
            if (creature.OwnerPlayerId == owner && creature.Health >= 1
                && creature.Dk2StateId != CreatureStateMachine.NoState
                && CreatureDeathRules.CanBeResurrected(context.Database.CreatureById(creature.CreatureTypeId)))
                resurrectable++;

        if (resurrectable < capacity) return true;
        if (any) RoomNeedRules.Post(context, owner, PlayerMessageKind.RoomFull, RoomTypeId.Graveyard);
        return false;
    }

    /// <summary>A sale frees the room and rebuilds what is left as new rooms with nothing copied for a
    /// Graveyard (room-object.md), so any sale of one of its tiles loses the tally (Q321).</summary>
    public static void Forget(WorldState world, RoomInstance graveyard) =>
        world.RawGraveyards.RemoveAll(state =>
            state.AnchorTileIndex == graveyard.TileIndices[0] && state.OwnerPlayerId == graveyard.OwnerPlayerId);
}
