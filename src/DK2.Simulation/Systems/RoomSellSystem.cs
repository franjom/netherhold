// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Applies this tick's <see cref="SellRoomCommand"/>s: a room tile back to the
/// floor it stood on, and half its cost back to the keeper. What may be sold and
/// what it is worth are <see cref="RoomRules"/>; where the gold goes is
/// <see cref="GoldStore"/>.
/// <para><b>A tile at a time, each refund banked before the next tile goes</b>:
/// selling a treasury shrinks the store the refund has to go into, so a keeper
/// selling the lot runs out of somewhere to put the money partway through, and
/// the run says exactly when. <b>Deliberate deviation: selling is instant</b> —
/// in DK2 an imp walks over and dismantles the tile, and no task kind for that
/// has been identified (imp-task-priority.md §3).</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public sealed class RoomSellSystem : ISimulationSystem
{
    private const string SystemName = "RoomSell";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (SellRoomCommand sell in context.Commands<SellRoomCommand>())
            Apply(context, sell);
    }

    private static void Apply(SimulationContext context, SellRoomCommand command)
    {
        WorldState world = context.World;

        int sold = 0;
        int refunded = 0;
        int lostRefund = 0;
        int displacedGold = 0;
        int bridges = 0;
        int lastSold = -1;
        var refusals = new Dictionary<RoomSale, int>();

        // A sold tile rebuilds or frees its room, and a Combat Pit's reset throws its fighters
        // out (0x004D7E00, 0x004D8000): the pits as they stood before the sale.
        List<RoomInstance> pits = CombatPitRules.PitsUnder(world, context.Database, command.Tiles);

        foreach (TilePos pos in command.Tiles)
        {
            if (!world.Contains(pos))
            {
                Count(refusals, RoomSale.OffMap);
                continue;
            }

            ref TileState tile = ref world.Tile(pos);
            RoomSale verdict = RoomRules.CanSell(context.Database, tile, command.PlayerId, out RoomDef? room);
            if (verdict != RoomSale.Allowed || room is null)
            {
                Count(refusals, verdict);
                continue;
            }

            int refund = RoomRules.RefundPerTile(context.Database, room);

            // A bridge goes back to the water or lava it spanned rather than to
            // the Claimed Path its terrain record names (CONFIRMED, Q64;
            // BridgeRules). The only sale whose outcome is not the
            // destroyed-type field, so it is counted and reported.
            if (BridgeRules.IsBridge(room)) bridges++;

            // A Graveyard sold even in part is rebuilt with its tally at 0 (Q321).
            if (room.TypeId == RoomTypeId.Graveyard
                && RoomIndex.RoomAt(world, context.Database, (pos.Y * world.Width) + pos.X) is RoomInstance graveyard)
                GraveyardRules.Forget(world, graveyard);

            // Unmake first, then bank. The other order would let a refund land on
            // the tile being sold and spill straight back out again.
            byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
            int displacedHere = RoomRules.Unmake(context.Database, ref tile);
            SlabChain.Changed(world, context.Database, (pos.Y * world.Width) + pos.X, terrainBefore, ownerBefore);
            displacedGold += displacedHere;
            sold++;

            // The store's contents stay where the store was, as a pile on the
            // floor of that very tile -- the original walks the room's gold
            // objects and remakes each one as a floor pile in place
            // (gold-on-the-floor.md section 4). Dropped before the refund is
            // banked, so the two cannot be confused for one another.
            lastSold = (pos.Y * world.Width) + pos.X;
            FloorGold.Drop(world, context.Database, lastSold, displacedHere);

            int banked = GoldStore.Deposit(world, context.Database, command.PlayerId, refund);
            // Each tile's refund is paid in at its tile (0x004C5DB0 to 0x004BA420); what no store takes
            // asks for a Treasury.
            if (banked < refund) RoomNeedRules.GoldLeftOver(context, command.PlayerId);
            refunded += banked;
            lostRefund += refund - banked;
        }

        context.Emit(SystemName, "sold",
            $"player={command.PlayerId} tiles={sold} refund={refunded} " +
            $"refused={Describe(refusals)}");
        if (sold > 0) foreach (RoomInstance pit in pits) CombatPitSystem.Reset(context, pit);

        // A refund with nowhere to go lands on the floor rather than vanishing:
        // the original's add-gold-to-player takes a position and, once every
        // treasury and heart has refused, makes a floor pile there
        // (gold-on-the-floor.md section 5). WHICH tile is ours -- the caller
        // chooses the position and this one has a whole command's worth of them,
        // so the last tile sold is a choice and not a reading (Q139).
        if (lostRefund > 0 && lastSold >= 0)
        {
            FloorGold.Drop(world, context.Database, lastSold, lostRefund);
            context.Emit(SystemName, "refund_overflowed",
                $"player={command.PlayerId} amount={lostRefund} " +
                $"reason=nowhere_to_store (now a pile on the floor at tile {lastSold})");
        }

        if (displacedGold > 0)
        {
            context.Emit(SystemName, "gold_displaced",
                $"player={command.PlayerId} amount={displacedGold} " +
                "reason=store_sold (now piles on the floor tiles that replaced it)");
        }

        if (bridges > 0)
        {
            context.Emit(SystemName, "bridge_sold",
                $"player={command.PlayerId} tiles={bridges} " +
                "note=reverted to the water or lava each one spanned (Q64)");
        }
    }

    private static void Count(Dictionary<RoomSale, int> refusals, RoomSale reason) =>
        refusals[reason] = refusals.TryGetValue(reason, out int n) ? n + 1 : 1;

    /// <summary>
    /// Refusals as "reason=count" pairs, ordered by the enum so that two runs of
    /// the same scenario produce byte-identical event text.
    /// </summary>
    private static string Describe(Dictionary<RoomSale, int> refusals) =>
        refusals.Count == 0
            ? "0"
            : string.Join(",", refusals
                .OrderBy(kv => (int)kv.Key)
                .Select(kv => $"{kv.Key}={kv.Value}"));
}
