// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using Xunit;

namespace DK2.Presentation.Tests;

public sealed class BlendedTerrainStreamTests
{
    private static readonly TestMaterial Water = new("water"), Fog = new("fog"), Glass = new("glass"),
        Opaque = new("rock", transparent: false), Dig = new("dig", tagOverlay: true), Red = new("red", tagOverlay: true);

    private static readonly TestMaterial[] Palette = [Water, Opaque, Fog, Dig, Glass, Red];

    /// <summary>Runs the same operations through the legacy transcription and the stream, comparing every upload.</summary>
    private sealed class Pair
    {
        public readonly LegacyTransparentTerrain Legacy = new();
        public readonly BlendedTerrainStream<TestMaterial> Stream = new(material => material.TagOverlay);
        // Unity's TerrainChunks kept one dictionary whose lists it cleared, so its order is first use across chunks.
        private readonly Dictionary<TestMaterial, List<int>> _byMaterial = new();
        private readonly List<int> _orders = new(), _legacyOrders = new();

        /// <summary>The stream's localToWorld and its inverse, in Unity's layout; effects are placed with the inverse
        /// when added, as Unity's adapter does, and the legacy copy collects with it.</summary>
        public Matrix4x4 Matrix { get; private set; } = Matrix4x4.Identity;
        public Matrix4x4 WorldToLocal { get; private set; } = Matrix4x4.Identity;

        public void MoveStream(Matrix4x4 matrix)
        {
            Matrix = matrix;
            Assert.True(Matrix4x4.Invert(matrix, out Matrix4x4 inverse));
            WorldToLocal = inverse;
        }

        public void SetChunk(int chunk, List<Vector3> positions, IEnumerable<(TestMaterial Material, int[] Indices)> groups)
        {
            foreach (List<int> list in _byMaterial.Values) list.Clear();
            foreach ((TestMaterial material, int[] indices) in groups)
            {
                if (!_byMaterial.TryGetValue(material, out List<int>? list)) _byMaterial[material] = list = new List<int>();
                list.AddRange(indices);
            }
            Legacy.SetChunk(chunk, positions, _byMaterial);
            Stream.BeginChunk(chunk, positions.Select(p => new BlendedVertex(p, Vector3.UnitY, default, default, VertexColour.White)).ToList());
            foreach (KeyValuePair<TestMaterial, List<int>> entry in _byMaterial)
                if (entry.Key.Transparent) Stream.AddChunkTriangles(entry.Key, entry.Value);
            Stream.EndChunk();
        }

        public void Effects(params (Vector3[] Positions, int[] Indices, TestMaterial Material, Matrix4x4 Transform)[] effects)
        {
            Legacy.BeginEffectFrame();
            Stream.BeginEffectFrame();
            foreach ((Vector3[] positions, int[] indices, TestMaterial material, Matrix4x4 transform) in effects)
            {
                Legacy.AddEffectMesh(positions, indices, material, transform);
                Matrix4x4 placed = LegacyTransparentTerrain.UnityMultiply(WorldToLocal, transform);
                BlendedVertex[] local = positions.Select(p => new BlendedVertex(LegacyTransparentTerrain.MultiplyPoint3x4(placed, p),
                    Vector3.UnitY, default, default, VertexColour.White)).ToArray();
                Stream.AddEffect(material, local, indices);
            }
            Legacy.EndEffectFrame();
            Stream.EndEffectFrame();
        }

        public void Clear()
        {
            Legacy.Clear();
            Stream.Clear();
        }

        public void Refresh(Camera camera)
        {
            Legacy.Refresh(camera.Forward, Matrix, WorldToLocal, camera.Frustum.ViewProjection, camera.Planes);
            Assert.Equal(Legacy.Uploaded != null, Stream.NeedsRefresh(camera.Forward, Matrix, camera.Frustum.ViewProjection));
            bool rebuilt = Stream.Refresh(camera.Forward, Matrix, camera.Frustum);
            Compare(rebuilt);
        }

