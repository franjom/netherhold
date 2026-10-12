// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Navigation;

/// <summary>The world as DK2's router and steering behaviours see it: one navigation byte per
/// tile (solid, water, lava or floor; a portal's centre is a hole, and a Heart's platform is floor),
/// the mesh built from those bytes and rebuilt whenever one changes, and the four probes
/// the behaviours call (docs/research/steering-behaviours.md §1). One instance serves one world for
/// its whole life; nothing here is simulation state, so nothing here is hashed.</summary>
/// <remarks>Not built: first-person-only obstacle classes;
/// the remembered entity of <c>Separation</c>'s second term. Each is recorded
/// on the method that would supply it.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/steering-behaviours.md")]
public sealed class WorldSteering : ISteeringWorld, INavByteGrid
{
    private readonly GameDatabase _database;
    private readonly WorldState _world;
    private byte[] _bytes = [];
    private byte[] _scratch = [];
    private NavMesh? _mesh;
    private MeshRouter? _router;
    private StructureBlocks? _structures;
    private readonly List<NavPoint> _waypoints = [];
    private readonly List<ObjectState> _objectObstacles = [];

    public WorldSteering(GameDatabase database, WorldState world)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public int Width => _world.Width;

    public int Height => _world.Height;

    /// <summary>How many times the mesh has been built; a change of navigation byte costs one.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Times <see cref="Refresh"/>, the mesh build and each route query. Set with the
    /// context's (<see cref="SimulationContext.Profiler"/>); observation only.</summary>
    public ISimulationProfiler Profiler { get; set; } = NullSimulationProfiler.Instance;

    /// <summary>Re-reads every tile's navigation byte and rebuilds the mesh if any changed. The
    /// original rebuilds around the tile its byte setter is told about; comparing the whole map is
    /// cheaper than hooking its five writers and gives the same mesh.</summary>
    public void Refresh()
    {
        using ProfileScope _ = Profiler.Scope(SimulationMarkers.NavRefresh);
        int count = _world.Width * _world.Height;
        if (_scratch.Length != count) _scratch = new byte[count];
        StructureBlocks structures = StructureBlocks.For(_world, _database);
        _structures = structures;
        FillNavigationBytes(_database, _world, structures, _scratch);

        if (_mesh != null && _scratch.AsSpan().SequenceEqual(_bytes)) return;
        (_bytes, _scratch) = (_scratch, _bytes);
        if (_scratch.Length != count) _scratch = new byte[count];
        using ProfileScope build = Profiler.Scope(SimulationMarkers.NavBuild);
        _mesh = NavMesh.Build(this);
        _router = new MeshRouter(_mesh);
        Rebuilds++;
    }

    /// <summary>Copies the navigation cache without constructing a routing mesh.</summary>
    public static byte[] CaptureNavigationBytes(GameDatabase database, WorldState world)
    {
        var bytes = new byte[world.TileCount];
        FillNavigationBytes(database, world, StructureBlocks.For(world, database), bytes);
        return bytes;
    }

