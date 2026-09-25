// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A keeper placing a door or trap blueprint during play (<c>blueprint-placement.md</c>,
/// ADR-0231): the tile tests <c>0x00450DB0</c> and <c>0x00450EB0</c>, the gold, and the same
/// blueprint and order an authored one gets (<see cref="ManufactureRules"/>, <see cref="CrateRules"/>).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/blueprint-placement.md")]
public static class BlueprintPlacementRules
{
    /// <summary>Why a placement was refused, or <see cref="Allowed"/>.</summary>
    public enum Verdict { Allowed, NoSuchType, NotTheKeepersTile, Solid, ARoom, Occupied, NoWalls, NotEnoughGold }

    /// <summary><c>0x00450EB0</c>, a trap: the keeper's own tile, not solid, not a room, and no
    /// door or trap on it.</summary>
    public static Verdict CanTrap(WorldState world, GameDatabase database, TilePos at, byte player)
    {
        if (!world.Contains(at)) return Verdict.NotTheKeepersTile;
        ref TileState tile = ref world.Tile(at);
        if (tile.OwnerPlayerId != player) return Verdict.NotTheKeepersTile;
        if (database.TerrainById(tile.TerrainId).IsSolid) return Verdict.Solid;
        if (database.RoomByTerrain(tile.TerrainId) is not null) return Verdict.ARoom;
        return Occupied(world, at) ? Verdict.Occupied : Verdict.Allowed;
    }

    /// <summary><c>0x00450DB0</c>, a door: the trap's tests, then, unless the door is a
    /// <see cref="DoorFlags.Barricade"/> (<c>def+0x8B</c> bit <c>0x40</c>), solid tiles on both sides
    /// north and south or east and west.</summary>
    public static Verdict CanDoor(WorldState world, GameDatabase database, TilePos at, byte player, DoorDef door)
    {
        Verdict verdict = CanTrap(world, database, at, player);
        if (verdict != Verdict.Allowed || door.Flags.HasFlag(DoorFlags.Barricade)) return verdict;
        bool northSouth = Solid(world, database, at.X, at.Y - 1) && Solid(world, database, at.X, at.Y + 1);
        bool eastWest = Solid(world, database, at.X + 1, at.Y) && Solid(world, database, at.X - 1, at.Y);
        return northSouth || eastWest ? Verdict.Allowed : Verdict.NoWalls;
    }

    /// <summary><c>0x004C33B0</c>: with the tile allowed and the keeper able to pay the door's
    /// <c>GoldCost</c> (<c>def+0x8F</c>), the gold is spent, the blueprint laid, its order pushed
    /// (<c>0x005066E0</c>) and it joins the blueprint list (<c>0x00505680</c>).</summary>
    public static Verdict PlaceDoor(WorldState world, GameDatabase database, TilePos at, byte player, byte doorId)
    {
        if (!database.Doors.TryGetValue(doorId, out DoorDef? door)) return Verdict.NoSuchType;
        Verdict verdict = CanDoor(world, database, at, player, door);
        if (verdict != Verdict.Allowed) return verdict;
        if (!GoldStore.TryWithdraw(world, player, door.GoldCost)) return Verdict.NotEnoughGold;

        world.AddDoor(new DoorState { X = at.X, Y = at.Y, DoorId = doorId, PlayerId = player, UnderConstruction = true, Unknown_0011 = new byte[DoorState.Unknown0011Length] });
        ManufactureRules.QueueOne(world, database, player, CrateRules.DoorKind, doorId);
        CrateRules.Register(world, player, new BlueprintRef(CrateRules.DoorKind, world.Doors.Count - 1));
        return Verdict.Allowed;
    }

    /// <summary><c>0x004C3860</c>: the trap's test, its <c>GoldCost</c> (<c>def+0x92</c>), and the
    /// same three steps.</summary>
    public static Verdict PlaceTrap(WorldState world, GameDatabase database, TilePos at, byte player, byte trapId)
    {
        if (!database.Traps.TryGetValue(trapId, out TrapDef? trap)) return Verdict.NoSuchType;
        Verdict verdict = CanTrap(world, database, at, player);
        if (verdict != Verdict.Allowed) return verdict;
        if (!GoldStore.TryWithdraw(world, player, trap.GoldCost)) return Verdict.NotEnoughGold;

        world.AddTrap(new TrapState { X = at.X, Y = at.Y, TrapId = trapId, PlayerId = player, UnderConstruction = true });
        ManufactureRules.QueueOne(world, database, player, CrateRules.TrapKind, trapId);
        CrateRules.Register(world, player, new BlueprintRef(CrateRules.TrapKind, world.Traps.Count - 1));
        return Verdict.Allowed;
    }

    private static bool Solid(WorldState world, GameDatabase database, int x, int y)
    {
        var at = new TilePos(x, y);
        return !world.Contains(at) || database.TerrainById(world.Tile(at).TerrainId).IsSolid;
    }

    private static bool Occupied(WorldState world, TilePos at)
    {
        foreach (DoorState d in world.Doors)
            if (d.X == at.X && d.Y == at.Y) return true;
        foreach (TrapState t in world.Traps)
            if (t.X == at.X && t.Y == at.Y) return true;
        return false;
    }
}