        public void PlaceItems(Camera camera, List<Vector3> items)
        {
            Legacy.PlaceItems(camera.Forward, Matrix, WorldToLocal, camera.Frustum.ViewProjection, camera.Planes, items, _legacyOrders);
            bool rebuilt = Stream.PlaceItems(camera.Forward, Matrix, camera.Frustum, items, _orders);
            Compare(rebuilt);
            Assert.Equal(_legacyOrders, _orders);
        }

        private void Compare(bool rebuilt)
        {
            LegacyTransparentTerrain.Upload? expected = Legacy.Uploaded;
            Assert.Equal(expected != null, rebuilt);
            if (expected == null) return;
            Assert.Equal(expected.GeometryChanged, Stream.GeometryChanged);
            Assert.Equal(expected.Positions, Stream.Vertices.Select(v => v.Position).ToArray());
            Assert.Equal(expected.Runs.Count, Stream.Runs.Count);
            for (int i = 0; i < expected.Runs.Count; i++)
            {
                Assert.Same(expected.Runs[i].Material, Stream.Material(Stream.Runs[i].MaterialIndex));
                Assert.Equal(expected.Runs[i].SortingOrder, Stream.SortingOrder(i));
                Assert.Equal(expected.Runs[i].Triangles, Stream.Runs[i].Triangles.ToArray());
            }
        }
    }

    private sealed record Camera(Vector3 Forward, ViewFrustum Frustum, Vector3 Position)
    {
        public Plane[] Planes => Enumerable.Range(0, 6).Select(i => Frustum[i]).ToArray();

