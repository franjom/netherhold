// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation.Tests;

/// <summary>The Hand's screen picker moved out of Unity's DungeonScene (migration plan step 2j), over neutral candidates:
/// the rendered stage, the resolver's refusal and the 3×3 sweep (Hand_ResolveScreenTarget 0x005992B0, 0x0044DEC0).</summary>
public sealed class WorldHoverPickerTests
{
    private static readonly TilePos Pointer = new(10, 10);
    // Straight down onto the centre of the pointer's tile, from 9 above the floor (TileHeights.Open).
    private static readonly HoverPickRay Down = new(new(10.5f, 10, -10.5f), -Vector3.UnitY, -Vector3.UnitY);
    private static readonly Vector3 Ground = new(10.5f, TileHeights.Open, -10.5f);

    private static WorldHoverHit Creature(int id, TilePos? tile = null)
    {
        return new(WorldHoverKind.Creature, tile ?? Pointer, id);
    }

    private static WorldHoverHit Thing(int id, TilePos? tile = null)
    {
        return new(WorldHoverKind.Object, tile ?? Pointer, id);
    }

    private static WorldHoverHit TileHit
    {
        get
        {
            return new(WorldHoverKind.Tile, Pointer);
        }
    }

    private static WorldHoverPicker Begin(Func<WorldHoverKind, int, bool>? rendered = null,
        Func<WorldHoverKind, int, bool>? fallback = null, Func<WorldHoverHit, bool>? resolves = null, bool objectsOnly = false)
    {
        var picker = new WorldHoverPicker();
        Assert.True(WorldHoverPicker.TryCastTerrain(Down.Origin, Down.Direction, solid: false, out float terrain, out Vector3 ground));
        picker.Begin(Down, terrain, Pointer, ground, rendered, fallback, resolves, objectsOnly);
        return picker;
    }

    /// <summary>Offers a candidate the way the adapter does; false when it was not admitted.</summary>
    private static bool Offer(WorldHoverPicker picker, WorldHoverHit candidate, float height, float radius = .5f,
        float? boundsEntry = null, float? triangle = null, float x = 10.5f)
    {
        if (!picker.Holds(new Vector3(x, height, -10.5f), radius, out HoverPickProbe probe))
        {
            return false;
        }

        if (!picker.Admits(candidate, probe, boundsEntry))
        {
            return false;
        }

        picker.Offer(candidate, probe, triangle);
        return true;
    }

    private static WorldHoverHit Rendered(WorldHoverPicker picker)
    {
        Assert.True(picker.TryRenderedResult(out WorldHoverHit hit));
        return hit;
    }

    [Fact]
    public void TheTerrainCastStopsTwoHundredthsPastTheTopPlane()
    {
        Assert.True(WorldHoverPicker.TryCastTerrain(Down.Origin, Down.Direction, solid: false, out float open, out Vector3 floor));
        Assert.Equal(10 - TileHeights.Open + .02f, open);
        Assert.Equal(new Vector3(10.5f, TileHeights.Open, -10.5f), floor);
        Assert.True(WorldHoverPicker.TryCastTerrain(Down.Origin, Down.Direction, solid: true, out float solid, out Vector3 top));
        Assert.Equal(10 - TileHeights.Solid + .02f, solid);
        Assert.Equal(TileHeights.Solid, top.Y);
        Assert.False(WorldHoverPicker.TryCastTerrain(Down.Origin, Vector3.UnitY, solid: false, out _, out _));
        Assert.False(WorldHoverPicker.TryCastTerrain(Down.Origin, Vector3.UnitX, solid: false, out _, out _));
    }

    [Fact]
    public void ThePickTakesThePointersTile()
    {
        Assert.Equal(new WorldHoverHit(WorldHoverKind.Creature, new TilePos(3, 4), 7),
            WorldHoverPicker.OnPointerTile(new WorldHoverHit(WorldHoverKind.Creature, new TilePos(2, 4), 7), new TilePos(3, 4)));
    }

    [Fact]
    public void TheRenderedStageOffersCreaturesDoorsTrapsAndHoverTargetObjects()
    {
        Assert.Equal(new WorldHoverHit(WorldHoverKind.Door, new TilePos(1, 2), 5),
            WorldHoverPicker.StructureHit(new StructureId(StructureKind.Door, 5), new TilePos(1, 2)));
        Assert.Equal(new WorldHoverHit(WorldHoverKind.Trap, new TilePos(1, 2), 6),
            WorldHoverPicker.StructureHit(new StructureId(StructureKind.Trap, 6), new TilePos(1, 2)));
        Assert.True(WorldHoverPicker.OffersObject(12, 40));
        Assert.False(WorldHoverPicker.OffersObject(0, ObjectHoverHighlight.StorePileObjectId));
        Assert.False(WorldHoverPicker.OffersObject(-1, 40));
    }

