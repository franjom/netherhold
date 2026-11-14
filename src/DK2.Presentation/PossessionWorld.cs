// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Presentation;

/// <summary>The world as the possessed view reads it, over a <see cref="WorldState"/> the caller
/// owns for the frame (the presented copy while the simulation ticks on a worker). It reads and
/// never writes. Call <see cref="Bind"/> when the world or the possessed creature changes; the
/// navigation bytes are captured then, as the tile cache DK2's client reads is the turn's.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-client.md")]
public sealed class PossessionWorld(GameDatabase database) : IPossessionWorld
{
    private readonly List<SteeringObstacle> _obstacles = [];
    private readonly List<SteeringNeighbour> _neighbours = [];
    private WorldState? _world;
    private WorldSteering? _steering;
    private CreatureState? _creature;
    private CreatureDef? _definition;
    private byte[] _nav = [];

    public int Width => World.Width;
    public int Height => World.Height;
    private WorldState World => _world ?? throw new InvalidOperationException("No world is bound.");
    private CreatureState Creature => _creature ?? throw new InvalidOperationException("No creature is bound.");

    public void Bind(WorldState world, CreatureState creature)
    {
        if (!ReferenceEquals(world, _world)) _steering = new WorldSteering(database, world);
        _world = world;
        _creature = creature;
        _definition = database.CreatureById(creature.CreatureTypeId);
        _nav = WorldSteering.CaptureNavigationBytes(database, world);
    }

    /// <summary>The creature as the step reads it. The three speeds are <c>0x0048ABB0</c>: the
    /// definition's per-turn speed, a fifth of it for a non-worker under a fifth of its health, half on
    /// water and half again as much on lava unless it flies, then the possession stat. DK2's
    /// <c>+0x3E6</c> bit 13 and the claim bob's request <c>+0x389</c> have no counterpart here.</summary>
    public PossessedBody Body()
    {
        CreatureState me = Creature;
        CreatureDef def = _definition!;
        bool flying = WaterSpeedRules.IsFlying(def, me);
        TerrainDef terrain = database.TerrainById(World.Tile(me.PosX >> 12, me.PosY >> 12).TerrainId);
        bool weak = LowHealthSpeedRules.IsWeak(database, def, me);
        int stat = database.FirstPersonStat(CreatureStatId.Speed) + 100;
        return new PossessedBody(Speed(def.Speed), Speed(def.RunSpeed), Speed(def.ShuffleSpeed), me.Creeping, flying,
            StepLocked: false, KeysLocked: false, def.FootprintRadius.Raw & 0xFFFF, def.EyeHeight.Raw & 0xFFFF,
            def.FirstPersonWaddle, def.FirstPersonBob, def.FirstPersonWalkCycleScale,
            (def.Flags & CreatureFlags.CameraRollsWhenTurning) != 0, me.Heading);

        int Speed(Fp32 file)
        {
            uint v = (uint)SimulationTiming.LoaderRatePerTick(file.Raw);
            if (weak) v = v * 20 / 100;
            if (!flying)
            {
                if (terrain.IsWater) v = v * 50 / 100;
                else if (terrain.IsLava) v = v * 150 / 100;
            }
            return (int)((uint)stat * v / 100);
        }
    }

    /// <summary>A frozen creature (<c>+0x447</c>) takes no step and no attack.</summary>
    public bool Frozen => Creature.FrozenTicks != 0;

    /// <summary><c>0x0040F6B0</c>: not solid; when the two navigation bytes differ and the creature
    /// does not stand in a doorway, a step the cost function allows (doors respected when the
    /// creature's <c>+0x3E6</c> bit 19 is set, water and lava by the definition or by flying, no
    /// portal-hole grant); and not a Hero Gate tile, a Portal's centre or a Temple's floor slot.</summary>
    public bool CanStepTo(int tileX, int tileY)
    {
        WorldState world = World;
        if (!world.Contains(tileX, tileY)) return false;
        CreatureState me = Creature;
        CreatureDef def = _definition!;
        byte from = Nav(me.PosX >> 12, me.PosY >> 12), to = Nav(tileX, tileY);
        if (NavBytes.IsSolid(to)) return false;
        if (from != to && !NavBytes.IsDoor(from))
        {
            bool flies = WaterSpeedRules.IsFlying(def, me);
            MoverProfile mover = WorldSteering.ProfileOf(def, me.OwnerPlayerId, (MoveOptions)((int)me.RouteOptions & 0x80));
            if (flies) mover = mover with { CanWalkOnWater = true, CanWalkOnLava = true };
            if (MeshRouter.TransitionCost(to, from, mover) == 0) return false;
        }

        int index = (tileY * world.Width) + tileX;
        RoomInstance? room = RoomIndex.RoomAt(world, database, index);
        return room?.Definition.TypeId switch
        {
            RoomTypeId.HeroGateTile or RoomTypeId.HeroGate2By2 or RoomTypeId.HeroGate3By1 => false,
            RoomTypeId.Portal => !FloorHeightRules.IsPortalCentre(world, world.Tile(tileX, tileY), new TilePos(tileX, tileY)),
            RoomTypeId.Temple => !AutoFurnitureRules.IsFloorSlot(world, room, index),
            _ => true,
        };
    }

