// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.AI;

/// <summary>
/// A captured observation for one keeper and one tick. It retains no mutable
/// world or creature references; advancing the simulation cannot change it.
/// </summary>
public sealed class KeeperView : INavByteGrid
{
    private readonly KeeperTileView[] _tiles;
    private readonly AvailabilityState[] _availability;

    public KeeperView(GameDatabase database, WorldState world, byte playerId,
        IEnumerable<byte>? playerActivationOrder = null)
    {
        Database = database;
        PlayerId = playerId;
        Tick = world.CurrentTick;
        Status = world.Player(playerId).Status;
        AlliedWith = world.Player(playerId).AlliedWith;
        // Original activation prepends to the active list. Hosts supply PLAYERS record order;
        // without it, use this simulation world's ascending creation order.
        PlayerOrder = Array.AsReadOnly((playerActivationOrder ?? world.PlayerIds)
            .Where(id => id != PlayerIds.Unowned).Reverse().ToArray());
        Width = world.Width;
        Height = world.Height;
        Gold = GoldStore.Total(world, playerId);
        Mana = world.Player(playerId).Mana;
        ImpCount = ImpSupplyRules.ImpCount(world, database, playerId);
        Messages = Array.AsReadOnly(world.Messages.Entries.Where(m => m.PlayerId == playerId).ToArray());
        _tiles = new KeeperTileView[world.TileCount];
        _availability = world.Player(playerId).Availabilities.ToArray();

        int heartIndex = ImpSupplyRules.HeartTileOf(world, database, playerId);
        Heart = heartIndex < 0 ? null : FromIndex(heartIndex);

        byte[] navigation = WorldSteering.CaptureNavigationBytes(database, world);
        int owned = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            TileState tile = world.TileAtIndex(i);
            _tiles[i] = new KeeperTileView(tile.TerrainId, tile.OwnerPlayerId, tile.TaggedBy, navigation[i]);
            if (_tiles[i].OwnerPlayerId == playerId) owned++;
        }
        OwnedTiles = owned;

