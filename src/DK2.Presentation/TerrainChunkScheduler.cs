// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Takes the chunks <see cref="TerrainChunkScheduler.Flush"/> uploads: each drawn tile of the chunk, rows from
/// the north, between <see cref="BeginChunk"/> and <see cref="EndChunk"/>, on the flushing thread.</summary>
public interface ITerrainChunkSink
{
    void BeginChunk(int chunk);

    void AddTile(int tileIndex, TerrainTileGeometry geometry);

    void EndChunk(int chunk);
}

/// <summary>Prepares a chunk's upload ahead of its <see cref="ITerrainChunkSink"/> calls (audit item 12): an engine packs
/// the chunk's vertices in its own format here, so the flushing thread only hands them over. Called on the build task,
/// after the tiles it rebuilt, for every chunk holding one of them; on the flushing thread for an inline build and for
/// a chunk whose changed tiles all stopped drawing. Never two calls at once, and never for a chunk that is being
/// uploaded; a chunk is packed again only after one of its tiles changed.</summary>
public interface ITerrainChunkPacker
{
    /// <param name="tiles">The chunk's drawn tiles that have geometry, rows from the north, as the sink will get them;
    /// valid for the call only.</param>
    void PackChunk(int chunk, ReadOnlySpan<TerrainTileGeometry> tiles);
}

/// <summary>Which terrain chunks to rebuild and when, for every engine (godot-step3-audit.md, 3c): a pushed tile marks
/// the tiles whose pieces it can change stale, each tile keeps its geometry, and a flush builds the stale ones and
/// uploads their 16x16 chunks lowest first within a budget (ADR-0457). With a budget the vertices are built on a
/// thread-pool task between flushes (ADR-0696); a change, a <see cref="Clear"/> or <see cref="JoinBuild"/> waits for
/// it. The task reads only the prepared geometry and lattice cells, so the world may be ticked meanwhile. With a
/// <see cref="Packer"/> the task also packs the chunks it rebuilt for the engine (audit item 12).</summary>
public sealed class TerrainChunkScheduler
{
    public const int ChunkSize = 16;

    /// <summary>How far a pushed tile marks tiles stale: its lattice cells reach a tile further, and they bend the
    /// pieces of the tiles one beyond (<see cref="LatticeDirtyTracker.TorchReach"/>), a 5x5 window where DK2 rebuilds
    /// the 3x3.</summary>
    public const int StaleReach = LatticeDirtyTracker.ForgetReach + 1;

    public const double DefaultFlushBudgetMilliseconds = 4.0;

    /// <summary>The profiling spans opened during a flush.</summary>
    public static class Markers
    {
        public static readonly SimulationMarker Prepare = SimulationMarker.Named("TerrainChunks.Prepare");
        public static readonly SimulationMarker Geometry = SimulationMarker.Named("TerrainChunks.Geometry");
        public static readonly SimulationMarker Join = SimulationMarker.Named("TerrainChunks.Join");
        public static readonly SimulationMarker Rebuild = SimulationMarker.Named("TerrainChunks.Rebuild");
    }

    private static readonly Func<double> StopwatchMilliseconds = () => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    private readonly TerrainLattice? _lattice;
    private readonly bool _stateTexture;
    private readonly int _width, _height, _chunksX;
    private readonly TileView[] _tiles;
    private readonly bool[] _drawn, _stale;
    private readonly TerrainTileGeometry?[] _geometry;
    private readonly bool[] _dirty, _shown, _packed;
    private readonly int[] _order;
    private readonly int[] _building, _packing;
    private readonly TerrainTileGeometry[] _packTiles = new TerrainTileGeometry[ChunkSize * ChunkSize];
    private readonly Action _buildPending;
    private readonly Func<double> _now;
    private int _dirtyCount, _shownCount, _buildingCount, _packingCount;
    private Task? _buildTask;

    public TerrainChunkScheduler(ITerrainPieceSource pieces, int width, int height, TerrainLattice? lattice = null)
        : this(pieces, width, height, lattice, null)
    {
    }

    public TerrainChunkScheduler(ITerrainPieceSource pieces, int width, int height, TerrainLattice? lattice, bool stateTexture)
        : this(pieces, width, height, lattice, null, stateTexture) { }

