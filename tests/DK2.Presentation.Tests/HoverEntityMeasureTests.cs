// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Core;

namespace DK2.Presentation.Tests;

/// <summary>The Hand's entity measures moved out of Unity's DungeonScene (migration plan step 3h): the bounds and sphere a
/// drawn thing is offered with, the view cull (0x00575D70), the triangle test, the admit sequence, and the hover overlay's
/// kind and swap rules. The legacy copies repeat DungeonScene's arithmetic before the move: they pin the move, not
/// Unity's engine internals.</summary>
public sealed class HoverEntityMeasureTests
{
    private static readonly TilePos Pointer = new(10, 10);
    private static readonly HoverPickRay Down = new(new(10.5f, 10, -10.5f), -Vector3.UnitY, -Vector3.UnitY);

    // x, y and z each between -1 and 1: Gribb and Hartmann over the identity.
    private static readonly ViewFrustum UnitView = ViewFrustum.FromViewProjection(Matrix4x4.Identity);

    private static WorldHoverHit Creature(int id) => new(WorldHoverKind.Creature, Pointer, id);

    private static WorldHoverPicker Begin(ViewFrustum? frustum = null, Func<WorldHoverKind, int, bool>? rendered = null)
    {
        var picker = new WorldHoverPicker();
        Assert.True(WorldHoverPicker.TryCastTerrain(Down.Origin, Down.Direction, solid: false, out float terrain, out Vector3 ground));
        picker.Begin(Down, terrain, Pointer, ground, rendered, frustum: frustum);
        return picker;
    }

    // A box of half size .5 around the point (10.5, height, -10.5).
    private static PickBox BoxAt(float height, float half = .5f) =>
        new(new Vector3(10.5f - half, height - half, -10.5f - half), new Vector3(10.5f + half, height + half, -10.5f + half));

    [Fact]
    public void ABoxAroundPointsTakesEachAxisLowAndHigh()
    {
        PickBox box = PickBox.Around([new(1, 5, -2), new(-3, 2, 4), new(2, -1, 0)]);
        Assert.Equal(new Vector3(-3, -1, -2), box.Min);
        Assert.Equal(new Vector3(2, 5, 4), box.Max);
        Assert.Equal(new Vector3(-.5f, 2, 1), box.Centre);
        Assert.Equal(new Vector3(2.5f, 3, 3), box.Extents);
        Assert.Equal(default, PickBox.Around([]));
        Assert.Equal(new PickBox(new(1, 2, 3), new(1, 2, 3)), PickBox.Around([new(1, 2, 3)]));
    }

    [Fact]
    public void APlacedBoxIsTheBoxAroundItsPlacedCorners()
    {
        var random = new Random(7);
        for (int run = 0; run < 200; run++)
        {
            var local = new PickBox(new Vector3(Next(random), Next(random), Next(random)),
                new Vector3(Next(random) + 3, Next(random) + 3, Next(random) + 3));
            Quaternion turn = Quaternion.Normalize(new Quaternion(Next(random), Next(random), Next(random), Next(random)));
            var position = new Vector3(Next(random) * 10, Next(random), Next(random) * 10);
            var scale = new Vector3(Next(random) + 2, -(Next(random) + 2), Next(random) + 2);
            PickBox placed = local.Placed(position, turn, scale);
            PickBox corners = PickBox.Around(Corners(local, position, turn, scale));
            AssertClose(corners.Min, placed.Min);
            AssertClose(corners.Max, placed.Max);
        }
        // A quarter turn about the vertical swaps the box's east and north extents.
        PickBox turned = new PickBox(new(-1, 0, -2), new(1, 3, 2))
            .Placed(new(5, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), Vector3.One);
        Assert.Equal(new Vector3(3, 0, -1), Round(turned.Min));
        Assert.Equal(new Vector3(7, 3, 1), Round(turned.Max));
    }