    /// <summary><c>0x004D1BC0</c>: the step swept from the start against the obstacles and creatures
    /// the steering enumerators return. The last thing touched within the step's length is kept, with
    /// no nearest test, and the wanted point is put on its edge. The obstacles DK2's callback skips
    /// (flag value 4) are a class this enumerator never returns, so none is skipped here: the Dungeon
    /// Heart's centre, whose flags are 3, stops a possessed step as it does in DK2.</summary>
    public (int X, int Y) ResolveAgainstThings(int startX, int startY, int wantedX, int wantedY)
    {
        CreatureState me = Creature;
        var start = new Fixed16Vector(startX << 4, startY << 4);
        var wanted = new Fixed16Vector(wantedX << 4, wantedY << 4);
        Fixed16Vector step = wanted - start;
        int length = step.Length();
        Fixed16Vector direction = length == 0 ? step : step.Over(length);
        int radius = me.Body.Radius, reach = length + radius;

        bool kept = false;
        Fixed16Vector centre = default;
        int keptRadius = 0;
        _obstacles.Clear();
        _steering!.CollectObstacles(start, reach, _obstacles);
        foreach (SteeringObstacle obstacle in _obstacles)
        {
            int at = TouchDistance(start, direction, obstacle.Pos, obstacle.Size + radius);
            if (at == int.MaxValue || at < -length || at > length) continue;
            (kept, centre, keptRadius) = (true, obstacle.Pos, obstacle.Size);
        }
        _neighbours.Clear();
        _steering.CollectNeighbours(start, reach, _neighbours);
        foreach (SteeringNeighbour neighbour in _neighbours)
        {
            if (ReferenceEquals(neighbour.Body, me.Body)) continue;
            int at = TouchDistance(start, direction, neighbour.Body.Pos, neighbour.Body.Radius + radius);
            if (at == int.MaxValue || at < -length || at > length) continue;
            (kept, centre, keptRadius) = (true, neighbour.Body.Pos, neighbour.Body.Radius);
        }
        if (!kept) return (wantedX, wantedY);
        Fixed16Vector edge = centre + Fixed16.Normalise(wanted - centre).Scale(keptRadius + radius);
        return (edge.X >> 4, edge.Y >> 4);
    }

    /// <summary><c>0x004D2230</c>: the smaller distance along a direction at which a point meets a
    /// circle, in 16.16, or <see cref="int.MaxValue"/> when the line misses or only grazes it. DK2
    /// computes this in doubles and so does this.</summary>
    public static int TouchDistance(Fixed16Vector point, Fixed16Vector direction, Fixed16Vector centre, int radius)
    {
        const double Scale = 1.0 / 65536.0;
        double dx = direction.X * Scale, dy = direction.Y * Scale;
        double px = (point.X * Scale) - (centre.X * Scale), py = (point.Y * Scale) - (centre.Y * Scale);
        double r = radius * Scale;
        double a = (dx * dx) + (dy * dy);
        double b = 2.0 * ((px * dx) + (py * dy));
        double c = (px * px) + (py * py) - (r * r);
        double discriminant = (b * b) - (4.0 * a * c);
        if (!(discriminant > 0.0)) return int.MaxValue;
        double q = 0.5 * (Math.Sqrt(discriminant) - b);
        int first = (int)(q / a * 65536.0), second = (int)(c / q * 65536.0);
        return Math.Min(first, second);
    }

    /// <summary>World slots <c>+0x20C</c> and <c>+0x240</c>: a solid navigation byte under the point,
    /// or a built, closed door on its tile.</summary>
    public bool Blocked(int x, int y)
    {
        int tileX = x >> 12, tileY = y >> 12;
        if (NavBytes.IsSolid(Nav(tileX, tileY))) return true;
        foreach (DoorState door in World.Doors)
            if (door.X == tileX && door.Y == tileY && !door.UnderConstruction && !door.Open) return true;
        return false;
    }

    public int FloorHeight(int x, int y) => FloorHeightRules.At(World, database, x, y);

    public bool WetAt(int x, int y) =>
        World.Contains(x >> 12, y >> 12) && database.TerrainById(World.Tile(x >> 12, y >> 12).TerrainId).IsWater;

    private byte Nav(int tileX, int tileY) =>
        World.Contains(tileX, tileY) && _nav.Length != 0 ? _nav[(tileY * World.Width) + tileX] : NavBytes.Solid;
}
