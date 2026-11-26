// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The pointer ray in the adapter's world axes (x east, y up, z north negated, as <see cref="TileTopPlane"/>),
/// with the camera's forward axis, along which view depth is measured.</summary>
public readonly record struct HoverPickRay(Vector3 Origin, Vector3 Direction, Vector3 ViewForward);

/// <summary>A candidate whose screen circle holds the pointer: its centre's view depth and the ray distance at that
/// depth.</summary>
public readonly record struct HoverPickProbe(float Depth, float RayDistance);

/// <summary>What the Hand points at (Hand_ResolveScreenTarget 0x005992B0), over neutral candidates the adapter measures
/// from its meshes. First the rendered hit: every candidate whose screen circle holds the pointer
/// (<see cref="HoverPickRules.CircleHolds"/>), which the terrain does not hide and the rendered filter admits, the nearest
/// triangle under the pointer winning over the nearest centre in view depth (<see cref="RenderedPick"/>). A winner the
/// resolver refuses leaves the tile hit without the sweep. Then the 3×3 sweep (0x0044DEC0): a creature within half a tile
/// of the pointer first, else the nearest object within the fallback radius; a refused sweep object leaves the tile hit.
/// Null filters admit everything.</summary>
/// <remarks>The filters read the world, so a pick runs while no tick does: the adapters pick before the worker tick
/// starts (ADR-0458). The picker itself holds no world state. One frame's sequence: <see cref="Begin"/>, the rendered
/// candidates (<see cref="Holds"/>, <see cref="Admits"/>, <see cref="Offer"/>), <see cref="TryRenderedResult"/>, then,
/// only without a rendered result, the sweep candidates and <see cref="SweepResult"/>.</remarks>
public sealed class WorldHoverPicker
{
    /// <summary>How far past the terrain hit a candidate's entry may lie and still count as in front of it.</summary>
    public const float TerrainMargin = .02f;

    private readonly List<WorldHoverHit> _candidates = new();
    private readonly List<SweepEntry> _sweepCreatures = new(), _sweepObjects = new();
    private RenderedPick _pick;
    private HoverPickRay _ray;
    private TilePos _tile;
    private Vector3 _pointerGround;
    private Func<WorldHoverKind, int, bool>? _rendered, _fallback;
    private Func<WorldHoverHit, bool>? _resolves;
    private bool _objectsOnly;
    private ViewFrustum? _frustum;

    private readonly record struct SweepEntry(int Id, TilePos Tile, Vector3 Position);

    /// <summary>The terrain distance the triangle test stops at, set by <see cref="Begin"/>.</summary>
    public float TerrainDistance { get; private set; }

    /// <summary>The pointer ray's hit on <paramref name="tile"/>'s top plane: false (the hover is the tile) when it misses
    /// the plane; else the terrain distance candidates must lie before and the ground point the sweep measures from.</summary>
    public static bool TryCastTerrain(Vector3 origin, Vector3 direction, bool solid, out float terrainDistance,
        out Vector3 pointerGround)
    {
        terrainDistance = 0;
        if (!TileTopPlane.TryHit(origin, direction, TileTopPlane.Height(solid), out pointerGround, out float distance))
        {
            return false;
        }

        terrainDistance = distance + TerrainMargin;
        return true;
    }

    /// <summary>The hover on the pointer's tile: a pick keeps its kind and entity and takes the tile under the pointer.</summary>
    public static WorldHoverHit OnPointerTile(WorldHoverHit pick, TilePos tile) => new(pick.Kind, tile, pick.EntityId);

    /// <summary>Starts a frame's pick on <paramref name="tile"/>; <paramref name="frustum"/> is the view
    /// <see cref="TryAdmit"/> culls by, none culling nothing.</summary>
    public void Begin(in HoverPickRay ray, float terrainDistance, TilePos tile, Vector3 pointerGround,
        Func<WorldHoverKind, int, bool>? rendered = null, Func<WorldHoverKind, int, bool>? fallback = null,
        Func<WorldHoverHit, bool>? resolves = null, bool objectsOnly = false, ViewFrustum? frustum = null)
    {
        _frustum = frustum;
        _ray = ray;
        TerrainDistance = terrainDistance;
        _tile = tile;
        _pointerGround = pointerGround;
        _rendered = rendered;
        _fallback = fallback;
        _resolves = resolves;
        _objectsOnly = objectsOnly;
        _pick = default;
        _candidates.Clear();
        _sweepCreatures.Clear();
        _sweepObjects.Clear();
    }

    /// <summary>The rendered stage offers creatures and doors and traps always, and objects that are hover targets
    /// (<see cref="ObjectHoverHighlight.IsHoverTarget"/>).</summary>
    public static bool OffersObject(int objectId, byte objectTypeId) => ObjectHoverHighlight.IsHoverTarget(objectId, objectTypeId);

