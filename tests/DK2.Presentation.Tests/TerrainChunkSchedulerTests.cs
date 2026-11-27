// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The dirty window and the budgeted, threaded flush moved out of Unity's TerrainChunks (godot-step3-audit.md,
/// 3c), pinned against the old code (<see cref="LegacyTerrainChunkSchedule"/>) upload for upload, and their rules
/// checked one by one.</summary>
public class TerrainChunkSchedulerTests
{
    [Fact]
    public void TextureStatePushesKeepLatticeCellsAndAllocateNothing()
    {
        TerrainLattice lattice = Lattice();
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height, lattice, true)
        { FlushBudgetMilliseconds = 0 };
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                scheduler.Set(At(x, y), Floor(x, y));
            }
        }

        scheduler.Flush(new CountingSink());
        TileView plain = Floor(20, 17);
        TileView tagged = Floor(20, 17, true, new TerrainLightColour(300, 1, 2));
        for (int i = 0; i < 100; i++)
        {
            scheduler.Set(At(20, 17), (i & 1) == 0 ? plain : tagged);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            scheduler.Set(At(20, 17), (i & 1) == 0 ? plain : tagged);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.True(lattice.IsBuilt(20, 17));
        Assert.Equal(0, scheduler.PendingChunks);
        Assert.False(scheduler.IsStale(At(20, 17)));
        Assert.False(scheduler.IsStale(At(19, 17)));
    }
    [Fact]
    public void TextureStateChangesSkipGeometryButClearAndStructuralChangesRebuild()
    {
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), 1, 1, null, true)
        { FlushBudgetMilliseconds = 0 };
        var sink = new SchedulerLog();
        TileView Tile(bool tagged = false, bool red = false, int tint = 0, int face = -1)
        {
            return new(new TilePos(0, 0), default, default, default, tagged, false, 0, 1000,
                pieces: default(TerrainPieces).WithFloor(), pieceKeys: Rock, ambientTint: new TerrainLightColour(tint, 0, 0),
                quarterAmbientTint: new TerrainLightColour(0, tint, 0), redHighlighted: red, faceTexture: face);
        }

        scheduler.Set(0, Tile());
        scheduler.Flush(sink);
        Assert.Single(sink.Uploads);
        scheduler.Set(0, Tile(true, true, 300));
        Assert.Equal(0, scheduler.PendingChunks);
        scheduler.Flush(sink);
        Assert.Single(sink.Uploads);
        scheduler.Set(0, Tile(true, true, 300, 2));
        Assert.Equal(1, scheduler.PendingChunks);
        scheduler.Flush(sink);
        Assert.Equal(2, sink.Uploads.Count);
        scheduler.Clear();
        scheduler.Set(0, Tile(true, true, 300, 2));
        scheduler.Flush(sink);
        Assert.Equal(3, sink.Uploads.Count);
    }
    // Three by three chunks, the last row and column partial.
    private const int Width = 40, Height = 36;

    private static readonly TerrainPieceKeys Rock = new("Rock");

    [Theory]
    [InlineData(false, 0.0)]
    [InlineData(false, 4.0)]
    [InlineData(true, 0.0)]
    [InlineData(true, 4.0)]
    public void RandomPushesUploadAsTheOldSchedulerDid(bool withLattice, double budget)
    {
        double clock = 0;
        var pieces = new TerrainTileBuilderTests.FakePieces();
        var scheduler = new TerrainChunkScheduler(pieces, Width, Height, withLattice ? Lattice() : null, () => clock)
        {
            FlushBudgetMilliseconds = budget,
        };
        var legacy = new LegacyTerrainChunkSchedule(pieces, Width, Height, withLattice ? Lattice() : null, () => clock)
        {
            FlushBudgetMilliseconds = budget,
            // A chunk's upload costs 1.5 ms, so a 4 ms budget stops after the third.
            Uploaded = () => clock += 1.5,
        };
        var log = new SchedulerLog { Uploaded = () => clock += 1.5 };
        var random = new Random(1234);

        for (int i = 0; i < Width * Height; i++)
        {
            TileView tile = RandomTile(random, i);
            Assert.Equal(legacy.TrySet(i, tile), scheduler.Set(i, tile));
        }
        for (int round = 0; round < 60; round++)
        {
            legacy.Flush();
            scheduler.Flush(log);
            Assert.Equal(legacy.PendingChunks, scheduler.PendingChunks);
            legacy.WaitForBuild();
            scheduler.WaitForBuild();
            int pushes = random.Next(0, 7);
            for (int p = 0; p < pushes; p++)
            {
                int index = random.Next(Width * Height);
                TileView tile = RandomTile(random, index);
                Assert.Equal(legacy.TrySet(index, tile), scheduler.Set(index, tile));
            }
            if (round == 30)
            {
                legacy.Clear();
                scheduler.Clear();
            }
        }

        Assert.True(log.Uploads.Count > 40, log.Uploads.Count.ToString());
        Assert.Equal(legacy.Uploads.Count, log.Uploads.Count);
        for (int i = 0; i < log.Uploads.Count; i++)
        {
            Assert.Equal(legacy.Uploads[i].Chunk, log.Uploads[i].Chunk);
            Assert.Equal(legacy.Uploads[i].Tiles, log.Uploads[i].Tiles);
            Assert.Equal(legacy.Uploads[i].Numbers, log.Uploads[i].Numbers);
        }
    }

    [Fact]
    public void AChangeWithTheLatticeMarksTheFiveByFiveTilesAndForgetsItsCells()
    {
        TerrainLattice lattice = Lattice();
        TerrainChunkScheduler scheduler = Built(lattice);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                lattice.Prepare(x, y);
            }
        }

        scheduler.Set(At(20, 17), Floor(20, 17));

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Assert.Equal(Math.Abs(x - 20) <= 2 && Math.Abs(y - 17) <= 2, scheduler.IsStale(At(x, y)));
            }
        }

        Assert.False(lattice.IsBuilt(20, 17));
        Assert.True(lattice.IsBuilt(22, 17));
        // The window crosses into the chunks west and north: (16, 16) is chunk 4, (18, 15) chunk 1.
        Assert.True(scheduler.IsDirty(scheduler.ChunkOf(At(18, 15))));
        Assert.Equal(2, scheduler.PendingChunks);
    }

    [Fact]
    public void TheWindowIsClampedToTheMap()
    {
        TerrainChunkScheduler scheduler = Built(Lattice());
        scheduler.Set(At(0, 0), Floor(0, 0));
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(x <= 2 && y <= 2, scheduler.IsStale(At(x, y)));
            }
        }

        Assert.False(scheduler.IsStale(At(Width - 1, 0)));
        Assert.False(scheduler.IsStale(At(Width - 2, Height - 1)));
    }

    [Fact]
    public void WithoutTheLatticeOnlyTheTileThatDrawsOrDrewIsStale()
    {
        TerrainChunkScheduler scheduler = Built(null);
        scheduler.Set(At(5, 5), Floor(5, 5));
        Assert.True(scheduler.IsStale(At(5, 5)));
        Assert.False(scheduler.IsStale(At(6, 5)));

        // A tile that drew and no longer does is stale (its chunk loses it); one that never drew is not.
        var empty = new TileView(new TilePos(7, 7), default, default, default, false, false, 0, 1000);
        scheduler.Set(At(7, 7), empty);
        Assert.True(scheduler.IsStale(At(7, 7)));
        var fresh = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height);
        fresh.Set(At(7, 7), empty);
        Assert.False(fresh.IsStale(At(7, 7)));
        Assert.Equal(0, fresh.PendingChunks);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ATagOrTintChangeMarksTheFourNeighbours(bool tag, bool tint)
    {
        TerrainChunkScheduler scheduler = Built(null);
        scheduler.Set(At(5, 5), Floor(5, 5, tagged: tag, tint: tint ? new TerrainLightColour(10, 20, 30) : default));
        foreach ((int x, int y) in new[] { (4, 5), (6, 5), (5, 4), (5, 6) })
        {
            Assert.True(scheduler.IsStale(At(x, y)), $"({x}, {y})");
        }

        foreach ((int x, int y) in new[] { (4, 4), (6, 6), (3, 5), (5, 7) })
        {
            Assert.False(scheduler.IsStale(At(x, y)), $"({x}, {y})");
        }
    }

    [Fact]
    public void ANeighbourOffTheMapEdgeIsNotMarked()
    {
        TerrainChunkScheduler scheduler = Built(null);
        scheduler.Set(At(Width - 1, 3), Floor(Width - 1, 3, tagged: true));
        Assert.False(scheduler.IsStale(At(0, 4)));
        scheduler.Set(At(0, Height - 1), Floor(0, Height - 1, tagged: true));
        Assert.False(scheduler.IsStale(At(Width - 1, Height - 2)));
        scheduler.Set(At(0, 0), Floor(0, 0, tagged: true));
        Assert.False(scheduler.IsStale(At(Width - 1, Height - 1)));
    }

    /// <summary>The first flush builds inline and uploads every chunk whatever the budget; a later flush under budget
    /// leaves the chunks whose tiles are building for the next, uploads lowest first and at least one.</summary>
    [Fact]
    public void TheFirstFlushTakesEveryChunkAndLaterOnesKeepTheBudget()
    {
        double clock = 0;
        var log = new SchedulerLog { Uploaded = () => clock += 10 };
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height, null, () => clock);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                scheduler.Set(At(x, y), Floor(x, y));
            }
        }

        scheduler.Flush(log);
        Assert.Equal(9, log.Uploads.Count);
        Assert.Equal(0, scheduler.PendingChunks);

        log.Uploads.Clear();
        foreach (int x in new[] { 35, 1, 20 })
        {
            scheduler.Set(At(x, 1), Floor(x, 1, tagged: true));
        }

        scheduler.Flush(log);
        Assert.Empty(log.Uploads);
        Assert.Equal(3, scheduler.PendingChunks);
        scheduler.WaitForBuild();
        scheduler.Flush(log);
        // 10 ms an upload against 4: one chunk, the lowest.
        Assert.Equal([0], log.Uploads.Select(u => u.Chunk));
        scheduler.Flush(log);
        scheduler.Flush(log);
        Assert.Equal([0, 1, 2], log.Uploads.Select(u => u.Chunk));
        Assert.Equal(0, scheduler.PendingChunks);
    }

    /// <summary>While the pool build runs, a budgeted flush returns at once: no upload, nothing joined, the chunks still
    /// pending. Once the build finishes the next flush takes it over and uploads.</summary>
    [Fact]
    public void AFlushDuringTheBuildReturnsWithoutWaiting()
    {
        var log = new SchedulerLog();
        TerrainChunkScheduler scheduler = Built(null, log);
        log.Uploads.Clear();
        using var gate = new ManualResetEventSlim(false);
        scheduler.BuildGate = gate;
        scheduler.Set(At(3, 3), Floor(3, 3, tagged: true));
        scheduler.Flush(log);
        Assert.True(scheduler.IsBuilding);

        var waited = System.Diagnostics.Stopwatch.StartNew();
        scheduler.Flush(log);
        // Returns at once: the gate holds the build for good, so any wait would take the whole timeout.
        Assert.True(waited.ElapsedMilliseconds < 2000, waited.ElapsedMilliseconds.ToString());
        Assert.True(scheduler.IsBuilding);
        Assert.True(scheduler.IsStale(At(3, 3)));
        Assert.Empty(log.Uploads);
        Assert.Equal(1, scheduler.PendingChunks);

        gate.Set();
        scheduler.WaitForBuild();
        scheduler.Flush(log);
        Assert.False(scheduler.IsBuilding);
        Assert.Equal([0], log.Uploads.Select(u => u.Chunk));
    }

    /// <summary>Clear waits for a running build and takes its tiles over before forgetting them.</summary>
    [Fact]
    public void ClearJoinsTheBuild()
    {
        TerrainChunkScheduler scheduler = Built(null);
        using var gate = new ManualResetEventSlim(false);
        scheduler.BuildGate = gate;
        scheduler.Set(At(3, 3), Floor(3, 3, tagged: true));
        scheduler.Flush(new SchedulerLog());
        Assert.True(scheduler.IsBuilding);
        // Its own thread, not the pool: a loaded test host can starve the pool past the gate's timeout, and the
        // build would then run ungated and Clear would return before the gate opened.
        var release = new Thread(() => { Thread.Sleep(50); gate.Set(); }) { IsBackground = true };
        release.Start();
        scheduler.Clear();
        Assert.True(gate.IsSet);
        Assert.False(scheduler.IsBuilding);
        Assert.False(scheduler.IsStale(At(3, 3)));
        release.Join();
    }

    /// <summary>The tag's neighbour marks reach the map's first and last row and column from one tile in.</summary>
    [Fact]
    public void ANeighbourOnTheMapEdgeIsMarked()
    {
        TerrainChunkScheduler scheduler = Built(null);
        scheduler.Set(At(1, 10), Floor(1, 10, tagged: true));
        Assert.True(scheduler.IsStale(At(0, 10)));
        scheduler.Set(At(Width - 2, 10), Floor(Width - 2, 10, tagged: true));
        Assert.True(scheduler.IsStale(At(Width - 1, 10)));
        scheduler.Set(At(10, 1), Floor(10, 1, tagged: true));
        Assert.True(scheduler.IsStale(At(10, 0)));
        scheduler.Set(At(10, Height - 2), Floor(10, Height - 2, tagged: true));
        Assert.True(scheduler.IsStale(At(10, Height - 1)));
    }

    /// <summary>The budget is spent when the elapsed time reaches it exactly.</summary>
    [Fact]
    public void TheBudgetIsSpentAtExactlyItsLength()
    {
        double clock = 0;
        var log = new SchedulerLog { Uploaded = () => clock += 4 };
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height, null, () => clock);
        Assert.Equal(4.0, TerrainChunkScheduler.DefaultFlushBudgetMilliseconds);
        Assert.Equal(4.0, scheduler.FlushBudgetMilliseconds);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                scheduler.Set(At(x, y), Floor(x, y));
            }
        }

        scheduler.Flush(log);
        log.Uploads.Clear();
        foreach (int x in new[] { 1, 20 })
        {
            scheduler.Set(At(x, 1), Floor(x, 1, tagged: true));
        }

        scheduler.Flush(log);
        scheduler.WaitForBuild();
        scheduler.Flush(log);
        Assert.Equal([0], log.Uploads.Select(u => u.Chunk));
    }

    /// <summary>A flush whose budget is spent before its first upload still uploads one chunk.</summary>
    [Fact]
    public void AFlushOverBudgetStillUploadsOneChunk()
    {
        double clock = 0;
        var log = new SchedulerLog();
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height, null, () => clock += 5);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                scheduler.Set(At(x, y), Floor(x, y));
            }
        }

        scheduler.Flush(log);
        log.Uploads.Clear();
        foreach (int x in new[] { 1, 20 })
        {
            scheduler.Set(At(x, 1), Floor(x, 1, tagged: true));
        }

        scheduler.Flush(log);
        scheduler.WaitForBuild();
        scheduler.Flush(log);
        Assert.Equal([0], log.Uploads.Select(u => u.Chunk));
        Assert.Equal(1, scheduler.PendingChunks);
    }

    /// <summary>A chunk whose drawn tiles have no vertices (the install has none of their pieces) does not count as
    /// shown, so the next flush is still inline.</summary>
    [Fact]
    public void AChunkWithoutVerticesDoesNotCountAsShown()
    {
        var log = new SchedulerLog();
        var scheduler = new TerrainChunkScheduler(new NoPieces(), 16, 16);
        scheduler.Set(0, Floor(0, 0));
        scheduler.Flush(log);
        Assert.Single(log.Uploads);
        Assert.Equal(0, log.Uploads[0].Vertices);
        scheduler.Set(1, Floor(1, 0));
        scheduler.Flush(log);
        Assert.Equal(2, log.Uploads.Count);
    }

    private sealed class NoPieces : ITerrainPieceSource
    {
        public TerrainPiece? Piece(DK2.Assets.AssetRef key, int textureIndex = -1)
        {
            return null;
        }
    }

    [Fact]
    public void NoBudgetBuildsAndUploadsEverythingAtOnce()
    {
        var log = new SchedulerLog();
        TerrainChunkScheduler scheduler = Built(null, log);
        scheduler.FlushBudgetMilliseconds = 0;
        for (int x = 0; x < Width; x += 3)
        {
            scheduler.Set(At(x, 20), Floor(x, 20, tagged: true));
        }

        log.Uploads.Clear();
        scheduler.Flush(log);
        Assert.Equal([3, 4, 5], log.Uploads.Select(u => u.Chunk));
        Assert.Equal(0, scheduler.PendingChunks);
    }

    [Fact]
    public void ClearMakesTheNextFlushInlineAgain()
    {
        var log = new SchedulerLog();
        TerrainChunkScheduler scheduler = Built(null, log);
        scheduler.Clear();
        scheduler.Set(At(30, 30), Floor(30, 30));
        log.Uploads.Clear();
        scheduler.Flush(log);
        Assert.Equal([4], log.Uploads.Select(u => u.Chunk));
        Assert.Equal([At(30, 30)], log.Uploads[0].Tiles);
    }

    /// <summary>A chunk whose tiles all stopped drawing is uploaded empty, and no longer counts as shown: with none
    /// shown, the next flush is inline.</summary>
    [Fact]
    public void AnEmptiedMapFlushesInlineAgain()
    {
        var log = new SchedulerLog();
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), 16, 16);
        scheduler.Set(0, Floor(0, 0));
        scheduler.Flush(log);
        var empty = new TileView(new TilePos(0, 0), default, default, default, false, false, 0, 1000);
        scheduler.Set(0, empty);
        scheduler.Flush(log);
        scheduler.WaitForBuild();
        scheduler.Flush(log);
        Assert.Equal(2, log.Uploads.Count);
        Assert.Empty(log.Uploads[1].Tiles);
        scheduler.Set(0, Floor(0, 0));
        scheduler.Flush(log);
        Assert.Equal(3, log.Uploads.Count);
    }

    /// <summary>A flush with nothing dirty, and an inline rebuild of a changed tile once its buffers have grown,
    /// allocate nothing (ADR-0695).</summary>
    [Fact]
    public void FlushesAllocateNothingOnceWarm()
    {
        var log = new CountingSink();
        TerrainChunkScheduler scheduler = Built(null, log);
        scheduler.FlushBudgetMilliseconds = 0;
        TileView tagged = Floor(9, 9, tagged: true), plain = Floor(9, 9);
        for (int warm = 0; warm < 3; warm++)
        {
            scheduler.Set(At(9, 9), tagged);
            scheduler.Flush(log);
            scheduler.Set(At(9, 9), plain);
            scheduler.Flush(log);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        scheduler.Flush(log);
        scheduler.Set(At(9, 9), tagged);
        scheduler.Flush(log);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.True(log.Tiles > 0);
    }

    [Fact]
    public void ChunksAreSixteenTilesSquareRowMajor()
    {
        TerrainChunkScheduler scheduler = Built(null);
        Assert.Equal(9, scheduler.ChunkCount);
        Assert.Equal(0, scheduler.ChunkOf(At(15, 15)));
        Assert.Equal(1, scheduler.ChunkOf(At(16, 0)));
        Assert.Equal(3, scheduler.ChunkOf(At(0, 16)));
        Assert.Equal(8, scheduler.ChunkOf(At(39, 35)));
        Assert.Equal(2, TerrainChunkScheduler.StaleReach);
    }

    /// <summary>With a packer every upload follows a pack of the same chunk over the same tiles: the first flush packs
    /// every chunk inline, a budgeted flush leaves the rebuilt chunk to the build task, which packs it, and nothing is
    /// uploaded before the build is taken over.</summary>
    [Fact]
    public void APackerPacksEachRebuiltChunkBeforeItsUploadOnTheBuildTask()
    {
        var log = new PackLog();
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height) { Packer = log };
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                scheduler.Set(At(x, y), Floor(x, y));
            }
        }

        scheduler.Flush(log);
        Assert.Equal(Enumerable.Range(0, 9), log.Packs.Select(p => p.Chunk));
        Assert.All(log.Packs, p => Assert.Equal(Environment.CurrentManagedThreadId, p.Thread));
        Assert.Equal(9, log.Uploads.Count);

        log.Packs.Clear();
        log.Uploads.Clear();
        using var gate = new ManualResetEventSlim(false);
        scheduler.BuildGate = gate;
        scheduler.Set(At(20, 20), Floor(20, 20, tagged: true));
        scheduler.Flush(log);
        Assert.Empty(log.Packs);
        Assert.Empty(log.Uploads);
        gate.Set();
        scheduler.WaitForBuild();
        // Packed by the build itself (WaitForBuild may run the task inline, so the thread is not asserted).
        Assert.Equal([4], log.Packs.Select(p => p.Chunk));
        scheduler.Flush(log);
        Assert.Equal([4], log.Uploads.Select(u => u.Chunk));
        Assert.Equal(0, log.Overlaps);
    }

    /// <summary>A chunk whose changed tile stopped drawing has nothing to build: it is packed on the flushing thread
    /// and uploaded in the same flush, as without a packer.</summary>
    [Fact]
    public void AChunkWithNothingToBuildIsPackedInlineAndUploadedAtOnce()
    {
        var log = new PackLog();
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), 16, 16) { Packer = log };
        scheduler.Set(0, Floor(0, 0));
        scheduler.Set(1, Floor(1, 0));
        scheduler.Flush(log);
        log.Packs.Clear();
        scheduler.Set(0, new TileView(new TilePos(0, 0), default, default, default, false, false, 0, 1000));
        scheduler.Flush(log);
        Assert.False(scheduler.IsBuilding);
        Assert.Single(log.Packs);
        Assert.Equal(Environment.CurrentManagedThreadId, log.Packs[0].Thread);
        Assert.Single(log.Packs[0].Tiles);
        Assert.Equal(2, log.Uploads.Count);
        Assert.Equal([1], log.Uploads[1].Tiles);
    }

    /// <summary>A chunk the budget left for a later flush keeps its pack: the next build packs only the chunks whose
    /// tiles changed since.</summary>
    [Fact]
    public void ADeferredChunkIsNotPackedAgain()
    {
        double clock = 0;
        var log = new PackLog { Uploaded = () => clock += 10 };
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height, null, () => clock)
        {
            Packer = log,
        };
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                scheduler.Set(At(x, y), Floor(x, y));
            }
        }

        scheduler.Flush(log);
        log.Packs.Clear();
        log.Uploads.Clear();
        foreach (int x in new[] { 35, 1, 20 })
        {
            scheduler.Set(At(x, 1), Floor(x, 1, tagged: true));
        }

        scheduler.Flush(log);
        scheduler.WaitForBuild();
        scheduler.Flush(log);
        Assert.Equal([0, 1, 2], log.Packs.Select(p => p.Chunk).Order());
        Assert.Equal([0], log.Uploads.Select(u => u.Chunk));

        log.Packs.Clear();
        scheduler.Set(At(5, 34), Floor(5, 34, tagged: true));
        scheduler.Flush(log);
        scheduler.WaitForBuild();
        while (scheduler.PendingChunks > 0)
        {
            scheduler.Flush(log);
        }

        Assert.Equal([6], log.Packs.Select(p => p.Chunk));
        Assert.Equal([0, 1, 2, 6], log.Uploads.Select(u => u.Chunk));
    }

    /// <summary>A packer that allocates nothing keeps an inline rebuild allocation-free once warm.</summary>
    [Fact]
    public void PackingAllocatesNothingOnceWarm()
    {
        var log = new CountingSink();
        var packer = new CountingPacker();
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height) { Packer = packer };
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                scheduler.Set(At(x, y), Floor(x, y));
            }
        }

        scheduler.Flush(log);
        scheduler.FlushBudgetMilliseconds = 0;
        TileView tagged = Floor(9, 9, tagged: true), plain = Floor(9, 9);
        for (int warm = 0; warm < 3; warm++)
        {
            scheduler.Set(At(9, 9), tagged);
            scheduler.Flush(log);
            scheduler.Set(At(9, 9), plain);
            scheduler.Flush(log);
        }
        int packs = packer.Packs;
        long before = GC.GetAllocatedBytesForCurrentThread();
        scheduler.Set(At(9, 9), tagged);
        scheduler.Flush(log);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(packs + 1, packer.Packs);
    }

    private sealed class CountingPacker : ITerrainChunkPacker
    {
        public int Packs;
        public void PackChunk(int chunk, ReadOnlySpan<TerrainTileGeometry> tiles)
        {
            Packs++;
        }
    }

    /// <summary>Records packs and uploads, and checks each upload's tiles against the chunk's last pack.</summary>
    private sealed class PackLog : ITerrainChunkPacker, ITerrainChunkSink
    {
        private readonly Dictionary<int, TerrainTileGeometry[]> _lastPack = new();
        private readonly List<TerrainTileGeometry> _uploading = new();
        private SchedulerLog.Upload? _open;
        private int _inside;

        public List<(int Chunk, int Thread, TerrainTileGeometry[] Tiles)> Packs { get; } = new();
        public List<SchedulerLog.Upload> Uploads { get; } = new();
        public Action? Uploaded { get; set; }
        public int Overlaps;

        public void PackChunk(int chunk, ReadOnlySpan<TerrainTileGeometry> tiles)
        {
            if (Interlocked.Increment(ref _inside) != 1)
            {
                Interlocked.Increment(ref Overlaps);
            }

            TerrainTileGeometry[] copy = tiles.ToArray();
            lock (Packs)
            {
                Packs.Add((chunk, Environment.CurrentManagedThreadId, copy));
                _lastPack[chunk] = copy;
            }
            Interlocked.Decrement(ref _inside);
        }

        public void BeginChunk(int chunk)
        {
            _open = new SchedulerLog.Upload(chunk);
            _uploading.Clear();
        }

        public void AddTile(int tileIndex, TerrainTileGeometry geometry)
        {
            _open!.Add(tileIndex, geometry);
            _uploading.Add(geometry);
        }

        public void EndChunk(int chunk)
        {
            TerrainTileGeometry[]? packed;
            lock (Packs)
            {
                Assert.True(_lastPack.TryGetValue(chunk, out packed), "chunk " + chunk + " was never packed");
            }

            Assert.Equal(packed, _uploading);
            Uploads.Add(_open!);
            Uploaded?.Invoke();
        }
    }

    private sealed class CountingSink : ITerrainChunkSink
    {
        public int Tiles;
        public void BeginChunk(int chunk) { }
        public void AddTile(int tileIndex, TerrainTileGeometry geometry)
        {
            Tiles++;
        }

        public void EndChunk(int chunk) { }
    }

    private static TerrainChunkScheduler Built(TerrainLattice? lattice, ITerrainChunkSink? sink = null)
    {
        var scheduler = new TerrainChunkScheduler(new TerrainTileBuilderTests.FakePieces(), Width, Height, lattice);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                scheduler.Set(At(x, y), Floor(x, y));
            }
        }

        scheduler.Flush(sink ?? new SchedulerLog());
        return scheduler;
    }

    private static int At(int x, int y)
    {
        return (y * Width) + x;
    }

    private static TileView Floor(int x, int y, bool tagged = false, TerrainLightColour tint = default)
    {
        return new(new TilePos(x, y), default, default, default, tagged, false, 0, 1000,
            pieces: default(TerrainPieces).WithFloor(), pieceKeys: Rock, ambientTint: tint);
    }

    private static TileView RandomTile(Random random, int index)
    {
        int x = index % Width, y = index / Width;
        return random.Next(6) switch
        {
            0 => new TileView(new TilePos(x, y), default, default, default, false, false, 0, 1000),
            1 => Floor(x, y, tagged: true),
            2 => Floor(x, y, tint: new TerrainLightColour(random.Next(300), 5, 9)),
            3 => new TileView(new TilePos(x, y), default, default, default, random.Next(2) == 0, true, 0, 1000,
                pieces: default(TerrainPieces).WithFace(1).WithFace(3).WithTop(), pieceKeys: Rock),
            _ => Floor(x, y),
        };
    }

    /// <summary>A lattice over a map of solid rock, which bends the walls and floors near every tile.</summary>
    private static TerrainLattice Lattice()
    {
        TerrainDef rock = new()
        {
            Name = "rock", TerrainId = 2, Flags = TerrainFlags.Solid, StartingHealth = 1, MaxHealth = 1, GoldValue = 0,
            ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 2, DestroyedTypeTerrainId = 2,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        TerrainDef floor = rock with { Name = "floor", TerrainId = 3, Flags = 0 };
        var database = new GameDatabase([rock, floor], [], [], new GlobalVariables());
        var tiles = new MapTile[Width * Height];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = new MapTile((byte)(i % 7 == 0 ? 3 : 2), PlayerIds.Unowned, 0, 0);
        }

        var world = WorldState.FromMap(database, new LevelMap(Width, Height, tiles), []);
        return new TerrainLattice(database, world, new PlayerId(PlayerIds.FirstKeeper));
    }
}