    private static void FillNavigationBytes(GameDatabase database, WorldState world,
        StructureBlocks structures, byte[] bytes)
    {
        // ByteOf per terrain id, read once rather than looked up per tile; an id with no
        // definition still goes to TerrainById, which throws as it always did.
        Span<bool> known = stackalloc bool[256];
        Span<bool> solid = stackalloc bool[256];
        Span<byte> open = stackalloc byte[256];
        foreach (KeyValuePair<byte, TerrainDef> entry in database.Terrain)
        {
            known[entry.Key] = true;
            solid[entry.Key] = entry.Value.IsSolid;
            open[entry.Key] = ByteOf(entry.Value, portalCentre: false);
        }
        Span<bool> doubleQuad = stackalloc bool[256];
        foreach (RoomDef room in database.Rooms.Values)
            if (IsDoubleQuad(room)) doubleQuad[room.TerrainId] = true;

        bool anyDoubleQuad = false;
        for (int index = 0; index < world.TileCount; index++)
        {
            byte terrain = world.TileAtIndex(index).TerrainId;
            if (!known[terrain]) database.TerrainById(terrain);
            bytes[index] = solid[terrain] ? NavBytes.Solid : open[terrain];
            anyDoubleQuad |= doubleQuad[terrain];
        }

        // A portal's centre is its hole unless its terrain is solid (ByteOf's order).
        foreach (int centre in structures.PortalCentres)
            if (!solid[world.TileAtIndex(centre).TerrainId]) bytes[centre] = NavBytes.PortalHole;

        // Only DoubleQuad rooms mark their floor slots, so only they are filled, and only when the
        // map holds one.
        if (anyDoubleQuad)
            foreach (RoomInstance room in RoomIndex.Build(world, database, IsDoubleQuad))
                foreach (int tile in AutoFurnitureRules.FloorSlotSet(world, room)) bytes[tile] = NavBytes.PortalHole;

        // A closed, completed door overwrites its tile (`0x004A66F0`); an open one leaves floor.
        // A barricade (definition flag bit 6) carries the lock bit whatever its lock mode.
        foreach (DoorState door in world.Doors)
        {
            if (door.UnderConstruction || door.Open || !world.Contains(door.X, door.Y)) continue;
            bool barricade = database.Doors.TryGetValue(door.DoorId, out DoorDef? def) && def.Flags.HasFlag(DoorFlags.Barricade);
            bytes[(door.Y * world.Width) + door.X] =
                NavBytes.Door(door.PlayerId, door.LockMode != 0 || barricade, world.Tile(door.X, door.Y).IsTaggedByAnyone);
        }
    }

    private static readonly Predicate<RoomDef> IsDoubleQuad = room => room.Construction == TileConstruction.DoubleQuad;

    /// <summary>The navigation byte the original's cache holds (structure-blocks.md §5.4): solid,
    /// water, lava or floor from the terrain, and 4 on a portal's centre. A heart's platform is
    /// floor in the cache; what keeps creatures off it is not the router (Q352).</summary>
    public static byte ByteOf(TerrainDef terrain, bool portalCentre)
    {
        if (terrain.IsSolid) return NavBytes.Solid;
        if (portalCentre) return NavBytes.PortalHole;
        if (terrain.IsWater) return NavBytes.Water;
        if (terrain.IsLava) return NavBytes.Lava;
        return NavBytes.Floor;
    }

    public bool CanMove(Fixed16Vector from, Fixed16Vector to)
    {
        if (_structures is null || !_database.Objects.TryGetValue(DungeonHeartObjectId, out ObjectDef? heart)) return true;
        to = new Fixed16Vector((to.X >> 4) << 4, (to.Y >> 4) << 4);
        long radius = (heart.Width.Raw >> 1) << 4;
        foreach (int index in _structures.HeartCentres)
        {
            long dx = from.X - ((long)CreatureState.CentreOf(index % _world.Width) << 4);
            long dy = from.Y - ((long)CreatureState.CentreOf(index / _world.Width) << 4);
            long sx = (long)to.X - from.X, sy = (long)to.Y - from.Y;
            long startSquared = dx * dx + dy * dy;
            long dot = dx * sx + dy * sy;
            // A loaded body inside the core may leave, but never move deeper into it.
            if (startSquared < radius * radius)
            {
                if (dot < 0) return false;
                continue;
            }
            long lengthSquared = sx * sx + sy * sy;
            if (lengthSquared == 0 || dot >= 0) continue;
            if (-dot >= lengthSquared)
            {
                if ((dx + sx) * (dx + sx) + (dy + sy) * (dy + sy) < radius * radius) return false;
            }
            else
            {
                long fraction = (-dot << 16) / lengthSquared;
                long closestX = dx + ((sx * fraction) >> 16), closestY = dy + ((sy * fraction) >> 16);
                if (closestX * closestX + closestY * closestY < (radius + 1) * (radius + 1)) return false;
            }
        }
        return true;
    }

