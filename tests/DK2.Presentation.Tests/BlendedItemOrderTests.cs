// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using Xunit;

namespace DK2.Presentation.Tests;

public sealed class BlendedItemOrderTests
{
    private const int Dust = 0, Spark = 1, Flower = 2, Ring = 3;

    private static BlendedItemOrder Order(float[] runs, params (int Batch, float Distance, BlendedItemBlend Blend)[] items)
    {
        var order = new BlendedItemOrder();
        foreach (float run in runs)
        {
            order.AddRun(run);
        }

        foreach ((int batch, float distance, BlendedItemBlend blend) in items)
        {
            order.Add(batch, distance, blend);
        }

        order.Order();
        return order;
    }

    private static int[] ItemsOf(BlendedItemOrder order, int segment)
    {
        BlendedItemSegment s = order.Segments[segment];
        return order.Items.Slice(s.Start, s.Count).ToArray();
    }

    private const BlendedItemBlend Add = BlendedItemBlend.Additive, Mix = BlendedItemBlend.Ordered;

    [Fact]
    public void OneBatchWithoutRunsIsOneDrawBackToFrontKeyedByItsFarthestItem()
    {
        BlendedItemOrder order = Order([], (Dust, 5f, Mix), (Dust, 9f, Mix), (Dust, 7f, Mix));
        BlendedItemSegment segment = Assert.Single(order.Segments);
        Assert.Equal(new BlendedItemSegment(Dust, 0, 3, 9f), segment);
        Assert.Equal([1, 2, 0], ItemsOf(order, 0));
    }

    [Fact]
    public void ARunBetweenTwoItemsSplitsTheirBatchAndEachDrawKeepsItsSide()
    {
        // The run at 6: the item at 9 draws before it, the one at 5 after it.
        BlendedItemOrder order = Order([6f], (Dust, 5f, Add), (Dust, 9f, Add));
        Assert.Equal(2, order.Segments.Count);
        Assert.Equal([1], ItemsOf(order, 0));
        Assert.Equal([0], ItemsOf(order, 1));
        Assert.True(order.Segments[0].Distance > 6f);
        Assert.True(order.Segments[1].Distance < 6f);
        Assert.Equal(9f, order.Segments[0].Distance);
        Assert.Equal(5f, order.Segments[1].Distance);
    }

    [Fact]
    public void AnItemAtARunsKeyDrawsAfterIt()
    {
        BlendedItemOrder order = Order([6f], (Dust, 6f, Mix));
        float key = Assert.Single(order.Segments).Distance;
        Assert.Equal(6f - BlendedItemOrder.KeyGap, key);
    }

    [Fact]
    public void InterleavedAdditiveBatchesTakeOneDrawEachWhereverTheyLie()
    {
        // Additive draws leave the same pixels in either order, so even covering the whole screen they pass each other.
        BlendedItemOrder order = Order([], (Dust, 9f, Add), (Spark, 8f, Add), (Dust, 7f, Add), (Spark, 6f, Add));
        Assert.Equal(2, order.Segments.Count);
        Assert.Equal(new BlendedItemSegment(Dust, 0, 2, 9f), order.Segments[0]);
        Assert.Equal(new BlendedItemSegment(Spark, 2, 2, 8f), order.Segments[1]);
        Assert.Equal([0, 2], ItemsOf(order, 0));
        Assert.Equal([1, 3], ItemsOf(order, 1));
    }

    private static readonly BlendedItemRect Left = new BlendedItemRect(0f, 0f, 1f, 1f), Right = new BlendedItemRect(5f, 0f, 6f, 1f),
        Middle = new BlendedItemRect(0.5f, 0f, 5.5f, 1f);

    private static BlendedItemOrder Boxed(float[] runs, params (int Batch, float Distance, BlendedItemBlend Blend, BlendedItemRect Rect)[] items)
    {
        var order = new BlendedItemOrder();
        foreach (float run in runs)
        {
            order.AddRun(run);
        }

        foreach ((int batch, float distance, BlendedItemBlend blend, BlendedItemRect rect) in items)
        {
            order.Add(batch, distance, blend, rect);
        }

        order.Order();
        return order;
    }

