// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A Treasury or a Dungeon Heart takes in the loose gold lying on its own tiles every
/// 16th turn (docs/research/gold-on-the-floor.md §11): the sweep <c>0x004F47E0</c>, run by the
/// Treasury update <c>0x004F39A0</c> while the room is not full and by the Heart's
/// <c>0x004D9690</c> unless ruined, on <c>turn &amp; 0xF == 0</c>. It walks the room's chain from
/// its first slab and hands each loose pile to the deposit <c>0x004F3C60</c> from the pile's tile
/// (<see cref="GoldStore.DepositFrom"/>), then destroys the pile: what does not fit goes back
/// down as a new pile at one of eight points, a draw on the world stream each time.
/// <para>Level 2 live capture (level2-live1): the owner dropped a 36,400 pile on a Heart tile at
/// 1020; at 1023, turn 1024, the Heart's sixteen rim piles filled to 1,000 and 20,900 lay on the
/// tile. Ours left all of it loose, and the Library at 1055 lacked the gold for up to eight tiles.</para>
/// <para>Deviates: rooms in <see cref="RoomIndex"/> order, not the player's room list (ADR-0327);
/// a Heart sweeps only while <c>SuckGold</c> (<c>+0x51</c>) is set, taken here as always.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/gold-on-the-floor.md")]
public sealed class StoreSweepSystem : ISimulationSystem
{
    /// <summary>The sweep's period in turns, the updates' <c>turn &amp; 0xF</c>.</summary>
    public const int SweepTurns = 16;

    public string Name => "store sweep";

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SweepTurns != 0) return;   // DK2's turn is our tick plus one (ADR-0119)

        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!AnyLoosePileInAStore(world, database)) return;

        List<ObjectState> onTile = [];
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!PlayerIds.IsKeeper(room.OwnerPlayerId) || !Sweeps(world, database, room)) continue;
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
                    int stored = GoldStore.DepositFrom(world, database, room, tile, before, context.Rng);
                    FloorGold.Take(world, database, pile, before);
                    context.Emit(Name, "swept",
                        $"player={room.OwnerPlayerId} room={room.Definition.Name} " +
                        $"at=({tile % world.Width},{tile / world.Width}) " +
                        $"stored={stored} left={before - stored}");
                }
            }
        }
    }

    /// <summary>The Treasury's gate, its tally below its capacity (<c>0x004F4700</c>); the Heart's
    /// update tests the tally against 16 × the Treasury cap, which a Heart never reaches, so a live
    /// Heart always sweeps and a ruined one never.</summary>
    private static bool Sweeps(WorldState world, GameDatabase database, RoomInstance room)
    {
        switch (room.Definition.TypeId)
        {
            case RoomTypeId.DungeonHeart:
                return !(world.HasPlayer(room.OwnerPlayerId) && world.Player(room.OwnerPlayerId).HeartRuined);
            case RoomTypeId.Treasury:
                long held = 0;
                foreach (int tile in room.TileIndices) held += world.TileAtIndex(tile).GoldHeld;
                return held < (long)room.TileIndices.Count * database[VariableTypeId.MaxGoldPerTreasuryTile];
            default:
                return false;
        }
    }

    private static bool IsLoosePile(ObjectState thing) =>
        thing.ObjectTypeId == FloorGold.FloorGoldObjectId && !thing.InHand
        && thing.CarriedByCreatureId < 0 && thing.GoldHeld > 0;

    // A cheap first pass, so the room index is built only on a turn with something to sweep.
    private static bool AnyLoosePileInAStore(WorldState world, GameDatabase database)
    {
        foreach (ObjectState thing in world.Objects)
            if (IsLoosePile(thing) && GoldStore.Capacity(database, world.TileAtIndex(thing.TileIndex)) > 0)
                return true;
        return false;
    }
}
