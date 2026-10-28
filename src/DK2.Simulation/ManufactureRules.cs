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
/// points (<c>player+0x217</c>, a word). When the list holds an order and the points cover the cost
/// word in the ring's slot 0 (<c>0x00506B50</c>; not the head's, Q618), a crate of the head order's
/// type appears in the keeper's nearest Workshop that someone is working
/// in, the points go to zero (the overshoot is lost), the order leaves the list and the
/// keeper's items manufactured go up. The list is a 128-entry ring, filled when a trap or door
/// blueprint has no crate of its type waiting (<c>0x005066E0</c>). With points and nobody
/// manufacturing, the player update takes <c>ManufacturePointLossWhileNoWorkersPerSecond</c> off
/// once a second (<c>0x004C3340</c>).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public static class ManufactureRules
{
    /// <summary>The list entry's kind byte for a door: 0, as on a crate (<c>0x00506BA0</c>).</summary>
    public const byte DoorKind = CrateRules.DoorKind;

    /// <summary>The list entry's kind byte for a trap: 1.</summary>
    public const byte TrapKind = CrateRules.TrapKind;

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

    /// <summary>The door loader's push (<c>0x00520F40</c>) for the authored door just placed: a
    /// blueprint makes an order through <c>0x005066E0</c> while the keeper's door blueprints of its
    /// type outnumber its crates of that type, both as loaded so far (the loader runs per record),
    /// then joins the keeper's blueprint list (<c>0x00505680</c>).</summary>
    /// <remarks>The trap loader <c>0x00520E50</c> does push an authored trap blueprint (an order
    /// at <c>0x00520F1A</c>, then the list) for a parsed NumberOfShots of 0; ours queues none. No
    /// shipped level authors one (Q667), so only a custom level would differ.</remarks>
    public static void QueueAuthoredDoor(WorldState world, GameDatabase database, DoorState door)
    {
        if (!door.UnderConstruction) return;
        if (!database.Doors.TryGetValue(door.DoorId, out DoorDef? def)) return;
        TryQueue(world, database, door.PlayerId, DoorKind, door.DoorId, (int)def.ManufactureRequired, def.CrateObjectId);
        CrateRules.Register(world, door.PlayerId, new BlueprintRef(DoorKind, door.Id));
    }

    /// <summary><c>0x005066E0(kind, type)</c> for one blueprint placed in play (ADR-0231): an order
    /// while the keeper's blueprints of the type outnumber its crates of it.</summary>
    public static void QueueOne(WorldState world, GameDatabase database, PlayerId playerId, byte kind, byte typeId)
    {
        if (kind == TrapKind)
        {
            if (database.Traps.TryGetValue(typeId, out TrapDef? trap))
                TryQueue(world, database, playerId, kind, typeId, trap.ManufactureRequired, trap.CrateObjectId);
        }
        else if (database.Doors.TryGetValue(typeId, out DoorDef? door))
            TryQueue(world, database, playerId, kind, typeId, (int)door.ManufactureRequired, door.CrateObjectId);
    }

    private static void TryQueue(
        WorldState world, GameDatabase database, PlayerId playerId, byte kind, byte typeId, int cost, byte crateId)
    {
        if (!PlayerIds.IsKeeper(playerId.Value) || !world.HasPlayer(playerId)) return;
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

        // Crates only: 0x005066E0 never reads the ring, so an order left by a destroyed blueprint
        // (Q668) does not stand in for a crate.
        int crates = 0;
        foreach (ObjectState o in world.Objects)
            if (crateId != 0 && o.ObjectTypeId == crateId && o.OwnerPlayerId == playerId) crates++;

        if (blueprints > crates) Enqueue(ref world.Player(playerId), new ManufactureOrder(kind, typeId, cost));
    }

    /// <summary>The push's write (<c>0x005066E0</c>): the entry goes in the ring's tail slot and the
    /// tail moves on, wrapping at 128. Only an entry written to slot 0 changes the cost the adder
    /// compares with (<see cref="PlayerState.ManufactureSlotZeroCost"/>).</summary>
    public static void Enqueue(ref PlayerState keeper, ManufactureOrder order)
    {
        if (keeper.ManufactureRingTail == 0) keeper.ManufactureSlotZeroCost = order.Cost & 0xFFFF;
        keeper.ManufactureRingTail = (byte)((keeper.ManufactureRingTail + 1) % QueueCapacity);
        keeper.ManufactureQueue.Add(order);
    }

    /// <summary>The adder: credit, and the crate of the head order once the points reach the cost
    /// word in ring slot 0 (<c>0x00506B50</c> compares with <c>[ring+0]</c>; the pop <c>0x00506970</c>
    /// only moves the head, so after the first crate that word is the first order's until an
    /// enqueue wraps onto it). On Level 4 every crate, a 1500-point Sentry Trap's included, is
    /// made as the points pass 3000, the first order's (a Wooden Door) cost (Q618). Returns the
    /// crate made, or null.</summary>
    /// <remarks>The crate maker <c>0x004F5240</c> first sends every creature attached to the
    /// Workshop to state 68, which idles it on its next turn (<see cref="CrateRules.CrateMadeState"/>),
    /// then makes the crate on the room's first tile. With space in the Workshop the crate is
    /// listed there and owned by the keeper; without, it is left belonging to nobody, loose for
    /// an imp to carry (ADR-0214).</remarks>
    public static ObjectState? Credit(
        in StateHookContext hooks, CreatureState creature, int amount)
    {
        WorldState world = hooks.World;
        GameDatabase database = hooks.Database;
        ref PlayerState keeper = ref world.Player(creature.OwnerPlayerId);
        keeper.ManufacturePoints = (keeper.ManufacturePoints + amount) & 0xFFFF;
        if (keeper.ManufactureQueue.Count == 0 || keeper.ManufactureSlotZeroCost > keeper.ManufacturePoints) return null;

        ManufactureOrder order = keeper.ManufactureQueue[0];
        byte crateId = CrateObjectId(database, order);
        RoomInstance? workshop = Workshop(world, database, creature);
        if (crateId == 0 || workshop is null) return null;

        foreach (CreatureState worker in world.Creatures)
        {
            if (worker.OwnerPlayerId != workshop.OwnerPlayerId || !IsAttached(world, workshop, worker)) continue;
            CreatureStateMachine.TrySetState(hooks, worker, CrateRules.CrateMadeState);
        }

        ObjectState crate = world.SpawnObject(database, crateId, PlayerIds.Unowned, workshop.TileIndices[0]);
        CrateRules.List(hooks, workshop, crate);
        keeper.ManufacturePoints = 0;
        keeper.ManufactureQueue.RemoveAt(0);
        keeper.ItemsManufactured++;
        return crate;
    }

    /// <summary><c>0x004C3B50</c>: the keeper's Workshop nearest the manufacturer with somebody at
    /// work in it. Ours, stated: distance to the first tile rather than to the room's position
    /// word, and the unread fallback <c>0x004C6CF0</c> is a failure that keeps the points.</summary>
    private static RoomInstance? Workshop(WorldState world, GameDatabase database, CreatureState creature)
    {
        RoomInstance? best = null;
        long bestDistance = long.MaxValue;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId != RoomTypeId.Workshop || room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            if (room.TileIndices.Count == 0 || !Occupied(world, room, creature.OwnerPlayerId)) continue;

            long distance = EatingRules.DistanceSquared(world, creature.Tile, room.TileIndices[0]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = room;
            }
        }

        return best;
    }

    private static bool IsAttached(WorldState world, RoomInstance room, CreatureState c) =>
        CreatureStateMachine.UpdatingCategory(c) == CreatureStateCategory.Manufacture
        && EatingRules.Contains(room, (c.Tile.Y * world.Width) + c.Tile.X);

    private static bool Occupied(WorldState world, RoomInstance room, byte keeper)
    {
        foreach (CreatureState c in world.Creatures)
        {
            if (c.OwnerPlayerId == keeper && IsAttached(world, room, c)) return true;
        }

        return false;
    }

    /// <summary><c>0x004C3340</c>, once a game second for each keeper: with points and a zero
    /// <see cref="PlayerState.ManufacturingCreatures"/> (<c>player+0x8A</c>; any nonzero count, a
    /// negative one included, holds the points), they fall by
    /// <c>ManufacturePointLossWhileNoWorkersPerSecond</c>, to no lower than zero.</summary>
    public static void Decay(WorldState world, GameDatabase database)
    {
        for (int p = 0; p < world.PlayerCount; p++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(p);
            if (player.ManufacturePoints == 0 || player.ManufacturingCreatures != 0) continue;
            int loss = database[VariableTypeId.ManufacturePointLossWhileNoWorkersPerSecond];
            player.ManufacturePoints = player.ManufacturePoints <= loss ? 0 : player.ManufacturePoints - loss;
        }
    }

    /// <summary>The enter hook <c>0x0047B0A0</c>: the count goes up before the lookup and claim, and
    /// a refused claim does not take it back. Not modelled: its clear of the state byte <c>+0xB9</c>,
    /// which nothing reads as a byte (<c>leave-hook-bodies.md</c>).</summary>
    public static bool Enter(in StateHookContext hooks, CreatureState creature)
    {
        CountMaker(hooks.World, creature, +1);
        return RoomCapacityRules.Claim(in hooks, creature);
    }

    /// <summary>The leave hook <c>0x0047B120</c>: the count goes down, with no floor, then the
    /// release. A maker that dies outright skips it, where DK2's death runs it through
    /// <c>SetState(1)</c> (<c>0x00476C80</c>), so its count stays raised (not built).</summary>
    public static void Leave(in StateHookContext hooks, CreatureState creature)
    {
        CountMaker(hooks.World, creature, -1);
        RoomCapacityRules.Release(creature);
    }

    /// <summary>Both hooks change the count of the keeper who owns the tile under the creature
    /// (<c>0x00455990</c>), not the creature's own. A tile with no keeper of ours is skipped.</summary>
    private static void CountMaker(WorldState world, CreatureState creature, int by)
    {
        if (!world.Contains(creature.Tile)) return;
        PlayerId owner = world.Tile(creature.Tile).OwnerPlayerId;
        if (world.HasPlayer(owner)) world.Player(owner).ManufacturingCreatures += by;
    }
}
