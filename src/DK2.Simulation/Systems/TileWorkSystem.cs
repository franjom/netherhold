// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

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
    private const byte ReinforcedWallTerrainId = 9;

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
            byte before = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
            bool wasTagged = tile.IsTaggedBy(order.PlayerId);

            TileWorkResult result;
            if (TileWorkRules.IsRoomScoped(order.Kind))
            {
                // The room's one health takes the work (0x004E5300); a room taken whole keeps its
                // chain, so the slab chain is not replayed.
                result = WorkRoom(context, order);
            }
            else
            {
                result = TileWorkRules.Apply(
                    context.Database, ref tile, order.Kind, order.PlayerId,
                    EfficiencyOf(context, order.WorkerCreatureId));

                // Anything that made or unmade a room tile, a rival breaking one down, goes
                // through the chain's own insert and replay.
                SlabChain.Changed(world, context.Database, order.TileIndex, before, ownerBefore);
                if (order.Kind == TileWorkKind.AttackTile && result.Outcome == TileWorkOutcome.Destroyed)
                    WallSupportRules.ReleaseUnsupported(world, context.Database,
                        new TilePos(order.TileIndex % world.Width, order.TileIndex / world.Width));
            }

            // Any change of terrain ends a kind-14 task: a claim, a conversion, an attacker. A dig
            // or a mine that leaves nothing to dig keeps its task in the pool; the digger raises
            // kind 14 from it when its action ends, if no dig walk dropped it first (ADR-0305).
            if (result.Outcome is TileWorkOutcome.Destroyed or TileWorkOutcome.Transformed)
            {
                tile.FreshlyDug = false;
                if (result.Outcome == TileWorkOutcome.Destroyed && IsExcavation(order.Kind)
                    && wasTagged && !tile.IsTaggedBy(order.PlayerId))
                    tile.DigTaskOutlivesWallFor = tile.DigTaskOutlivesWallFor.With(order.PlayerId);
            }

            // Each work action pays its maker once per hit whose tile test passed; the room attack
            // (action 11, 0x00497DF0) pays only a Good creature (ADR-0386).
            if (result.Outcome != TileWorkOutcome.Rejected
                && (order.Kind != TileWorkKind.AttackRoom || order.PlayerId == PlayerIds.Good)
                && world.CreatureById(order.WorkerCreatureId) is { } worker)
                CreatureExperienceSystem.AwardWork(context, worker);

            if (TileWorkRules.CompletesUpward(order.Kind) && result.Outcome == TileWorkOutcome.Transformed)
            {
                EffectId effect = context.Database.TerrainById(before).MaxHealthEffectId;
                var at = new TilePos(order.TileIndex % world.Width, order.TileIndex / world.Width);
                if (tile.TerrainId == ReinforcedWallTerrainId)
                {
                    TilePos[] points = [new(at.X - 1, at.Y), new(at.X, at.Y - 1),
                        new(at.X + 1, at.Y), new(at.X, at.Y + 1)];
                    foreach (TilePos point in points)
                        context.RequestEffect(SystemName, ViewEffectRequest.AtTileCentre(
                            effect, order.PlayerId, point, new Fp32(0)));
                }
                else
                    context.RequestEffect(SystemName, ViewEffectRequest.AtTileCentre(
                        effect, order.PlayerId, at, new Fp32(Fp32.One)));
            }
            PostThreat(context, order, result, before, ownerBefore);
            if (result.Outcome == TileWorkOutcome.Destroyed && before != tile.TerrainId)
                context.RequestEffect(SystemName, ViewEffectRequest.AtTileCentre(
                    context.Database.TerrainById(before).DestroyedEffectId, order.PlayerId,
                    new TilePos(order.TileIndex % world.Width, order.TileIndex / world.Width), new Fp32(Fp32.One)));
            CreditGoldMined(context, order, result.GoldReleased);
            int carried = HandTo(context, order, result.GoldReleased, out int putDown);
            Report(context, order, result, before, world, carried, putDown);
        }

        world.RawPendingWork.RemoveRange(0, count);
    }

    /// <summary>The keeper messages tile work raises: 12 to the owner of a Solid, Ownable tile another
    /// player's work brought down (<c>0x0045313E</c>, tile x and y), and 34 to the owner of a room a
    /// Good creature's blow left standing (action 11, <c>0x00497F29</c>: room type, room).</summary>
    /// <remarks>Not built: the player events <c>0x38</c>/<c>0x39</c> that follow 12, and 36/37 for a
    /// blow that unmade the tile.</remarks>
    private static void PostThreat(
        SimulationContext context, TileWorkOrder order, TileWorkResult result, byte before, PlayerId ownerBefore)
    {
        WorldState world = context.World;
        int x = order.TileIndex % world.Width, y = order.TileIndex / world.Width;
        if (result.Outcome == TileWorkOutcome.Destroyed && ownerBefore != order.PlayerId && world.HasPlayer(ownerBefore))
        {
            TerrainDef terrain = context.Database.TerrainById(before);
            if (terrain.IsSolid && terrain.IsOwnable)
                world.Messages.Post(world, ownerBefore.Value, PlayerMessageKind.WallBreached, x, y);
        }

        if (order.Kind == TileWorkKind.AttackRoom && order.PlayerId == PlayerIds.Good
            && RoomIndex.RoomAt(world, context.Database, order.TileIndex) is RoomInstance room
            && world.HasPlayer(room.OwnerPlayerId))
            world.Messages.PostOwnership(world, room.OwnerPlayerId, PlayerMessageKind.RoomAttacked,
                (RoomTypeId)room.Definition.RoomId, SlabChain.FirstSlab(world, room));
    }

    /// <summary>
    /// The Efficiency of whoever swung, or the default 80% when nothing did.
    ///
    /// <para>Work queued by a test or by the harness has no worker
    /// (<see cref="TileWorkOrder.WorkerCreatureId"/> of -1) and gets the rate
    /// every worker is born at, so every measurement this project took before
    /// Efficiency existed still holds exactly.</para>
    /// </summary>
    internal static int EfficiencyOf(SimulationContext context, int workerCreatureId)
    {
        if (workerCreatureId < 0) return EfficiencyRules.BaseModifier;

        CreatureState? worker = context.World.CreatureById(workerCreatureId);
        if (worker is null) return EfficiencyRules.BaseModifier;

        return EfficiencyRules.Modifier(
            worker, context.Database.CreatureById(worker.CreatureTypeId));
    }

    /// <summary>One application of room-scoped work against the room under the tile: action 10's
    /// conversion (<c>0x00497BF0</c>), at the rival or the neutral rate by the tile's owner, or
    /// action 12's repair (<c>0x00498120</c>).</summary>
    /// <remarks>CONFIRMED (owner, 2026-09-04, and the captures): one tile is worked and the room
    /// changes hands whole when its health falls below 1, so a portal is never half-taken. No tile's
    /// terrain or health is written: a rival who chipped at your treasury's tiles hands them over
    /// chipped, as a claimed burning bridge keeps burning (Q68), and a treasury's gold changes
    /// hands with it (Q40).</remarks>
    private static TileWorkResult WorkRoom(SimulationContext context, TileWorkOrder order)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        RoomInstance? room = RoomIndex.RoomAt(world, database, order.TileIndex);
        int efficiency = EfficiencyOf(context, order.WorkerCreatureId);
        TileWorkResult result = order.Kind == TileWorkKind.RepairRoom
            ? RoomHealthRules.Repair(world, database, order.TileIndex, order.PlayerId, efficiency)
            : RoomHealthRules.Convert(world, database,
                new TilePos(order.TileIndex % world.Width, order.TileIndex / world.Width), order.PlayerId, efficiency, context);

        if (result.Outcome == TileWorkOutcome.Transformed && room is not null)
            context.Emit(SystemName, "room_converted",
                $"({order.TileIndex % world.Width},{order.TileIndex / world.Width}) " +
                $"player={order.PlayerId} {room.Definition.Name} " +
                $"tiles={room.TileCount} from={room.OwnerPlayerId}");
        return result;
    }

    /// <summary>
    /// Puts freed gold into the hands of whoever swung, and says how much fitted.
    /// What does not fit is put down at the worker's feet (<see cref="FloorGold.PutDown"/>),
    /// as DK2's mining strike <c>0x004971D0</c> does: it hands the imp what it can still
    /// hold and gives the rest to <c>0x004510C0</c> at the imp's own position (Q138, Q140).
    ///
    /// The creature is looked up rather than passed in because the queue is the
    /// only thing that connects a swing to a swinger, and the queue survives a
    /// snapshot: an order is a fact about the world, not a reference to an
    /// object. The limit is <c>MaxGoldHeld</c> at the worker's level (imp-hands-full.md).
    /// </summary>
    private static int HandTo(SimulationContext context, TileWorkOrder order, int goldReleased, out int putDown)
    {
        putDown = 0;
        if (goldReleased <= 0 || order.WorkerCreatureId < 0) return 0;

        CreatureState? worker = context.World.CreatureById(order.WorkerCreatureId);
        if (worker is null) return 0;

        int capacity = CreatureLevelRules.MaxGoldHeld(
            context.Database, context.Database.CreatureById(worker.CreatureTypeId), worker.Level);
        int taken = Math.Max(0, Math.Min(capacity - worker.GoldHeld, goldReleased));
        worker.GoldHeld += taken;

        int spilled = goldReleased - taken;
        byte cell = worker.WorkSlot == MiningSlots.None ? MiningSlots.AnyCell : MiningSlots.Cell(worker.WorkSlot);
        if (spilled > 0 && FloorGold.PutDown(context.World, context.Database,
                worker.Tile.Y * context.World.Width + worker.Tile.X, cell, spilled) is { } pile)
        {
            putDown = spilled;
            context.Emit(SystemName, "gold_put_down",
                $"imp={worker.Id} at=({worker.Tile.X},{worker.Tile.Y}) gold={spilled} pile={pile.GoldHeld}");
        }
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
        int goldCarried,
        int goldPutDown)
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

        // Gold a worker cannot hold is put down at its feet (HandTo), so only a swing with no
        // worker to receive it leaves gold uncollected: work a test or the harness queued.
        int uncollected = result.GoldReleased - goldCarried - goldPutDown;
        if (uncollected > 0)
            context.Emit(SystemName, "gold_uncollected", $"{where} amount={uncollected} (nobody swung)");

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