    /// <summary>The hit a door or trap offers.</summary>
    public static WorldHoverHit StructureHit(StructureId structure, TilePos tile) =>
        new(structure.Kind == StructureKind.Door ? WorldHoverKind.Door : WorldHoverKind.Trap, tile, structure.Value);

    /// <summary>The screen-circle test on a candidate's bounding sphere: false when the camera looks away from the ray
    /// or the circle misses the pointer.</summary>
    public bool Holds(Vector3 centre, float radius, out HoverPickProbe probe)
    {
        probe = default;
        float depth = Dot(Subtract(centre, _ray.Origin), _ray.ViewForward);
        float along = Dot(_ray.Direction, _ray.ViewForward);
        if (along <= 0f)
        {
            return false;
        }

        float rayDistance = depth / along;
        if (!HoverPickRules.CircleHolds(depth, radius, SquaredLength(Subtract(centre, PointAlong(rayDistance)))))
        {
            return false;
        }

        probe = new HoverPickProbe(depth, rayDistance);
        return true;
    }

    /// <summary>Whether a candidate that <see cref="Holds"/> lies before the terrain and the rendered filter takes it.
    /// <paramref name="boundsEntry"/> is the ray's entry into its bounds; without one, the ray distance at its centre's
    /// depth stands in.</summary>
    public bool Admits(WorldHoverHit candidate, in HoverPickProbe probe, float? boundsEntry)
    {
        float entryDistance = boundsEntry ?? probe.RayDistance;
        if (entryDistance >= TerrainDistance)
        {
            return false;
        }

        return _rendered == null || _rendered(candidate.Kind, candidate.EntityId);
    }

    /// <summary>A drawn candidate's measures before testing its triangles. First the
    /// view cull: DK2 tests the bounding sphere against the view's four side planes (0x00575D70) before the pick circle,
    /// so a sphere reaching behind the eye cannot hold every pointer (<see cref="HoverPickRules.CircleHolds"/>); ours tests
    /// the bounds box against six planes (<see cref="ViewFrustum.Excludes(in PickBox)"/>). Then the
    /// screen circle of <paramref name="sphere"/> (placed; the adapter passes one only where the model's own is
    /// <see cref="PickSphere.Usable"/>), else of the bounds' centre and half diagonal; then <see cref="Admits"/> with the
    /// ray's entry into the bounds. On true, the adapter offers it with its triangle.</summary>
    public bool TryAdmit(WorldHoverHit candidate, in PickBox bounds, PickSphere? sphere, out HoverPickProbe probe)
    {
        probe = default;
        if (_frustum is ViewFrustum frustum && frustum.Excludes(bounds))
        {
            return false;
        }

        Vector3 centre = sphere?.Centre ?? bounds.Centre;
        float radius = sphere?.Radius ?? MathF.Sqrt(SquaredLength(bounds.Extents));
        if (!Holds(centre, radius, out probe))
        {
            return false;
        }

        return Admits(candidate, probe, bounds.RayEntry(_ray.Origin, _ray.Direction));
    }

    /// <summary>Offers an admitted candidate: <paramref name="triangleDistance"/> is the ray distance to its nearest
    /// triangle before <see cref="TerrainDistance"/>, or null when the pointer is on none (or it has no mesh). The ray
    /// distance stands in for DK2's mean vertex depth.</summary>
    public void Offer(WorldHoverHit candidate, in HoverPickProbe probe, float? triangleDistance)
    {
        _pick.Offer(_candidates.Count, triangleDistance, probe.Depth);
        _candidates.Add(candidate);
    }

    /// <summary>The rendered stage's hover, false when nothing was offered: the winner, or the tile when the resolver
    /// refuses it.</summary>
    public bool TryRenderedResult(out WorldHoverHit hit)
    {
        if (!_pick.TryResult(out int picked, out _))
        {
            hit = default;
            return false;
        }
        WorldHoverHit winner = _candidates[picked];
        hit = _resolves == null || _resolves(winner) ? winner : new WorldHoverHit(WorldHoverKind.Tile, _tile);
        return true;
    }

    /// <summary>Whether the sweep looks at a creature on <paramref name="tile"/>: not with objects only, and only in the
    /// 3×3 tiles around the pointer's.</summary>
    public bool SweepsCreature(TilePos tile) => !_objectsOnly && InSweep(tile);

    /// <summary>Whether the sweep looks at an object: a hover target in the 3×3 tiles around the pointer's.</summary>
    public bool SweepsObject(int objectId, byte objectTypeId, TilePos tile) =>
        ObjectHoverHighlight.IsHoverTarget(objectId, objectTypeId) && InSweep(tile);

