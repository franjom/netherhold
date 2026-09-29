// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a worker may pick up off the floor, and where it takes it.</summary>
/// <remarks>
/// <para>The two halves of the owner's death pipeline (Q99,
/// <see cref="CreatureDeathRules"/>): a collapsed creature to its own bed inside
/// <c>CreatureDyingStateDurationSeconds</c> (60), where it heals; a corpse to a
/// graveyard inside <c>DeadBodyDiesAfterSeconds</c> (also 60).</para>
/// <para>DK2's imp task table has one kind for each (imp-task-priority.md §4): kind 13, carry an
/// unconscious creature — its keeper's own to a Lair, an enemy's to a Prison — and kind 12, carry a
/// corpse to the Graveyard. They are the last two entries of Tier A, below claiming and above
/// everything found by distance.</para></remarks>
public static class CarryRules
{
    /// <summary>Whether <paramref name="body"/> is something a worker could pick up at all —
    /// collapsed and rescuable, or dead and carryable. Says nothing about whether there is anywhere
    /// to take it; that is <see cref="TryDestinationFor"/>, kept separate because a body with
    /// nowhere to go is a body nobody should walk to.</summary>
    public static bool IsCarryable(CreatureState body) =>
        !body.InHand
        && body.CarriedByCreatureId < 0
        && !PrisonRules.IsImprisoned(body)
        && (CreatureDeathRules.IsDying(body) || CreatureDeathRules.IsCorpse(body));

    /// <summary>Whether <paramref name="worker"/> may carry <paramref name="body"/>. A carrier
    /// carries one thing, DK2's imp having one carry slot: a worker already holding a body is not
    /// offered another, and one with gold in its hands is running to a treasury (Q31,
    /// ADR-0018).</summary>
    public static bool CanCarry(CreatureDef definition, CreatureState worker, CreatureState body)
    {
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)) return false;
        if (worker.Id == body.Id) return false;
        if (worker.CarriedCreatureId >= 0) return false;
        if (worker.GoldHeld > 0) return false;
        if (HandRules.IsHeld(worker) || CreatureDeathRules.IsDown(worker)) return false;

        return IsCarryable(body);
    }

    /// <summary>Where <paramref name="body"/> goes, as a tile index, or -1 for nowhere.
    /// <list type="bullet">
    /// <item>Collapsed, the carrier's keeper's own — its own bed, not any free lair tile; it kept
    /// that bed through the collapse so this walk would have a destination
    /// (<see cref="CreatureDeathRules.Collapse"/>).</item>
    /// <item>Collapsed, somebody else's — a free cell in the worker's keeper's nearest Prison,
    /// or the rim of a full one (<see cref="PrisonRules.DeliveryTileIn"/>, ADR-0064).</item>
    /// <item>A corpse — the keeper's nearest Graveyard tile.</item></list></summary>
    public static int TryDestinationFor(
        WorldState world, GameDatabase database, CreatureState worker, CreatureState body)
    {
        if (CreatureDeathRules.IsCorpse(body))
            return NearestGraveyardTile(world, database, worker);

        // A collapsed enemy goes to the WORKER's keeper's prison, not the
        // body's (kind 13's other branch). A keeper with no prison leaves it
        // lying where it fell; a keeper whose prisons are all full still sends
        // the imp, and the refusal happens at the door (ADR-0148).
        if (body.OwnerPlayerId != worker.OwnerPlayerId)
        {
            int cell = PrisonRules.DeliveryTileIn(
                world, database, worker.OwnerPlayerId, worker.Tile);

            // Ours: a body already lying at a prison that will not take it stays
            // there, rather than being carried round and round the room it was
            // refused at. The end state is the owner's - left on the rim.
            if (cell >= 0
                && !PrisonRules.AdmitsAt(world, database, worker.OwnerPlayerId, cell)
                && PrisonRules.StandsInPrisonOf(world, database, body, worker.OwnerPlayerId))
                return -1;

            return cell;
        }

        return LairRules.BedTileIndex(world, database, body);
    }

    /// <summary>The keeper's nearest Graveyard tile by straight-line distance, or -1. Straight-line
    /// rather than walked, which is how the original measures rooms everywhere else this project has
    /// read it — the lair finder does the same (ADR-0060). Whether a route exists is settled by the
    /// pathfinder afterwards, at the one place that can answer it.</summary>
    public static int NearestGraveyardTile(
        WorldState world, GameDatabase database, CreatureState worker)
    {
        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != worker.OwnerPlayerId) continue;
            if (room.Definition.TypeId != RoomTypeId.Graveyard) continue;

            foreach (int tileIndex in room.TileIndices)
            {
                long dx = (tileIndex % world.Width) - worker.Tile.X;
                long dy = (tileIndex / world.Width) - worker.Tile.Y;
                long distance = (dx * dx) + (dy * dy);

                // Ties go to the lower tile index (row-major), the order RoomInstance keeps its
                // tiles in and every other room walk here uses.
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = tileIndex;
            }
        }

        return best;
    }

    /// <summary>Puts <paramref name="body"/> into <paramref name="worker"/>'s arms. The body keeps
    /// every clock it was running: the rescue window does not pause for being carried, so an imp
    /// with too far to walk arrives with a corpse. UNKNOWN whether the original pauses it
    /// (Q164).</summary>
    public static void PickUp(CreatureState worker, CreatureState body)
    {
        worker.CarriedCreatureId = body.Id;
        body.CarriedByCreatureId = worker.Id;

        // A corpse that was walking when it died would otherwise keep its route; collapsing
        // already clears the path.
        body.StopMoving();
    }

    /// <summary>Takes <paramref name="body"/> out of <paramref name="worker"/>'s arms.</summary>
    public static void PutDown(CreatureState worker, CreatureState body)
    {
        worker.CarriedCreatureId = -1;
        body.CarriedByCreatureId = -1;
    }

    /// <summary>Releases a dragged creature at its carrier's current position on cleanup.</summary>
    public static void DropWhatever(WorldState world, CreatureState worker)
    {
        if (worker.CarriedCreatureId < 0) return;
        CreatureState? body = world.CreatureById(worker.CarriedCreatureId);
        if (body is not null)
        {
            FollowCarrier(worker, body);
            PutDown(worker, body);
        }
        else worker.CarriedCreatureId = -1;
    }

    /// <summary>Moves a carried body to wherever its carrier is. Called after the mover has run, so
    /// a body is never a tick behind the arms holding it — which matters because <c>LairRules</c>
    /// and the graveyard both ask what tile it is standing on.</summary>
    public static void FollowCarrier(CreatureState worker, CreatureState body)
    {
        body.PosX = worker.PosX;
        body.PosY = worker.PosY;
    }
}
