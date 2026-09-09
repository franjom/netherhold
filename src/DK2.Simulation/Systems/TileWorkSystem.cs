// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>Resolves the work applications queued on the world this tick, in queue
/// order, and clears the queue.</summary>
/// <remarks>No policy here: who works, on what, and how often are creature decisions
/// and a timing question (Q19/Q1). This turns "one application of kind K by player P
/// on tile I" into the health change and terrain transition DK2's data says it is,
/// via <see cref="TileWorkRules"/>. Gold released by mining goes into the hands of
/// the creature named on the order — CONFIRMED (Q31): the imp that freed the gold is
/// the imp that carries it away, and <see cref="ImpTaskSystem"/> does the carrying.
/// Work with no creature behind it (a test, the harness) has its gold reported and
/// dropped, as does any remainder past what a worker's hands hold — with the shipped
/// numbers a worker stops before a swing that would overflow it, so that branch
/// exists to make data which broke the assumption show up in the log.</remarks>
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

            // The room this tile belongs to, read BEFORE the work lands and only
            // for the one kind of work whose outcome is room-scoped. It has to be
            // before: a room instance is tiles of one terrain with one owner
            // (ADR-0027), so the moment the worked tile changes hands it becomes
            // a room of its own and the rest of the room is unreachable from it.
            RoomInstance? convertingRoom = TileWorkRules.CompletesAsTakeover(order.Kind)
                ? RoomIndex.RoomAt(world, context.Database, order.TileIndex)
                : null;

            ref TileState tile = ref world.TileAtIndex(order.TileIndex);
            byte before = tile.TerrainId;

            TileWorkResult result = TileWorkRules.Apply(
                context.Database, ref tile, order.Kind, order.PlayerId,
                EfficiencyOf(context, order.WorkerCreatureId));

            if (convertingRoom is not null && result.Outcome == TileWorkOutcome.Transformed)
                SpreadTakeover(context, order, convertingRoom);

            // A tile that has just changed terrain is freshly dug exactly when
            // the work that changed it was excavation (Q154). A claim, a
            // conversion, an attacker breaking it: all of those change the
            // terrain too, and all of them end its being fresh.
            if (result.Outcome is TileWorkOutcome.Destroyed or TileWorkOutcome.Transformed)
                tile.FreshlyDug = result.Outcome == TileWorkOutcome.Destroyed && IsExcavation(order.Kind);

            CreditGoldMined(context, order, result.GoldReleased);
            int carried = HandTo(context, order, result.GoldReleased);
            Report(context, order, result, before, world, carried);
        }

        world.RawPendingWork.RemoveRange(0, count);
    }

    /// <summary>
    /// The Efficiency of whoever swung, or the default 80% when nothing did.
    ///
    /// <para>Work queued by a test or by the harness has no worker
    /// (<see cref="TileWorkOrder.WorkerCreatureId"/> of -1) and gets the rate
    /// every worker is born at, so every measurement this project took before
    /// Efficiency existed still holds exactly.</para>
    /// </summary>
    private static int EfficiencyOf(SimulationContext context, int workerCreatureId)
    {
        if (workerCreatureId < 0) return EfficiencyRules.BasePercent;

        CreatureState? worker = context.World.CreatureById(workerCreatureId);
        if (worker is null) return EfficiencyRules.BasePercent;

        return EfficiencyRules.Percent(
            worker, context.Database.CreatureById(worker.CreatureTypeId));
    }

    /// <summary>Hands the rest of a converted room to its new owner, all at
    /// once.</summary>
    /// <remarks>CONFIRMED (owner, 2026-09-04): "rooms are instantly claimed by imps,
    /// not tile by tile" — one tile is worked and the room changes hands, so a keeper
    /// never claims a portal nine times or leaves one half-taken. Only the owner is
    /// written, not the terrain or the health: the worked tile had its health,
    /// accumulated work and remainder reset by <see cref="TileWorkRules.Apply"/>,
    /// while the others were never worked, so resetting theirs would repair a room by
    /// taking it — a rival who chipped at your treasury and then claims it gets the
    /// damage with it, as a claimed burning bridge keeps burning (Q68). Gold is left
    /// where it is: a treasury's contents change hands with the treasury (Q40), and
    /// nothing spills because capacity belongs to the terrain, which did not change.</remarks>
    private static void SpreadTakeover(
        SimulationContext context, TileWorkOrder order, RoomInstance room)
    {
        WorldState world = context.World;
        int spread = 0;

        foreach (int index in room.TileIndices)
        {
            if (index == order.TileIndex) continue;

            ref TileState other = ref world.TileAtIndex(index);
            if (other.OwnerPlayerId == order.PlayerId) continue;

            other.OwnerPlayerId = order.PlayerId;
            spread++;
        }

        context.Emit(SystemName, "room_converted",
            $"({order.TileIndex % world.Width},{order.TileIndex / world.Width}) " +
            $"player={order.PlayerId} {room.Definition.Name} " +
            $"tiles={room.TileCount} spread={spread} from={room.OwnerPlayerId}");
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

    /// <summary>
    /// Whether this kind of work can only have come from a dig marker — the
    /// same test <see cref="ImpTaskSystem"/> makes, read off
    /// <see cref="TileWorkRules.TaggedWork"/> so a kind added to the marker
    /// mapping cannot fall out of it.
    /// </summary>
    private static bool IsExcavation(TileWorkKind kind)
    {
        IReadOnlyList<TileWorkKind> tagged = TileWorkRules.TaggedWork;
        for (int i = 0; i < tagged.Count; i++)
            if (tagged[i] == kind) return true;
        return false;
    }

    /// <summary>Adds what this swing freed from the seam to the swinger's keeper.</summary>
    /// <remarks>
    /// <para>Rule: DK2 credits <c>[creature+0x2A]</c> — the <b>miner's owner</b>, not
    /// whoever ordered the work — at <c>0x004973CF</c>, and does it before anything asks
    /// whether the imp can carry the gold. So a full imp still adds to the total, and the
    /// uncollected gold reported below is counted here. `player-object.md`.</para>
    /// <para>No worker, no credit: DK2's adder lives inside the creature's mining routine,
    /// so gold a test or the harness releases with <c>WorkerCreatureId</c> -1 has no
    /// keeper to credit.</para>
    /// </remarks>
    private static void CreditGoldMined(
        SimulationContext context, TileWorkOrder order, int released)
    {
        if (released <= 0) return;
        CreatureState? miner = context.World.CreatureById(order.WorkerCreatureId);
        if (miner is null || !context.World.HasPlayer(miner.OwnerPlayerId)) return;
        context.World.Player(miner.OwnerPlayerId).GoldMined += released;
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
                    ? "(nobody swung)"
                    : "(would not fit in the worker's hands)") +
                " (Q138: whether the original leaves this on the floor is unread)");
        }

        // A store that stopped being one keeps its contents, on the floor of the
        // tile it stood on. That is the original's own answer and it is exact:
        // for every treasury pile in the room it makes a floor pile on the same
        // tile, moves the amount across, and takes it off the player's total
        // (gold-on-the-floor.md section 4). Nothing is clamped on the way, so a
        // tile holding 3000 becomes a pile above MaxGoldPileOutsideTreasury.
        if (result.GoldSpilled > 0)
        {
            FloorGold.Drop(context.World, context.Database, order.TileIndex, result.GoldSpilled);
            context.Emit(SystemName, "gold_spilled",
                $"{where} amount={result.GoldSpilled} (now a pile on the floor)");
        }
    }
}