    public Fixed16Vector ConstrainTarget(Fixed16Vector from, Fixed16Vector target)
    {
        if (!_database.Objects.TryGetValue(DungeonHeartObjectId, out ObjectDef? heart)) return target;
        (int x, int y) = target.Tile();
        if (!_world.Contains(new TilePos(x, y)) ||
            _database.RoomByTerrain(_world.Tile(x, y).TerrainId)?.TypeId != RoomTypeId.DungeonHeart) return target;
        RoomInstance? room = RoomIndex.RoomAt(_world, _database, y * _world.Width + x);
        if (room is null || room.Width < 3 || room.Height < 3 ||
            (_world.HasPlayer(room.OwnerPlayerId) && _world.Player(room.OwnerPlayerId).HeartRuined)) return target;
        int radius = (heart.Width.Raw >> 1) << 4;
        var centre = new Fixed16Vector(CreatureState.CentreOf((room.MinX + room.MaxX) / 2) << 4,
            CreatureState.CentreOf((room.MinY + room.MaxY) / 2) << 4);
        long dx = (long)target.X - centre.X, dy = (long)target.Y - centre.Y;
        if (dx * dx + dy * dy >= (long)radius * radius) return target;
        var direction = Fixed16.Normalise(from - centre);
        if (direction == Fixed16Vector.Zero) direction = new Fixed16Vector(Fixed16.One, 0);
        return centre + direction.Scale(radius + (Fixed16.One >> 3));
    }