    [Fact]
    public void OrderedBatchesThatDoNotOverlapShareTheirDraws()
    {
        BlendedItemOrder order = Boxed([], (Flower, 9f, Mix, Left), (Ring, 8.5f, Mix, Right), (Flower, 8f, Mix, Left),
            (Ring, 7.5f, Mix, Right));
        Assert.Equal(2, order.Segments.Count);
        Assert.Equal([0, 2], ItemsOf(order, 0));
        Assert.Equal([1, 3], ItemsOf(order, 1));
        Assert.True(order.Segments[0].Distance > order.Segments[1].Distance);
    }

    [Fact]
    public void AnItemStopsAtADrawItOverlapsAndStartsItsOwn()
    {
        // The second flower overlaps the ring drawn between them, so it must draw after the ring.
        BlendedItemOrder order = Boxed([], (Flower, 9f, Mix, Left), (Ring, 8.5f, Mix, Middle), (Flower, 8f, Mix, Right));
        Assert.Equal(3, order.Segments.Count);
        Assert.Equal([Flower, Ring, Flower], [order.Segments[0].Batch, order.Segments[1].Batch, order.Segments[2].Batch]);
    }

    [Fact]
    public void AnAdditiveItemPassesOrderedDrawsItMissesButNotOnesItOverlaps()
    {
        BlendedItemOrder clear = Boxed([], (Dust, 9f, Add, Left), (Flower, 8f, Mix, Right), (Dust, 7f, Add, Left));
        Assert.Equal(2, clear.Segments.Count);
        Assert.Equal([0, 2], ItemsOf(clear, 0));
        BlendedItemOrder covered = Boxed([], (Dust, 9f, Add, Left), (Flower, 8f, Mix, Middle), (Dust, 7f, Add, Left));
        Assert.Equal(3, covered.Segments.Count);
    }

    [Fact]
    public void AnItemNeverPassesARunEvenWhereNothingOverlaps()
    {
        BlendedItemOrder order = Boxed([8.5f], (Dust, 9f, Add, Left), (Dust, 8f, Add, Left));
        Assert.Equal(2, order.Segments.Count);
    }

    [Fact]
    public void AnItemLooksBackOnlySoFar()
    {
        var items = new List<(int, float, BlendedItemBlend, BlendedItemRect)> { (Dust, 100f, Mix, Left) };
        for (int i = 0; i < BlendedItemOrder.LookBack; i++)
        {
            items.Add((10 + i, 99f - i, Mix, Right));
        }

        items.Add((Dust, 1f, Mix, Left));
        BlendedItemOrder order = Boxed([], [.. items]);
        Assert.Equal(BlendedItemOrder.LookBack + 2, order.Segments.Count);
        items.RemoveAt(1);
        Assert.Equal(BlendedItemOrder.LookBack, Boxed([], [.. items]).Segments.Count);
    }

    [Fact]
    public void InterleavedOrderedBatchesTakeADrawPerRunOfABatchWithFallingKeys()
    {
        BlendedItemOrder order = Order([], (Flower, 9f, Mix), (Ring, 8.99f, Mix), (Flower, 8.98f, Mix), (Flower, 5f, Mix));
        Assert.Equal(3, order.Segments.Count);
        Assert.Equal([Flower, Ring, Flower], [order.Segments[0].Batch, order.Segments[1].Batch, order.Segments[2].Batch]);
        Assert.Equal([0], ItemsOf(order, 0));
        Assert.Equal([1], ItemsOf(order, 1));
        Assert.Equal([2, 3], ItemsOf(order, 2));
        Assert.Equal(9f, order.Segments[0].Distance);
        Assert.Equal(8.99f, order.Segments[1].Distance);
        Assert.Equal(8.98f, order.Segments[2].Distance);
    }

    [Fact]
    public void AnOrderedItemSplitsTheAdditiveGroupAroundIt()
    {
        BlendedItemOrder order = Order([], (Dust, 9f, Add), (Flower, 8f, Mix), (Dust, 7f, Add));
        Assert.Equal(3, order.Segments.Count);
        Assert.Equal([Dust, Flower, Dust], [order.Segments[0].Batch, order.Segments[1].Batch, order.Segments[2].Batch]);
        Assert.True(order.Segments[0].Distance > order.Segments[1].Distance);
        Assert.True(order.Segments[1].Distance > order.Segments[2].Distance);
    }

