// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using Xunit;

namespace DK2.Presentation.Tests;

public sealed class BlendedRunSlotsTests
{
    private static readonly TestMaterial Water = new("water"), Glass = new("glass"), Dig = new("dig", tagOverlay: true);

    private static readonly Vector3 FrontEye = new(.5f, .5f, -2f), BackEye = new(.5f, .5f, 14f);

    /// <summary>One chunk of unit quads facing the view along z, each two triangles of its material.</summary>
    private static BlendedTerrainStream<TestMaterial> Stream(params (float X, float Z, TestMaterial Material)[] quads)
    {
        var stream = new BlendedTerrainStream<TestMaterial>(material => material.TagOverlay);
        var vertices = new List<BlendedVertex>();
        var byMaterial = new Dictionary<TestMaterial, List<int>>();
        foreach ((float x, float z, TestMaterial material) in quads)
        {
            int at = vertices.Count;
            foreach (Vector3 corner in new Vector3[] { new(x, 0, z), new(x + 1, 0, z), new(x + 1, 1, z), new(x, 1, z) })
            {
                vertices.Add(new BlendedVertex(corner, Vector3.UnitY, default, default, VertexColour.White));
            }

            if (!byMaterial.TryGetValue(material, out List<int>? list))
            {
                byMaterial[material] = list = new List<int>();
            }

            list.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
        }
        stream.BeginChunk(0, vertices);
        foreach (KeyValuePair<TestMaterial, List<int>> entry in byMaterial)
        {
            stream.AddChunkTriangles(entry.Key, entry.Value);
        }

        stream.EndChunk();
        return stream;
    }

    private static Vector3 Refresh(BlendedTerrainStream<TestMaterial> stream, bool fromBehind = false)
    {
        Vector3 eye = fromBehind ? BackEye : FrontEye, forward = fromBehind ? -Vector3.UnitZ : Vector3.UnitZ;
        var pose = new ClassicCameraPose(eye, forward, Vector3.UnitY, 60f, 50f, 0f, fromBehind ? 180f : 0f);
        Assert.True(stream.Refresh(forward, Matrix4x4.Identity, ViewFrustum.FromPose(pose, 2f, .3f)));
        return eye;
    }

    private static float NearestCentroid(BlendedTerrainStream<TestMaterial> stream, int run, Vector3 eye) =>
        stream.Runs[run].Triangles.Min(t => Vector3.Distance(
            (stream.Vertices[t.A].Position + stream.Vertices[t.B].Position + stream.Vertices[t.C].Position) / 3f, eye));

    /// <summary>The slot's indices, read back through its vertices, are the run's triangles in the run's order.</summary>
    private static void AssertDraws(BlendedTerrainStream<TestMaterial> stream, BlendedRunSlots<TestMaterial> slots)
    {
        Assert.Equal(stream.Runs.Count, slots.Drawn.Count);
        for (int i = 0; i < stream.Runs.Count; i++)
        {
            BlendedRunSlot<TestMaterial> slot = slots.Drawn[i];
            Assert.Same(stream.Material(stream.Runs[i].MaterialIndex), slot.Material);
            int[] read = slot.Indices.ToArray().Select(index => slot.Vertices[index]).ToArray();
            int[] expected = stream.Runs[i].Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
            Assert.Equal(expected, read);
        }
    }

    [Fact]
    public void Each_run_is_keyed_by_its_nearest_centroid_and_the_keys_fall_down_the_stream()
    {
        // Back to front: water at z 9, glass at z 6 and 5.5, water at z 3.
        BlendedTerrainStream<TestMaterial> stream = Stream((0, 9, Water), (0, 6, Glass), (0, 5.5f, Glass), (0, 3, Water));
        var slots = new BlendedRunSlots<TestMaterial>();
        Vector3 eye = Refresh(stream);
        slots.Update(stream, eye);
        AssertDraws(stream, slots);
        Assert.Equal(3, slots.Drawn.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(NearestCentroid(stream, i, eye), slots.Drawn[i].Distance);
        }

        Assert.True(slots.Drawn[0].Distance > slots.Drawn[1].Distance && slots.Drawn[1].Distance > slots.Drawn[2].Distance);
    }