    /// <param name="now">The flush budget's clock in milliseconds (tests); the stopwatch when null.</param>
    internal TerrainChunkScheduler(ITerrainPieceSource pieces, int width, int height, TerrainLattice? lattice,
        Func<double>? now, bool stateTexture = false)
    {
        _stateTexture = stateTexture;
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Builder = new TerrainTileBuilder(pieces, width, height, lattice, stateTexture);
        _lattice = lattice;
        _width = width;
        _height = height;
        _chunksX = (width + ChunkSize - 1) / ChunkSize;
        ChunkCount = _chunksX * ((height + ChunkSize - 1) / ChunkSize);
        _tiles = new TileView[width * height];
        _drawn = new bool[width * height];
        _stale = new bool[width * height];
        _geometry = new TerrainTileGeometry?[width * height];
        _dirty = new bool[ChunkCount];
        _shown = new bool[ChunkCount];
        _packed = new bool[ChunkCount];
        _order = new int[ChunkCount];
        _building = new int[width * height];
        _packing = new int[ChunkCount];
        _buildPending = BuildOnPool;
        _now = now ?? StopwatchMilliseconds;
    }

    public TerrainTileBuilder Builder { get; }

    public int ChunkCount { get; }

    /// <summary>How long one <see cref="Flush"/> may spend before it leaves the rest for the next, in milliseconds; at
    /// least one chunk is uploaded per flush, and while no chunk shows (the first flush, or the first after a
    /// <see cref="Clear"/>) every chunk is built inline and uploaded, so a level appears whole. Zero or less builds
    /// and uploads everything at once, on the calling thread.</summary>
    public double FlushBudgetMilliseconds { get; set; } = DefaultFlushBudgetMilliseconds;

    /// <summary>How many chunks the last <see cref="Flush"/> left for a later one.</summary>
    public int PendingChunks => _dirtyCount;

    /// <summary>What the last <see cref="Flush"/> spent on the calling thread.</summary>
    public double LastFlushMilliseconds { get; private set; }

    /// <summary>Where to time the flush's spans (<see cref="Markers"/>); called on the flushing thread only.</summary>
    public ISimulationProfiler Profiler { get; set; } = NullSimulationProfiler.Instance;

    /// <summary>Packs each rebuilt chunk before its upload (<see cref="ITerrainChunkPacker"/>); null packs nothing.
    /// Set it before the first <see cref="Flush"/>.</summary>
    public ITerrainChunkPacker? Packer { get; set; }

    public int ChunkOf(int tileIndex) => (tileIndex / _width / ChunkSize * _chunksX) + (tileIndex % _width / ChunkSize);

    /// <summary>Takes a pushed tile; false when it draws no terrain. With a lattice the change forgets its cells and
    /// marks the tiles within <see cref="StaleReach"/> stale; without, the tile alone if it draws or drew. A change of
    /// the tag or the tint marks the four neighbours too: their room walls facing the tile take its tag and tint.
    /// In state-texture mode, tag, red and tint changes retain the geometry and lattice cells.</summary>
    public bool Set(int tileIndex, in TileView tile)
    {
        bool draws = TerrainTileBuilder.Draws(tile);
        if (_stateTexture && _drawn[tileIndex] && TerrainTileState.GeometryEquals(_tiles[tileIndex], tile))
        {
            _tiles[tileIndex] = tile;
            return draws;
        }
        JoinBuild();
        if (_lattice != null)
        {
            int x = tileIndex % _width, y = tileIndex / _width;
            _lattice.Invalidate(x, y);
            for (int ty = Math.Max(0, y - StaleReach); ty <= Math.Min(_height - 1, y + StaleReach); ty++)
                for (int tx = Math.Max(0, x - StaleReach); tx <= Math.Min(_width - 1, x + StaleReach); tx++)
                    MarkStale((ty * _width) + tx);
        }
        else if (draws || _drawn[tileIndex]) MarkStale(tileIndex);
        if (!_stateTexture && (tile.Tagged != _tiles[tileIndex].Tagged || tile.AmbientTint != _tiles[tileIndex].AmbientTint))
            MarkNeighboursStale(tileIndex);
        _tiles[tileIndex] = tile;
        _drawn[tileIndex] = draws;
        return draws;
    }

