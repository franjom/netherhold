// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Runtime.InteropServices;
using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Navigation;

/// <summary>The world as DK2's router and steering behaviours see it: one navigation byte per
/// tile (solid, water, lava or floor; a portal's centre is a hole, and a Heart's platform is floor),
/// the mesh built from those bytes, and the four probes
/// the behaviours call (docs/research/steering-behaviours.md §1). One instance serves one world for
/// its whole life. The mesh is edited in place as tiles change (ADR-0461), so it, the bytes it was
/// last brought to and the platforms are simulation state: saved, restored and hashed.</summary>
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
    private byte[] _routingBytes = [];
    private byte[] _routingScratch = [];
    private readonly PlatformRoutingGrid _routingGrid;
    private NavMesh? _mesh;
    private MeshRouter? _router;
    private StructureBlocks? _structures;
    private IReadOnlyList<int> _platformCentres = [];
    private TileState[] _tileSnapshot = [];
    private readonly List<DoorKey> _doorSnapshot = [];
    private readonly List<long> _cryptSnapshot = [];
    private readonly List<long> _cryptScratch = [];
    private ulong _ruinedSnapshot;
    private int _navStampSnapshot;
    private readonly List<NavPoint> _waypoints = [];
    private readonly List<ObjectState> _objectObstacles = [];
    private readonly bool _wholeBuilds;
    private int[] _builtLocate = [];

    public WorldSteering(GameDatabase database, WorldState world) : this(database, world, wholeBuilds: false) { }

    /// <param name="wholeBuilds">Rebuild whole when routing bytes change, never edit in place,
    /// and keep the build's locate cache for <see cref="RestoreBuiltLocate"/>. Used by independent
    /// keeper probes; the simulation's own steering keeps its in-place edits.</param>
    public WorldSteering(GameDatabase database, WorldState world, bool wholeBuilds)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _routingGrid = new PlatformRoutingGrid(this);
        _wholeBuilds = wholeBuilds;
    }

    public int Width => _world.Width;

    public int Height => _world.Height;

    /// <summary>Refreshes that built or edited the mesh: the first build, and each refresh after
    /// which a navigation byte differed.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Times <see cref="Refresh"/>, the mesh build and each route query. Set with the
    /// context's (<see cref="SimulationContext.Profiler"/>); observation only.</summary>
    public ISimulationProfiler Profiler { get; set; } = NullSimulationProfiler.Instance;

    /// <summary>Builds the mesh once, then edits it in place around each routing byte that changed
    /// since the last refresh, as the original does at each write (<c>0x004CE770</c>'s partial
    /// path, navigation-partial-rebuild.md, Q591, ADR-0461). The original edits at the moment a
    /// byte is written; ours edits every byte changed since the last refresh, in tile order, so a
    /// refresh that finds several changes may edit them in another order than the writes came.
    /// The mesh is therefore state: a save carries it (<see cref="Write"/>) and the state hash
    /// folds it in (<see cref="HashInto"/>).</summary>
    public void Refresh()
    {
        using ProfileScope _ = Profiler.Scope(SimulationMarkers.NavRefresh);
        // Everything below is a function of the tiles, the doors, the stamped bytes
        // (WorldState.NavStamps), which hearts are ruined, where the crypts stand (a Graveyard's
        // floor slots) and the database. While those are what the last refresh saw, its result stands, and the comparison is one pass over the tile array,
        // where the refresh is several with a branch per tile (ADR-0454). The snapshot is taken
        // once the refresh has succeeded, so one that throws before the bytes are swapped is
        // retried; an edit that throws leaves the mesh as the throw left it (ADR-0461).
        if (_mesh != null && Unchanged()) return;
        if (!RoutingBytesChanged())
        {
            Snapshot();
            return;
        }
        using ProfileScope build = Profiler.Scope(SimulationMarkers.NavBuild);
        if (_mesh is null || _wholeBuilds)
        {
            _mesh = NavMesh.Build(_routingGrid);
            _router = new MeshRouter(_mesh);
            if (_wholeBuilds) _builtLocate = _mesh.CaptureLocateCache();
        }
        else
        {
            int count = _routingBytes.Length;
            for (int i = 0; i < count; i++)
            {
                if (_routingBytes[i] == _routingScratch[i]) continue;
                int x = i % Width, y = i / Width;
                _mesh.Retriangulate(_routingGrid, x, y, x + 1, y + 1);
            }
        }
        Rebuilds++;
        Snapshot();
    }

    /// <summary>Restores the last whole build's locate cache for an independent questioner.</summary>
    public void RestoreBuiltLocate()
    {
        if (!_wholeBuilds) throw new InvalidOperationException("only a whole-build steering keeps its build's locate cache");
        _mesh?.RestoreLocateCache(_builtLocate);
    }

    /// <summary>Recomputes the navigation bytes. False when a mesh stands and
    /// the bytes have not changed; otherwise the new routing bytes are
    /// in <see cref="_routingBytes"/> and the ones the mesh was last brought to in
    /// <see cref="_routingScratch"/>.</summary>
    private bool RoutingBytesChanged()
    {
        int count = _world.Width * _world.Height;
        if (_scratch.Length != count) _scratch = new byte[count];
        StructureBlocks structures = StructureBlocks.For(_world, _database);
        _structures = structures;
        _platformCentres = structures.HeartCentres;
        FillNavigationBytes(_database, _world, structures, _scratch);

        if (_mesh != null && _scratch.AsSpan().SequenceEqual(_bytes)) return false;
        (_bytes, _scratch) = (_scratch, _bytes);
        if (_scratch.Length != count) _scratch = new byte[count];
        if (_routingScratch.Length != count) _routingScratch = new byte[count];
        Array.Copy(_bytes, _routingScratch, count);
        (_routingBytes, _routingScratch) = (_routingScratch, _routingBytes);
        if (_routingScratch.Length != count) _routingScratch = new byte[count];
        return true;
    }

    /// <summary>The mesh, the bytes it was last brought to and the live-Heart platforms those bytes
    /// masked, for a save; nothing when no route has been asked for yet, since the mesh is then
    /// built from the tiles at the first ask.</summary>
    public void Write(BinaryWriter w)
    {
        if (w is null) throw new ArgumentNullException(nameof(w));
        w.Write(_mesh is not null);
        if (_mesh is null) return;
        w.Write(_bytes.Length);
        w.Write(_bytes);
        w.Write(_routingBytes.Length);
        w.Write(_routingBytes);
        w.Write(_platformCentres.Count);
        foreach (int centre in _platformCentres) w.Write(centre);
        _mesh.Write(w);
    }

    /// <summary>Restores what <see cref="Write"/> saved. No tile snapshot is taken, so the next
    /// <see cref="Refresh"/> compares the tiles afresh and edits the mesh for every byte that
    /// differs from the saved ones, as the run that was saved would have at its next refresh; the
    /// saved platforms keep that comparison the saved run's. The structure blocks the probes read
    /// are derived from the loaded tiles.</summary>
    public void Read(BinaryReader r)
    {
        if (r is null) throw new ArgumentNullException(nameof(r));
        if (!r.ReadBoolean()) return;
        int count = _world.Width * _world.Height;
        int length = r.ReadInt32();
        if (length != count) throw new InvalidDataException($"a navigation save of {length} bytes for a map of {count} tiles");
        _bytes = r.ReadBytes(count);
        length = r.ReadInt32();
        if (length != count) throw new InvalidDataException($"a routing save of {length} bytes for a map of {count} tiles");
        _routingBytes = r.ReadBytes(count);
        int centres = r.ReadInt32();
        if (centres < 0 || centres > count) throw new InvalidDataException($"{centres} heart platforms in a navigation save");
        var platforms = new int[centres];
        for (int i = 0; i < centres; i++) platforms[i] = r.ReadInt32();
        _platformCentres = platforms;
        _mesh = NavMesh.Read(r);
        _router = new MeshRouter(_mesh);
        _structures = StructureBlocks.For(_world, _database);
        _tileSnapshot = [];
    }

    /// <summary>Folds the mesh into the state hash; nothing while no mesh stands.</summary>
    public void HashInto(ref Hash64 hash)
    {
        hash.Add(_mesh is not null);
        _mesh?.HashInto(ref hash);
    }

    /// <summary>What of a door the navigation bytes read.</summary>
    private readonly record struct DoorKey(int X, int Y, byte DoorId, byte PlayerId, byte LockMode, bool UnderConstruction, bool Open);

    private static DoorKey KeyOf(DoorState door) =>
        new(door.X, door.Y, door.DoorId, door.PlayerId, door.LockMode, door.UnderConstruction, door.Open);

    private bool Unchanged()
    {
        TileState[] tiles = _world.RawTiles;
        if (tiles.Length != _tileSnapshot.Length
            || !MemoryMarshal.AsBytes(tiles.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(_tileSnapshot.AsSpan())))
            return false;
        IReadOnlyList<DoorState> doors = _world.Doors;
        if (doors.Count != _doorSnapshot.Count) return false;
        for (int i = 0; i < doors.Count; i++)
            if (KeyOf(doors[i]) != _doorSnapshot[i]) return false;
        if (RuinedHearts() != _ruinedSnapshot) return false;
        if (_world.NavStamps.Count != _navStampSnapshot) return false;
        Crypts(_cryptScratch);
        if (_cryptScratch.Count != _cryptSnapshot.Count) return false;
        for (int i = 0; i < _cryptScratch.Count; i++)
            if (_cryptScratch[i] != _cryptSnapshot[i]) return false;
        return true;
    }

    /// <summary>The crypts on the map, as a Graveyard's floor slots read them
    /// (<see cref="AutoFurnitureRules.FloorSlotSet"/>): tile and whether held, in object order.</summary>
    private void Crypts(List<long> into)
    {
        into.Clear();
        foreach (ObjectState thing in _world.Objects)
            if (thing.ObjectTypeId == AutoFurnitureRules.CryptObjectId)
                into.Add(((long)thing.TileIndex << 1) | (thing.InHand ? 1L : 0L));
    }

    /// <summary>One bit per player slot whose heart is ruined; the structure mask drops such a heart.</summary>
    private ulong RuinedHearts()
    {
        ulong mask = 0;
        for (int i = 0; i < _world.PlayerCount && i < 64; i++)
            if (_world.PlayerAtIndex(i).HeartRuined) mask |= 1UL << i;
        return mask;
    }

    private void Snapshot()
    {
        TileState[] tiles = _world.RawTiles;
        if (_tileSnapshot.Length != tiles.Length) _tileSnapshot = new TileState[tiles.Length];
        Array.Copy(tiles, _tileSnapshot, tiles.Length);
        _doorSnapshot.Clear();
        foreach (DoorState door in _world.Doors) _doorSnapshot.Add(KeyOf(door));
        _ruinedSnapshot = RuinedHearts();
        _navStampSnapshot = _world.NavStamps.Count;
        Crypts(_cryptSnapshot);
    }

    /// <summary>The routing bytes the steering currently holds, read through the steering so that
    /// the arrays may be swapped under it.</summary>
    private sealed class PlatformRoutingGrid(WorldSteering owner) : INavByteGrid
    {
        public int Width => owner.Width;
        public int Height => owner.Height;
        public byte NavByte(int x, int y) => x < 0 || y < 0 || x >= owner.Width || y >= owner.Height
            ? NavBytes.Solid : owner._routingBytes[(y * owner.Width) + x];
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

        // A removed door's 0 over water or lava (Q600) and the Heart builder's 3 (Q601) stand until
        // the terrain changes. Index loop: the list is an interface, and a foreach would box.
        IReadOnlyList<NavStamp> stamps = world.NavStamps;
        for (int i = 0; i < stamps.Count; i++)
            if (world.TileAtIndex(stamps[i].Tile).TerrainId == stamps[i].TerrainId) bytes[stamps[i].Tile] = stamps[i].Byte;

        // A closed, completed door overwrites its tile (`0x004A66F0`); an open one leaves floor.
        // A barricade (definition flag bit 6) carries the lock bit whatever its lock mode.
        foreach (DoorState door in world.Doors)
        {
            if (door.UnderConstruction || door.Open || !world.Contains(door.X, door.Y)) continue;
            bool barricade = database.Doors.TryGetValue(door.DoorId, out DoorDef? def) && def.Flags.HasFlag(DoorFlags.Barricade);
            bytes[(door.Y * world.Width) + door.X] =
                NavBytes.Door(door.PlayerId, door.LockMode != 0 || barricade, world.Tile(door.X, door.Y).SecretDoor);
        }
    }

    private static readonly Predicate<RoomDef> IsDoubleQuad = room => room.Construction == TileConstruction.DoubleQuad;

    /// <summary>The navigation byte the original's cache holds (structure-blocks.md §5.4): solid,
    /// water, lava or floor from the terrain, and 4 on a portal's centre. A heart's platform is
    /// floor in the native cache but for the builder's 3 on its centre (<see cref="WorldState.NavStamps"/>,
    /// Q601). Routing reads these same bytes.</summary>
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
        var centre = new Fixed16Vector(CreatureState.CentreOf((room.MinX + room.MaxX) / 2) << 4,
            CreatureState.CentreOf((room.MinY + room.MaxY) / 2) << 4);
        int radius = (heart.Width.Raw >> 1) << 4;
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

    /// <summary>What <c>Creature_RequestRoute</c> (<c>0x004D0F60</c>) does before every route
    /// load, found or not: the body takes the entity position and a zero velocity
    /// (<c>0x004D59A0</c>). Only <c>Follow_Path</c>'s own window refills skip it.</summary>
    private static void ResetBodyForRequest(CreatureState creature)
    {
        SyncBody(creature);
        creature.Body.Vel = Fixed16Vector.Zero;
    }

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
        ResetBodyForRequest(creature);
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
        ResetBodyForRequest(creature);
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
        to = ConstrainTarget(from, to);
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
        int half = range / 2;
        int x0 = Math.Max(0, (centre.X - half) >> 16);
        int y0 = Math.Max(0, (centre.Y - half) >> 16);
        int x1 = Math.Min(Width - 1, (((centre.X + half) >> 4) + 0xFFF) >> 12);
        int y1 = (((centre.Y + half) >> 4) + 0xFFF) >> 12;
        // The native upper-Y clamp uses width, even on a non-square map.
        if (y1 >= Height) y1 = Width - 1;
        foreach (CreatureState c in _world.Creatures)
        {
            TilePos tile = c.Tile;
            if (!_world.Contains(tile.X, tile.Y) || tile.X < x0 || tile.X > x1 || tile.Y < y0 || tile.Y > y1) continue;
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