    [Fact]
    public void GroupsAtOneDistanceAreHeldAKeyGapApart()
    {
        BlendedItemOrder order = Order([], (Flower, 4f, Mix), (Ring, 4f, Mix), (Flower, 4f, Mix));
        Assert.Equal(3, order.Segments.Count);
        Assert.Equal(4f, order.Segments[0].Distance);
        Assert.Equal(4f - BlendedItemOrder.KeyGap, order.Segments[1].Distance);
        Assert.Equal(4f - 2 * BlendedItemOrder.KeyGap, order.Segments[2].Distance);
        // Equal distances keep the order they were added in.
        Assert.Equal([0], ItemsOf(order, 0));
        Assert.Equal([1], ItemsOf(order, 1));
        Assert.Equal([2], ItemsOf(order, 2));
    }

    [Fact]
    public void AGapTooNarrowForItsGroupsSpreadsThemEvenlyBetweenItsRuns()
    {
        float upper = 10f, lower = 10f - BlendedRunSlots<object>.KeyStep;
        float mid = (upper + lower) / 2;
        BlendedItemOrder order = Order([upper, lower], (Flower, mid, Mix), (Ring, mid, Mix), (Flower, mid, Mix),
            (Ring, mid, Mix), (Flower, mid, Mix));
        Assert.Equal(5, order.Segments.Count);
        float previous = upper;
        for (int i = 0; i < 5; i++)
        {
            float key = order.Segments[i].Distance;
            Assert.True(key < previous, $"segment {i} at {key} is not below {previous}");
            Assert.True(key > lower, $"segment {i} at {key} is not above the next run");
            Assert.Equal(upper - (upper - lower) * (i + 1) / 6, key, 6);
            previous = key;
        }
    }

    [Fact]
    public void ItemsBeyondEveryRunAndNearerThanEveryRunKeepTheirOwnDistances()
    {
        BlendedItemOrder order = Order([20f, 10f], (Dust, 30f, Mix), (Spark, 25f, Mix), (Dust, 2f, Mix), (Spark, 1f, Mix));
        Assert.Equal([30f, 25f, 2f, 1f], [order.Segments[0].Distance, order.Segments[1].Distance,
            order.Segments[2].Distance, order.Segments[3].Distance]);
    }

    [Fact]
    public void ItemsBetweenRunsSplitAtEachRun()
    {
        BlendedItemOrder order = Order([20f, 10f], (Dust, 25f, Add), (Dust, 15f, Add), (Dust, 12f, Add), (Dust, 5f, Add));
        Assert.Equal(3, order.Segments.Count);
        Assert.Equal([0], ItemsOf(order, 0));
        Assert.Equal([1, 2], ItemsOf(order, 1));
        Assert.Equal([3], ItemsOf(order, 2));
        Assert.Equal(15f, order.Segments[1].Distance);
    }

    [Fact]
    public void AFirstGroupAtTheRunBeforeItIsKeptBelowIt()
    {
        BlendedItemOrder order = Order([20f, 10f], (Dust, 20f, Mix), (Spark, 19f, Mix));
        Assert.Equal(20f - BlendedItemOrder.KeyGap, order.Segments[0].Distance);
        Assert.Equal(19f, order.Segments[1].Distance);
    }

    [Fact]
    public void RunKeysMustFall()
    {
        var order = new BlendedItemOrder();
        order.AddRun(10f);
        Assert.Throws<ArgumentException>(() => order.AddRun(10f));
        Assert.Throws<ArgumentException>(() => order.AddRun(11f));
        Assert.Equal(1, order.RunCount);
    }

    [Fact]
    public void ClearingItemsKeepsTheRunsAndAFreshOrderStartsAgain()
    {
        BlendedItemOrder order = Order([6f], (Dust, 9f, Add), (Dust, 5f, Add));
        order.ClearItems();
        Assert.Empty(order.Segments);
        Assert.Equal(0, order.Items.Length);
        Assert.Equal(0, order.Add(Spark, 5f, Add));
        order.Order();
        Assert.True(Assert.Single(order.Segments).Distance < 6f);
        order.ClearRuns();
        order.ClearItems();
        order.Add(Spark, 5f, Add);
        order.Order();
        Assert.Equal(5f, Assert.Single(order.Segments).Distance);
    }

