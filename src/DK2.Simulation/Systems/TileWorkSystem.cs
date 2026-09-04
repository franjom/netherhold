// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>
/// Resolves the work applications queued on the world this tick, in queue
/// order, and clears the queue.
///
/// The system owns no policy. It does not decide who works, what they work on,
/// or how often — those are creature decisions (M3, later) and a timing question
/// (Q19/Q1). All it does is turn "one application of kind K by player P on tile
/// I" into the health change and terrain transition that DK2's data says it is,
/// via <see cref="TileWorkRules"/>.
///
/// <para>
/// Gold released by mining goes into the hands of the creature named on the
/// order — CONFIRMED (Q31): in DK2 the imp that freed the gold is the imp that
/// carries it away, and <see cref="ImpTaskSystem"/> is what does the carrying.
/// Work with no creature behind it — a test, the harness — still has its gold
/// reported and dropped, because nothing was there to pick it up.
/// </para>
///
/// <para>
/// An application that frees more than the worker's hands will hold has its
/// remainder reported as uncollected and dropped. With the shipped numbers a
/// worker never gets into that position, because it stops mining before a swing
/// that would overflow it; the branch exists so that data which broke that
/// assumption would show up in the event log rather than silently inflate the
/// economy.
/// </para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
public sealed class TileWorkSystem : ISimulationSystem
{
    private const string SystemName = "TileWork";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        if (world.PendingWork.Count == 0) return;

        // Snapshot the count first: resolving an order must not be able to make
        // the loop consume work queued by the same tick's later systems.
        int count = world.PendingWork.Count;

        for (int i = 0; i < count; i++)
        {
            TileWorkOrder order = world.PendingWork[i];
            ref TileState tile = ref world.TileAtIndex(order.TileIndex);
            byte before = tile.TerrainId;

            TileWorkResult result = TileWorkRules.Apply(
                context.Database, ref tile, order.Kind, order.PlayerId);

            int carried = HandTo(context, order, result.GoldReleased);
            Report(context, order, result, before, world, carried);
        }

        world.RawPendingWork.RemoveRange(0, count);
    }

    /// <summary>
    /// Puts freed gold into the hands of whoever swung, and says how much fitted.
    ///
    /// The creature is looked up rather than passed in because the queue is the
    /// only thing that connects a swing to a swinger, and the queue survives a
    /// snapshot: an order is a fact about the world, not a reference to an
    /// object.
    /// </summary>
    private static int HandTo(SimulationContext context, TileWorkOrder order, int goldReleased)
    {
        if (goldReleased <= 0 || order.WorkerCreatureId < 0) return 0;

        CreatureState? worker = context.World.CreatureById(order.WorkerCreatureId);
        if (worker is null) return 0;

        int capacity = context.Database.CreatureById(worker.CreatureTypeId).MaxGoldHeld;
        int taken = Math.Max(0, Math.Min(capacity - worker.GoldHeld, goldReleased));
        worker.GoldHeld += taken;
        return taken;
    }

    private static void Report(
        SimulationContext context,
        TileWorkOrder order,
        in TileWorkResult result,
        byte terrainBefore,
        WorldState world,
        int goldCarried)
    {
        int x = order.TileIndex % world.Width;
        int y = order.TileIndex / world.Width;
        string where = $"({x},{y}) player={order.PlayerId} {order.Kind}";

        switch (result.Outcome)
        {
            case TileWorkOutcome.Rejected:
                context.Emit(SystemName, "rejected",
                    $"{where} terrain={terrainBefore}");
                return;

            case TileWorkOutcome.NoChange:
                return;

            case TileWorkOutcome.Destroyed:
                context.Emit(SystemName, "destroyed",
                    $"{where} terrain {terrainBefore}->{result.NewTerrainId}");
                break;

            case TileWorkOutcome.Transformed:
                context.Emit(SystemName, "transformed",
                    $"{where} terrain {terrainBefore}->{result.NewTerrainId}");
                break;

            case TileWorkOutcome.Progressed:
            default:
                break;
        }

        if (goldCarried > 0)
        {
            CreatureState? worker = context.World.CreatureById(order.WorkerCreatureId);
            context.Emit(SystemName, "gold_mined",
                $"{where} amount={goldCarried} " +
                $"imp={order.WorkerCreatureId} carrying={worker?.GoldHeld ?? 0}");
        }

        int uncollected = result.GoldReleased - goldCarried;
        if (uncollected > 0)
        {
            context.Emit(SystemName, "gold_uncollected",
                $"{where} amount={uncollected} " +
                (order.WorkerCreatureId < 0
                    ? "(nobody swung: gold piles are objects, not implemented)"
                    : "(would not fit in the worker's hands)"));
        }

        if (result.GoldSpilled > 0)
        {
            context.Emit(SystemName, "gold_spilled",
                $"{where} amount={result.GoldSpilled} (lost: gold piles are objects, not implemented)");
        }
    }
}
