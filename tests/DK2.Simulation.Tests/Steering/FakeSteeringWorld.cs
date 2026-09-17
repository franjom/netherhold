// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests.Steering;

/// <summary>A world for the steering tests: a character grid ('#' solid, '~' water, 'L' lava),
/// a list of obstacles, a fixed crowd weight, and routes from the real <see cref="MeshRouter"/>
/// unless a fixed route is given.</summary>
internal sealed class FakeSteeringWorld : ISteeringWorld, INavByteGrid
{
    private readonly string[] _rows;
    private MeshRouter? _router;

    public FakeSteeringWorld(params string[] rows)
    {
        _rows = rows;
        Height = rows.Length;
        Width = rows[0].Length;
    }

    public int Width { get; }

    public int Height { get; }

    public List<SteeringObstacle> Obstacles { get; } = new();

    public int CrowdWeightValue { get; set; }

    public int RouteRequests { get; private set; }

    public List<Fixed16Vector>? FixedRoute { get; set; }

    public static Fixed16Vector Tiles(double x, double y) => new((int)(x * 65536), (int)(y * 65536));

    public byte NavByte(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return NavBytes.Solid;
        return _rows[y][x] switch
        {
            '#' => NavBytes.Solid,
            '~' => NavBytes.Water,
            'L' => NavBytes.Lava,
            _ => NavBytes.Floor,
        };
    }

    public void CollectObstacles(Fixed16Vector centre, int range, List<SteeringObstacle> into)
    {
        foreach (SteeringObstacle o in Obstacles)
        {
            if ((o.Pos - centre).Octile() <= range + o.Size) into.Add(o);
        }
    }

    public int CrowdWeight(Fixed16Vector point, int span) => CrowdWeightValue;

    public List<SteeringNeighbour> Neighbours { get; } = new();

    public SteeringBody? Related { get; set; }

    public int RelatedReachTiles { get; set; } = 4;

    public void CollectNeighbours(Fixed16Vector centre, int range, List<SteeringNeighbour> into)
    {
        foreach (SteeringNeighbour n in Neighbours)
        {
            if ((n.Body.Pos - centre).Octile() <= range) into.Add(n);
        }
    }

    public SteeringBody? RelatedBody(SteeringMover mover, out int reachTiles)
    {
        reachTiles = RelatedReachTiles;
        return Related;
    }

    public bool Route(Fixed16Vector from, Fixed16Vector to, MoverProfile mover, List<Fixed16Vector> points)
    {
        RouteRequests++;
        if (FixedRoute != null)
        {
            points.AddRange(FixedRoute);
            return points.Count != 0;
        }

        _router ??= new MeshRouter(NavMesh.Build(this));
        var waypoints = new List<NavPoint>();
        if (!_router.TryRoute(new NavPoint(from.X >> 8, from.Y >> 8), new NavPoint(to.X >> 8, to.Y >> 8), mover, waypoints))
            return false;
        foreach (NavPoint p in waypoints) points.Add(new Fixed16Vector(p.X << 8, p.Y << 8));
        return true;
    }
}