    [Fact]
    public void AnEmptyOrderHasNoDraws()
    {
        BlendedItemOrder order = Order([6f]);
        Assert.Empty(order.Segments);
    }

    [Fact]
    public void ReorderingAllocatesNothingOnceGrown()
    {
        var order = new BlendedItemOrder();
        for (int r = 0; r < 40; r++)
        {
            order.AddRun(100f - r);
        }

        void Frame(int shift)
        {
            order.ClearItems();
            for (int i = 0; i < 300; i++)
            {
                order.Add(i % 3, 30f + (i * 37 + shift) % 97 * 0.73f, i % 3 == 2 ? Mix : Add);
            }

            for (int b = 0; b < 40 + shift % 7; b++)
            {
                order.AddBarrier(30f + (b * 53 + shift) % 89 * 0.79f, Left);
            }

            order.Order();
        }
        // The first pass grows the buffers to these frames' largest; the same frames again allocate nothing.
        for (int f = 0; f < 20; f++)
        {
            Frame(f);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 20; f++)
        {
            Frame(f);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void EveryItemLandsOnceAndEachDrawKeepsItsRunGap()
    {
        float[] runs = [40f, 35f, 35f - BlendedRunSlots<object>.KeyStep, 20f, 10f];
        var order = new BlendedItemOrder();
        foreach (float run in runs)
        {
            order.AddRun(run);
        }

        var random = new Random(7);
        float[] distances = new float[500];
        for (int i = 0; i < distances.Length; i++)
        {
            distances[i] = (float)(random.NextDouble() * 50);
            order.Add(random.Next(4), distances[i], random.Next(2) == 0 ? Add : Mix);
        }
        order.Order();
        Assert.Equal(Enumerable.Range(0, distances.Length), order.Items.ToArray().OrderBy(i => i));
        foreach (BlendedItemSegment segment in order.Segments)
        {
            foreach (int item in order.Items.Slice(segment.Start, segment.Count).ToArray())
            {
                // The draw falls between the same two runs as each of its items.
                Assert.Equal(runs.Count(run => run >= distances[item]), runs.Count(run => run > segment.Distance));
            }
        }

        for (int s = 1; s < order.Segments.Count; s++)
        {
            Assert.True(order.Segments[s].Distance < order.Segments[s - 1].Distance);
        }
    }

    [Fact]
    public void EveryPairThatOverlapsAndDoesNotCommuteDrawsBackToFront()
    {
        // The pixels are DK2's: any two items of different batches whose boxes overlap, one of them ordered, draw in
        // their back-to-front order, and every item keeps its side of every run.
        float[] runs = [30f, 20f, 10f];
        var random = new Random(11);
        var order = new BlendedItemOrder();
        foreach (float run in runs)
        {
            order.AddRun(run);
        }

        int n = 400;
        float[] distances = new float[n];
        int[] batches = new int[n];
        var blends = new BlendedItemBlend[n];
        var rects = new BlendedItemRect[n];
        for (int i = 0; i < n; i++)
        {
            distances[i] = (float)Math.Round(random.NextDouble() * 40, 1);
            batches[i] = random.Next(5);
            blends[i] = batches[i] < 2 ? Add : Mix;
            float x = (float)random.NextDouble() * 20, y = (float)random.NextDouble() * 20;
            rects[i] = new BlendedItemRect(x, y, x + 1.5f, y + 1.5f);
            order.Add(batches[i], distances[i], blends[i], rects[i]);
        }
        order.Order();
        // Each item's place in the drawing: its draw, then its place in the draw.
        int[] drawnAt = new int[n];
        int[] segmentOf = new int[n];
        int at = 0;
        foreach (BlendedItemSegment segment in order.Segments)
        {
            foreach (int item in order.Items.Slice(segment.Start, segment.Count).ToArray())
            {
                drawnAt[item] = at++;
            }
        }

        for (int s = 0; s < order.Segments.Count; s++)
        {
            foreach (int item in order.Items.Slice(order.Segments[s].Start, order.Segments[s].Count).ToArray())
            {
                segmentOf[item] = s;
            }
        }

        Assert.True(order.Segments.Count < n / 2, $"{order.Segments.Count} draws for {n} items");
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++)
            {
                if (a == b || batches[a] == batches[b] || !(distances[a] > distances[b] || (distances[a] == distances[b] && a < b)))
                {
                    continue;
                }

                if (blends[a] == Add && blends[b] == Add || !rects[a].Overlaps(rects[b]))
                {
                    continue;
                }

                Assert.True(drawnAt[a] < drawnAt[b], $"item {a} at {distances[a]} draws after item {b} at {distances[b]}");
                // The renderer orders draws by key alone, so a later draw must be keyed strictly nearer.
                if (segmentOf[a] != segmentOf[b])
                {
                    Assert.True(order.Segments[segmentOf[a]].Distance > order.Segments[segmentOf[b]].Distance,
                        $"draw {segmentOf[a]} is not keyed beyond draw {segmentOf[b]}");
                }
            }
        }

        foreach (BlendedItemSegment segment in order.Segments)
        {
            foreach (int item in order.Items.Slice(segment.Start, segment.Count).ToArray())
            {
                Assert.Equal(runs.Count(run => run >= distances[item]), runs.Count(run => run > segment.Distance));
            }
        }
    }

