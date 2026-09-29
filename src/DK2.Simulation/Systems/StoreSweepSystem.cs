// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A Treasury or a Dungeon Heart takes in the loose gold lying on its own tiles every
/// 16th turn (docs/research/gold-on-the-floor.md §11): the sweep <c>0x004F47E0</c>, run by the
/// Treasury update <c>0x004F39A0</c> and the Heart's <c>0x004D9690</c> on <c>turn &amp; 0xF == 0</c>,
/// deposits each pile through <c>0x004F3C60</c> along the room's chain from the pile's tile, and
/// what does not fit goes back down there.
/// <para>Level 2 live capture (level2-live1): the owner dropped a 36,400 pile on a Heart tile at
/// 1020; at 1023, turn 1024, the Heart's sixteen rim piles filled to 1,000 and 20,900 lay on the
/// tile. Ours left all of it loose, and the Library at 1055 lacked the gold for up to eight tiles.</para>
/// <para>Deviates: the pile keeps its object and loses what went in, where DK2 destroys it and
/// makes a new one at one of eight points of the tile. The update's "not full" test is left out,
/// since a full store takes nothing either way. Not built: a Heart sweeps only while room
/// <c>+0x51</c> is non-zero (Q472, unread), taken here as always.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/gold-on-the-floor.md")]
public sealed class StoreSweepSystem : ISimulationSystem
{
    /// <summary>The sweep's period in turns, the updates' <c>turn &amp; 0xF</c>.</summary>
    public const int SweepTurns = 16;

    public string Name => "store sweep";

    public void Execute(SimulationContext context)
    {
        if (context.Tick.Value % SweepTurns != 0) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;
        List<ObjectState>? piles = null;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.ObjectTypeId != FloorGold.FloorGoldObjectId || thing.InHand
                || thing.CarriedByCreatureId >= 0 || thing.GoldHeld <= 0)
                continue;
            if (GoldStore.Capacity(database, world.TileAtIndex(thing.TileIndex)) == 0) continue;
            (piles ??= []).Add(thing);
        }
        if (piles is null) return;

        foreach (ObjectState pile in piles)
        {
            RoomInstance? room = RoomIndex.RoomAt(world, database, pile.TileIndex);
            if (room is null || !PlayerIds.IsKeeper(room.OwnerPlayerId)) continue;

            int before = pile.GoldHeld;
            int stored = before - GoldStore.FillAlongChainFrom(world, database, room, pile.TileIndex, before);
            if (stored <= 0) continue;
            FloorGold.Take(world, database, pile, stored);
            context.Emit(Name, "swept",
                $"player={room.OwnerPlayerId} room={room.Definition.Name} " +
                $"at=({pile.TileIndex % world.Width},{pile.TileIndex / world.Width}) " +
                $"stored={stored} left={before - stored}");
        }
    }
}