    [Fact]
    public void TheScreenCircleMustHoldThePointer()
    {
        WorldHoverPicker picker = Begin();
        Assert.True(picker.Holds(new Vector3(10.5f, 1, -10.5f), .5f, out HoverPickProbe probe));
        Assert.Equal(new HoverPickProbe(9, 9), probe);
        Assert.True(picker.Holds(new Vector3(10.9f, 1, -10.5f), .5f, out _));
        Assert.False(picker.Holds(new Vector3(11.1f, 1, -10.5f), .5f, out _));
        // A sphere reaching behind the eye holds every pointer.
        Assert.True(picker.Holds(new Vector3(30f, 9.5f, -10.5f), 1f, out _));
    }

    [Fact]
    public void ACameraLookingAwayFromTheRayHoldsNothing()
    {
        var picker = new WorldHoverPicker();
        picker.Begin(Down with { ViewForward = Vector3.UnitY }, 10, Pointer, Ground);
        Assert.False(picker.Holds(new Vector3(10.5f, 1, -10.5f), 100f, out _));
    }

    [Fact]
    public void ACandidateBehindTheTerrainIsNotOffered()
    {
        WorldHoverPicker picker = Begin();
        Assert.True(picker.Holds(new Vector3(10.5f, 1, -10.5f), .5f, out HoverPickProbe probe));
        Assert.Equal(9f + WorldHoverPicker.TerrainMargin, picker.TerrainDistance);
        Assert.True(picker.Admits(Creature(1), probe, boundsEntry: 9.01f));
        Assert.False(picker.Admits(Creature(1), probe, boundsEntry: picker.TerrainDistance));
        // Without a bounds entry, the ray distance at the centre's depth stands in.
        Assert.True(picker.Admits(Creature(1), probe, boundsEntry: null));
        Assert.True(picker.Holds(new Vector3(10.5f, .5f, -10.5f), 1f, out HoverPickProbe below));
        Assert.False(picker.Admits(Creature(1), below, boundsEntry: null));
        Assert.True(picker.Admits(Creature(1), below, boundsEntry: 8.5f));
    }

    [Fact]
    public void TheRenderedFilterRefusesACandidate()
    {
        WorldHoverPicker picker = Begin(rendered: (kind, id) => id != 2);
        Assert.False(Offer(picker, Creature(2), height: 2));
        Assert.False(picker.TryRenderedResult(out _));
        Assert.True(Offer(picker, Creature(3), height: 1));
        Assert.Equal(Creature(3), Rendered(picker));
    }

    [Fact]
    public void TheNearestTriangleWinsOverANearerCentre()
    {
        WorldHoverPicker picker = Begin();
        Offer(picker, Creature(1), height: 3);
        Offer(picker, Creature(2), height: 1, triangle: 8.5f);
        Offer(picker, Creature(3), height: 2, triangle: 8.9f);
        Assert.Equal(Creature(2), Rendered(picker));
    }

    [Fact]
    public void WithoutATriangleTheNearestCentreWins()
    {
        WorldHoverPicker picker = Begin();
        Offer(picker, Creature(1), height: 1);
        Offer(picker, Creature(2), height: 3);
        Offer(picker, Creature(3), height: 2);
        Assert.Equal(Creature(2), Rendered(picker));
    }

    [Fact]
    public void ARefusedRenderedWinnerLeavesTheTileWithoutTheSweep()
    {
        WorldHoverPicker picker = Begin(resolves: hit => hit.EntityId != 2);
        Offer(picker, Creature(1), height: 1);
        Offer(picker, Thing(2), height: 3);
        Assert.Equal(TileHit, Rendered(picker));
    }

    [Fact]
    public void AnAcceptedRenderedWinnerKeepsItsOwnTile()
    {
        WorldHoverPicker picker = Begin(resolves: _ => true);
        Offer(picker, Thing(2, new TilePos(11, 10)), height: 3);
        Assert.Equal(Thing(2, new TilePos(11, 10)), Rendered(picker));
    }

    [Fact]
    public void TheSweepCoversTheThreeByThreeTilesAroundThePointer()
    {
        WorldHoverPicker picker = Begin();
        Assert.True(picker.SweepsCreature(new TilePos(9, 11)));
        Assert.False(picker.SweepsCreature(new TilePos(12, 10)));
        Assert.True(picker.SweepsObject(5, 40, new TilePos(11, 9)));
        Assert.False(picker.SweepsObject(5, 40, new TilePos(10, 8)));
        Assert.False(picker.SweepsObject(0, 40, new TilePos(10, 10)));
        Assert.False(Begin(objectsOnly: true).SweepsCreature(Pointer));
    }