    /// <summary>Adds a drawn creature the sweep looks at, in the scene's order; <paramref name="position"/> is where it
    /// is drawn.</summary>
    public void AddSweepCreature(int creatureId, TilePos tile, Vector3 position) =>
        _sweepCreatures.Add(new SweepEntry(creatureId, tile, position));

    /// <inheritdoc cref="AddSweepCreature"/>
    public void AddSweepObject(int objectId, TilePos tile, Vector3 position) =>
        _sweepObjects.Add(new SweepEntry(objectId, tile, position));

    /// <summary>The sweep's hover. Tiles are taken row by row from the top left; on each, the first creature within half a
    /// tile of the pointer that the fallback admits wins outright, and the objects it admits compete by distance (the
    /// first of equals keeps its place). An object the resolver refuses leaves the tile hit; the next-nearest object is
    /// not tried (the branch at 0x00599710 inside 0x005992B0).</summary>
    public WorldHoverHit SweepResult()
    {
        var hit = new WorldHoverHit(WorldHoverKind.Tile, _tile);
        var pointer = new FpPosition3(new Fp32((int)(_pointerGround.X * Fp32.One)),
            new Fp32((int)(-_pointerGround.Z * Fp32.One)), new Fp32(0));
        int closestObject = HoverPickRules.FallbackObjectRadius(_objectsOnly);
        for (int y = _tile.Y - 1; y <= _tile.Y + 1; y++)
        {
            for (int x = _tile.X - 1; x <= _tile.X + 1; x++)
            {
                var at = new TilePos(x, y);
                foreach (SweepEntry creature in _sweepCreatures)
                {
                    if (!_objectsOnly && creature.Tile == at && Distance(pointer, creature.Position) < Fp32.One / 2
                        && (_fallback == null || _fallback(WorldHoverKind.Creature, creature.Id)))
                    {
                        return new WorldHoverHit(WorldHoverKind.Creature, creature.Tile, creature.Id);
                    }
                }

                foreach (SweepEntry thing in _sweepObjects)
                {
                    if (thing.Tile == at && (_fallback == null || _fallback(WorldHoverKind.Object, thing.Id)))
                    {
                        int distance = Distance(pointer, thing.Position);
                        if (distance < closestObject)
                        {
                            closestObject = distance;
                            hit = new WorldHoverHit(WorldHoverKind.Object, thing.Tile, thing.Id);
                        }
                    }
                }
            }
        }

        return hit.Kind == WorldHoverKind.Object && _resolves != null && !_resolves(hit)
            ? new WorldHoverHit(WorldHoverKind.Tile, _tile) : hit;
    }

    /// <summary>The ray distance to the triangle (a, b, c), either winding, or false for a miss, a ray in its plane or a
    /// hit not ahead of the origin (Möller-Trumbore). DK2 (0x00585AD0) accepts one screen winding only; which of ours that
    /// is has not been mapped through the KMF import and DK2's projection, so both are taken. A ray through a closed mesh
    /// meets its near side first either way, so this differs only where the pointer reaches a triangle's back alone.</summary>
    public static bool TryHitTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0;
        Vector3 ab = Subtract(b, a), ac = Subtract(c, a);
        Vector3 p = Cross(direction, ac);
        float det = Dot(ab, p);
        if (det > -1e-9f && det < 1e-9f)
        {
            return false;
        }

        float inv = 1f / det;
        Vector3 s = Subtract(origin, a);
        float u = Dot(s, p) * inv;
        if (u < 0f || u > 1f)
        {
            return false;
        }

        Vector3 q = Cross(s, ab);
        float v = Dot(direction, q) * inv;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }

        distance = Dot(ac, q) * inv;
        return distance > 0f;
    }

    private bool InSweep(TilePos tile) => Math.Abs(tile.X - _tile.X) <= 1 && Math.Abs(tile.Y - _tile.Y) <= 1;

    private Vector3 PointAlong(float distance) => new(_ray.Origin.X + (_ray.Direction.X * distance),
        _ray.Origin.Y + (_ray.Direction.Y * distance), _ray.Origin.Z + (_ray.Direction.Z * distance));

    private static int Distance(FpPosition3 pointer, Vector3 position) =>
        HandHoverDistance.Between(pointer, new FpPosition3(new Fp32((int)(position.X * Fp32.One)),
            new Fp32((int)(-position.Z * Fp32.One)), new Fp32(0)));

    // Unity's Vector3 arithmetic, term by term, so a pick decides as the adapter's float steps did.
    private static float Dot(Vector3 a, Vector3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
    private static float SquaredLength(Vector3 a) => (a.X * a.X) + (a.Y * a.Y) + (a.Z * a.Z);
    private static Vector3 Subtract(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static Vector3 Cross(Vector3 a, Vector3 b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
}