    public byte NavByte(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _world.Width || y >= _world.Height || _bytes.Length == 0) return NavBytes.Solid;
        return _bytes[(y * _world.Width) + x];
    }

    /// <summary>The cost function's six globals for one mover (navigation-mesh-and-search.md §4):
    /// the owner, the two definition flags that unlock water and lava, and the three door bits of
    /// the state's option word (<see cref="MoveOptions"/>: 7 respect doors, 1 pass own locked
    /// doors, 2 enter a portal's hole).</summary>
    public static MoverProfile ProfileOf(CreatureDef definition, PlayerId owner, MoveOptions options) =>
        new(definition.Flags.HasFlag(CreatureFlags.CanWalkOnWater), definition.Flags.HasFlag(CreatureFlags.CanWalkOnLava),
            owner.Value,
            RespectDoors: ((int)options & 0x80) != 0,
            PassOwnLockedDoors: ((int)options & 0x02) != 0,
            EntersPortalHole: ((int)options & 0x04) != 0);

    /// <summary>The body's position from the creature's, as the original does before every mover
    /// update (<c>0x004D59A0</c>): the entity's 1/4096 units shifted up four.</summary>
    public static void SyncBody(CreatureState creature) =>
        creature.Body.Pos = new Fixed16Vector(creature.PosX << 4, creature.PosY << 4);

    /// <summary>The state machine's route request (<c>0x004D0F60</c> → <c>0x004D59E0</c>): aim the
    /// body at the centre of <paramref name="to"/> with the <see cref="MoveOptions"/> word the
    /// original's state pushes, and load the first window. A creature already on that tile gets
    /// a finished route and is treated as arrived. False when the mesh has no route. The word's
    /// door bits reach the cost function through <see cref="ProfileOf"/>; most requests in a
    /// running game are the imp pass, which passes none of them (Q357).</summary>
    public bool TryRoute(CreatureState creature, TilePos to, MoveOptions options) =>
        TryRouteTo(creature, CreatureState.CentreOf(to.X), CreatureState.CentreOf(to.Y), options);

    /// <summary>The same request aimed at a position rather than a tile centre — what the fight
    /// state does with its target's own position (<c>0x00471D10</c>). A creature already on the
    /// position's tile gets a finished route.</summary>
    public bool TryRouteTo(CreatureState creature, int x, int y, MoveOptions options)
    {
        SyncBody(creature);
        creature.RouteOptions = options;
        byte flags = options.PathFlags();
        var target = new Fixed16Vector(x << 4, y << 4);
        if (creature.Tile == new TilePos(x / Fp32.One, y / Fp32.One))
        {
            RouteWindow.Load(creature.Body, [], target, flags);
            return true;
        }

        CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
        return RouteWindow.Request(creature.Body, this, ProfileOf(definition, creature.OwnerPlayerId, options), target, flags);
    }

    /// <summary>A worker's walk to its stance point inside the tile it works from (the dig and
    /// reinforce handlers route to <c>0x00698A80</c> and <c>0x00698AE0</c>'s points): as
    /// <see cref="TryRouteTo"/>, except that a creature already on that tile steps to the point
    /// rather than getting a finished route (ADR-0289). The fight approach asks the same way: DK2's
    /// route within one triangle is the one point (<c>0x004CF490</c>, ADR-0388).</summary>
    public bool TryRouteToStance(CreatureState creature, long x, long y, MoveOptions options)
    {
        if (creature is null) throw new ArgumentNullException(nameof(creature));
        if (creature.Tile != new TilePos((int)(x / Fp32.One), (int)(y / Fp32.One)))
            return TryRouteTo(creature, (int)x, (int)y, options);
        SyncBody(creature);
        creature.RouteOptions = options;
        var target = ConstrainTarget(creature.Body.Pos, new Fixed16Vector((int)x << 4, (int)y << 4));
        RouteWindow.Load(creature.Body, [target], target, options.PathFlags());
        return true;
    }

    public bool Route(Fixed16Vector from, Fixed16Vector to, MoverProfile mover, List<Fixed16Vector> points) =>
        Route(from, to, mover, points, refresh: true);

    /// <summary>The route query with the rebuild check left to the caller: a scan that asks many
    /// times in one tick refreshes once and passes false.</summary>
    public bool Route(Fixed16Vector from, Fixed16Vector to, MoverProfile mover, List<Fixed16Vector> points, bool refresh)
    {
        if (refresh || _router is null) Refresh();
        using ProfileScope _ = Profiler.Scope(SimulationMarkers.NavRoute);
        if (!_router!.TryRoute(new NavPoint(from.X >> 8, from.Y >> 8), new NavPoint(to.X >> 8, to.Y >> 8), mover, _waypoints))
            return false;
        foreach (NavPoint p in _waypoints) points.Add(new Fixed16Vector(p.X << 8, p.Y << 8));
        return true;
    }

    /// <summary>The three objects a running game holds on a heart's centre tile (Level 10 object
    /// dump, structure-blocks.md §5.3): the <c>Dungeon Heart Obstacle</c> (100, width 3.0, solid),
    /// which DK2's enumerator skips on its tile and forwards once from the room's centre with flag
    /// bits 0 and 1, and the visible <c>Dungeon Heart</c> (13, width 1.0) and <c>Arches</c> (86,
    /// width 0.6), plain shoves. Torture devices also contribute their shipped position,
    /// Obstacle flag and width/2. Ordinary queries retain the binary's flag toggle
    /// and tile-head order (structure-blocks.md §5).</summary>
    public void CollectObstacles(Fixed16Vector centre, int range, List<SteeringObstacle> into)
    {
        int half = Math.Min((range / 2) >> 4, Fp32.One);
        int x0 = Math.Max(0, ((centre.X >> 4) - half) >> 12);
        int y0 = Math.Max(0, ((centre.Y >> 4) - half) >> 12);
        int x1 = Math.Min(_world.Width - 1, ((centre.X >> 4) + half) >> 12);
        int y1 = Math.Min(_world.Height - 1, ((centre.Y >> 4) + half) >> 12);
        _objectObstacles.Clear();
        foreach (ObjectState device in _world.Objects)
        {
            if (!device.TileLink.IsLinked || device.ObjectTypeId == DungeonHeartObstacleObjectId) continue;
            int tx = device.TileLink.TileIndex % _world.Width, ty = device.TileLink.TileIndex / _world.Width;
            if (tx < x0 || tx > x1 || ty < y0 || ty > y1) continue;
            _objectObstacles.Add(device);
        }
        _objectObstacles.Sort((left, right) => left.TileLink.TileIndex != right.TileLink.TileIndex
            ? left.TileLink.TileIndex.CompareTo(right.TileLink.TileIndex)
            : right.TileLink.Order.CompareTo(left.TileLink.Order));

        bool solid = false;
        foreach (ObjectState device in _objectObstacles)
        {
            ObjectDef? definition = _database.ObjectById(device.ObjectTypeId);
            if (definition is null || !definition.Flags.HasFlag(ObjectFlags.Obstacle)) continue;
            int size = (definition.Width.Raw >> 1) << 4;
            if (size <= 0) continue;
            // 0x004D1439 ORs bit 0; 0x004D14A1 XORs it, retaining the previous callback's flags.
            solid = definition.Flags.HasFlag(ObjectFlags.SolidObstacle) || !solid;
            int x = CreatureState.CentreOf(device.TileIndex % _world.Width);
            int y = CreatureState.CentreOf(device.TileIndex / _world.Width);
            if (device.PosX != 0 || device.PosY != 0) (x, y) = (device.PosX, device.PosY);
            if (device.AutoPlaced && RoomIndex.RoomAt(_world, _database, device.TileIndex) is { } room)
                (x, y) = AutoFurnitureRules.FurniturePosition(_world, _database, room, device);
            var position = new Fixed16Vector(x << 4, y << 4);
            into.Add(new SteeringObstacle(position, size, solid, false));
        }
        if (_structures is null || _structures.HeartCentres.Count == 0) return;

        foreach (int index in _structures.HeartCentres)
        {
            var pos = new Fixed16Vector(
                CreatureState.CentreOf(index % _world.Width) << 4, CreatureState.CentreOf(index / _world.Width) << 4);
            foreach (byte id in HeartObjectIds)
            {
                if (!_database.Objects.TryGetValue(id, out ObjectDef? def)) continue;
                int size = (def.Width.Raw << 4) >> 1;
                if ((pos - centre).Octile() > range + size) continue;
                into.Add(new SteeringObstacle(pos, size, def.Flags.HasFlag(ObjectFlags.SolidObstacle), id == DungeonHeartObstacleObjectId));
            }
        }
    }

    /// <summary>OBJECTS record 100, the invisible object a heart keeps creatures off with.</summary>
    public const byte DungeonHeartObstacleObjectId = 100;

    /// <summary>OBJECTS records 13 (<c>Dungeon Heart</c>) and 86 (<c>Arches</c>), also on the centre.</summary>
    public const byte DungeonHeartObjectId = 13, ArchesObjectId = 86;

    private static readonly byte[] HeartObjectIds = [DungeonHeartObstacleObjectId, DungeonHeartObjectId, ArchesObjectId];

    /// <summary>The crowd probe <c>0x004D15E0</c>: tiles from the floor of the square's low edge to
    /// the ceiling of its high edge, both inclusive, and every creature standing on one of them adds
    /// its radius word, the asking creature included.</summary>
    public int CrowdWeight(Fixed16Vector point, int span)
    {
        int half = span / 2;
        int x0 = (point.X - half) >> 16, x1 = (((point.X + half) >> 4) + 0xFFF) >> 12;
        int y0 = (point.Y - half) >> 16, y1 = (((point.Y + half) >> 4) + 0xFFF) >> 12;
        int sum = 0;
        foreach (CreatureState c in _world.Creatures)
        {
            TilePos t = c.Tile;
            if (t.X >= x0 && t.X <= x1 && t.Y >= y0 && t.Y <= y1) sum += (short)c.Body.Radius;
        }

        return sum;
    }

    public void CollectNeighbours(Fixed16Vector centre, int range, List<SteeringNeighbour> into)
    {
        foreach (CreatureState c in _world.Creatures)
        {
            if ((c.Body.Pos - centre).Octile() > range) continue;
            into.Add(new SteeringNeighbour(c.Body, _database.CreatureById(c.CreatureTypeId).SizeClass));
        }
    }

    /// <summary>The remembered entity is not supplied to <c>Separation</c> yet
    /// (navigation-and-steering.md §3.2), so its second term never applies.</summary>
    public SteeringBody? RelatedBody(SteeringMover mover, out int reachTiles)
    {
        reachTiles = 0;
        return null;
    }
}
