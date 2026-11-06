// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>Reconciles room-owned furniture with the current room shapes.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/auto-placed-furniture.md")]
public sealed class AutoFurnitureSystem : ISimulationSystem
{
    public string Name => "AutoFurniture";

    // One tick's working sets, kept across ticks: five containers a tick were 30 KB of garbage.
    private readonly List<(PlayerId Owner, int Tile, byte Type)> _desired = [];
    private readonly HashSet<(PlayerId Owner, int Tile, byte Type)> _unmatched = [];
    private readonly List<(int TileIndex, byte ObjectTypeId)> _roomFurniture = [];
    private readonly List<RoomInstance> _rebuilt = [];
    private readonly List<int> _remove = [];

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        List<(PlayerId Owner, int Tile, byte Type)> desired = _desired;
        HashSet<(PlayerId Owner, int Tile, byte Type)> unmatched = _unmatched;
        List<(int TileIndex, byte ObjectTypeId)> roomFurniture = _roomFurniture;
        desired.Clear();
        unmatched.Clear();

        long now = world.CurrentTick.Value;
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, context.Database);
        // A rebuilt Graveyard (its size stamp this turn, map rooms at the first) runs its own pass
        // before the stones (Room_Rebuild, 0x004DD5C0).
        List<RoomInstance> rebuilt = _rebuilt;
        rebuilt.Clear();
        foreach (RoomInstance room in rooms)
            if (room.Definition.TypeId == RoomTypeId.Graveyard && SlabChain.SizeChangedTick(world, room) == now)
            {
                GraveyardRules.PlaceCrypts(context, room);
                rebuilt.Add(room);
            }

        foreach (RoomInstance room in rooms)
        {
            AutoFurnitureRules.DesiredFurniture(world, context.Database, room, roomFurniture);
            foreach ((int tile, byte type) in roomFurniture)
            {
                var key = (room.OwnerPlayerId, tile, type);
                if (unmatched.Add(key)) desired.Add(key);
            }
        }

        List<int> remove = _remove;
        remove.Clear();
        foreach (ObjectState thing in world.Objects)
        {
            // Held objects retain their old TileIndex. Shipped furniture cannot
            // reach this state (Q196), but no held object may satisfy a room slot.
            if (thing.InHand) continue;

            var key = (thing.OwnerPlayerId, thing.TileIndex, thing.ObjectTypeId);
            if (unmatched.Remove(key)) continue;
            // A Hatchery's coops are deleted and placed by its rebuild (HatcheryRules.RebuildCoops, ADR-0672).
            if (thing.AutoPlaced && thing.ObjectTypeId != HatcheryRules.CoopObjectId) remove.Add(thing.Id);
        }

        foreach (int id in remove) world.RemoveObject(id);
        foreach ((PlayerId owner, int tile, byte type) in desired)
        {
            if (!unmatched.Contains((owner, tile, type))) continue;
            if (type == HatcheryRules.CoopObjectId) continue;
            if (context.Database.ObjectById(type) is null) continue;
            world.SpawnObject(context.Database, type, owner, tile).AutoPlaced = true;
        }

        // Then its tile-change slot takes in the corpses lying on it (0x004DD750). Not at the level's
        // start: Level_InitTriggersAndStart rebuilds the map's rooms without Room_OnTilesChanged.
        if (now > 0)
            foreach (RoomInstance room in rebuilt) GraveyardRules.AdoptCorpses(context, room);
        LevelPlacedCreatureRules.Land(context);
    }
}
