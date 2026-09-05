// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// What a worker may pick up off the floor, and where it must take it.
///
/// <para>The owner described the whole pipeline in one breath (Q99), and both
/// halves of it end here:</para>
///
/// <code>
///   health -> 0
///      |
///   COLLAPSED   CreatureDyingStateDurationSeconds = 60
///      |        an imp carries it to its own bed, and it heals there
///      v
///   CORPSE      DeadBodyDiesAfterSeconds = 60
///      |        an imp carries it to a graveyard
///      v
///   gone
/// </code>
///
/// <para>Both timers had been parsed for months with nothing able to read
/// either, and the named reason was <i>"a corpse is an object"</i> — which was
/// checked in the end and found to be false (<c>objects.md</c> §4). None of the
/// 145 objects is a corpse. <b>A corpse is a creature in a state</b>, so what
/// was missing was never a record: it was this, an imp task that carries a
/// creature.</para>
///
/// <para>DK2's own imp task table has one kind for each
/// (<c>imp-task-priority.md</c> §4): <b>kind 13</b>, carry an unconscious
/// creature — its keeper's own to a Lair, an enemy's to a Prison — and
/// <b>kind 12</b>, carry a corpse to the Graveyard. They are the last two
/// entries of Tier A, below claiming and above everything found by distance.</para>
/// </summary>
public static class CarryRules
{
    /// <summary>
    /// Whether <paramref name="body"/> is something a worker could pick up at
    /// all — collapsed and rescuable, or dead and carryable.
    ///
    /// <para>Says nothing about whether there is anywhere to take it. That is
    /// <see cref="TryDestinationFor"/>, and the two are separate because a body
    /// with nowhere to go is a body nobody should walk to.</para>
    /// </summary>
    public static bool IsCarryable(CreatureState body) =>
        !body.InHand
        && body.CarriedByCreatureId < 0
        && (CreatureDeathRules.IsDying(body) || CreatureDeathRules.IsCorpse(body));

    /// <summary>
    /// Whether <paramref name="worker"/> may carry <paramref name="body"/>.
    ///
    /// <para><b>A carrier carries one thing.</b> DK2's imp has one carry slot
    /// and this has one field; a worker already holding a body is not offered
    /// another, and a worker with gold in its hands is running to a treasury
    /// (Q31, ADR-0018) rather than weighing anything.</para>
    /// </summary>
    public static bool CanCarry(CreatureDef definition, CreatureState worker, CreatureState body)
    {
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)) return false;
        if (worker.Id == body.Id) return false;
        if (worker.CarriedCreatureId >= 0) return false;
        if (worker.GoldHeld > 0) return false;
        if (HandRules.IsHeld(worker) || CreatureDeathRules.IsDown(worker)) return false;

        return IsCarryable(body);
    }

    /// <summary>
    /// Where <paramref name="body"/> has to go, as a tile index, or -1 when
    /// there is nowhere to take it.
    ///
    /// <list type="bullet">
    /// <item><b>Collapsed, the carrier's keeper's own</b> — to its <i>own
    /// bed</i>, which is where it recovers. Not to any free lair tile: the bed
    /// is a placed object that belongs to this creature and it kept that bed
    /// through the collapse precisely so this walk would have a destination
    /// (<see cref="CreatureDeathRules.Collapse"/>).</item>
    /// <item><b>Collapsed, somebody else's</b> — to a <b>Prison</b>, which does
    /// not exist here. Returns -1, and the worker leaves it lying (Q163).</item>
    /// <item><b>A corpse</b> — to the nearest reachable tile of one of the
    /// keeper's <b>Graveyards</b>.</item>
    /// </list>
    /// </summary>
    public static int TryDestinationFor(
        WorldState world, GameDatabase database, CreatureState worker, CreatureState body)
    {
        if (CreatureDeathRules.IsCorpse(body))
            return NearestGraveyardTile(world, database, worker);

        // A collapsed enemy belongs in a prison, and there are none. DK2 has an
        // imp task for it (kind 13's other branch, room 11) and this project has
        // no prison at all, so an enemy left on the floor here simply dies where
        // it fell rather than being quietly rescued into the wrong dungeon.
        if (body.OwnerPlayerId != worker.OwnerPlayerId) return -1;

        return LairRules.BedTileIndex(world, database, body);
    }

    /// <summary>
    /// The keeper's nearest Graveyard tile by straight-line distance, or -1.
    ///
    /// <para>Straight-line rather than walked, which is how the original
    /// measures rooms everywhere else this project has read it — the lair
    /// finder does the same (ADR-0060). Whether a route exists is settled by the
    /// pathfinder afterwards, at the one place that can answer it.</para>
    /// </summary>
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

                // Ties go to the lower tile index, which is row-major order and
                // is the order RoomInstance already keeps its tiles in. Any
                // stable rule would do; this one costs nothing and is the same
                // one every other room walk here uses.
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = tileIndex;
            }
        }

        return best;
    }

    /// <summary>
    /// Puts <paramref name="body"/> into <paramref name="worker"/>'s arms.
    ///
    /// <para>The body keeps every clock it was running. <b>The rescue window
    /// does not pause for being carried</b>, so an imp that has too far to walk
    /// arrives with a corpse — which is a real outcome rather than an oversight,
    /// and is what makes a lair near the fighting worth building. UNKNOWN
    /// whether the original pauses it (Q164).</para>
    /// </summary>
    public static void PickUp(CreatureState worker, CreatureState body)
    {
        worker.CarriedCreatureId = body.Id;
        body.CarriedByCreatureId = worker.Id;

        // A carried body stops going anywhere of its own accord. It has no path
        // to begin with - collapsing clears it - but a corpse that was walking
        // when it died would otherwise keep its route.
        body.Path.Clear();
        body.PathIndex = 0;
    }

    /// <summary>Takes <paramref name="body"/> out of <paramref name="worker"/>'s arms.</summary>
    public static void PutDown(CreatureState worker, CreatureState body)
    {
        worker.CarriedCreatureId = -1;
        body.CarriedByCreatureId = -1;
    }

    /// <summary>
    /// Moves a carried body to wherever its carrier is.
    ///
    /// <para>Called after the mover has run, so a body is never a tick behind
    /// the arms holding it — which matters because <c>LairRules</c> and the
    /// graveyard both ask what tile it is standing on.</para>
    /// </summary>
    public static void FollowCarrier(CreatureState worker, CreatureState body)
    {
        body.PosX = worker.PosX;
        body.PosY = worker.PosY;
    }
}