        AllRooms = RoomIndex.Build(world, database).Select(r => new KeeperRoomView(r)).ToList().AsReadOnly();
        OwnRooms = AllRooms.Where(r => r.OwnerPlayerId == playerId).ToList().AsReadOnly();
        // Each owner's acquisition-ordered list, the order DK2's keeper walks (ADR-0273).
        var ranks = new Dictionary<int, int>();
        foreach (byte owner in world.PlayerIds)
        {
            int rank = 0;
            foreach (CreatureState listed in world.OwnedCreatures(owner)) ranks[listed.Id] = rank++;
        }
        List<HandItem> hand = world.Player(playerId).Hand;
        KeeperSpellDef? callToArms = database.KeeperSpellById(CallToArmsRules.SpellId);
        KeeperSpellDef? thunderbolt = database.KeeperSpellById(KeeperSpellRules.Thunderbolt);
        WorldSteering? steering = null;
        var route = new List<Fixed16Vector>();
        var creatures = new List<KeeperCreatureView>(world.Creatures.Count);
        foreach (CreatureState c in world.Creatures)
        {
            // A corpse record holds no state (-1) and no table row; it is never a candidate.
            bool hasState = CreatureStateMachine.HasState(c);
            int resolved = hasState ? NeedRules.ResolvedStateId(c) : c.Dk2StateId;
            // The battle step's fighter: an own creature in the combat category through a walk.
            // DK2's held creature sits in state 76 or 78, outside that category; ours holds idle.
            bool fighter = hasState && c.OwnerPlayerId == playerId && !c.InHand
                && CreatureStateTable.Get(resolved).Category == CreatureStateCategory.Combat;
            bool routed = false;
            if (fighter && Heart is TilePos heartTile)
            {
                steering ??= new WorldSteering(database, world);
                route.Clear();
                routed = steering.Route(StagingPoint(heartTile), new Fixed16Vector(c.PosX << 4, c.PosY << 4),
                    StagingWalker(playerId), route, refresh: false);
            }
            creatures.Add(new KeeperCreatureView(
                c.Id, c.CreatureTypeId, c.OwnerPlayerId, c.Activity, c.Health, c.Level,
                c.LastSlapTick, HandRules.CanSlap(database, c, playerId),
                c.PosX, c.PosY, c.InHand, c.CarriedByCreatureId, c.RescuePriorityMask,
                HandRules.HasPickupPermission(world, database, c, playerId),
                HandRules.CanPickUp(world, database, c, playerId),
                c.Dk2StateId, resolved,
                hasState && DK2.Simulation.Combat.CombatRules.MayLookForAFight(world, database, c, false),
                CreatureLevelRules.MaxHealth(database, database.CreatureById(c.CreatureTypeId), c.Level),
                HandRules.LastDroppedCounter(c, world.CurrentTick.Value), hand.Contains(HandItem.Of(c)),
                ranks.TryGetValue(c.Id, out int rankOf) ? rankOf : -1,
                CallToArmsRules.IsUnderCallToArms(world, c),
                FightTargetOwner(world, c),
                fighter && callToArms is not null && KeeperSpellRules.PlaceAllowed(world, database, this, playerId, callToArms, c.Tile),
                routed,
                c.OwnerPlayerId != playerId && c.Activity != CreatureActivity.Dead && thunderbolt is not null
                    && KeeperSpellRules.CreatureTargetAllowed(world, database, this, playerId, thunderbolt, c)));
        }
        Creatures = creatures.AsReadOnly();
        CallToArms = world.Player(playerId).CallToArms;
        Traps = world.Traps.Select(t => new KeeperTrapView(t.TrapId, t.PlayerId, t.X, t.Y,
            database.Traps[t.TrapId].Threat)).ToList().AsReadOnly();
    }

    /// <summary>The engagement target's owner as the battle step reads it: the creature's owner
    /// while it lives, the heart's owner for a heart, 0 with no target.</summary>
    private static byte FightTargetOwner(WorldState world, CreatureState creature)
    {
        if (!CreatureStateMachine.HasState(creature)) return 0;
        switch (creature.FightTargetKind)
        {
            case DK2.Simulation.Combat.FightTargetKind.Creature:
                CreatureState? target = world.CreatureById(creature.FightTargetId);
                return target is null || target.Activity == CreatureActivity.Dead ? (byte)0 : target.OwnerPlayerId;
            case DK2.Simulation.Combat.FightTargetKind.Heart:
                return creature.FightTargetPlayerId;
            default:
                return 0;
        }
    }

    /// <summary>Two tiles east of the heart tile, at its centre: where the battle step's route test
    /// starts (<c>0x00503060</c>, heart x + 2).</summary>
    public static Fixed16Vector StagingPoint(TilePos heart) =>
        new(CreatureState.CentreOf(heart.X + 2) << 4, CreatureState.CentreOf(heart.Y) << 4);

    /// <summary>The route test's mover (<c>0x004D1700</c> with options 1, 0, 0, 0): the player's
    /// own locked doors pass, no lava; its water flag is a stale global, taken as off.</summary>
    public static MoverProfile StagingWalker(byte playerId) =>
        new(false, false, playerId, RespectDoors: false, PassOwnLockedDoors: true, EntersPortalHole: false);

    internal KeeperView(GameDatabase database, byte playerId, GameTick tick, byte status, byte alliedWith, byte[] playerIds,
        int width, int height, int gold, int impCount, TilePos? heart,
        KeeperTileView[] tiles, AvailabilityState[] availability,
        KeeperCreatureView[] creatures, KeeperTrapView[] traps, KeeperRoomView[] rooms, PlayerMessage[] messages,
        CallToArmsState callToArms = default, int mana = 0)
    {
        CallToArms = callToArms;
        Database = database;
        PlayerId = playerId;
        Tick = tick;
        Status = status;
        AlliedWith = alliedWith;
        PlayerOrder = Array.AsReadOnly(playerIds);
        Width = width;
        Height = height;
        Gold = gold;
        Mana = mana;
        ImpCount = impCount;
        Heart = heart;
        _tiles = tiles;
        _availability = availability;
        Creatures = Array.AsReadOnly(creatures);
        Traps = Array.AsReadOnly(traps);
        Messages = Array.AsReadOnly(messages);
        AllRooms = Array.AsReadOnly(rooms);
        OwnRooms = rooms.Where(r => r.OwnerPlayerId == playerId).ToList().AsReadOnly();
        OwnedTiles = tiles.Count(t => t.OwnerPlayerId == playerId);
    }

    internal IReadOnlyList<AvailabilityState> AvailabilityEntries => _availability;

    public GameDatabase Database { get; }
    public byte PlayerId { get; }
    public GameTick Tick { get; }
    public byte Status { get; }
    public byte AlliedWith { get; }
    public IReadOnlyList<byte> PlayerOrder { get; }

    public bool AreAllied(byte otherId) => otherId == PlayerId
        || (otherId < 8 && (AlliedWith & (1 << otherId)) != 0);
    public IReadOnlyList<KeeperCreatureView> Creatures { get; }
    public IReadOnlyList<KeeperTrapView> Traps { get; }
    public IReadOnlyList<PlayerMessage> Messages { get; }

    /// <summary>This keeper's Call to Arms: on, its flag's tile and the turn its upkeep starts.</summary>
    public CallToArmsState CallToArms { get; }

    public IReadOnlyList<KeeperRoomView> AllRooms { get; }
    public IReadOnlyList<KeeperRoomView> OwnRooms { get; }

    /// <summary>The keeper's Dungeon Heart tile, or null when it has none left.</summary>
    public TilePos? Heart { get; }

    public int OwnedTiles { get; }

    public int Gold { get; }

    /// <summary>The keeper's mana pool (<c>player+0x82</c>), which the battle step's cast tests.</summary>
    public int Mana { get; }
    public int ImpCount { get; }

    /// <summary>The navigation byte captured for a tile (<see cref="INavByteGrid"/>).</summary>
    public byte NavByte(int x, int y) => _tiles[(y * Width) + x].NavigationByte;

    /// <summary>The availability row for a kind and type, or null when the player has none.</summary>
    public AvailabilityState? AvailabilityRow(ushort kind, int typeId)
    {
        foreach (AvailabilityState entry in _availability)
            if (entry.Kind == kind && entry.TypeId == typeId) return entry;
        return null;
    }

    public int Width { get; }
    public int Height { get; }
    public int TileCount => _tiles.Length;

    public bool InBounds(TilePos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
    public int IndexOf(TilePos p) => (p.Y * Width) + p.X;
    public TilePos FromIndex(int index) => new(index % Width, index / Width);

    public KeeperTileView Tile(TilePos p)
    {
        if (!InBounds(p)) throw new ArgumentOutOfRangeException(nameof(p));
        return _tiles[IndexOf(p)];
    }

    public int Availability(ushort kind, int typeId)
    {
        foreach (AvailabilityState entry in _availability)
            if (entry.Kind == kind && entry.TypeId == typeId) return entry.Value;
        return 1;
    }

    public bool IsUpgraded(ushort kind, int typeId)
    {
        foreach (AvailabilityState entry in _availability)
            if (entry.Kind == kind && entry.TypeId == typeId) return entry.Upgraded;
        return false;
    }

    public TerrainDef Terrain(TilePos p) => Database.TerrainById(Tile(p).TerrainId);

    public RoomPlacement CanBuild(TilePos p, RoomDef room) =>
        InBounds(p) ? RoomRules.CanBuild(Database, Tile(p).ToTileState(), room, PlayerId)
            : RoomPlacement.OffMap;

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

    public bool IsMine(TilePos p) => InBounds(p) && Tile(p).OwnerPlayerId == PlayerId;

    /// <summary>Threat-reader candidate gate: ownership and navigation bit 3 (0x00509200).</summary>
    public bool IsThreatDropCandidate(TilePos p) => IsMine(p) && !NavBytes.IsSolid(Tile(p).NavigationByte);
    public bool HasRoom(TilePos p) => InBounds(p) && Terrain(p).IsRoom;
    public bool IsTaggedByMe(TilePos p) => InBounds(p) && Tile(p).IsTaggedBy(PlayerId);
    public bool IsTaggedByAnyone(TilePos p) => InBounds(p) && Tile(p).IsTaggedByAnyone;

    /// <summary>Solid rock that yields gold when dug.</summary>
    public bool IsGoldRock(TilePos p)
    {
        if (!InBounds(p)) return false;
        TerrainDef t = Terrain(p);
        return t.IsSolid && t.GoldValue > 0;
    }

    /// <summary>A room instance may be dug into or built on when it is nobody's or the neutral side's.</summary>
    public static bool IsNeutral(KeeperRoomView room) =>
        room.OwnerPlayerId == PlayerIds.Unowned || room.OwnerPlayerId == PlayerIds.Neutral;

    public static TilePos Centre(KeeperRoomView room) =>
        new((room.MinX + room.MaxX) / 2, (room.MinY + room.MaxY) / 2);

    public static int DistanceSquared(TilePos a, TilePos b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    public KeeperRoomView? OwnRoomOfType(byte roomId)
    {
        foreach (KeeperRoomView r in OwnRooms)
            if (r.Definition.RoomId == roomId) return r;
        return null;
    }

    public KeeperRoomView? NearestOwnRoom(TilePos to, bool avoidBridges = false)
    {
        KeeperRoomView? best = null;
        int bestDist = int.MaxValue;
        foreach (KeeperRoomView r in OwnRooms)
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
