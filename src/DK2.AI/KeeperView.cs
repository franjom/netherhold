// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

/// <summary>
/// One keeper's read-only look at the world for one tick. Everything the keeper
/// asks about tiles, rooms and its own holdings goes through here, so the
/// eventual <c>IWorldView</c> facade (docs/KEEPER_AI.md §12.2) has one place to
/// replace. Nothing in this type writes to the world.
/// </summary>
public sealed class KeeperView
{
    public KeeperView(GameDatabase database, WorldState world, byte playerId)
    {
        Database = database;
        World = world;
        PlayerId = playerId;

        AllRooms = RoomIndex.Build(world, database);
        OwnRooms = AllRooms.Where(r => r.OwnerPlayerId == playerId).ToList();

        int heartIndex = ImpSupplyRules.HeartTileOf(world, database, playerId);
        Heart = heartIndex < 0 ? null : FromIndex(heartIndex);

        int owned = 0;
        for (int i = 0; i < world.TileCount; i++)
            if (world.TileAtIndex(i).OwnerPlayerId == playerId) owned++;
        OwnedTiles = owned;
    }

    public GameDatabase Database { get; }
    public WorldState World { get; }
    public byte PlayerId { get; }

    public IReadOnlyList<RoomInstance> AllRooms { get; }
    public IReadOnlyList<RoomInstance> OwnRooms { get; }

    /// <summary>The keeper's Dungeon Heart tile, or null when it has none left.</summary>
    public TilePos? Heart { get; }

    public int OwnedTiles { get; }

    public int Gold => GoldStore.Total(World, PlayerId);
    public int ImpCount => ImpSupplyRules.ImpCount(World, Database, PlayerId);

    public int Width => World.Width;
    public int Height => World.Height;

    public bool InBounds(TilePos p) => World.Contains(p);
    public int IndexOf(TilePos p) => (p.Y * World.Width) + p.X;
    public TilePos FromIndex(int index) => new(index % World.Width, index / World.Width);

    public TerrainDef Terrain(TilePos p) => Database.TerrainById(World.Tile(p).TerrainId);

    /// <summary>Solid and not impenetrable: rock an imp can dig (the original's tile predicate, §5.2).</summary>
    public bool IsDiggable(TilePos p)
    {
        if (!InBounds(p)) return false;
        TerrainDef t = Terrain(p);
        return t.IsSolid && !t.IsImpenetrable;
    }

    /// <summary>Solid, or off the map.</summary>
    public bool IsSolid(TilePos p) => !InBounds(p) || Terrain(p).IsSolid;

    public bool IsWater(TilePos p) => InBounds(p) && Terrain(p).IsWater;
    public bool IsLava(TilePos p) => InBounds(p) && Terrain(p).IsLava;

    /// <summary>Open ground a creature walks on: not solid, not water, not lava.</summary>
    public bool IsPassable(TilePos p)
    {
        if (!InBounds(p)) return false;
        TerrainDef t = Terrain(p);
        return !t.IsSolid && !t.IsWater && !t.IsLava;
    }

    public bool IsMine(TilePos p) => InBounds(p) && World.Tile(p).OwnerPlayerId == PlayerId;
    public bool HasRoom(TilePos p) => InBounds(p) && Terrain(p).IsRoom;
    public bool IsTaggedByMe(TilePos p) => InBounds(p) && World.Tile(p).IsTaggedBy(PlayerId);
    public bool IsTaggedByAnyone(TilePos p) => InBounds(p) && World.Tile(p).IsTaggedByAnyone;

    /// <summary>Solid rock that yields gold when dug.</summary>
    public bool IsGoldRock(TilePos p)
    {
        if (!InBounds(p)) return false;
        TerrainDef t = Terrain(p);
        return t.IsSolid && t.GoldValue > 0;
    }

    /// <summary>A room instance may be dug into or built on when it is nobody's or the neutral side's.</summary>
    public static bool IsNeutral(RoomInstance room) =>
        room.OwnerPlayerId == PlayerIds.Unowned || room.OwnerPlayerId == PlayerIds.Neutral;

    public static TilePos Centre(RoomInstance room) =>
        new((room.MinX + room.MaxX) / 2, (room.MinY + room.MaxY) / 2);

    public static int DistanceSquared(TilePos a, TilePos b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    public RoomInstance? OwnRoomOfType(byte roomId)
    {
        foreach (RoomInstance r in OwnRooms)
            if (r.Definition.RoomId == roomId) return r;
        return null;
    }

    public RoomInstance? NearestOwnRoom(TilePos to, bool avoidBridges = false)
    {
        RoomInstance? best = null;
        int bestDist = int.MaxValue;
        foreach (RoomInstance r in OwnRooms)
        {
            if (avoidBridges && BridgeRules.IsBridge(r.Definition)) continue;
            int d = DistanceSquared(Centre(r), to);
            if (d < bestDist)
            {
                bestDist = d;
                best = r;
            }
        }
        return best;
    }

    /// <summary>
    /// Every tile reachable from the heart over passable ground, plus tiles the
    /// keeper has tagged when <paramref name="throughMyTags"/> is set — that is
    /// "will be connected once the imps have dug". Empty when there is no heart.
    /// </summary>
    public HashSet<int> ReachableFromHeart(bool throughMyTags)
    {
        var seen = new HashSet<int>();
        if (Heart is not TilePos heart) return seen;

        var queue = new Queue<TilePos>();
        queue.Enqueue(heart);
        seen.Add(IndexOf(heart));
        while (queue.Count > 0)
        {
            TilePos p = queue.Dequeue();
            foreach ((int dx, int dy) in KeeperDirections.Clockwise)
            {
                var n = new TilePos(p.X + dx, p.Y + dy);
                if (!InBounds(n)) continue;
                int idx = IndexOf(n);
                if (seen.Contains(idx)) continue;
                if (!(IsPassable(n) || (throughMyTags && IsTaggedByMe(n)))) continue;
                seen.Add(idx);
                queue.Enqueue(n);
            }
        }
        return seen;
    }
}
