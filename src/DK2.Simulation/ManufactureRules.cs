// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>One entry of a keeper's manufacture list (<c>player+0x219</c>): what to make and the
/// points it costs, the definition's <c>ManufactureRequired</c> copied when it was queued.</summary>
public readonly record struct ManufactureOrder(byte Kind, byte TypeId, int Cost)
{
    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(Kind);
        hash.Add(TypeId);
        hash.Add(Cost);
    }
}

/// <summary>The Workshop: what a manufacturer earns its keeper, and the crate it pays for (ADR-0203,
/// <c>state-category-hooks.md</c> §3.4).</summary>
/// <remarks>The category 5 update <c>0x0047B190</c> credits the level-scaled
/// <c>ManufacturePerSecond</c> every turn through <c>0x004C3200</c>, which adds it to the keeper's
/// points (<c>player+0x217</c>, a word). When the list holds an order and its cost is covered, a
/// crate of the order's type appears in the keeper's nearest Workshop that someone is working
/// in, the points go to zero (the overshoot is lost), the order leaves the list and the
/// keeper's items manufactured go up. The list is a 128-entry ring, filled when a trap or door
/// blueprint has no crate of its type waiting (<c>0x005066E0</c>). With points and nobody
/// manufacturing, the player update takes <c>ManufacturePointLossWhileNoWorkersPerSecond</c> off
/// once a second (<c>0x004C3340</c>).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public static class ManufactureRules
{
    /// <summary>The list entry's kind byte for a trap.</summary>
    public const byte TrapKind = 0;

    /// <summary>The list entry's kind byte for a door.</summary>
    public const byte DoorKind = 1;

    /// <summary>The ring's size: 128 entries of 16 bytes, refused past it.</summary>
    public const int QueueCapacity = 128;

    /// <summary>The hook's ceiling on one credit, as for research.</summary>
    public const int MaxCredit = 0xFFFE;

    /// <summary>What one turn of manufacturing is worth: <c>ManufacturePerSecond</c>, which the loader
    /// divides by variable 19 with a floor of one (<c>0x004938B8</c>), scaled for levels 2–10 by
    /// stat row 18 and clamped to <c>0..0xFFFE</c>; level 1 bypasses the table.</summary>
    public static int PerOriginalTick(GameDatabase database, CreatureDef creature, CreatureState state)
    {
        int perTick = SimulationTiming.LoaderRatePerTick(creature.ManufacturePerSecond);
        if (perTick == 0) return 0;

        int level = CreatureLevelRules.Clamp(state.Level);
        if (level == CreatureLevelRules.FirstLevel) return perTick;

        int scaled = CreatureLevelRules.Scale(
            perTick, CreatureLevelRules.Percent(database, CreatureStatId.ManufacturePerSecond, level));
        return Math.Clamp(scaled, 0, MaxCredit);
    }

    /// <summary>The crate object a trap or door order produces, or 0.</summary>
    public static byte CrateObjectId(GameDatabase database, ManufactureOrder order) =>
        order.Kind == TrapKind
            ? database.Traps.TryGetValue(order.TypeId, out TrapDef? trap) ? trap.CrateObjectId : (byte)0
            : database.Doors.TryGetValue(order.TypeId, out DoorDef? door) ? door.CrateObjectId : (byte)0;

    /// <summary>The level loader's pushes (<c>0x00520F40</c> through <c>0x005066E0</c>): one order for
    /// every placed blueprint of a type beyond the crates of that type the keeper already holds.</summary>
    /// <remarks>Ours, stated: the counts are taken once over the whole level rather than as each
    /// record is read, and orders go in blueprint order.</remarks>
    public static void QueueBlueprints(WorldState world, GameDatabase database)
    {
        foreach (TrapState trap in world.Traps)
        {
            if (!trap.UnderConstruction) continue;
            if (!database.Traps.TryGetValue(trap.TrapId, out TrapDef? def)) continue;
            TryQueue(world, database, trap.PlayerId, TrapKind, trap.TrapId, def.ManufactureRequired, def.CrateObjectId);
        }

        foreach (DoorState door in world.Doors)
        {
            if (!door.UnderConstruction) continue;
            if (!database.Doors.TryGetValue(door.DoorId, out DoorDef? def)) continue;
            TryQueue(world, database, door.PlayerId, DoorKind, door.DoorId, (int)def.ManufactureRequired, def.CrateObjectId);
        }
    }

    private static void TryQueue(
        WorldState world, GameDatabase database, byte playerId, byte kind, byte typeId, int cost, byte crateId)
    {
        if (!PlayerIds.IsKeeper(playerId) || !world.HasPlayer(playerId)) return;
        List<ManufactureOrder> queue = world.Player(playerId).ManufactureQueue;
        if (queue.Count >= QueueCapacity) return;

        int blueprints = 0;
        if (kind == TrapKind)
        {
            foreach (TrapState t in world.Traps)
                if (t.UnderConstruction && t.PlayerId == playerId && t.TrapId == typeId) blueprints++;
        }
        else
        {
            foreach (DoorState d in world.Doors)
                if (d.UnderConstruction && d.PlayerId == playerId && d.DoorId == typeId) blueprints++;
        }

        int crates = 0;
        foreach (ObjectState o in world.Objects)
            if (crateId != 0 && o.ObjectTypeId == crateId && o.OwnerPlayerId == playerId) crates++;
        foreach (ManufactureOrder o in queue)
            if (o.Kind == kind && o.TypeId == typeId) crates++;

        if (blueprints > crates) queue.Add(new ManufactureOrder(kind, typeId, cost));
    }

    /// <summary>The adder: credit, and the crate if the head order is paid for. Returns the crate
    /// made, or null.</summary>
    public static ObjectState? Credit(
        WorldState world, GameDatabase database, CreatureState creature, int amount)
    {
        ref PlayerState keeper = ref world.Player(creature.OwnerPlayerId);
        keeper.ManufacturePoints = (keeper.ManufacturePoints + amount) & 0xFFFF;
        if (keeper.ManufactureQueue.Count == 0 || keeper.ManufactureQueue[0].Cost > keeper.ManufacturePoints) return null;

        ManufactureOrder order = keeper.ManufactureQueue[0];
        byte crateId = CrateObjectId(database, order);
        int tile = WorkshopTile(world, database, creature);
        if (crateId == 0 || tile < 0) return null;

        keeper.ManufacturePoints = 0;
        keeper.ManufactureQueue.RemoveAt(0);
        keeper.ItemsManufactured++;
        return world.SpawnObject(database, crateId, creature.OwnerPlayerId, tile);
    }

    /// <summary><c>0x004C3B50</c>: the keeper's Workshop nearest the manufacturer with somebody at
    /// work in it; the crate goes on the first tile of its chain. Ours, stated: distance to that
    /// tile rather than to the room's position word, no capacity test (unbuilt, as for every
    /// room), and the unread fallback <c>0x004C6CF0</c> is a failure that keeps the points.</summary>
    private static int WorkshopTile(WorldState world, GameDatabase database, CreatureState creature)
    {
        int best = -1;
        long bestDistance = long.MaxValue;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId != RoomTypeId.Workshop || room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            if (room.TileIndices.Count == 0 || !Occupied(world, room, creature.OwnerPlayerId)) continue;

            long distance = EatingRules.DistanceSquared(world, creature.Tile, room.TileIndices[0]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = room.TileIndices[0];
            }
        }

        return best;
    }

    private static bool Occupied(WorldState world, RoomInstance room, byte keeper)
    {
        foreach (CreatureState c in world.Creatures)
        {
            if (c.OwnerPlayerId == keeper && CreatureStateMachine.UpdatingCategory(c) == CreatureStateCategory.Manufacture
                && EatingRules.Contains(room, (c.Tile.Y * world.Width) + c.Tile.X)) return true;
        }

        return false;
    }

    /// <summary><c>0x004C3340</c>, once a game second for each keeper: with points and nobody in the
    /// Manufacture category (<c>player+0x8A</c>, which its enter and leave hooks count), the points
    /// fall by <c>ManufacturePointLossWhileNoWorkersPerSecond</c>, to no lower than zero.</summary>
    public static void Decay(WorldState world, GameDatabase database)
    {
        for (int p = 0; p < world.PlayerCount; p++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(p);
            if (player.ManufacturePoints == 0 || HasManufacturer(world, player.PlayerId)) continue;
            int loss = database[VariableTypeId.ManufacturePointLossWhileNoWorkersPerSecond];
            player.ManufacturePoints = player.ManufacturePoints <= loss ? 0 : player.ManufacturePoints - loss;
        }
    }

    private static bool HasManufacturer(WorldState world, byte playerId)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.OwnerPlayerId == playerId && CreatureStateMachine.UpdatingCategory(c) == CreatureStateCategory.Manufacture) return true;
        return false;
    }
}