        public static Camera At(Vector3 focus, float yawDegrees, float pitchDegrees = 50f, float distance = 9f, float shift = .2f)
        {
            float yaw = yawDegrees * MathF.PI / 180f, pitch = pitchDegrees * MathF.PI / 180f;
            var forward = Vector3.Normalize(new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)));
            var pose = new ClassicCameraPose(focus - forward * distance, forward, Vector3.UnitY, 60f, 40f, pitchDegrees, yawDegrees, shift);
            return new Camera(forward, ViewFrustum.FromPose(pose, 16f / 9f, .3f), pose.Position);
        }
    }

    private static (List<Vector3> Positions, List<(TestMaterial, int[])> Groups) RandomChunk(Random random, int chunk)
    {
        var positions = new List<Vector3>();
        int count = random.Next(3, 40);
        float ox = chunk % 4 * 8f, oz = chunk / 4 * 8f;
        for (int i = 0; i < count; i++)
            positions.Add(new Vector3(ox + (float)random.NextDouble() * 8f, (float)random.NextDouble() * 2f, oz + (float)random.NextDouble() * 8f));
        var groups = new List<(TestMaterial, int[])>();
        foreach (TestMaterial material in Palette)
        {
            if (random.Next(3) == 0) continue;
            int triangles = random.Next(0, 12);
            var indices = new int[triangles * 3];
            for (int i = 0; i < indices.Length; i++) indices[i] = random.Next(count);
            groups.Add((material, indices));
        }
        // Unity's dictionary order is not the palette's: shuffle which material a chunk adds first.
        return (positions, groups.OrderBy(_ => random.Next()).ToList());
    }

    /// <summary>A tilted, moved stream transform (Unity's layout), so every row of the world transform matters.</summary>
    private static readonly Matrix4x4 Tilted =
        Matrix4x4.Transpose(Matrix4x4.CreateRotationX(.2f) * Matrix4x4.CreateRotationZ(-.1f) * Matrix4x4.CreateTranslation(1.5f, .25f, -2f));

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    public void Matches_the_legacy_unity_stream_run_for_run(int seed, bool transformed)
    {
        var random = new Random(seed);
        var pair = new Pair();
        if (transformed) pair.MoveStream(Tilted);
        var focus = new Vector3(16f, 0f, 8f);
        for (int chunk = 0; chunk < 8; chunk++)
        {
            (List<Vector3> positions, List<(TestMaterial, int[])> groups) = RandomChunk(random, chunk);
            pair.SetChunk(chunk, positions, groups);
        }
        Camera camera = Camera.At(focus, 0f);
        pair.Refresh(camera);
        pair.Refresh(camera);
        for (int step = 0; step < 30; step++)
        {
            switch (random.Next(8))
            {
                case 6:
                    BandChunk(pair, camera, random.Next(6));
                    break;
                case 0:
                    camera = Camera.At(focus + new Vector3((float)random.NextDouble() * 4f, 0, (float)random.NextDouble() * 4f),
                        random.Next(0, 360), 35f + random.Next(30));
                    break;
                case 1:
                {
                    int chunk = random.Next(10);
                    (List<Vector3> positions, List<(TestMaterial, int[])> groups) = RandomChunk(random, chunk);
                    pair.SetChunk(chunk, positions, groups);
                    break;
                }
                case 2:
                    // A chunk whose blended faces are gone, or one that never had any.
                    pair.SetChunk(random.Next(12), [Vector3.Zero, Vector3.One, Vector3.UnitX], [(Opaque, [0, 1, 2])]);
                    break;
                case 3:
                {
                    Vector3[] quad = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)];
                    int[] indices = [0, 1, 2, 0, 2, 3];
                    Matrix4x4 at = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(focus + new Vector3(random.Next(-4, 5), 1, random.Next(-4, 5))));
                    if (random.Next(2) == 0) pair.Effects((quad, indices, Glass, at), (quad, indices, Water, Matrix4x4.Identity));
                    else pair.Effects((quad, indices, Glass, at));
                    break;
                }
                case 4:
                    pair.PlaceItems(camera, Enumerable.Range(0, random.Next(0, 4))
                        .Select(_ => focus + new Vector3(random.Next(-6, 7), 1.5f, random.Next(-6, 7))).ToList());
                    break;
                case 5:
                    pair.Effects();
                    break;
                default:
                    break;
            }
            pair.Refresh(camera);
        }
        pair.Clear();
        pair.Refresh(camera);
    }

    /// <summary>A chunk of two triangles just outside one plane of <paramref name="camera"/>'s frustum, all three corners
    /// of one at 5e-5 (inside the cull margin, kept) and of the other at 5e-4 (beyond it, culled).</summary>
    private static void BandChunk(Pair pair, Camera camera, int planeIndex)
    {
        Plane plane = camera.Frustum[planeIndex];
        Vector3 n = plane.Normal;
        Vector3 inside = camera.Position + camera.Forward * 6f;
        Vector3 onPlane = inside - n * (Vector3.Dot(n, inside) + plane.D);
        Vector3 t = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Y) < .9f ? Vector3.UnitY : Vector3.UnitX)), u = Vector3.Cross(n, t);
        var positions = new List<Vector3>();
        foreach (float distance in new[] { 5e-5f, 5e-4f })
            foreach (Vector3 offset in new[] { Vector3.Zero, t * .3f, u * .3f })
                positions.Add(LegacyTransparentTerrain.MultiplyPoint3x4(pair.WorldToLocal, onPlane + offset - n * distance));
        pair.SetChunk(20, positions, [(Fog, [0, 1, 2, 3, 4, 5])]);
    }

    [Fact]
    public void A_moved_stream_transform_recomputes_world_depths_as_unity_did()
    {
        var pair = new Pair();
        var random = new Random(11);
        for (int chunk = 0; chunk < 6; chunk++)
        {
            (List<Vector3> positions, List<(TestMaterial, int[])> groups) = RandomChunk(random, chunk);
            pair.SetChunk(chunk, positions, groups);
        }
        Camera camera = Camera.At(new Vector3(12, 0, 6), 30f);
        pair.Refresh(camera);
        pair.MoveStream(Matrix4x4.Transpose(Matrix4x4.CreateTranslation(1.5f, 0, -2f) * Matrix4x4.CreateRotationY(.3f)));
        pair.Refresh(camera);
        pair.Refresh(camera);
        pair.MoveStream(Tilted);
        pair.Refresh(camera);
        pair.MoveStream(Matrix4x4.Identity);
        pair.Refresh(camera);
    }

    [Fact]
    public void Effects_under_a_moved_stream_transform_are_collected_again_as_unity_did()
    {
        var pair = new Pair();
        var random = new Random(3);
        (List<Vector3> positions, List<(TestMaterial, int[])> groups) = RandomChunk(random, 1);
        pair.SetChunk(1, positions, groups);
        Vector3[] quad = [new(8, 1, 0), new(9, 1, 0), new(9, 2, 0)];
        pair.Effects((quad, [0, 1, 2], Glass, Matrix4x4.Identity));
        Camera camera = Camera.At(new Vector3(10, 0, 4), 0f);
        pair.Refresh(camera);
        pair.MoveStream(Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, 0, 1f)));
        // Unity's adapter places the effect again with the moved transform's inverse before the render.
        pair.Effects((quad, [0, 1, 2], Glass, Matrix4x4.Identity));
        pair.Refresh(camera);
        Assert.True(pair.Stream.GeometryChanged);
    }

    private static BlendedTerrainStream<TestMaterial> OneTriangle(out ClassicCameraPose pose)
    {
        var stream = new BlendedTerrainStream<TestMaterial>(_ => false);
        stream.BeginChunk(0, [new BlendedVertex(new Vector3(0, 0, 5), Vector3.UnitY, default, default, VertexColour.White),
            new BlendedVertex(new Vector3(1, 0, 5), Vector3.UnitY, default, default, VertexColour.White),
            new BlendedVertex(new Vector3(1, 1, 5), Vector3.UnitY, default, default, VertexColour.White)]);
        stream.AddChunkTriangles(Water, [0, 1, 2]);
        stream.EndChunk();
        pose = new ClassicCameraPose(new Vector3(.5f, .5f, 0f), Vector3.UnitZ, Vector3.UnitY, 60f, 50f, 0f, 0f);
        stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(pose, 1f, .3f));
        return stream;
    }

    [Fact]
    public void A_new_forward_alone_or_a_new_view_alone_sorts_again()
    {
        BlendedTerrainStream<TestMaterial> stream = OneTriangle(out ClassicCameraPose pose);
        ViewFrustum frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        Assert.False(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        // Unity's Vector3 ==: a change under 1e-5 in each axis is the same forward, one above is not.
        Assert.False(stream.Refresh(new Vector3(5e-6f, 0, 1f), Matrix4x4.Identity, frustum));
        // A squared change of 9e-10 is a new forward: ten times the epsilon, within a looser one.
        Assert.True(stream.Refresh(new Vector3(5e-6f + 3e-5f, 0, 1f), Matrix4x4.Identity, frustum));
        Assert.True(stream.Refresh(Vector3.Normalize(new Vector3(.01f, 0, 1f)), Matrix4x4.Identity, frustum));
        // A pan keeps the forward and moves the view.
        ViewFrustum panned = ViewFrustum.FromPose(pose with { Position = pose.Position + Vector3.UnitX }, 1f, .3f);
        Assert.True(stream.Refresh(Vector3.Normalize(new Vector3(.01f, 0, 1f)), Matrix4x4.Identity, panned));
    }

    [Fact]
    public void The_geometry_revision_counts_every_collection_the_upload_must_follow()
    {
        BlendedTerrainStream<TestMaterial> stream = OneTriangle(out ClassicCameraPose pose);
        ViewFrustum frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        long first = stream.GeometryRevision;
        Assert.True(first > 0);
        // A chunk change with the same triangles in other places.
        stream.BeginChunk(0, [new BlendedVertex(new Vector3(0, 0, 6), Vector3.UnitY, default, default, VertexColour.White),
            new BlendedVertex(new Vector3(1, 0, 6), Vector3.UnitY, default, default, VertexColour.White),
            new BlendedVertex(new Vector3(1, 1, 6), Vector3.UnitY, default, default, VertexColour.White)]);
        stream.AddChunkTriangles(Water, [0, 1, 2]);
        stream.EndChunk();
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        Assert.True(stream.GeometryChanged);
        Assert.Equal(first + 1, stream.GeometryRevision);
        BlendedVertex[] effect = [new(new Vector3(0, 0, 4), Vector3.UnitY, default, default, VertexColour.White),
            new(new Vector3(1, 0, 4), Vector3.UnitY, default, default, VertexColour.White),
            new(new Vector3(1, 1, 4), Vector3.UnitY, default, default, VertexColour.White)];
        stream.BeginEffectFrame();
        stream.AddEffect(Glass, effect, [0, 1, 2]);
        stream.EndEffectFrame();
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        Assert.Equal(first + 2, stream.GeometryRevision);
        // A repeated effect keeps the collection.
        stream.BeginEffectFrame();
        stream.RepeatEffect();
        stream.EndEffectFrame();
        Assert.False(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        Assert.Equal(first + 2, stream.GeometryRevision);
    }

    [Fact]
    public void An_effect_whose_material_alone_changes_is_collected_again()
    {
        var stream = new BlendedTerrainStream<TestMaterial>(_ => false);
        BlendedVertex[] triangle = [new(new Vector3(0, 0, 5), Vector3.UnitY, default, default, VertexColour.White),
            new(new Vector3(1, 0, 5), Vector3.UnitY, default, default, VertexColour.White),
            new(new Vector3(1, 1, 5), Vector3.UnitY, default, default, VertexColour.White)];
        var pose = new ClassicCameraPose(new Vector3(.5f, .5f, 0f), Vector3.UnitZ, Vector3.UnitY, 60f, 50f, 0f, 0f);
        ViewFrustum frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        stream.BeginEffectFrame();
        stream.AddEffect(Glass, triangle, [0, 1, 2]);
        stream.EndEffectFrame();
        stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum);
        stream.BeginEffectFrame();
        stream.AddEffect(Water, triangle, [0, 1, 2]);
        stream.EndEffectFrame();
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        Assert.Same(Water, stream.Material(Assert.Single(stream.Runs).MaterialIndex));
    }

    [Fact]
    public void Repeating_an_effect_that_was_never_added_is_refused()
    {
        var stream = new BlendedTerrainStream<TestMaterial>(_ => false);
        stream.BeginEffectFrame();
        Assert.Throws<InvalidOperationException>(stream.RepeatEffect);
    }

    [Fact]
    public void Needs_refresh_answers_what_refresh_would_do()
    {
        BlendedTerrainStream<TestMaterial> stream = OneTriangle(out ClassicCameraPose pose);
        ViewFrustum frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        Assert.False(stream.NeedsRefresh(Vector3.UnitZ, Matrix4x4.Identity, frustum.ViewProjection));
        ViewFrustum panned = ViewFrustum.FromPose(pose with { Position = pose.Position + Vector3.UnitX }, 1f, .3f);
        Assert.True(stream.NeedsRefresh(Vector3.UnitZ, Matrix4x4.Identity, panned.ViewProjection));
        Assert.True(stream.NeedsRefresh(Vector3.UnitZ, Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, 1, 0)), frustum.ViewProjection));
        Assert.True(stream.NeedsRefresh(Vector3.Normalize(new Vector3(.01f, 0, 1f)), Matrix4x4.Identity, frustum.ViewProjection));
        // An item between the camera and the triangle changes the breaks only.
        stream.SetItems(Vector3.UnitZ, [new Vector3(0, 0, 2f)]);
        Assert.True(stream.NeedsRefresh(Vector3.UnitZ, Matrix4x4.Identity, frustum.ViewProjection));
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        Assert.False(stream.NeedsRefresh(Vector3.UnitZ, Matrix4x4.Identity, frustum.ViewProjection));
        var orders = new List<int>();
        stream.ItemOrders(orders);
        Assert.Equal([1], orders);
    }

    [Fact]
    public void An_effect_whose_indices_alone_change_is_collected_again()
    {
        var stream = new BlendedTerrainStream<TestMaterial>(_ => false);
        BlendedVertex[] quad = [new(new Vector3(0, 0, 5), Vector3.UnitY, default, default, VertexColour.White),
            new(new Vector3(1, 0, 5), Vector3.UnitY, default, default, VertexColour.White),
            new(new Vector3(1, 1, 5), Vector3.UnitY, default, default, VertexColour.White)];
        var pose = new ClassicCameraPose(new Vector3(.5f, .5f, 0f), Vector3.UnitZ, Vector3.UnitY, 60f, 50f, 0f, 0f);
        ViewFrustum frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        stream.BeginEffectFrame();
        stream.AddEffect(Glass, quad, [0, 1, 2]);
        stream.EndEffectFrame();
        stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum);
        stream.BeginEffectFrame();
        stream.AddEffect(Glass, quad, [0, 2, 1]);
        stream.EndEffectFrame();
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        Assert.Equal(new RenderTriangle(0, 2, 1, 0), Assert.Single(Assert.Single(stream.Runs).Triangles));
    }

    [Fact]
    public void Tag_overlays_draw_before_everything_and_the_rest_follow_the_stream_from_one()
    {
        Assert.Equal(1, BlendedTerrainStream.SortingOrder(0, tagOverlay: false));
        Assert.Equal(8, BlendedTerrainStream.SortingOrder(7, tagOverlay: false));
        Assert.Equal(-short.MaxValue, BlendedTerrainStream.SortingOrder(0, tagOverlay: true));
        Assert.Equal(7 - short.MaxValue, BlendedTerrainStream.SortingOrder(7, tagOverlay: true));
    }

    [Fact]
    public void Runs_are_back_to_front_and_split_by_material()
    {
        var stream = new BlendedTerrainStream<TestMaterial>(material => material.TagOverlay);
        // Three quads along the view's forward (+z), far to near: water, water, fog.
        var vertices = new List<BlendedVertex>();
        foreach (float z in new[] { 9f, 6f, 3f })
            foreach (Vector3 corner in new Vector3[] { new(0, 0, z), new(1, 0, z), new(1, 1, z), new(0, 1, z) })
                vertices.Add(new BlendedVertex(corner, Vector3.UnitY, default, default, VertexColour.White));
        stream.BeginChunk(0, vertices);
        stream.AddChunkTriangles(Fog, [8, 9, 10, 8, 10, 11]);
        stream.AddChunkTriangles(Water, [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7]);
        stream.AddChunkTriangles(Dig, [4, 5, 6]);
        stream.EndChunk();
        var pose = new ClassicCameraPose(new Vector3(.5f, .5f, -2f), Vector3.UnitZ, Vector3.UnitY, 60f, 50f, 0f, 0f);
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(pose, 1f, .3f)));
        // Equal depths keep the collected order: the water at z 6 (added before the dig tag) draws first.
        Assert.Equal([Water, Dig, Fog], stream.Runs.Select(run => stream.Material(run.MaterialIndex)).ToArray());
        Assert.Equal([1, 1 - short.MaxValue, 3], Enumerable.Range(0, 3).Select(stream.SortingOrder).ToArray());
        Assert.Equal(4, stream.Runs[0].Triangles.Count);
        Assert.False(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(pose, 1f, .3f)));
    }

    [Fact]
    public void A_frustum_cull_drops_triangles_wholly_outside_one_plane()
    {
        var stream = new BlendedTerrainStream<TestMaterial>(_ => false);
        var vertices = new List<BlendedVertex>();
        // One quad in view, one behind the camera.
        foreach (float z in new[] { 5f, -5f })
            foreach (Vector3 corner in new Vector3[] { new(0, 0, z), new(1, 0, z), new(1, 1, z) })
                vertices.Add(new BlendedVertex(corner, Vector3.UnitY, default, default, VertexColour.White));
        stream.BeginChunk(3, vertices);
        stream.AddChunkTriangles(Water, [0, 1, 2, 3, 4, 5]);
        stream.EndChunk();
        var pose = new ClassicCameraPose(new Vector3(.5f, .5f, 0f), Vector3.UnitZ, Vector3.UnitY, 60f, 50f, 0f, 0f);
        stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(pose, 1f, .3f));
        RenderTriangle only = Assert.Single(Assert.Single(stream.Runs).Triangles);
        Assert.Equal(new RenderTriangle(0, 1, 2, 0), only);
    }

    [Fact]
    public void Moving_items_only_resplits_the_last_order_without_collecting()
    {
        var stream = new BlendedTerrainStream<TestMaterial>(_ => false);
        var vertices = new List<BlendedVertex>();
        foreach (float z in new[] { 9f, 3f })
            foreach (Vector3 corner in new Vector3[] { new(0, 0, z), new(1, 0, z), new(1, 1, z) })
                vertices.Add(new BlendedVertex(corner, Vector3.UnitY, default, default, VertexColour.White));
        stream.BeginChunk(0, vertices);
        stream.AddChunkTriangles(Water, [0, 1, 2, 3, 4, 5]);
        stream.EndChunk();
        var pose = new ClassicCameraPose(new Vector3(.5f, .5f, -2f), Vector3.UnitZ, Vector3.UnitY, 60f, 50f, 0f, 0f);
        ViewFrustum frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        var orders = new List<int>();
        Assert.True(stream.PlaceItems(Vector3.UnitZ, Matrix4x4.Identity, frustum, [], orders));
        Assert.Single(stream.Runs);
        long revision = stream.GeometryRevision;
        Assert.True(stream.PlaceItems(Vector3.UnitZ, Matrix4x4.Identity, frustum, [new Vector3(0, 0, 6f)], orders));
        Assert.False(stream.GeometryChanged);
        Assert.Equal(revision, stream.GeometryRevision);
        Assert.Equal(2, stream.Runs.Count);
        Assert.Equal([1], orders);
        Assert.False(stream.PlaceItems(Vector3.UnitZ, Matrix4x4.Identity, frustum, [new Vector3(0, 0, 6f)], orders));
    }

    [Fact]
    public void An_unchanged_effect_frame_keeps_the_collected_stream()
    {
        var stream = new BlendedTerrainStream<TestMaterial>(_ => false);
        BlendedVertex[] quad = [new(Vector3.Zero, Vector3.UnitY, default, default, VertexColour.White),
            new(Vector3.UnitX, Vector3.UnitY, default, default, VertexColour.White),
            new(Vector3.One, Vector3.UnitY, default, default, VertexColour.White)];
        var pose = new ClassicCameraPose(new Vector3(.5f, .5f, -2f), Vector3.UnitZ, Vector3.UnitY, 60f, 50f, 0f, 0f);
        ViewFrustum frustum = ViewFrustum.FromPose(pose, 1f, .3f);
        stream.BeginEffectFrame();
        stream.AddEffect(Glass, quad, [0, 1, 2]);
        stream.EndEffectFrame();
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        stream.BeginEffectFrame();
        stream.AddEffect(Glass, quad, [0, 1, 2]);
        stream.EndEffectFrame();
        Assert.False(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        stream.BeginEffectFrame();
        stream.EndEffectFrame();
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, frustum));
        Assert.Empty(stream.Runs);
    }

    [Fact]
    public void A_moving_camera_refreshes_without_allocating_once_warm()
    {
        var stream = new BlendedTerrainStream<TestMaterial>(material => material.TagOverlay);
        var random = new Random(5);
        for (int chunk = 0; chunk < 8; chunk++)
        {
            (List<Vector3> positions, List<(TestMaterial Material, int[] Indices)> groups) = RandomChunk(random, chunk);
            stream.BeginChunk(chunk, positions.Select(p => new BlendedVertex(p, Vector3.UnitY, default, default, VertexColour.White)).ToList());
            foreach ((TestMaterial material, int[] indices) in groups)
                if (material.Transparent) stream.AddChunkTriangles(material, indices);
            stream.EndChunk();
        }
        Camera[] cameras = Enumerable.Range(0, 16).Select(i => Camera.At(new Vector3(16, 0, 8), i * 22.5f)).ToArray();
        for (int pass = 0; pass < 3; pass++)
            foreach (Camera camera in cameras) stream.Refresh(camera.Forward, Matrix4x4.Identity, camera.Frustum);
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (Camera camera in cameras) stream.Refresh(camera.Forward, Matrix4x4.Identity, camera.Frustum);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
