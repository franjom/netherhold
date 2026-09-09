// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>DK2's imp task kind 8: carry a loose object to the room it belongs in — a book back to
/// a library, a crate back to a workshop. Kinds 13 and 12 carry creatures instead, the unconscious
/// to a bed or a cell and the dead to a graveyard (ADR-0063, ADR-0064).</summary>
/// <remarks>
/// <para>It sits second in DK2's Tier A table, <c>14, 8, 9, 1, 3, 13, 12</c> (imp-task-priority.md
/// §4), above marked excavation and above claiming: a keeper who marks a wall and knocks a book
/// onto the floor gets the book put away first.</para>
/// <para>DK2 keeps the carry slot separate from the drag slot — its creature dump names
/// <c>CarryingId</c> at <c>0x3c3</c> and <c>DraggingId</c> at <c>0x3c5</c>, an object being carried
/// and a creature dragged. Reproduced as <see cref="CreatureState.CarriedObjectId"/> and
/// <see cref="CreatureState.CarriedCreatureId"/>, so an imp cannot hold a book and a body at once.</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/objects.md")]
public static class ObjectCarryRules
{
    /// <summary>The room an object of this type belongs in, or null when no room claims it.</summary>
    /// <remarks>
    /// <para>Read backwards out of <c>Rooms.kwd</c>: each room record carries an <c>ObjectIds</c>
    /// list — Library 64 and 58, Workshop 16, 15, 46, Prison 49, 50, 51, Graveyard 31-34 (Q120) —
    /// and this walks them to find the room that lists a given object. The original goes the other
    /// way, looking a room up from the object's own definition, so there is presumably a room field
    /// in the OBJECTS record this project has not identified (as for a bed's owner, Q130).</para>
    /// <para>The mapping is not one-to-one and the first room in table order wins: objects 49 and 50
    /// are listed by both the Prison and the Combat Pit, and the Torture Chamber lists object 8
    /// three times. Which room the original would pick is UNKNOWN (Q169); a stable rule — the lower
    /// room id — is what matters here.</para></remarks>
    public static RoomTypeId? HomeRoomFor(GameDatabase database, byte objectTypeId)
    {
        RoomTypeId? best = null;

        foreach (RoomDef room in database.Rooms.Values)
        {
            if (room.TypeId is not { } type) continue;
            if (!room.ObjectIds.Contains(objectTypeId)) continue;
            if (best is null || (byte)type < (byte)best) best = type;
        }

        return best;
    }

    /// <summary>Whether this object is lying loose and belongs somewhere — what kind 8 looks for.
    /// DK2's test is objects owned by nobody, standing on the imp's own floor or on water. "Owned
    /// by nobody" separates a loose book from one already shelved, and it is why a bed and a
    /// treasury's gold are never candidates: a bed belongs to the creature that put it down, and a
    /// floor pile is owned by the level's floor-gold id, which is kind 7's business (Q144).</summary>
    public static bool IsLoose(GameDatabase database, ObjectState thing) =>
        thing.OwnerPlayerId == PlayerIds.Unowned
        && thing.CarriedByCreatureId < 0
        && thing.CreatureId == 0
        && thing.GoldHeld == 0
        && HomeRoomFor(database, thing.ObjectTypeId) is not null;

