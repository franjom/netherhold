// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Navigation;

/// <summary>The world as DK2's router and steering behaviours see it: one navigation byte per
/// tile (solid, water, lava or floor; a portal's centre and a heart's platform count as solid,
/// Q88), the mesh built from those bytes and rebuilt whenever one changes, and the four probes
/// the behaviours call (docs/research/steering-behaviours.md §1). One instance serves one world for
/// its whole life; nothing here is simulation state, so nothing here is hashed.</summary>
/// <remarks>Not built: <c>Obstacle</c> objects other than the heart's, so <c>Repel_Obstacle</c>
/// sees no furniture; the remembered entity of <c>Separation</c>'s second term. Each is recorded
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

    public WorldSteering(GameDatabase database, WorldState world)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public int Width => _world.Width;

    public int Height => _world.Height;

    /// <summary>How many times the mesh has been built; a change of navigation byte costs one.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Re-reads every tile's navigation byte and rebuilds the mesh if any changed. The
    /// original rebuilds around the tile its byte setter is told about; comparing the whole map is
    /// cheaper than hooking its five writers and gives the same mesh.</summary>
    public void Refresh()
    {
        int count = _world.Width * _world.Height;
        if (_scratch.Length != count) _scratch = new byte[count];
        StructureBlocks structures = StructureBlocks.For(_world, _database);
        _structures = structures;
        for (int y = 0; y < _world.Height; y++)
        {
            for (int x = 0; x < _world.Width; x++)
            {
                int index = (y * _world.Width) + x;
                _scratch[index] = ByteOf(_database.TerrainById(_world.Tile(x, y).TerrainId), structures.IsPortalCentre(index));
            }
        }

        // A closed, completed door overwrites its tile (`0x004A66F0`); an open one leaves floor.
        foreach (DoorState door in _world.Doors)
        {
            if (door.UnderConstruction || door.Open || !_world.Contains(door.X, door.Y)) continue;
            _scratch[(door.Y * _world.Width) + door.X] =
                NavBytes.Door(door.PlayerId, door.LockMode != 0, _world.Tile(door.X, door.Y).IsTaggedByAnyone);
        }

        if (_mesh != null && _scratch.AsSpan().SequenceEqual(_bytes)) return;
        (_bytes, _scratch) = (_scratch, _bytes);
        if (_scratch.Length != count) _scratch = new byte[count];
        _mesh = NavMesh.Build(this);
        _router = new MeshRouter(_mesh);
        Rebuilds++;
    }

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

    public byte NavByte(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _world.Width || y >= _world.Height || _bytes.Length == 0) return NavBytes.Solid;
        return _bytes[(y * _world.Width) + x];
    }

    /// <summary>The cost function's six globals for one mover (navigation-mesh-and-search.md §4):
    /// the owner, the two definition flags that unlock water and lava, and the three door bits of
    /// the state's option word (<see cref="MoveOptions"/>: 7 respect doors, 1 pass own locked
    /// doors, 2 enter a portal's hole).</summary>
    public static MoverProfile ProfileOf(CreatureDef definition, byte owner, int options) =>
        new(definition.Flags.HasFlag(CreatureFlags.CanWalkOnWater), definition.Flags.HasFlag(CreatureFlags.CanWalkOnLava),
            owner,
            RespectDoors: (options & 0x80) != 0,
            PassOwnLockedDoors: (options & 0x02) != 0,
            EntersPortalHole: (options & 0x04) != 0);

    /// <summary>The body's position from the creature's, as the original does before every mover
    /// update (<c>0x004D59A0</c>): the entity's 1/4096 units shifted up four.</summary>
    public static void SyncBody(CreatureState creature) =>
        creature.Body.Pos = new Fixed16Vector(creature.PosX << 4, creature.PosY << 4);

    /// <summary>The state machine's route request (<c>0x004D0F60</c> → <c>0x004D59E0</c>): aim the
    /// body at the centre of <paramref name="to"/> with the <see cref="MoveOptions"/> word the
    /// original's state pushes, and load the first window. A creature already on that tile gets
    /// a finished route and is treated as arrived. False when the mesh has no route. The word's
    /// door bits are inert here: doors are not in the navigation byte yet.</summary>
    public bool TryRoute(CreatureState creature, TilePos to, int options)
    {
        SyncBody(creature);
        creature.RouteOptions = options;
        byte flags = MoveOptions.PathFlagsOf(options);
        var target = new Fixed16Vector(CreatureState.CentreOf(to.X) << 4, CreatureState.CentreOf(to.Y) << 4);
        if (creature.Tile == to)
        {
            RouteWindow.Load(creature.Body, [], target, flags);
            return true;
        }

        CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
        return RouteWindow.Request(creature.Body, this, ProfileOf(definition, creature.OwnerPlayerId, options), target, flags);
    }

    public bool Route(Fixed16Vector from, Fixed16Vector to, MoverProfile mover, List<Fixed16Vector> points)
    {
        Refresh();
        if (!_router!.TryRoute(new NavPoint(from.X >> 8, from.Y >> 8), new NavPoint(to.X >> 8, to.Y >> 8), mover, _waypoints))
            return false;
        foreach (NavPoint p in _waypoints) points.Add(new Fixed16Vector(p.X << 8, p.Y << 8));
        return true;
    }

    /// <summary>The heart's obstacle object, <c>Dungeon Heart Obstacle</c> (object 100: flags
    /// <c>Obstacle</c> and <c>SolidObstacle</c>, width 3.0 tiles), standing on the heart's centre
    /// tile with a radius of half its width — which is exactly the 3×3 platform the owner sees
    /// creatures keep off (Q88, Q352) while the navigation cache reads floor there. That the room
    /// spawns one such object at its centre, and that the probe's size is half the width, are
    /// HYPOTHESES (Q355); furniture and other objects are not yet obstacles.</summary>
    public void CollectObstacles(Fixed16Vector centre, int range, List<SteeringObstacle> into)
    {
        if (_structures is null || _structures.HeartCentres.Count == 0) return;
        if (!_database.Objects.TryGetValue(DungeonHeartObstacleObjectId, out ObjectDef? obstacle)) return;

        int size = (obstacle.Width.Raw << 4) >> 1;
        foreach (int index in _structures.HeartCentres)
        {
            var pos = new Fixed16Vector(
                CreatureState.CentreOf(index % _world.Width) << 4, CreatureState.CentreOf(index / _world.Width) << 4);
            if ((pos - centre).Octile() > range + size) continue;
            into.Add(new SteeringObstacle(pos, size, obstacle.Flags.HasFlag(ObjectFlags.SolidObstacle), false));
        }
    }

    /// <summary>OBJECTS record 100, the invisible object a heart keeps creatures off with.</summary>
    public const byte DungeonHeartObstacleObjectId = 100;

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

    /// <summary>Nothing remembers a victim yet (combat is unbuilt, Q8), so the second term of
    /// <c>Separation</c> never applies.</summary>
    public SteeringBody? RelatedBody(SteeringMover mover, out int reachTiles)
    {
        reachTiles = 0;
        return null;
    }
}