    [Fact]
    public void A_run_off_the_view_axis_keeps_the_stream_order_one_step_nearer()
    {
        // The glass is nearer along the view (z 8 against 9) but farther from the eye, being far to the side.
        BlendedTerrainStream<TestMaterial> stream = Stream((0, 9, Water), (8, 8, Glass));
        var slots = new BlendedRunSlots<TestMaterial>();
        Vector3 eye = Refresh(stream);
        slots.Update(stream, eye);
        Assert.Equal([Water, Glass], slots.Drawn.Select(slot => slot.Material).ToArray());
        Assert.True(NearestCentroid(stream, 1, eye) > NearestCentroid(stream, 0, eye));
        Assert.Equal(slots.Drawn[0].Distance - BlendedRunSlots<TestMaterial>.KeyStep, slots.Drawn[1].Distance);
    }

    [Fact]
    public void A_tag_overlay_run_keeps_its_own_distance_outside_the_chain()
    {
        // The dig tag ahead is nearer than the glass after it in the stream, which sits off to the side: were the overlay in
        // the chain, the glass would be clamped under the overlay's distance.
        BlendedTerrainStream<TestMaterial> stream = Stream((0, 9, Water), (0, 4, Dig), (5, 3.5f, Glass));
        var slots = new BlendedRunSlots<TestMaterial>();
        Vector3 eye = Refresh(stream);
        slots.Update(stream, eye);
        Assert.Equal([Water, Dig, Glass], slots.Drawn.Select(slot => slot.Material).ToArray());
        Assert.Equal([false, true, false], slots.Drawn.Select(slot => slot.TagOverlay).ToArray());
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(NearestCentroid(stream, i, eye), slots.Drawn[i].Distance);
        }

