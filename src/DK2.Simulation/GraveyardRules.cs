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
    /// one draw of its tile count along the chain (<c>0x004E4D90</c>), or, with crypts, on the room's last
    /// listed Graveyard Crypt (object 75) after a draw of their count (<see cref="PlaceCrypts"/>).
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
        // 0x004DD820: with crypts, random(count) and the last on the room's newest-first list,
        // which is the first placed, whatever the draw; with none, a random tile of the chain.
        List<ObjectState> crypts = [.. world.Objects
            .Where(o => o.ObjectTypeId == AutoFurnitureRules.CryptObjectId && graveyard.Contains(o.TileIndex))
            .OrderBy(o => o.Id)];
        int tile;
        if (crypts.Count > 0)
        {
            context.Rng.NextInt(crypts.Count);
            tile = crypts[0].TileIndex;
        }
        else
        {
            int[] chain = SlabChain.Order(world, graveyard);
            tile = chain[context.Rng.NextInt(chain.Length)];
        }

        CreatureState creature = world.SpawnCreature(
            database, creatureTypeId, graveyard.OwnerPlayerId, new(tile % world.Width, tile / world.Width));
        return new GraveyardBurialResult(state.DecomposeValue, creature.Id, creatureTypeId);
    }

    /// <summary>The corpse selections, at the first corpse met on the worker's keeper's tile: the
    /// per-tile <c>0x0046CF40</c> (the drop scan) and the search <c>0x0046D3C0</c>. No Graveyard posts
    /// 41 at availability 3 (once a level, player <c>+0xF4E</c>); none with a place
    /// (<see cref="HasPlace"/>) posts 42, the search always and the per-tile one at availability 3;
    /// otherwise <c>0x004C6860</c> draws one of those with a place. Returns the nearest such tile
    /// (<see cref="CarryRules.NearestGraveyardTile"/>), or -1 and no carry; the pick-up chooses the
    /// Graveyard itself (<see cref="CorpseDestination"/>).</summary>
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

    /// <summary><c>Room_HasCapacity</c> for a corpse (one unit, <c>+0x672</c>) in a Graveyard: its used
    /// count (<c>+0x6B</c>, the corpses lying in it, <c>0x004E6490</c>) within its floor slots.</summary>
    public static bool HasPlace(WorldState world, RoomInstance graveyard) =>
        Lying(world, graveyard) + RoomCapacityRules.Units <= AutoFurnitureRules.FloorSlotCount(world, graveyard);

    /// <summary>The corpses lying in a Graveyard (<see cref="CreatureState.GraveyardSlot"/>).</summary>
    public static int Lying(WorldState world, RoomInstance graveyard)
    {
        int count = 0;
        foreach (CreatureState c in world.Creatures)
            if (c.GraveyardSlot >= 0 && graveyard.Contains(c.GraveyardSlot)) count++;
        return count;
    }

    /// <summary><c>0x004E6A20</c>: <c>random(size)</c> steps along the slab chain from its first slab,
    /// then round the chain to the first floor slot with no dead body on it; null with none. One draw.</summary>
    public static int? CorpsePoint(WorldState world, RoomInstance graveyard, SimRandom rng)
    {
        int[] chain = SlabChain.Order(world, graveyard);
        int start = rng.NextInt(chain.Length);
        HashSet<int> slots = AutoFurnitureRules.FloorSlotSet(world, graveyard);
        for (int k = 0; k < chain.Length; k++)
        {
            int tile = chain[(start + k) % chain.Length];
            if (slots.Contains(tile) && !HoldsACorpse(world, tile)) return tile;
        }

        return null;
    }

    private static bool HoldsACorpse(WorldState world, int tile)
    {
        int w = world.Width;
        foreach (CreatureState c in world.Creatures)
            if (CreatureDeathRules.IsCorpse(c) && c.CarriedByCreatureId < 0 && (c.Tile.Y * w) + c.Tile.X == tile) return true;
        return false;
    }

    /// <summary>State 40 (<c>0x004678A0</c>), the pick-up: the first of the keeper's Graveyards in room
    /// order with a place and a point (<see cref="CorpsePoint"/>, a draw for each one asked); -1 with
    /// none.</summary>
    public static int CorpseDestination(SimulationContext context, PlayerId owner)
    {
        foreach (RoomInstance room in RoomIndex.Build(context.World, context.Database))
            if (room.OwnerPlayerId == owner && room.Definition.TypeId == RoomTypeId.Graveyard
                && HasPlace(context.World, room) && CorpsePoint(context.World, room, context.Rng) is int point)
                return point;
        return -1;
    }

    /// <summary>State 41 (<c>0x00467AA0</c>) off a place to lay the corpse: the Graveyard with a place
    /// nearest the worker (<c>0x004C6610</c>, its position, reach option 0) and a point; failing that,
    /// the last in room order with a place and a point (a draw for each); -1 with none.</summary>
    public static int CorpseRedirect(SimulationContext context, CreatureState worker, CreatureDef definition)
    {
        WorldState world = context.World;
        RoomInstance? nearest = null;
        long best = long.MaxValue;
        var scratch = new List<Steering.Fixed16Vector>();
        var places = new List<RoomInstance>();
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.OwnerPlayerId != worker.OwnerPlayerId || room.Definition.TypeId != RoomTypeId.Graveyard || !HasPlace(world, room)) continue;
            places.Add(room);
            TilePos at = RoomNeedRules.Position(world, room);
            long dx = (long)CreatureState.CentreOf(at.X) - worker.PosX, dy = (long)CreatureState.CentreOf(at.Y) - worker.PosY;
            long distance = ((dx * dx) >> 12) + ((dy * dy) >> 12);
            if (distance >= best || !ChickenRules.Reachable(context, worker, definition, at, scratch)) continue;
            (nearest, best) = (room, distance);
        }

        if (nearest is not null && CorpsePoint(world, nearest, context.Rng) is int point) return point;
        int found = -1;
        foreach (RoomInstance room in places)
            if (CorpsePoint(world, room, context.Rng) is int p) found = p;
        return found;
    }

    /// <summary>The Graveyard's pass in <c>Room_Rebuild</c> (<c>0x004DD5C0</c>), run after the rebuild
    /// has destroyed the room's furniture (<c>0x004E6BC0</c>) and before the stones: <c>size / 10</c>
    /// tries, each drawing among the floor slots whose eight neighbours are floor slots of the room
    /// (<c>0x004DD9B0</c>, in slab-chain order, <c>random(count)</c> on the world stream) and standing a
    /// crypt (object 75) there. A placed crypt is no floor slot, so a 5x5 gets one, at its centre, and
    /// its second try finds none; anything smaller than a 3x3 block of slots gets none.</summary>
    public static void PlaceCrypts(SimulationContext context, RoomInstance graveyard)
    {
        WorldState world = context.World;
        foreach (ObjectState crypt in world.Objects
                     .Where(o => o.ObjectTypeId == AutoFurnitureRules.CryptObjectId && graveyard.Contains(o.TileIndex)).ToList())
            world.RemoveObject(crypt.Id);

        int[] chain = SlabChain.Order(world, graveyard);
        for (int i = 0; i < graveyard.TileCount / 10; i++)
        {
            HashSet<int> slots = AutoFurnitureRules.FloorSlotSet(world, graveyard);
            var candidates = new List<int>();
            foreach (int tile in chain)
                if (slots.Contains(tile) && SurroundedBySlots(world, slots, tile)) candidates.Add(tile);
            if (candidates.Count == 0) continue;
            int at = candidates[context.Rng.NextInt(candidates.Count)];
            if (context.Database.ObjectById(AutoFurnitureRules.CryptObjectId) is null) continue;
            world.SpawnObject(context.Database, AutoFurnitureRules.CryptObjectId, graveyard.OwnerPlayerId, at).AutoPlaced = true;
        }
    }

    private static bool SurroundedBySlots(WorldState world, HashSet<int> slots, int tile)
    {
        int x = tile % world.Width, y = tile / world.Width;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && (!world.Contains(x + dx, y + dy) || !slots.Contains(((y + dy) * world.Width) + x + dx)))
                    return false;
        return true;
    }

    /// <summary>The Graveyard's tile-change slot <c>+0x09</c> (<c>0x004DD530</c> through
    /// <c>0x004DD750</c>), after a rebuild in play: every dead body on its tiles whose owner word is not
    /// the room's (<see cref="CreatureState.BodyOwnerPlayerId"/>), lying in no room and not carried, is
    /// made the room owner's and offered to <c>Room_AddCorpse</c>, which, while the room has a place,
    /// links it and counts it whatever its cell (the cell's tests come after the count). So a
    /// Graveyard built over the dead takes them in, but a body it once held and a sale let go is
    /// already its owner's and stays out, rotting where it lies, no selection walking it.</summary>
    /// <remarks>DK2 walks the chain and each tile's things, so when places run out the bodies taken
    /// can differ from ours, in creature order. DK2's <c>+0x24</c> would first take a body out of
    /// another room; a tile of this room lies in no other, so ours skips that.</remarks>
    public static void AdoptCorpses(SimulationContext context, RoomInstance graveyard)
    {
        WorldState world = context.World;
        foreach (CreatureState c in world.Creatures)
        {
            if (!CreatureDeathRules.IsCorpse(c) || c.BodyOwnerPlayerId == graveyard.OwnerPlayerId) continue;
            int tile = (c.Tile.Y * world.Width) + c.Tile.X;
            if (!graveyard.Contains(tile)) continue;
            c.BodyOwnerPlayerId = graveyard.OwnerPlayerId;
            if (c.GraveyardSlot >= 0 || c.CarriedByCreatureId >= 0 || !HasPlace(world, graveyard)) continue;
            c.GraveyardSlot = tile;
        }
    }

    /// <summary>The tombstones, objects 31 to 34: the range <c>0x004537E0</c> is asked over, bytes
    /// <c>0x006A1D3C</c>..<c>0x006A1D3F</c>, which are the Graveyard's own <c>ObjectIds</c>. The room's
    /// furniture pass stands one on each of its floor slots (<see cref="AutoFurnitureRules"/>).</summary>
    public const byte FirstTombStoneId = 31, LastTombStoneId = 34;

    /// <summary>Whether a tombstone stands on the tile. DK2 also wants its <c>+0x14A</c> zero; that word
    /// is zeroed when the stone is made (<c>0x0049B1C0</c>) and set only at level start, under a corpse
    /// the level places on it (<c>0x0050BD4C</c>), which no level of ours does.</summary>
    public static bool HasTombStone(WorldState world, int tile)
    {
        foreach (ObjectState thing in world.Objects)
            if (thing.TileIndex == tile && !thing.InHand && thing.CarriedByCreatureId < 0
                && thing.ObjectTypeId is >= FirstTombStoneId and <= LastTombStoneId)
                return true;
        return false;
    }

    /// <remarks><c>Room_AddCorpse</c> links, counts and re-owns the body before its cell tests, so a
    /// refusal there leaves it linked; state 41 asks <c>0x004E6380</c>'s same tests first, so ours
    /// refuses whole, and only a body refused after passing them would differ.</remarks>
    /// <summary>State 41's arrival (<c>0x004E6380</c>, then <c>Room_AddCorpse</c> <c>0x004E6490</c>): on a
    /// Graveyard with a place, a floor slot with a tombstone (<see cref="HasTombStone"/>) and no lying
    /// corpse, the corpse is laid there.</summary>
    public static bool TryLay(WorldState world, GameDatabase database, CreatureState corpse, int tile)
    {
        if (RoomIndex.RoomAt(world, database, tile) is not { Definition.TypeId: RoomTypeId.Graveyard } room
            || !HasPlace(world, room) || !AutoFurnitureRules.FloorSlotSet(world, room).Contains(tile)
            || !HasTombStone(world, tile))
            return false;
        foreach (CreatureState c in world.Creatures)
            if (c.GraveyardSlot == tile) return false;
        corpse.GraveyardSlot = tile;
        corpse.BodyOwnerPlayerId = room.OwnerPlayerId;   // Room_AddCorpse's +0x24
        corpse.PosX = CreatureState.CentreOf(tile % world.Width);
        corpse.PosY = CreatureState.CentreOf(tile / world.Width);
        return true;
    }

    /// <summary><c>Room_RemoveCorpse</c> for every corpse of a freed room: it lies on as any corpse.</summary>
    public static void Unlink(WorldState world, ICollection<int> tiles)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.GraveyardSlot >= 0 && tiles.Contains(c.GraveyardSlot)) c.GraveyardSlot = -1;
    }

    /// <summary>A lying corpse's end (<c>0x004B1070</c> from the thing sweep's removal pass, its
    /// lifetime below 1): buried in its Graveyard (<see cref="Bury"/>) and its place freed. Null when
    /// it lies in none.</summary>
    public static GraveyardBurialResult? BuryLying(SimulationContext context, CreatureState corpse)
    {
        if (corpse.GraveyardSlot < 0
            || RoomIndex.RoomAt(context.World, context.Database, corpse.GraveyardSlot) is not { Definition.TypeId: RoomTypeId.Graveyard } room)
            return null;
        corpse.GraveyardSlot = -1;
        return Bury(context, room, context.Database.CreatureById(corpse.CreatureTypeId), corpse.Level);
    }

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