    [Fact]
    public void ACreatureWithinHalfATileWinsOverANearerObject()
    {
        WorldHoverPicker picker = Begin();
        picker.AddSweepCreature(1, Pointer, Ground + new Vector3(.45f, 0, 0));
        picker.AddSweepObject(2, new TilePos(9, 9), Ground);
        Assert.Equal(Creature(1), picker.SweepResult());
    }

    [Fact]
    public void ACreatureHalfATileAwayIsNotTaken()
    {
        WorldHoverPicker picker = Begin();
        picker.AddSweepCreature(1, Pointer, Ground + new Vector3(.5f, 0, 0));
        Assert.Equal(TileHit, picker.SweepResult());
    }

    [Fact]
    public void TheSweepTakesTilesRowByRowFromTheTopLeft()
    {
        WorldHoverPicker picker = Begin();
        picker.AddSweepCreature(1, new TilePos(9, 10), Ground);
        picker.AddSweepCreature(2, new TilePos(11, 9), Ground);
        Assert.Equal(Creature(2, new TilePos(11, 9)), picker.SweepResult());

        picker = Begin();
        picker.AddSweepCreature(3, Pointer, Ground);
        picker.AddSweepCreature(4, Pointer, Ground);
        Assert.Equal(Creature(3), picker.SweepResult());
    }

    [Fact]
    public void TheNearestObjectWithinAQuarterTileWinsAndTheFirstOfEqualsKeepsItsPlace()
    {
        WorldHoverPicker picker = Begin();
        picker.AddSweepObject(1, Pointer, Ground + new Vector3(.2f, 0, 0));
        picker.AddSweepObject(2, Pointer, Ground + new Vector3(0, 0, .1f));
        picker.AddSweepObject(3, Pointer, Ground + new Vector3(-.1f, 0, 0));
        picker.AddSweepObject(4, Pointer, Ground + new Vector3(.3f, 0, 0));
        Assert.Equal(Thing(2), picker.SweepResult());

        picker = Begin();
        picker.AddSweepObject(5, Pointer, Ground + new Vector3(.25f, 0, 0));
        Assert.Equal(TileHit, picker.SweepResult());
    }

    [Fact]
    public void ObjectsOnlyIgnoresCreaturesAndWidensTheObjectRadius()
    {
        WorldHoverPicker picker = Begin(objectsOnly: true);
        picker.AddSweepCreature(1, Pointer, Ground);
        picker.AddSweepObject(2, Pointer, Ground + new Vector3(.4f, 0, 0));
        Assert.Equal(Thing(2), picker.SweepResult());
    }

    [Fact]
    public void TheFallbackFilterRefusesSweepCandidates()
    {
        WorldHoverPicker picker = Begin(fallback: (kind, id) => id is not (1 or 3));
        picker.AddSweepCreature(1, Pointer, Ground);
        picker.AddSweepObject(3, Pointer, Ground);
        picker.AddSweepObject(4, Pointer, Ground + new Vector3(.2f, 0, 0));
        Assert.Equal(Thing(4), picker.SweepResult());
    }

    [Fact]
    public void ARefusedSweepObjectLeavesTheTileAndTheNextNearestIsNotTried()
    {
        WorldHoverPicker picker = Begin(resolves: hit => hit.EntityId != 1);
        picker.AddSweepObject(1, Pointer, Ground);
        picker.AddSweepObject(2, Pointer, Ground + new Vector3(.1f, 0, 0));
        Assert.Equal(TileHit, picker.SweepResult());

        picker = Begin(resolves: hit => false);
        picker.AddSweepCreature(3, Pointer, Ground);
        Assert.Equal(Creature(3), picker.SweepResult());
    }

    /// <summary>An oblique ray whose every axis counts: the depth is the centre's offset along the view axis, the ray
    /// distance that depth over the ray's own reach along it, and the circle is measured from the ray's point there.</summary>
    [Fact]
    public void AnObliqueRayMeasuresDepthAlongTheViewAxisAndTheCircleAtTheRaysPoint()
    {
        var picker = new WorldHoverPicker();
        // Neither vector is a unit one: the picker scales nothing, as Unity's Vector3 sums did not.
        picker.Begin(new HoverPickRay(new(1, 2, 3), new(1, 1, 1), new(1, 2, 3)), 100, Pointer, Ground);
        var centre = new Vector3(5, 7, 9);
        // (5, 7, 9) - (1, 2, 3) = (4, 5, 6), and (4, 5, 6) . (1, 2, 3) = 32; along = 6; the point 32/6 along is 16/3 on each axis from the origin.
        Assert.True(picker.Holds(centre, 100, out HoverPickProbe probe));
        Assert.Equal(32f, probe.Depth);
        Assert.Equal(32f / 6f, probe.RayDistance);
        // The offset from that point is (-4/3, -1/3, 2/3), whose squared length is 7/3.
        float offset = MathF.Sqrt(7f / 3f);
        Assert.False(picker.Holds(centre, offset - .001f, out _));
        Assert.True(picker.Holds(centre, offset + .001f, out _));
    }