    [Fact]
    public void ARayEntersABoxWhereItCrossesTheLastNearSlabAndFromInsideByOurDefinition()
    {
        var box = new PickBox(new(0, 0, 0), new(2, 2, 2));
        Assert.Equal(3f, box.RayEntry(new(1, 5, 1), -Vector3.UnitY));
        Assert.Equal(1f, box.RayEntry(new(-1, -1, 1), new(1, 1, 0)));
        Assert.Equal(2f, box.RayEntry(new(-2, -1, 1), new(1, 1, 0)));
        // From inside: negative, our definition; a pick cannot tell, as Admits compares with a positive terrain distance.
        Assert.Equal(-1f, box.RayEntry(new(1, 1, 1), Vector3.UnitX));
        Assert.Null(box.RayEntry(new(1, 5, 1), Vector3.UnitY));
        Assert.Null(box.RayEntry(new(3, 5, 1), -Vector3.UnitY));
        Assert.Null(box.RayEntry(new(5, 1, 1), new(0, 1, 0)));
        // Within each slab at some time, never within both at once.
        Assert.Null(box.RayEntry(new(-1, 6, 1), new(1, -1, 0)));
        Assert.Equal(3f, box.RayEntry(new(-1, 5, 1), new(1, -1, 0)));
        Assert.Equal(3f, box.RayEntry(new(2, 5, 1), -Vector3.UnitY));
        Assert.Equal(3f, box.RayEntry(new(0, 5, 0), -Vector3.UnitY));
    }

    [Fact]
    public void AModelsSphereIsItsFirstFrameOrItsMesh()
    {
        var mesh = new KmfMesh("mesh", new Vector3(1, 2, 3), 1.5f, []);
        var first = new KmfMesh("first", new Vector3(4, 5, 6), 2.5f, []);
        var second = new KmfMesh("second", new Vector3(7, 8, 9), 3.5f, []);
        Assert.Equal(new PickSphere(new Vector3(1, -3, -2), 1.5f), PickSphere.Of(new KmfModel(1, KmfKind.Mesh, [], mesh)));
        Assert.Equal(new PickSphere(new Vector3(4, -6, -5), 2.5f),
            PickSphere.Of(new KmfModel(1, KmfKind.Animation, [], mesh, new KmfAnimation(2, [first, second]))));
        // An incomplete clip leaves the mesh, and with no mesh there is none.
        Assert.Equal(new PickSphere(new Vector3(1, -3, -2), 1.5f),
            PickSphere.Of(new KmfModel(1, KmfKind.Animation, [], mesh, new KmfAnimation(3, [first, second]))));
        Assert.Null(PickSphere.Of(new KmfModel(1, KmfKind.Animation, [], null, new KmfAnimation(3, [first]))));
        Assert.Null(PickSphere.Of(new KmfModel(1, KmfKind.Mesh, [], null)));
        Assert.Throws<ArgumentNullException>(() => PickSphere.Of(null!));
    }

    [Fact]
    public void OnlyASphereWithRadiusIsUsed()
    {
        Assert.True(new PickSphere(default, .001f).Usable);
        Assert.False(new PickSphere(default, 0f).Usable);
        Assert.False(new PickSphere(default, -1f).Usable);
    }

    [Fact]
    public void APlacedSphereMatchesTheMovedTransformPointAndRadius()
    {
        var random = new Random(11);
        for (int run = 0; run < 200; run++)
        {
            var sphere = new PickSphere(new Vector3(Next(random), Next(random), Next(random)), Next(random) + 1);
            Quaternion turn = Quaternion.Normalize(new Quaternion(Next(random), Next(random), Next(random), Next(random)));
            var position = new Vector3(Next(random) * 10, Next(random), Next(random) * 10);
            var scale = new Vector3(Next(random) * 3, Next(random) * 3, Next(random) * 3);
            PickSphere placed = sphere.Placed(position, turn, scale);
            AssertClose(LegacyTransformPoint(sphere.Centre, position, turn, scale), placed.Centre);
            Assert.Equal(LegacyRadius(sphere.Radius, scale), placed.Radius);
            Assert.Equal(LegacyRadius(sphere.Radius, scale), PickSphere.ScaledRadius(sphere.Radius, scale));
        }
        Assert.Equal(6f, PickSphere.ScaledRadius(2, new Vector3(1, -3, 2)));
        Assert.Equal(4f, PickSphere.ScaledRadius(2, new Vector3(2, 1, -1)));
        Assert.Equal(8f, PickSphere.ScaledRadius(2, new Vector3(1, 2, -4)));
    }

    [Fact]
    public void TheNearestTriangleMatchesTheMovedLoop()
    {
        var random = new Random(3);
        for (int run = 0; run < 300; run++)
        {
            var positions = new Vector3[12];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector3(Next(random), Next(random), Next(random));
            int[] triangles = Enumerable.Range(0, 3 * 8 + random.Next(3)).Select(_ => random.Next(positions.Length)).ToArray();
            var origin = new Vector3(Next(random) * 3, 4, Next(random) * 3);
            Vector3 direction = Vector3.Normalize(new Vector3(Next(random), -1, Next(random)));
            float limit = 3 + Next(random) * 4;
            float best = limit;
            bool found = PickTriangles.TryNearest(positions, triangles, origin, direction, ref best);
            float? legacy = LegacyNearestTriangle(positions, triangles, origin, direction, limit);
            Assert.Equal(legacy.HasValue, found);
            Assert.Equal(legacy ?? limit, best);
        }
    }

