// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The store rooms' own turn: the Treasury update <c>0x004F39A0</c> and the head of the
/// Heart's <c>0x004D9690</c>, which returns first on a ruined Heart. Each live store first runs
/// the sparkle <c>0x004F3A00</c> (gold-hover-and-sparkle.md §3), every turn, then on
/// <c>turn &amp; 0xF == 0</c> the sweep <c>0x004F47E0</c> (gold-on-the-floor.md §11): the room's
/// chain from its first slab, each loose pile handed to the deposit <c>0x004F3C60</c> from its tile
/// (<see cref="GoldStore.DepositFrom"/>) and destroyed, what does not fit going back down as a new
/// pile at one of eight points, a draw on the world stream each time.
/// <para>Deviates: rooms in <see cref="RoomIndex"/> order, not DK2's room list (ADR-0327); a Heart
/// sweeps only while <c>SuckGold</c> (<c>+0x51</c>) is set, taken here as always.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/gold-on-the-floor.md")]
public sealed class StoreSweepSystem : ISimulationSystem
{
    /// <summary>The sweep's period in turns, the updates' <c>turn &amp; 0xF</c>.</summary>
    public const int SweepTurns = 16;

    /// <summary><c>0x004F3A00</c>: a store's tiles are looked at when <c>random(100)</c> is below this.</summary>
    public const int SparkleRoomChance = 30;

    /// <summary><c>0x004F3A00</c>: each store pile sparkles when its own <c>random(100)</c> is below this.</summary>
    public const int SparklePileChance = 10;

    /// <summary>A Treasury or Heart tile's floor, where a store pile stands (<c>0x004545C0</c>:
    /// ordinary room floor <c>0x1000</c>).</summary>
    public const int StoreFloorHeight = Fp32.One;

    public string Name => "store sweep";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        // DK2's turn is our tick plus one (ADR-0119).
        bool sweepTurn = (context.Tick.Value + 1) % SweepTurns == 0 && AnyLoosePileInAStore(world, database);
        ObjectDef? pile = database.ObjectById(FloorGold.TreasuryPileObjectId);

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!IsLiveStore(world, room)) continue;
            Sparkle(context, room, pile);
            if (sweepTurn && PlayerIds.IsKeeper(room.OwnerPlayerId) && Sweeps(world, database, room))
                Sweep(context, room);
        }
    }

    /// <summary>A Treasury, or a Heart whose keeper's heart is not ruined: the rooms whose update
    /// reaches the sparkle (the Heart's returns on <c>+0x9A</c> bit 0 first).</summary>
    private static bool IsLiveStore(WorldState world, RoomInstance room) => room.Definition.TypeId switch
    {
        RoomTypeId.Treasury => true,
        RoomTypeId.DungeonHeart => !(world.HasPlayer(room.OwnerPlayerId) && world.Player(room.OwnerPlayerId).HeartRuined),
        _ => false,
    };

    /// <summary><c>0x004F3A00</c>: <c>random(100) &lt; 30</c> on the world stream, then along the chain
    /// each store pile draws <c>random(100)</c> and below 10 asks for its definition's idle effect
    /// (34, Gold Sparkle) at its point raised by its Height, heading 0, for the room's owner. A tile
    /// holding gold holds one pile (<c>0x004F3C60</c> creates it, <c>0x004F4510</c> deletes it with its
    /// last gold), at the tile centre on the floor. Not candidates: a lifted store pile dropped back
    /// on a store tile, which DK2's landing hands to the store (Q532).</summary>
    private static void Sparkle(SimulationContext context, RoomInstance room, ObjectDef? pile)
    {
        if (context.Rng.NextInt(100) >= SparkleRoomChance || pile is not { IsGold: true }) return;
        WorldState world = context.World;
        foreach (int tile in SlabChain.Order(world, room))
        {
            if (world.TileAtIndex(tile).GoldHeld <= 0 || context.Rng.NextInt(100) >= SparklePileChance) continue;
            context.RequestEffect("store sparkle", ViewEffectRequest.AtTileCentre(new EffectId(pile.IdleEffectId),
                room.OwnerPlayerId, world.TilePosition(tile), new Fp32(StoreFloorHeight + pile.Height.Raw)));
        }
    }

    private void Sweep(SimulationContext context, RoomInstance room)
    {
        WorldState world = context.World;
        List<ObjectState> onTile = [];
        foreach (int tile in SlabChain.Order(world, room))
        {
            // The tile's chain, head first: the newest link first (0x004B5020).
            onTile.Clear();
            foreach (ObjectState thing in world.Objects)
                if (IsLoosePile(thing) && thing.TileLink.TileIndex == tile) onTile.Add(thing);
            onTile.Sort((a, b) => b.TileLink.Order.CompareTo(a.TileLink.Order));

            foreach (ObjectState pile in onTile)
            {
                int before = pile.GoldHeld;
                int stored = GoldStore.DepositFrom(world, context.Database, room, tile, before, context.Rng);
                FloorGold.Take(world, context.Database, pile, before);
                context.Emit(Name, "swept",
                    $"player={room.OwnerPlayerId} room={room.Definition.Name} " +
                    $"at=({tile % world.Width},{tile / world.Width}) " +
                    $"stored={stored} left={before - stored}");
            }
        }
    }

    /// <summary>The Treasury's gate, its tally below its capacity (<c>0x004F4700</c>); the Heart's
    /// update tests the tally against 16 × the Treasury cap, which a Heart never reaches, so a live
    /// Heart always sweeps.</summary>
    private static bool Sweeps(WorldState world, GameDatabase database, RoomInstance room)
    {
        if (room.Definition.TypeId != RoomTypeId.Treasury) return true;
        long held = 0;
        foreach (int tile in room.TileIndices) held += world.TileAtIndex(tile).GoldHeld;
        return held < (long)room.TileIndices.Count * database[VariableTypeId.MaxGoldPerTreasuryTile];
    }

    private static bool IsLoosePile(ObjectState thing) =>
        thing.ObjectTypeId == FloorGold.FloorGoldObjectId && !thing.InHand
        && thing.CarriedByCreatureId < 0 && thing.GoldHeld > 0;

    // A cheap first pass, so the chains are walked only on a turn with something to sweep.
    private static bool AnyLoosePileInAStore(WorldState world, GameDatabase database)
    {
        foreach (ObjectState thing in world.Objects)
            if (IsLoosePile(thing) && GoldStore.Capacity(database, world.TileAtIndex(thing.TileIndex)) > 0)
                return true;
        return false;
    }
}
