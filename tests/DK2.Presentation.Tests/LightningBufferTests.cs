// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
namespace DK2.Presentation.Tests;
public sealed class LightningBufferTests
{
    [Theory]
    [InlineData(LightningAmplitude.Shot)]
    [InlineData(LightningAmplitude.Creature)]
    public void ReusedBoltsAndRibbonsMatchTheOriginalAlgorithmBitForBit(LightningAmplitude amplitude)
    {
        for (uint seed = 0; seed < 80; seed++)
        {
            var original = new LightningBolts(seed);
            var reused = new LightningBolts(seed);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            foreach ((Vector3 start, Vector3 end) in Cases())
            {
                IReadOnlyList<LightningPath> expected = original.Build(start, end, amplitude);
                IReadOnlyList<LightningPathBuffer> actual = reused.BuildInto(start, end, amplitude);
                Assert.Equal(expected.Count, actual.Count);
                for (int p = 0; p < expected.Count; p++)
                {
                    Assert.Equal(expected[p].SubdivisionNodeCount, actual[p].SubdivisionNodeCount);
                    EqualBits(expected[p].SubdivisionMaximumSegmentLength, actual[p].SubdivisionMaximumSegmentLength);
                    Assert.Equal(expected[p].Nodes.Count, actual[p].Nodes.Count);
                    for (int n = 0; n < expected[p].Nodes.Count; n++)
                    {
                        EqualBits(expected[p].Nodes[n], actual[p].Nodes[n]);
                    }

                    var ribbon = LightningRibbon.Build(expected[p].Nodes);
                    int firstVertex = vertices.Count, firstIndex = triangles.Count;
                    LightningRibbon.Append(actual[p].Nodes, vertices, uvs, triangles);
                    Assert.Equal(firstVertex + ribbon.Vertices.Count, vertices.Count);
                    for (int v = 0; v < ribbon.Vertices.Count; v++)
                    {
                        EqualBits(ribbon.Vertices[v], vertices[firstVertex + v]);
                        EqualBits(ribbon.Uvs[v].X, uvs[firstVertex + v].X);
                        EqualBits(ribbon.Uvs[v].Y, uvs[firstVertex + v].Y);
                    }
                    Assert.Equal(firstIndex + ribbon.Triangles.Count, triangles.Count);
                    for (int t = 0; t < ribbon.Triangles.Count; t++)
                    {
                        Assert.Equal(firstVertex + ribbon.Triangles[t], triangles[firstIndex + t]);
                    }
                }
            }
        }
    }
    [Fact]
    public void ReusedCracklePairsKeepFilteringRandomCallsAndOrder()
    {
        LightningControlPoint[] controls = [new(3000, Vector3.One), new(1000, Vector3.Zero),
            new(1001, Vector3.UnitX), new(1010, Vector3.UnitY), new(1011, Vector3.UnitZ),
            new(0, new(4, 5, 6)), new(1999, new(3, 2, 1)), new(2000, -Vector3.One)];
        for (uint seed = 0; seed < 100; seed++)
        {
            var original = new LightningCracklePairs(new Dk2Lcg(seed));
            var reused = new LightningCracklePairs(new Dk2Lcg(seed));
            var actual = new List<LightningSegment>();
            for (int i = 0; i < 5; i++)
            {
                LightningSegment[] expected = original.Choose(controls).ToArray();
                reused.ChooseInto(controls, actual);
                Assert.Equal(expected, actual);
            }
            reused.ChooseInto([], actual);
            Assert.Empty(actual);
        }
    }
    [Fact]
    public void ReusedGeometryAndCrackleAllocateNothingAfterWarming()
    {
        var bolts = new LightningBolts(77);
        var pairs = new LightningCracklePairs(new Dk2Lcg(5));
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var segments = new List<LightningSegment>();
        LightningControlPoint[] controls = Enumerable.Range(0, 25)
            .Select(i => new LightningControlPoint((uint)(i * 10), new(i, i % 3, 0))).ToArray();
        void Frame()
        {
            vertices.Clear(); uvs.Clear(); triangles.Clear();
            IReadOnlyList<LightningPathBuffer> paths = bolts.BuildInto(new(1, 3, -4), new(2, -1, 4));
            for (int i = 0; i < paths.Count; i++)
            {
                LightningRibbon.Append(paths[i].Nodes, vertices, uvs, triangles);
            }

            pairs.ChooseInto(controls, segments);
        }
        for (int i = 0; i < 4096; i++)
        {
            Frame();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            Frame();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }
    private static IEnumerable<(Vector3, Vector3)> Cases()
    {
        yield return (Vector3.Zero, Vector3.Zero);
        yield return (Vector3.Zero, new(.05f, 0, 0));
        yield return (new(1, 2, 3), new(1, 2, 8));
        yield return (new(-2, 4, -.2f), new(3, -1, 5));
        yield return (new(.1f, .2f, .1f), new(-.4f, -.5f, -3));
        yield return (new(1, 1, -5), new(2, 2, -4));
    }
    private static void EqualBits(Vector3 expected, Vector3 actual)
    {
        EqualBits(expected.X, actual.X); EqualBits(expected.Y, actual.Y); EqualBits(expected.Z, actual.Z);
    }
    private static void EqualBits(float expected, float actual) =>
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));
}