    [Fact]
    public void ATriangleCountsOnlyNearerThanTheBestSoFar()
    {
        Vector3[] positions = [new(-1, 0, -1), new(1, 0, -1), new(0, 0, 1), new(-1, 2, -1), new(1, 2, -1), new(0, 2, 1)];
        var origin = new Vector3(0, 5, 0);
        float best = 4f;
        Assert.False(PickTriangles.TryNearest(positions, [0, 1, 2], origin, -Vector3.UnitY, ref best));
        Assert.Equal(4f, best);
        Assert.True(PickTriangles.TryNearest(positions, [0, 1, 2, 3, 4, 5], origin, -Vector3.UnitY, ref best));
        Assert.Equal(3f, best);
        best = 3f;
        Assert.False(PickTriangles.TryNearest(positions, [3, 4, 5], origin, -Vector3.UnitY, ref best));
        best = 10f;
        Assert.True(PickTriangles.TryNearest(positions, [0, 1, 2, 3, 4, 5, 0, 1], origin, -Vector3.UnitY, ref best));
        Assert.Equal(3f, best);
        Assert.Throws<ArgumentNullException>(() => PickTriangles.TryNearest(null!, [0], origin, origin, ref best));
        Assert.Throws<ArgumentNullException>(() => PickTriangles.TryNearest(positions, null!, origin, origin, ref best));
    }

    [Fact]
    public void ARayInAModelsSpaceKeepsItsDistances()
    {
        Vector3[] local = [new(-1, 0, -1), new(1, 0, -1), new(0, 0, 1)];
        Quaternion turn = Quaternion.CreateFromYawPitchRoll(.7f, .3f, -.4f);
        var position = new Vector3(5, 1, -3);
        var scale = new Vector3(2, 1.5f, .5f);
        Vector3[] world = local.Select(p => LegacyTransformPoint(p, position, turn, scale)).ToArray();
        Vector3 centre = (world[0] + world[1] + world[2]) / 3;
        Vector3 origin = centre + new Vector3(.3f, 4, -.2f), direction = Vector3.Normalize(centre - origin);
        Assert.True(WorldHoverPicker.TryHitTriangle(origin, direction, world[0], world[1], world[2], out float expected));
        Vector3 localOrigin = origin, localDirection = direction;
        PickTriangles.ToLocal(position, turn, scale, ref localOrigin, ref localDirection);
        float best = float.MaxValue;
        Assert.True(PickTriangles.TryNearest(local, [0, 1, 2], localOrigin, localDirection, ref best));
        Assert.Equal(expected, best, 4);
    }

    [Fact]
    public void ABoxIsCulledOnlyWhollyOutsideOnePlane()
    {
        Assert.False(UnitView.Excludes(new PickBox(new(-.5f, -.5f, -.5f), new(.5f, .5f, .5f))));
        Assert.False(UnitView.Excludes(new PickBox(new(.9f, -.5f, -.5f), new(3, .5f, .5f))));
        Assert.False(UnitView.Excludes(new PickBox(new(1, 0, 0), new(2, 0, 0))));
        Assert.True(UnitView.Excludes(new PickBox(new(1.01f, 0, 0), new(2, 0, 0))));
        Assert.True(UnitView.Excludes(new PickBox(new(-3, 0, 0), new(-1.01f, 0, 0))));
        Assert.True(UnitView.Excludes(new PickBox(new(0, 1.5f, 0), new(0, 2, 0))));
        Assert.True(UnitView.Excludes(new PickBox(new(0, -2, 0), new(0, -1.5f, 0))));
        Assert.True(UnitView.Excludes(new PickBox(new(0, 0, 1.5f), new(0, 0, 2))));
        Assert.True(UnitView.Excludes(new PickBox(new(0, 0, -2), new(0, 0, -1.5f))));
        // Outside two planes at a corner, but on neither wholly: kept. A box just touching a plane is kept too, our
        // choice for the tie.
        Assert.False(UnitView.Excludes(new PickBox(new(.5f, .5f, 0), new(3, 3, 0))));
    }

