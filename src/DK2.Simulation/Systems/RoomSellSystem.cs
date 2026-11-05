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
/// <para><b>A tile at a time, each refund banked before the next tile goes</b>: selling a treasury
/// shrinks the store the refund has to go into. A dragged rectangle is queued here and sold one
/// diagonal a turn from its maximum corner by <see cref="RoomSellWaveSystem"/> (room-slab-chain.md §4).</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public sealed class RoomSellSystem : ISimulationSystem
{
    private const string SystemName = "RoomSell";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (SellRoomCommand sell in context.Commands<SellRoomCommand>())
            if (sell.Mode == RoomSellMode.PlayerWave) Queue(context, sell);
            else Apply(context, sell);
    }

    /// <summary>Action <c>0x31</c> (<c>0x004C70A0</c>): the rectangle goes into the first free of three slots, with
    /// no pre-check and no cost; with all three busy the request is dropped.</summary>
    private static void Queue(SimulationContext context, SellRoomCommand command)
    {
        WorldState world = context.World;
        if (!world.HasPlayer(command.PlayerId)) return;
        for (int slotIndex = 0; slotIndex < RoomSellWaveRules.SlotsPerPlayer; slotIndex++)
        {
            ref RoomSellWaveState? slot = ref world.RoomSellWave(command.PlayerId, slotIndex);
            if (slot.HasValue) continue;
            slot = new RoomSellWaveState(command.Min, command.Max, context.Tick);
            return;
        }
        context.Emit(SystemName, "sell_refused", $"player={command.PlayerId} reason=queue_full");
    }

    /// <summary>The sell wave <c>0x004C7160</c>: this turn's diagonal of each queued sale, every tile sold and paid
    /// (<c>0x004C5DB0</c> with pay flag 1). Reaching the minimum corner clears the slot and ends the player's
    /// drain, as the build wave's completion does.</summary>
    internal static void Drain(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        if (!playerId.IsKeeper || world.Player(playerId).KilledByPlayerId != 0) return;
        for (int slotIndex = 0; slotIndex < RoomSellWaveRules.SlotsPerPlayer; slotIndex++)
        {
            ref RoomSellWaveState? slot = ref world.RoomSellWave(playerId, slotIndex);
            if (slot is not RoomSellWaveState wave) continue;
            TilePos end = RoomBuildWaveRules.ClampPress(wave.Min);
            foreach (TilePos pos in RoomSellWaveRules.Diagonal(wave, context.Tick, world))
            {
                if (world.Contains(pos)) SellWaveTile(context, playerId, pos);
                if (pos == end)
                {
                    slot = null;
                    return;
                }
            }
        }
    }

    private static void SellWaveTile(SimulationContext context, PlayerId playerId, TilePos pos)
    {
        TileSale sale = SellOne(context, playerId, pos, wave: true);
        if (sale.Verdict != RoomSale.Allowed) return;
        context.Emit(SystemName, "sold",
            $"player={playerId} tiles=1 refund={sale.Banked} refused=0 tile=({pos.X},{pos.Y})");
        if (sale.Displaced > 0)
            context.Emit(SystemName, "gold_displaced",
                $"player={playerId} amount={sale.Displaced} reason=store_sold (now piles on the floor tiles that replaced it)");
        if (sale.Bridge)
            context.Emit(SystemName, "bridge_sold",
                $"player={playerId} tiles=1 note=reverted to the water or lava each one spanned (Q64)");
    }

    private readonly record struct TileSale(RoomSale Verdict, int Banked, int Lost, int Displaced, bool Bridge);

    /// <summary>One tile, in <c>0x004C5DB0</c>'s order: unmake it, leave its stored gold on its floor, bank the
    /// refund and sound the sale. For a <paramref name="wave"/> tile the Combat Pit it cut is reset with the unmake
    /// (inside <c>0x004ED440</c>) and a refund no store takes lands on this tile (<c>0x004BA420</c>); the immediate
    /// path does both once for the whole command.</summary>
    private static TileSale SellOne(SimulationContext context, PlayerId playerId, TilePos pos, bool wave = false)
    {
        WorldState world = context.World;
        if (!world.Contains(pos)) return new TileSale(RoomSale.OffMap, 0, 0, 0, false);

        ref TileState tile = ref world.Tile(pos);
        RoomSale verdict = RoomRules.CanSell(context.Database, tile, playerId, out RoomDef? room);
        if (verdict != RoomSale.Allowed || room is null) return new TileSale(verdict, 0, 0, 0, false);

        int refund = RoomRules.RefundPerTile(context.Database, room);
        int index = (pos.Y * world.Width) + pos.X;

        // A bridge goes back to the water or lava it spanned rather than to the Claimed Path its terrain
        // record names (CONFIRMED, Q64; BridgeRules), so it is counted and reported.
        bool bridge = BridgeRules.IsBridge(room);

        // A Graveyard sold even in part is rebuilt with its tally at 0 (Q321).
        if (room.TypeId == RoomTypeId.Graveyard
            && RoomIndex.RoomAt(world, context.Database, index) is RoomInstance graveyard)
            GraveyardRules.Forget(world, graveyard);

        List<RoomInstance>? pits = wave && room.TypeId == RoomTypeId.CombatPit
            ? CombatPitRules.PitsUnder(world, context.Database, [pos]) : null;

        // Unmake first, then bank: the other order would let a refund land on the tile being sold.
        byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
        int displaced = RoomRules.Unmake(context.Database, ref tile);
        SlabChain.Changed(world, context.Database, index, terrainBefore, ownerBefore);
        if (pits is not null) foreach (RoomInstance pit in pits) CombatPitSystem.Reset(context, pit);

        // The store's contents stay where the store was, as a pile on the floor of that very tile
        // (gold-on-the-floor.md section 4), dropped before the refund is banked.
        FloorGold.Drop(world, context.Database, index, displaced);

        int banked = GoldStore.Deposit(world, context.Database, playerId, refund);
        // What no store takes asks for a Treasury (0x004BA420).
        if (banked < refund) RoomNeedRules.GoldLeftOver(context, playerId);
        if (wave && banked < refund)
        {
            FloorGold.Drop(world, context.Database, index, refund - banked);
            context.Emit(SystemName, "refund_overflowed",
                $"player={playerId} amount={refund - banked} reason=nowhere_to_store (now a pile on the floor at tile {index})");
        }

        // The refund floats up from the tile (0x004C5ED0), every paid tile, a refund of 0 included.
        CostNumberRules.Show(context, SystemName, playerId, CostNumberRules.TileCentre(pos, 0x1000), refund);

        // The sale's own sound, after the refund is paid in (0x004C5EF8): handle 0, the room's terrain
        // category, the tile's centre at height 0x1000.
        ThingSoundRules.RequestTile(context, SystemName, room.TerrainId, index, ThingSoundEvent.RoomTileSold,
            height: 0x1000, SoundSourceKind.Room);
        return new TileSale(RoomSale.Allowed, banked, refund - banked, displaced, bridge);
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
            TileSale sale = SellOne(context, command.PlayerId, pos);
            if (sale.Verdict != RoomSale.Allowed)
            {
                Count(refusals, sale.Verdict);
                continue;
            }
            sold++;
            if (sale.Bridge) bridges++;
            displacedGold += sale.Displaced;
            refunded += sale.Banked;
            lostRefund += sale.Lost;
            lastSold = (pos.Y * world.Width) + pos.X;
        }

        context.Emit(SystemName, "sold",
            $"player={command.PlayerId} tiles={sold} refund={refunded} " +
            $"refused={Describe(refusals)}");
        if (sold > 0) foreach (RoomInstance pit in pits) CombatPitSystem.Reset(context, pit);

        // A refund with nowhere to go lands on the floor rather than vanishing (gold-on-the-floor.md section 5).
        // An immediate multi-tile sale has no single tile of DK2's to use, so the last tile sold is a choice (Q139).
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