    /// <summary>Whether <paramref name="worker"/> may pick <paramref name="thing"/> up. The same
    /// shape and the same one-slot rule as <see cref="CarryRules.CanCarry"/>: a worker already
    /// carrying an object is not offered another, and one carrying gold or a body is doing
    /// something else.</summary>
    public static bool CanCarry(
        GameDatabase database, CreatureDef definition, CreatureState worker, ObjectState thing)
    {
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)) return false;
        if (worker.CarriedObjectId >= 0) return false;
        if (worker.CarriedCreatureId >= 0) return false;
        if (worker.GoldHeld > 0) return false;
        if (HandRules.IsHeld(worker) || CreatureDeathRules.IsDown(worker)) return false;
        if (PrisonRules.IsImprisoned(worker)) return false;

        return IsLoose(database, thing);
    }

    /// <summary>A tile with nothing standing on it in <paramref name="playerId"/>'s nearest room of
    /// the right kind, or -1 when there is nowhere to put it. Chosen before the walk starts, like a
    /// bed, a cell and a graveyard tile, so an imp with nowhere to put a book does not cross the
    /// dungeon first. One object per tile is ours, the same occupancy rule every room here uses
    /// (Q166): DK2 stores <c>UsedObjectCapacity</c> on the room and each object definition carries
    /// its own <c>RoomCapacity</c>, so the original's accounting is finer and has not been read.</summary>
    public static int FreeTileFor(
        WorldState world, GameDatabase database, byte playerId, RoomTypeId type, TilePos from)
    {
        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != playerId) continue;
            if (room.Definition.TypeId != type) continue;

            foreach (int tileIndex in room.TileIndices)
            {
                if (ObjectOn(world, tileIndex) is not null) continue;

                long dx = (tileIndex % world.Width) - from.X;
                long dy = (tileIndex / world.Width) - from.Y;
                long distance = (dx * dx) + (dy * dy);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = tileIndex;
            }
        }

        return best;
    }

    /// <summary>The object standing on one tile, or null. Ties go to the lower id.</summary>
    public static ObjectState? ObjectOn(WorldState world, int tileIndex)
    {
        ObjectState? best = null;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.TileIndex != tileIndex) continue;
            if (thing.CarriedByCreatureId >= 0) continue;
            if (best is null || thing.Id < best.Id) best = thing;
        }
        return best;
    }

    /// <summary>Where <paramref name="thing"/> has to go, as a tile index, or -1.</summary>
    public static int TryDestinationFor(
        WorldState world, GameDatabase database, CreatureState worker, ObjectState thing)
    {
        if (HomeRoomFor(database, thing.ObjectTypeId) is not { } type) return -1;
        return FreeTileFor(world, database, worker.OwnerPlayerId, type, worker.Tile);
    }

    /// <summary>Puts <paramref name="thing"/> into <paramref name="worker"/>'s arms.</summary>
    public static void PickUp(CreatureState worker, ObjectState thing)
    {
        worker.CarriedObjectId = thing.Id;
        thing.CarriedByCreatureId = worker.Id;
    }

    /// <summary>Takes it back out, wherever it happens to be standing.</summary>
    public static void PutDown(CreatureState worker, ObjectState thing)
    {
        worker.CarriedObjectId = -1;
        thing.CarriedByCreatureId = -1;
    }

    /// <summary>Moves a carried object to wherever its carrier is standing. An object has a tile and
    /// not a position, so this is coarser than <see cref="CarryRules.FollowCarrier"/> — which is
    /// right: nothing asks where within a tile a book is.</summary>
    public static void FollowCarrier(WorldState world, CreatureState worker, ObjectState thing) =>
        thing.TileIndex = (worker.Tile.Y * world.Width) + worker.Tile.X;

    /// <summary>Makes <paramref name="worker"/> let go of whatever it was carrying, wherever it is
    /// standing, and says whether it was holding anything. Called wherever a worker stops being a
    /// worker mid-job — picked up by the keeper, killed, put in a cell. The object stays loose: it
    /// is on the floor where the imp was, and the next imp along will take it to the same
    /// room.</summary>
    public static bool DropWhatever(WorldState world, CreatureState worker)
    {
        worker.TargetObjectId = -1;
        if (worker.CarriedObjectId < 0) return false;

        ObjectState? thing = world.ObjectById(worker.CarriedObjectId);
        if (thing is null)
        {
            worker.CarriedObjectId = -1;
            return false;
        }

        FollowCarrier(world, worker, thing);
        PutDown(worker, thing);
        return true;
    }

    /// <summary>Puts an object down in a room and makes it that room's: the object's owner becomes
    /// the keeper, which is what takes it out of <see cref="IsLoose"/> and stops it being fetched
    /// again for ever.</summary>
    public static void Deliver(
        CreatureState worker, ObjectState thing, byte playerId, int tileIndex)
    {
        PutDown(worker, thing);
        thing.TileIndex = tileIndex;
        thing.OwnerPlayerId = playerId;
    }
}