    [Fact]
    public void ABoxIsCulledWhenAllEightCornersLieOutsideOnePlane()
    {
        // System.Numerics multiplies row vectors; the frustum takes Unity's column layout.
        ViewFrustum view = ViewFrustum.FromViewProjection(Matrix4x4.Transpose(
            Matrix4x4.CreateLookAt(new(0, 8, 8), Vector3.Zero, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1f, 1.5f, .3f, 50f)));
        var random = new Random(5);
        int culled = 0;
        for (int run = 0; run < 2000; run++)
        {
            var low = new Vector3(Next(random) * 40, Next(random) * 40, Next(random) * 40);
            var box = new PickBox(low, low + new Vector3(random.Next(1, 6), random.Next(1, 6), random.Next(1, 6)));
            bool expected = false;
            for (int plane = 0; plane < 6 && !expected; plane++)
                expected = Corners(box, Vector3.Zero, Quaternion.Identity, Vector3.One).All(c => Plane.DotCoordinate(view[plane], c) < 0f);
            Assert.Equal(expected, view.Excludes(box));
            if (expected) culled++;
        }
        Assert.InRange(culled, 200, 1900);
    }

    [Fact]
    public void ACandidateOutsideTheViewIsNotAdmitted()
    {
        // The unit cube moved to the pointer's column at height 2.
        var view = ViewFrustum.FromViewProjection(Matrix4x4.Transpose(Matrix4x4.CreateTranslation(-10.5f, -2f, 10.5f)));
        WorldHoverPicker picker = Begin(view);
        Assert.True(picker.TryAdmit(Creature(1), BoxAt(2), null, out _));
        Assert.False(picker.TryAdmit(Creature(2), BoxAt(5), null, out _));
        Assert.True(Begin().TryAdmit(Creature(2), BoxAt(5), null, out _));
    }

    [Fact]
    public void AGivenSphereIsMeasuredElseTheBoundsCentreAndHalfDiagonal()
    {
        // A box beside the pointer whose half diagonal reaches it (1.73 over 1.3), and a small sphere at its centre that
        // does not; the ray misses the box, so the centre's ray distance stands in for its entry.
        var beside = new PickBox(new(10.8f, 1, -11.5f), new(12.8f, 3, -9.5f));
        WorldHoverPicker picker = Begin();
        Assert.False(picker.TryAdmit(Creature(1), beside, new PickSphere(beside.Centre, .5f), out _));
        Assert.True(picker.TryAdmit(Creature(1), beside, null, out HoverPickProbe none));
        Assert.Equal(8f, none.Depth);
        // The half diagonal itself, not its square: .866 holds a pointer .8 from the centre, .75 would not.
        Assert.True(picker.TryAdmit(Creature(1), new PickBox(new(10.8f, 1.5f, -11f), new(11.8f, 2.5f, -10f)), null, out _));
        Assert.False(picker.TryAdmit(Creature(1), new PickBox(new(10.9f, 1.5f, -11f), new(11.9f, 2.5f, -10f)), null, out _));
        // A sphere given is measured even at no radius (a model drawn at zero scale), as DungeonScene measured it.
        Assert.False(picker.TryAdmit(Creature(1), beside, new PickSphere(beside.Centre, 0f), out _));
        // The sphere's own centre is measured, not the box's.
        Assert.True(picker.TryAdmit(Creature(1), beside, new PickSphere(new(10.5f, 3, -10.5f), .2f), out HoverPickProbe own));
        Assert.Equal(7f, own.Depth);
        Assert.Equal(7f, own.RayDistance);
    }

    [Fact]
    public void TheBoundsEntryMustLieBeforeTheTerrain()
    {
        WorldHoverPicker picker = Begin();
        // The floor sits at TileHeights.Open; the terrain cast stops .02 past it.
        float floor = TileHeights.Open;
        Assert.True(picker.TryAdmit(Creature(1), BoxAt(floor + .5f), null, out _));
        Assert.True(picker.TryAdmit(Creature(1), new PickBox(new(10, floor - 2, -11), new(11, floor + .03f, -10)), null, out _));
        Assert.False(picker.TryAdmit(Creature(1), new PickBox(new(10, floor - 2, -11), new(11, floor - .03f, -10)), null, out _));
        // A box the ray misses falls back on the centre's ray distance.
        Assert.False(picker.TryAdmit(Creature(1), new PickBox(new(10.6f, floor - 3, -10.4f), new(11, floor - 1, -10)),
            new PickSphere(new(10.5f, floor - 2, -10.5f), 3f), out _));
        Assert.True(picker.TryAdmit(Creature(1), new PickBox(new(10.6f, floor + 1, -10.4f), new(11, floor + 2, -10)),
            new PickSphere(new(10.5f, floor + 1.5f, -10.5f), 3f), out _));
    }