    private static readonly System.Numerics.Matrix4x4 TiltedView = ViewFrustum.FromPose(new ClassicCameraPose(
        new System.Numerics.Vector3(3f, 6f, -4f), System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.2f, -0.7f, 1f)),
        System.Numerics.Vector3.UnitY, 50f, 80f, 0.1f, 0f), 16f / 9f, 0.3f).ViewProjection;

    [Fact]
    public void ASpheresBoxHoldsEveryPointOfTheSphereOnScreen()
    {
        var random = new Random(3);
        int boxed = 0;
        for (int trial = 0; trial < 200; trial++)
        {
            var centre = new System.Numerics.Vector3((float)random.NextDouble() * 12 - 3, (float)random.NextDouble() * 3,
                (float)random.NextDouble() * 12);
            float radius = (float)random.NextDouble() * 0.8f + 0.01f;
            var box = BlendedItemRect.OfSphere(TiltedView, centre, radius);
            if (box == BlendedItemRect.Everywhere)
            {
                continue;
            }

            boxed++;
            for (int k = 0; k < 64; k++)
            {
                Vector3 d = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3((float)random.NextDouble() - .5f,
                    (float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f)) * radius;
                var clip = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(centre + d, 1f),
                    System.Numerics.Matrix4x4.Transpose(TiltedView));
                float x = clip.X / clip.W, y = clip.Y / clip.W;
                Assert.True(x >= box.MinX - 1e-5f && x <= box.MaxX + 1e-5f && y >= box.MinY - 1e-5f && y <= box.MaxY + 1e-5f,
                    $"trial {trial}: ({x}, {y}) outside {box}");
            }
        }
        Assert.True(boxed >= 150, $"only {boxed} of 200 spheres were boxed");
    }

    [Fact]
    public void ASphereReachingTheEyesPlaneCoversEverything()
    {
        var eye = new System.Numerics.Vector3(3f, 6f, -4f);
        Assert.Equal(BlendedItemRect.Everywhere, BlendedItemRect.OfSphere(TiltedView, eye, 0.5f));
        Assert.Equal(BlendedItemRect.Everywhere, BlendedItemRect.OfSphere(TiltedView, eye - new System.Numerics.Vector3(0.2f, -0.7f, 1f), 0.1f));
    }

    [Fact]
    public void BoxesOverlapAndJoinAsBoxes()
    {
        Assert.True(Left.Overlaps(Middle));
        Assert.False(Left.Overlaps(Right));
        Assert.True(BlendedItemRect.Everywhere.Overlaps(Left));
        Assert.False(BlendedItemRect.Nowhere.Overlaps(Left));
        Assert.Equal(new BlendedItemRect(0f, 0f, 6f, 1f), Left.Union(Right));
        Assert.Equal(Left, BlendedItemRect.Nowhere.Union(Left));
    }

    [Fact]
    public void ABarrierSplitsTheDrawsItOverlapsAndKeysThemToEitherSide()
    {
        var order = new BlendedItemOrder();
        order.Add(Dust, 9f, Add);
        order.AddBarrier(8f, BlendedItemRect.Everywhere);
        order.Add(Dust, 7f, Add);
        order.Order();
        Assert.Equal(2, order.Segments.Count);
        Assert.Equal([0], ItemsOf(order, 0));
        Assert.Equal([1], ItemsOf(order, 1));
        Assert.Equal(9f, order.Segments[0].Distance);
        Assert.Equal(7f, order.Segments[1].Distance);
        Assert.Equal(2, order.Items.Length);
    }

    [Fact]
    public void AnItemPassesABarrierItDoesNotOverlap()
    {
        var order = new BlendedItemOrder();
        order.Add(Dust, 9f, Mix, Left);
        order.AddBarrier(8f, Right);
        order.Add(Dust, 7f, Mix, Left);
        order.Order();
        BlendedItemSegment only = Assert.Single(order.Segments);
        Assert.Equal(new BlendedItemSegment(Dust, 0, 2, 9f), only);
    }

    [Fact]
    public void DrawsAtABarriersDistanceAreKeptOffIt()
    {
        var order = new BlendedItemOrder();
        order.AddBarrier(8f, BlendedItemRect.Everywhere);
        order.Add(Dust, 8f, Mix);
        order.Add(Spark, 9f, Mix);
        order.AddBarrier(9f, BlendedItemRect.Everywhere);
        order.Order();
        // Equal distances keep the order added: the spark before its barrier, the dust after its.
        Assert.Equal(2, order.Segments.Count);
        Assert.True(order.Segments[0].Distance > 9f);
        Assert.True(order.Segments[1].Distance < 8f);
    }

    [Fact]
    public void BarriersAloneMakeNoDraws()
    {
        var order = new BlendedItemOrder();
        order.AddBarrier(8f, BlendedItemRect.Everywhere);
        order.Order();
        Assert.Empty(order.Segments);
        order.Add(Dust, 5f, Add);
        order.Order();
        Assert.Equal(5f, Assert.Single(order.Segments).Distance);
    }

    [Fact]
    public void EveryItemKeepsItsSideOfEachBarrierItOverlaps()
    {
        float[] runs = [25f, 12f];
        var random = new Random(5);
        var order = new BlendedItemOrder();
        foreach (float run in runs)
        {
            order.AddRun(run);
        }

        int n = 300, barriers = 40;
        float[] distances = new float[n];
        var rects = new BlendedItemRect[n];
        var walls = new (float Distance, BlendedItemRect Rect)[barriers];
        for (int i = 0; i < barriers; i++)
        {
            float x = (float)random.NextDouble() * 20, y = (float)random.NextDouble() * 20;
            walls[i] = ((float)Math.Round(random.NextDouble() * 40, 2), new BlendedItemRect(x, y, x + 3f, y + 3f));
            order.AddBarrier(walls[i].Distance, walls[i].Rect);
        }
        for (int i = 0; i < n; i++)
        {
            distances[i] = (float)Math.Round(random.NextDouble() * 40, 2);
            float x = (float)random.NextDouble() * 20, y = (float)random.NextDouble() * 20;
            rects[i] = new BlendedItemRect(x, y, x + 1.5f, y + 1.5f);
            order.Add(random.Next(4), distances[i], random.Next(2) == 0 ? Add : Mix, rects[i]);
        }
        order.Order();
        Assert.Equal(n, order.Items.Length);
        foreach (BlendedItemSegment segment in order.Segments)
        {
            foreach (int item in order.Items.Slice(segment.Start, segment.Count).ToArray())
            {
                Assert.Equal(runs.Count(run => run >= distances[item]), runs.Count(run => run > segment.Distance));
                foreach ((float distance, BlendedItemRect rect) in walls)
                {
                    if (distance == distances[item] || !rect.Overlaps(rects[item]))
                    {
                        continue;
                    }

                    Assert.True(distances[item] > distance == segment.Distance > distance,
                        $"item {item} at {distances[item]} drawn at {segment.Distance} across the barrier at {distance}");
                }
            }
        }
    }

    [Fact]
    public void AGapWithNothingAboveSpreadsBelowItsFirstItemAndAboveTheRun()
    {
        // Five overlapping ordered draws a hair beyond the run: the chain of key gaps would cross the run, so they spread.
        float near = 10f + BlendedItemOrder.KeyGap * 0.5f;
        BlendedItemOrder order = Order([10f], (Flower, near, Mix), (Ring, near, Mix), (Flower, near, Mix), (Ring, near, Mix),
            (Flower, near, Mix));
        Assert.Equal(5, order.Segments.Count);
        float previous = near + BlendedItemOrder.KeyGap;
        foreach (BlendedItemSegment segment in order.Segments)
        {
            Assert.True(segment.Distance < previous);
            Assert.True(segment.Distance > 10f);
            previous = segment.Distance;
        }
    }

    [Fact]
    public void ABarrierAtARunsKeyKeysTheDrawsAfterItBelowBoth()
    {
        var order = new BlendedItemOrder();
        order.AddRun(10f);
        order.AddBarrier(10f, BlendedItemRect.Everywhere);
        order.Add(Dust, 10.5f, Mix);
        order.Add(Dust, 9.5f, Mix);
        order.Order();
        Assert.Equal(2, order.Segments.Count);
        Assert.True(order.Segments[0].Distance > 10f);
        Assert.True(order.Segments[1].Distance < 10f);
    }

    [Fact]
    public void AnAdditiveBarrierLetsAdditiveItemsPassButNotOrderedOnes()
    {
        var order = new BlendedItemOrder();
        order.Add(Dust, 9f, Add);
        order.AddBarrier(8f, BlendedItemRect.Everywhere, Add);
        order.Add(Dust, 7f, Add);
        order.Order();
        Assert.Single(order.Segments);
        order.ClearItems();
        order.Add(Flower, 9f, Mix);
        order.AddBarrier(8f, BlendedItemRect.Everywhere, Add);
        order.Add(Flower, 7f, Mix);
        order.Order();
        Assert.Equal(2, order.Segments.Count);
    }

    [Fact]
    public void SpreadKeysKeepTheMinimumGapAtTheRenderersMagnitude()
    {
        // Two runs one float step apart near an eye 200 from the origin: no gap fits, so the draws share a key that
        // stays within the runs; with room they keep at least the minimum gap.
        var order = new BlendedItemOrder { EyeDistance = 200f };
        float upper = 100f, lower = upper - 4 * order.MinimumGap(upper);
        order.AddRun(upper);
        order.AddRun(lower);
        float mid = (upper + lower) / 2;
        for (int i = 0; i < 6; i++)
        {
            order.Add(i % 2 == 0 ? Flower : Ring, mid, Mix);
        }

        order.Order();
        float gap = order.MinimumGap(upper);
        float previous = upper;
        foreach (BlendedItemSegment segment in order.Segments)
        {
            Assert.True(segment.Distance <= previous - gap || segment.Distance == previous, $"{segment.Distance} after {previous}");
            Assert.True(segment.Distance >= lower + gap || segment.Distance == (upper + lower) * 0.5f);
            Assert.True(segment.Distance < upper && segment.Distance > lower);
            previous = segment.Distance;
        }
        Assert.True(gap > 0f && gap < BlendedItemOrder.KeyGap);
        Assert.Equal(4 * (MathF.BitIncrement(300f) - 300f), new BlendedItemOrder { EyeDistance = 200f }.MinimumGap(100f));
    }

    [Fact]
    public void EnsuringCapacityLetsAFirstFrameAllocateNothing()
    {
        static void Frame(BlendedItemOrder order)
        {
            for (int r = 0; r < 60; r++)
            {
                order.AddRun(100f - r);
            }

            for (int i = 0; i < 400; i++)
            {
                order.Add(i % 5, 40f + i * 0.13f % 50, i % 2 == 0 ? Add : Mix, Left);
            }

            for (int b = 0; b < 100; b++)
            {
                order.AddBarrier(45f + b * 0.3f, Right);
            }

            order.Order();
        }
        // A first order on another instance compiles the code; the measured one only fills ensured buffers.
        Frame(new BlendedItemOrder());
        var order = new BlendedItemOrder();
        order.EnsureCapacity(512, 512, 64);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Frame(order);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