        Assert.True(slots.Drawn[2].Distance > slots.Drawn[1].Distance);
        Assert.True(slots.Drawn[2].Distance < slots.Drawn[0].Distance);
    }

    [Fact]
    public void A_reversed_camera_reorders_indices_into_the_vertices_the_slot_holds()
    {
        BlendedTerrainStream<TestMaterial> stream = Stream((0, 9, Water), (0, 6, Water), (0, 3, Glass));
        var slots = new BlendedRunSlots<TestMaterial>();
        slots.Update(stream, Refresh(stream));
        BlendedRunSlot<TestMaterial> water = slots.Drawn[0];
        Assert.Equal(0, water.FirstNewVertex);
        Assert.Equal(8, water.VertexCount);
        Assert.True(water.IndicesChanged);

        slots.Update(stream, Refresh(stream, fromBehind: true));
        AssertDraws(stream, slots);
        Assert.Equal([Glass, Water], slots.Drawn.Select(slot => slot.Material).ToArray());
        Assert.Same(water, slots.Drawn[1]);
        Assert.Equal(water.VertexCount, water.FirstNewVertex);
        Assert.Equal(8, water.VertexCount);
        Assert.True(water.IndicesChanged);

        slots.Update(stream, Refresh(stream));
        Assert.Same(water, slots.Drawn[0]);
        Assert.Equal(water.VertexCount, water.FirstNewVertex);
    }

    [Fact]
    public void Runs_vanishing_at_the_far_end_leave_the_others_in_the_slots_that_hold_their_vertices()
    {
        // Water and glass alternating from z 3 to 12, as the Portal's crystals and chasm do.
        (float, float, TestMaterial)[] quads = Enumerable.Range(0, 10).Select(i => (0f, 12f - i, i % 2 == 0 ? Water : Glass)).ToArray();
        BlendedTerrainStream<TestMaterial> stream = Stream(quads);
        var slots = new BlendedRunSlots<TestMaterial>();
        var deep = new ClassicCameraPose(FrontEye, Vector3.UnitZ, Vector3.UnitY, 60f, 50f, 0f, 0f);
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(deep, 2f, .3f)));
        slots.Update(stream, FrontEye);
        Assert.Equal(10, slots.Drawn.Count);

        // A nearer far plane drops the two farthest quads: every remaining run moves up two places.
        ClassicCameraPose shallow = deep with { FarClipDistance = 12.5f };
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(shallow, 2f, .3f)));
        slots.Update(stream, FrontEye);
        AssertDraws(stream, slots);
        Assert.Equal(8, slots.Drawn.Count);
        Assert.All(slots.Drawn, slot =>
        {
            Assert.Equal(slot.VertexCount, slot.FirstNewVertex);
            Assert.False(slot.IndicesChanged);
        });
    }

    [Fact]
    public void A_run_appearing_at_the_far_end_leaves_every_other_run_in_its_slot()
    {
        (float, float, TestMaterial)[] quads = Enumerable.Range(0, 10).Select(i => (0f, 12f - i, i % 2 == 0 ? Water : Glass)).ToArray();
        BlendedTerrainStream<TestMaterial> stream = Stream(quads);
        var slots = new BlendedRunSlots<TestMaterial>();
        var shallow = new ClassicCameraPose(FrontEye, Vector3.UnitZ, Vector3.UnitY, 60f, 12.5f, 0f, 0f);
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(shallow, 2f, .3f)));
        slots.Update(stream, FrontEye);
        Assert.Equal(8, slots.Drawn.Count);
        var before = new Dictionary<int, int>();
        foreach (BlendedRunSlot<TestMaterial> slot in slots.Drawn)
        {
            before[slot.Vertices[slot.Indices[0]]] = slot.Id;
        }

        // A deeper far plane brings the two farthest quads in ahead of every run of both materials.
        ClassicCameraPose deep = shallow with { FarClipDistance = 50f };
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(deep, 2f, .3f)));
        slots.Update(stream, FrontEye);
        AssertDraws(stream, slots);
        Assert.Equal(10, slots.Drawn.Count);
        int kept = 0;
        foreach (BlendedRunSlot<TestMaterial> slot in slots.Drawn)
        {
            if (before.TryGetValue(slot.Vertices[slot.Indices[0]], out int id))
            {
                Assert.Equal(id, slot.Id);
                Assert.Equal(slot.VertexCount, slot.FirstNewVertex);
                kept++;
            }
        }

        Assert.Equal(8, kept);
    }

    [Fact]
    public void A_first_corner_shared_by_two_materials_never_hands_a_run_the_other_material_s_slot()
    {
        // One tilted quad, its two triangles of different materials sharing corner 0; seen from either side, the run that
        // starts at corner 0 changes material.
        var stream = new BlendedTerrainStream<TestMaterial>(material => material.TagOverlay);
        BlendedVertex V(float x, float y, float z) => new(new Vector3(x, y, z), Vector3.UnitY, default, default, VertexColour.White);
        stream.BeginChunk(0, [V(0, 0, 4), V(1, 0, 4), V(1, 1, 8), V(0, 1, 8)]);
        stream.AddChunkTriangles(Water, [0, 1, 2]);
        stream.AddChunkTriangles(Glass, [0, 2, 3]);
        stream.EndChunk();
        var slots = new BlendedRunSlots<TestMaterial>();
        for (int pass = 0; pass < 4; pass++)
        {
            slots.Update(stream, Refresh(stream, fromBehind: pass % 2 == 1));
            AssertDraws(stream, slots);
        }
        Assert.Equal(2, slots.Slots.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_renderer_applying_only_what_each_update_reports_always_draws_every_run(int seed)
    {
        // A fake renderer per slot: it keeps the vertex data it was handed, takes [FirstNewVertex, VertexCount) and the
        // indices only when they changed, and must then draw each run's triangles exactly, through collections, hidden
        // and shown slots, restarts and slots taken by other runs.
        var random = new Random(seed);
        TestMaterial[] palette = [Water, Glass, new("fog")];
        var stream = new BlendedTerrainStream<TestMaterial>(material => material.TagOverlay);
        void Build(int chunk, float shift)
        {
            var vertices = new List<BlendedVertex>();
            Dictionary<TestMaterial, List<int>> byMaterial = palette.ToDictionary(material => material, _ => new List<int>());
            for (int q = 0; q < 40; q++)
            {
                float x = (chunk * 10) + random.Next(10) + shift, z = random.Next(4, 30);
                int at = vertices.Count;
                foreach (Vector3 corner in new Vector3[] { new(x, 0, z), new(x + 1, 0, z), new(x + 1, 1, z + .5f), new(x, 1, z + .5f) })
                {
                    vertices.Add(new BlendedVertex(corner, Vector3.UnitY, new Vector2(q, chunk), default, VertexColour.White));
                }

                byMaterial[palette[random.Next(palette.Length)]].AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            }
            stream.BeginChunk(chunk, vertices);
            foreach (KeyValuePair<TestMaterial, List<int>> entry in byMaterial)
            {
                stream.AddChunkTriangles(entry.Key, entry.Value);
            }

            stream.EndChunk();
        }
        Build(0, 0);
        Build(1, 0);
        var slots = new BlendedRunSlots<TestMaterial>();
        var held = new Dictionary<int, List<BlendedVertex>>();
        var indices = new Dictionary<int, int[]>();
        var lastFirst = new Dictionary<int, int>();
        int collections = 0, restarts = 0, hidden = 0, taken = 0;
        long revision = -1;
        for (int step = 0; step < 300; step++)
        {
            if (step % 37 == 36)
            {
                Build(random.Next(2), (float)random.NextDouble());
                collections++;
            }
            float yaw = (float)(random.NextDouble() * 360), distance = 4f + (float)(random.NextDouble() * 20);
            Vector3 focus = new(random.Next(20), 0, random.Next(4, 30));
            float r = yaw * MathF.PI / 180f;
            var forward = Vector3.Normalize(new Vector3(MathF.Sin(r) * .7f, -.7f, MathF.Cos(r) * .7f));
            var pose = new ClassicCameraPose(focus - forward * distance, forward, Vector3.UnitY, 60f, 20f + (float)(random.NextDouble() * 40),
                45f, yaw);
            if (!stream.Refresh(forward, Matrix4x4.Identity, ViewFrustum.FromPose(pose, 16f / 9f, .3f)))
            {
                continue;
            }

            bool forgot = stream.GeometryRevision != revision;
            revision = stream.GeometryRevision;
            slots.Update(stream, pose.Position);
            hidden += slots.Slots.Count(slot => !slot.Drawn);
            ReadOnlySpan<BlendedVertex> vertices = slots.Vertices;
            for (int i = 0; i < slots.Drawn.Count; i++)
            {
                BlendedRunSlot<TestMaterial> slot = slots.Drawn[i];
                if (!held.TryGetValue(slot.Id, out List<BlendedVertex>? buffer))
                {
                    held[slot.Id] = buffer = new List<BlendedVertex>();
                }

                if (slot.FirstNewVertex == 0 && buffer.Count > 0 && !forgot)
                {
                    restarts++;
                }

                int first = stream.Runs[i].Triangles[0].A;
                if (lastFirst.TryGetValue(slot.Id, out int previous) && previous != first)
                {
                    taken++;
                }

                lastFirst[slot.Id] = first;
                // The renderer's buffer keeps what it was given before; only the new range is written.
                if (buffer.Count > slot.FirstNewVertex)
                {
                    buffer.RemoveRange(slot.FirstNewVertex, buffer.Count - slot.FirstNewVertex);
                }

                Assert.Equal(slot.FirstNewVertex, buffer.Count);
                for (int v = slot.FirstNewVertex; v < slot.VertexCount; v++)
                {
                    buffer.Add(vertices[slot.Vertices[v]]);
                }

                if (slot.IndicesChanged || !indices.ContainsKey(slot.Id))
                {
                    indices[slot.Id] = slot.Indices.ToArray();
                }

                BlendedVertex[] drawn = indices[slot.Id].Select(index => buffer[index]).ToArray();
                BlendedVertex[] expected = stream.Runs[i].Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Select(v => stream.Vertices[v]).ToArray();
                Assert.Equal(expected, drawn);
            }
        }
        Assert.True(collections > 0 && restarts > 0 && hidden > 0 && taken > 0,
            $"collections {collections}, restarts {restarts}, hidden {hidden}, taken {taken}");
    }

    [Fact]
    public void An_unchanged_run_reports_no_new_vertices_and_no_new_indices()
    {
        BlendedTerrainStream<TestMaterial> stream = Stream((0, 9, Water), (0, 3, Glass));
        var slots = new BlendedRunSlots<TestMaterial>();
        Vector3 eye = Refresh(stream);
        slots.Update(stream, eye);
        slots.Update(stream, eye);
        Assert.All(slots.Drawn, slot =>
        {
            Assert.Equal(slot.VertexCount, slot.FirstNewVertex);
            Assert.False(slot.IndicesChanged);
        });
    }

    [Fact]
    public void A_material_takes_its_own_slots_in_run_order_and_unused_slots_are_not_drawn()
    {
        BlendedTerrainStream<TestMaterial> stream = Stream((0, 9, Water), (0, 6, Glass), (0, 3, Water));
        var slots = new BlendedRunSlots<TestMaterial>();
        slots.Update(stream, Refresh(stream));
        Assert.Equal([0, 1, 2], slots.Drawn.Select(slot => slot.Id).ToArray());
        Assert.Equal(3, slots.Slots.Count);

        BlendedTerrainStream<TestMaterial> merged = Stream((0, 9, Water), (0, 6, Water), (0, 3, Glass));
        // A new stream is a new collection; the slot set carries over by material.
        slots.Update(merged, Refresh(merged));
        AssertDraws(merged, slots);
        Assert.Equal([0, 1], slots.Drawn.Select(slot => slot.Id).ToArray());
        Assert.False(slots.Slots[2].Drawn);
        Assert.Equal(0, slots.Drawn[0].FirstNewVertex);
    }

    [Fact]
    public void A_new_collection_forgets_every_slot_s_vertices()
    {
        BlendedTerrainStream<TestMaterial> stream = Stream((0, 9, Water), (0, 3, Glass));
        var slots = new BlendedRunSlots<TestMaterial>();
        Vector3 eye = Refresh(stream);
        slots.Update(stream, eye);
        stream.BeginChunk(0, [new(new(0, 0, 9), Vector3.UnitY, default, default, VertexColour.White),
            new(new(1, 0, 9), Vector3.UnitY, default, default, VertexColour.White),
            new(new(1, 1, 9), Vector3.UnitY, default, default, VertexColour.White)]);
        stream.AddChunkTriangles(Water, [0, 1, 2]);
        stream.EndChunk();
        Refresh(stream);
        slots.Update(stream, eye);
        AssertDraws(stream, slots);
        BlendedRunSlot<TestMaterial> water = Assert.Single(slots.Drawn);
        Assert.Equal(0, water.FirstNewVertex);
        Assert.Equal(3, water.VertexCount);
        Assert.True(water.IndicesChanged);
    }

    [Fact]
    public void A_slot_holding_far_more_than_its_run_starts_again_from_the_run()
    {
        // Forty water quads in a row, then the camera turned so that only the nearest stays in view.
        (float, float, TestMaterial Water)[] quads = Enumerable.Range(0, 40).Select(i => ((float)i * 2f, 9f, Water)).ToArray();
        BlendedTerrainStream<TestMaterial> stream = Stream(quads);
        var slots = new BlendedRunSlots<TestMaterial>();
        var wide = new ClassicCameraPose(new Vector3(40f, 0.5f, -60f), Vector3.UnitZ, Vector3.UnitY, 60f, 100f, 0f, 0f);
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(wide, 2f, .3f)));
        slots.Update(stream, wide.Position);
        Assert.Equal(160, slots.Drawn[0].VertexCount);

        var narrow = new ClassicCameraPose(new Vector3(.5f, .5f, 7f), Vector3.UnitZ, Vector3.UnitY, 20f, 50f, 0f, 0f);
        Assert.True(stream.Refresh(Vector3.UnitZ, Matrix4x4.Identity, ViewFrustum.FromPose(narrow, 1f, .3f)));
        slots.Update(stream, narrow.Position);
        AssertDraws(stream, slots);
        BlendedRunSlot<TestMaterial> water = Assert.Single(slots.Drawn);
        Assert.True(water.VertexCount < 40, water.VertexCount.ToString());
        Assert.Equal(0, water.FirstNewVertex);
        Assert.True(water.IndicesChanged);
    }

    [Fact]
    public void Updates_allocate_nothing_once_warm()
    {
        (float, float, TestMaterial)[] quads = Enumerable.Range(0, 30).Select(i => ((float)(i % 6), 3f + i, i % 3 == 0 ? Glass : Water)).ToArray();
        BlendedTerrainStream<TestMaterial> stream = Stream(quads);
        var slots = new BlendedRunSlots<TestMaterial>();
        for (int pass = 0; pass < 3; pass++)
        {
            slots.Update(stream, Refresh(stream));
            slots.Update(stream, Refresh(stream, fromBehind: true));
        }
        Vector3 front = Refresh(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();
        slots.Update(stream, front);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
