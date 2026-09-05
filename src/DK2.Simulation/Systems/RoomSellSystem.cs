// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Applies this tick's <see cref="SellRoomCommand"/>s: takes a room tile back to
/// the floor it stood on and pays the keeper half of what it cost.
///
/// <para>The mirror of <see cref="RoomBuildSystem"/>, and it carries no policy of
/// its own for the same reason: what may be sold and what it is worth are
/// <see cref="RoomRules"/>, and where the gold goes is <see cref="GoldStore"/>.
/// All that happens here is the per-tile loop and the reporting.</para>
///
/// <para><b>A tile at a time, and each refund banked before the next tile goes.</b>
/// That ordering is not arbitrary. Selling a treasury shrinks the very store the
/// refund has to go into, so a keeper selling their whole treasury will run out
/// of somewhere to put the money partway through — and this way the run says
/// exactly when, instead of the answer depending on which order the loop
/// happened to use.</para>
///
/// <para><b>A bridge is the one sale whose outcome is not the destroyed-type
/// field.</b> It goes back to the water or lava it spanned (Q64, CONFIRMED), and
/// <see cref="BridgeRules"/> is what knows which; the event log names it when a
/// sale involved one.</para>
///
/// <para><b>Selling is instant, and the imp that should do it is missing.</b> In
/// DK2 an imp walks over and dismantles the tile, the same worker job that builds
/// it (Q24). Nothing here pretends to time that. What is not in doubt is the part
/// this does: the keeper asked, the room was theirs, and the gold came back.</para>
///
/// <para><b>What happens to gold stored on a sold treasury tile is CONFIRMED</b>
/// (owner, 2026-09-03): the tile reverts to claimed floor and <b>the gold drops
/// onto that same tile as a pile</b>, coming off the keeper's spendable total
/// until somebody fetches it. Two of those three things are reproduced here — the
/// tile reverts, and the total falls by exactly that much, so the <i>economy</i>
/// of a sale is right. What is missing is the pile itself, because a pile is an
/// OBJECT and objects arrive at M6 (Q31), so a keeper here loses permanently what
/// a keeper there could pick back up. Same for a refund with nowhere to go. Both
/// are reported rather than hidden, which is the honest way round: the decision
/// is DK2's and the recovery is what we lack.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public sealed class RoomSellSystem : ISimulationSystem
{
    private const string SystemName = "RoomSell";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            if (command is not SellRoomCommand sell) continue;
            Apply(context, sell);
        }
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
            // the Claimed Path its terrain record names (Q64, BridgeRules).
            // Counted so the run says when one was involved, because it is the
            // only sale whose outcome is not the destroyed-type field.
            if (BridgeRules.IsBridge(room)) bridges++;

            // Unmake first, then bank. The other order would let a refund land on
            // the tile being sold and spill straight back out again.
            int displacedHere = RoomRules.Unmake(context.Database, ref tile);
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
            refunded += banked;
            lostRefund += refund - banked;
        }

        context.Emit(SystemName, "sold",
            $"player={command.PlayerId} tiles={sold} refund={refunded} " +
            $"refused={Describe(refusals)}");

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