    [Fact]
    public void TheRenderedFilterDecidesLast()
    {
        WorldHoverPicker picker = Begin(rendered: (kind, id) => id != 2);
        Assert.True(picker.TryAdmit(Creature(1), BoxAt(3), null, out _));
        Assert.False(picker.TryAdmit(Creature(2), BoxAt(3), null, out _));
    }

    [Fact]
    public void TheOverlayDrawsOverCreaturesObjectsDoorsAndTraps()
    {
        foreach (WorldHoverKind kind in Enum.GetValues<WorldHoverKind>())
            Assert.Equal(kind is WorldHoverKind.Creature or WorldHoverKind.Object or WorldHoverKind.Door or WorldHoverKind.Trap,
                HoverOverlayPlan.Overlays(kind));
    }

    [Fact]
    public void OnlyAnObjectWithTheHoverTextureSwaps()
    {
        var plan = new HoverOverlayPlan();
        Assert.True(plan.TryBegin(new WorldHoverHit(WorldHoverKind.Object, Pointer, 4), false, out HoverOverlayDraw thing));
        Assert.True(HoverOverlayPlan.Swaps(thing, hoverTexture: true));
        Assert.False(HoverOverlayPlan.Swaps(thing, hoverTexture: false));
        Assert.True(plan.TryBegin(new WorldHoverHit(WorldHoverKind.Creature, Pointer, 4), false, out HoverOverlayDraw creature));
        Assert.False(HoverOverlayPlan.Swaps(creature, hoverTexture: true));
        Assert.True(plan.TryBegin(new WorldHoverHit(WorldHoverKind.Door, Pointer, 4), true, out HoverOverlayDraw door));
        Assert.False(HoverOverlayPlan.Swaps(door, hoverTexture: true));
    }

    [Fact]
    public void TheRedPassIsRedTexturesAdditiveColour()
    {
        ObjectMaterialLook red = ObjectMaterialLook.HoverOverlay();
        Assert.Equal(KmfBlend.Additive, red.Blend);
        Assert.False(red.WritesDepth);
        Assert.True(red.FullLight);
        // RedTexture's colour, the bytes 255, 64, 65 over 255, untextured and adding no light.
        Assert.Equal(new Vector4(1f, 64f / 255f, 65f / 255f, 1f), red.Colour);
        Assert.Equal("", red.Texture);
        Assert.Equal(Vector3.Zero, red.AddedLight);
        Assert.False(red.EnvironmentMapped);
        Assert.Equal(0, red.QueueOffset);
    }

    private static float Next(Random random) => (float)(random.NextDouble() * 2 - 1);

    private static Vector3 Round(Vector3 v) => new(MathF.Round(v.X, 4), MathF.Round(v.Y, 4), MathF.Round(v.Z, 4));

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    private static Vector3[] Corners(PickBox box, Vector3 position, Quaternion turn, Vector3 scale)
    {
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
            corners[i] = LegacyTransformPoint(new Vector3((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y,
                (i & 4) == 0 ? box.Min.Z : box.Max.Z), position, turn, scale);
        return corners;
    }

    // Legacy: position + rotation * (scale * local), Transform.TransformPoint's formula for a root object as the move
    // reads it; it pins the move, not Unity's own floats.
    private static Vector3 LegacyTransformPoint(Vector3 local, Vector3 position, Quaternion turn, Vector3 scale) =>
        position + Vector3.Transform(new Vector3(local.X * scale.X, local.Y * scale.Y, local.Z * scale.Z), turn);

    // Legacy: DungeonScene's radius before the move, sphere.w * Mathf.Max(|x|, Mathf.Max(|y|, |z|)); it pins the move.
    private static float LegacyRadius(float radius, Vector3 scale) =>
        radius * Math.Max(Math.Abs(scale.X), Math.Max(Math.Abs(scale.Y), Math.Abs(scale.Z)));

    // Legacy: DungeonScene.NearestTriangle's loop before the move, over one submesh; it pins the move.
    private static float? LegacyNearestTriangle(Vector3[] vertices, int[] triangles, Vector3 origin, Vector3 direction, float limit)
    {
        float best = limit;
        bool found = false;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
            if (WorldHoverPicker.TryHitTriangle(origin, direction, vertices[triangles[i]], vertices[triangles[i + 1]],
                    vertices[triangles[i + 2]], out float t) && t < best)
            { best = t; found = true; }
        return found ? best : null;
    }
}