    /// <summary>Takes a finished build over, starts one for the tiles that changed since, and uploads the dirty chunks
    /// whose tiles are all built, lowest chunk first, within <see cref="FlushBudgetMilliseconds"/>; what does not fit
    /// stays dirty. While a build runs under a budget the flush only waits for the next one.</summary>
    public void Flush(ITerrainChunkSink sink)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));
        LastFlushMilliseconds = 0;
        if (_dirtyCount == 0) return;
        double start = _now();
        bool budgeted = FlushBudgetMilliseconds > 0 && _shownCount > 0;
        if (_buildTask != null)
        {
            if (budgeted && !_buildTask.IsCompleted)
            {
                LastFlushMilliseconds = _now() - start;
                return;
            }
            JoinBuild();
        }

        int count = 0;
        for (int chunk = 0; chunk < _dirty.Length; chunk++)
            if (_dirty[chunk]) _order[count++] = chunk;
        if (budgeted) StartBuild(count);
        else BuildInline(count);

        for (int i = 0; i < count; i++)
        {
            if (budgeted && _dirtyCount < count && _now() - start >= FlushBudgetMilliseconds) break;
            int chunk = _order[i];
            if (AnyStale(chunk)) continue;
            using (Profiler.Scope(Markers.Rebuild)) Upload(chunk, sink);
            _dirty[chunk] = false;
            _dirtyCount--;
        }
        LastFlushMilliseconds = _now() - start;
    }

    /// <summary>Forgets every drawn tile and chunk, so the next flush builds the whole level inline again; the pushed
    /// tiles are kept for their tag and tint comparison.</summary>
    public void Clear()
    {
        JoinBuild();
        Array.Clear(_dirty, 0, _dirty.Length);
        Array.Clear(_shown, 0, _shown.Length);
        Array.Clear(_packed, 0, _packed.Length);
        _dirtyCount = 0;
        _shownCount = 0;
        Array.Clear(_drawn, 0, _drawn.Length);
        Array.Clear(_geometry, 0, _geometry.Length);
        Array.Clear(_stale, 0, _stale.Length);
    }

    /// <summary>Waits for the build task, if one runs, and takes its tiles over. Call it before changing the lattice
    /// (its latches forget cells the task may be reading).</summary>
    public void JoinBuild()
    {
        Task? build = _buildTask;
        if (build == null) return;
        using (Profiler.Scope(Markers.Join))
        {
            _buildTask = null;
            try
            {
                build.GetAwaiter().GetResult();
            }
            finally
            {
                for (int i = 0; i < _buildingCount; i++) _stale[_building[i]] = false;
                _buildingCount = 0;
                for (int i = 0; i < _packingCount; i++) _packed[_packing[i]] = true;
                _packingCount = 0;
            }
        }
    }

    /// <summary>Whether tile <paramref name="tileIndex"/> waits for a build (tests).</summary>
    internal bool IsStale(int tileIndex) => _stale[tileIndex];

    /// <summary>Whether chunk <paramref name="chunk"/> waits for an upload (tests).</summary>
    internal bool IsDirty(int chunk) => _dirty[chunk];

    /// <summary>Waits for a running build without taking it over, so the next flush finds it complete (tests).</summary>
    internal void WaitForBuild() => _buildTask?.Wait();

    /// <summary>Holds a pool build until set, so a test can flush while it runs.</summary>
    internal ManualResetEventSlim? BuildGate { get; set; }

    /// <summary>How long a gated build waits at most, so a test whose flush wrongly joins it fails instead of hanging.</summary>
    internal static readonly TimeSpan BuildGateTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Whether a pool build has been started and not yet taken over (tests).</summary>
    internal bool IsBuilding => _buildTask != null;

    private void MarkNeighboursStale(int tileIndex)
    {
        int x = tileIndex % _width, y = tileIndex / _width;
        if (x > 0) MarkStale(tileIndex - 1);
        if (x < _width - 1) MarkStale(tileIndex + 1);
        if (y > 0) MarkStale(tileIndex - _width);
        if (y < _height - 1) MarkStale(tileIndex + _width);
    }

    private void MarkStale(int tileIndex)
    {
        _stale[tileIndex] = true;
        int chunk = ChunkOf(tileIndex);
        _packed[chunk] = false;
        if (_dirty[chunk]) return;
        _dirty[chunk] = true;
        _dirtyCount++;
    }

    private bool AnyStale(int chunk)
    {
        int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
        for (int y = y0; y < Math.Min(y0 + ChunkSize, _height); y++)
            for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
                if (_stale[(y * _width) + x]) return true;
        return false;
    }

    /// <summary>The first phase for every stale tile of the dirty chunks, on this thread; a stale tile no longer drawn
    /// just stops being stale.</summary>
    private void PrepareStale(int count)
    {
        using ProfileScope _ = Profiler.Scope(Markers.Prepare);
        for (int i = 0; i < count; i++)
        {
            int chunk = _order[i];
            int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
            for (int y = y0; y < Math.Min(y0 + ChunkSize, _height); y++)
                for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
                {
                    int index = (y * _width) + x;
                    if (!_stale[index]) continue;
                    if (!_drawn[index])
                    {
                        _stale[index] = false;
                        continue;
                    }
                    Builder.Prepare(x, y, _tiles, _geometry[index] ??= new TerrainTileGeometry());
                    _building[_buildingCount++] = index;
                }
        }
    }

    private void StartBuild(int count)
    {
        PrepareStale(count);
        PlanPacking(count);
        if (_buildingCount == 0) return;
        _buildTask = Task.Run(_buildPending);
    }

    private void BuildInline(int count)
    {
        PrepareStale(count);
        PlanPacking(count);
        using (Profiler.Scope(Markers.Geometry))
        {
            BuildPending();
            PackPending();
        }
        for (int i = 0; i < _buildingCount; i++) _stale[_building[i]] = false;
        _buildingCount = 0;
        for (int i = 0; i < _packingCount; i++) _packed[_packing[i]] = true;
        _packingCount = 0;
    }

    private void BuildOnPool()
    {
        BuildGate?.Wait(BuildGateTimeout);
        BuildPending();
        PackPending();
    }

    private void BuildPending()
    {
        for (int i = 0; i < _buildingCount; i++) Builder.Build(_geometry[_building[i]]!);
    }

    /// <summary>After <see cref="PrepareStale"/>: the dirty chunks not packed since their last change. One with a tile
    /// to build waits for the build (its tiles stay stale until then, so no flush uploads it meanwhile); one whose
    /// changed tiles all stopped drawing is packed here, so it uploads in this flush as without a packer.</summary>
    private void PlanPacking(int count)
    {
        if (Packer == null) return;
        for (int i = 0; i < count; i++)
        {
            int chunk = _order[i];
            if (_packed[chunk]) continue;
            if (AnyStale(chunk)) _packing[_packingCount++] = chunk;
            else
            {
                Pack(chunk);
                _packed[chunk] = true;
            }
        }
    }

    private void PackPending()
    {
        for (int i = 0; i < _packingCount; i++) Pack(_packing[i]);
    }

    private void Pack(int chunk)
    {
        int count = 0;
        int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
        for (int y = y0; y < Math.Min(y0 + ChunkSize, _height); y++)
            for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
            {
                int index = (y * _width) + x;
                if (_drawn[index] && _geometry[index] is TerrainTileGeometry geometry) _packTiles[count++] = geometry;
            }
        Packer!.PackChunk(chunk, new ReadOnlySpan<TerrainTileGeometry>(_packTiles, 0, count));
    }

    private void Upload(int chunk, ITerrainChunkSink sink)
    {
        sink.BeginChunk(chunk);
        bool shows = false;
        int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
        for (int y = y0; y < Math.Min(y0 + ChunkSize, _height); y++)
            for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
            {
                int index = (y * _width) + x;
                if (!_drawn[index] || _geometry[index] is not TerrainTileGeometry geometry) continue;
                sink.AddTile(index, geometry);
                shows |= geometry.VertexCount > 0;
            }
        sink.EndChunk(chunk);
        if (shows != _shown[chunk]) _shownCount += shows ? 1 : -1;
        _shown[chunk] = shows;
    }
}