    /// <summary>The nearest-centre pass compares view depths, not ray distances. Over one frame the two differ by the
    /// constant factor along the ray, so only their rounding can part them: two centres whose depths are adjacent floats
    /// can share one ray distance, and then the nearer depth must still win, whichever was offered first.</summary>
    [Fact]
    public void TheNearestCentreIsTheNearestViewDepthNotTheNearestRayDistance()
    {
        const float along = .75f;
        float near = .5f, far = 0;
        for (int bits = BitConverter.SingleToInt32Bits(.5f); ; bits++)
        {
            near = BitConverter.Int32BitsToSingle(bits);
            far = BitConverter.Int32BitsToSingle(bits + 1);
            if (near / along == far / along)
            {
                break;
            }
        }
        var picker = new WorldHoverPicker();
        // The view axis is straight down and the ray leans east, so it reaches 0.75 of a unit down per unit.
        picker.Begin(new HoverPickRay(Vector3.Zero, new(1, -along, 0), -Vector3.UnitY), 100, Pointer, Ground);
        // Radius 2 reaches behind the eye, so both circles hold the pointer.
        Assert.True(picker.Holds(new Vector3(0, -far, 0), 2, out HoverPickProbe farProbe));
        Assert.True(picker.Holds(new Vector3(0, -near, 0), 2, out HoverPickProbe nearProbe));
        Assert.True(nearProbe.Depth < farProbe.Depth);
        Assert.Equal(nearProbe.RayDistance, farProbe.RayDistance);
        picker.Offer(Creature(1), farProbe, null);
        picker.Offer(Creature(2), nearProbe, null);

        Assert.Equal(Creature(2), Rendered(picker));
    }

    [Fact]
    public void AnObliqueRayMeetsATriangleWhereItsPlaneIsCrossed()
    {
        Vector3 a = new(0, 0, 0), b = new(2, 0, 0), c = new(0, 3, 0);
        var origin = new Vector3(.5f, .5f, 5);
        // Meets z = 0 at (1, 1): inside, x/2 + y/3 < 1.
        Assert.True(WorldHoverPicker.TryHitTriangle(origin, new(.1f, .1f, -1), a, b, c, out float t));
        Assert.Equal(5f, t, 5);
        // Meets z = 0 at (1.5, 2.5): beyond the hypotenuse.
        Assert.False(WorldHoverPicker.TryHitTriangle(origin, new(.2f, .4f, -1), a, b, c, out _));
        // Meets z = 0 at (-0.5, 1): beyond the y axis edge.
        Assert.False(WorldHoverPicker.TryHitTriangle(origin, new(-.2f, .1f, -1), a, b, c, out _));
    }

    [Fact]
    public void BeginForgetsTheLastFramesCandidates()
    {
        WorldHoverPicker picker = Begin();
        Offer(picker, Creature(1), height: 1);
        picker.AddSweepCreature(2, Pointer, Ground);
        Assert.True(WorldHoverPicker.TryCastTerrain(Down.Origin, Down.Direction, false, out float terrain, out Vector3 ground));
        picker.Begin(Down, terrain, Pointer, ground);
        Assert.False(picker.TryRenderedResult(out _));
        Assert.Equal(TileHit, picker.SweepResult());
    }

    [Fact]
    public void TheTriangleTestTakesEitherWindingAheadOfTheOrigin()
    {
        Vector3 a = new(0, 0, 0), b = new(1, 0, 0), c = new(0, 0, 1);
        var origin = new Vector3(.25f, 2, .25f);
        Assert.True(WorldHoverPicker.TryHitTriangle(origin, -Vector3.UnitY, a, b, c, out float t));
        Assert.Equal(2f, t);
        Assert.True(WorldHoverPicker.TryHitTriangle(origin, -Vector3.UnitY, a, c, b, out t));
        Assert.Equal(2f, t);
        Assert.False(WorldHoverPicker.TryHitTriangle(origin, Vector3.UnitY, a, b, c, out _));
        Assert.False(WorldHoverPicker.TryHitTriangle(new Vector3(.75f, 2, .75f), -Vector3.UnitY, a, b, c, out _));
        Assert.False(WorldHoverPicker.TryHitTriangle(origin, Vector3.UnitX, a, b, c, out _));
    }
}
