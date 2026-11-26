// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>The Unity adapter's chunk scheduling as it stood at 0906dff6 (TerrainChunks.TrySet, Flush, Clear,
/// DirtyNeighbours, MarkStale, ChunkOf, AnyStale, PrepareStale, StartBuild, BuildInline, JoinBuild and Rebuild's tile
/// walk), transcribed before it moved to <see cref="TerrainChunkScheduler"/>. Mathf is Math, the flush stopwatch is
/// <see cref="Now"/>, the chunk objects are the set of chunks whose last upload had vertices, and an upload is
/// recorded in <see cref="Uploads"/> instead of building a mesh.</summary>
internal sealed class LegacyTerrainChunkSchedule
{
    private const int ChunkSize = 16;

    private readonly TerrainLattice? _lattice;
    private readonly TerrainTileBuilder _builder;
    private readonly int _width;
    private readonly int _chunksX;
    private readonly TileView[] _tiles;
    private readonly bool[] _drawn;
    private readonly HashSet<int> _dirty = new();
    private readonly List<int> _dirtyOrder = new();
    private readonly HashSet<int> _chunks = new();
    private readonly TerrainTileGeometry?[] _geometry;
    private readonly bool[] _stale;
    private readonly List<int> _building = new();
    private Task? _buildTask;
    private double _restartedAt;

    public LegacyTerrainChunkSchedule(ITerrainPieceSource pieces, int width, int height, TerrainLattice? lattice, Func<double> now)
    {
        _lattice = lattice;
        _builder = new TerrainTileBuilder(pieces, width, height, lattice);
        _width = width;
        _chunksX = (width + ChunkSize - 1) / ChunkSize;
        _tiles = new TileView[width * height];
        _drawn = new bool[width * height];
        _geometry = new TerrainTileGeometry?[width * height];
        _stale = new bool[width * height];
        Now = now;
    }

    public Func<double> Now { get; }

    /// <summary>Called after each recorded upload (a test's clock advances here).</summary>
    public Action? Uploaded { get; set; }

    public double FlushBudgetMilliseconds { get; set; } = 4.0;

    public int PendingChunks => _dirty.Count;

    public List<SchedulerLog.Upload> Uploads { get; } = new();

    public bool TrySet(int tileIndex, in TileView tile)
    {
        JoinBuild();
        bool draws = TerrainTileBuilder.Draws(tile);
        if (_lattice != null)
        {
            int x = tileIndex % _width, y = tileIndex / _width, height = _tiles.Length / _width;
            _lattice.Invalidate(x, y);
            for (int ty = Math.Max(0, y - 2); ty <= Math.Min(height - 1, y + 2); ty++)
            {
                for (int tx = Math.Max(0, x - 2); tx <= Math.Min(_width - 1, x + 2); tx++)
                {
                    MarkStale((ty * _width) + tx);
                }
            }
        }
        else if (draws || _drawn[tileIndex])
        {
            MarkStale(tileIndex);
        }

        if (tile.Tagged != _tiles[tileIndex].Tagged || tile.AmbientTint != _tiles[tileIndex].AmbientTint)
        {
            DirtyNeighbours(tileIndex);
        }

        _tiles[tileIndex] = tile;
        _drawn[tileIndex] = draws;
        return draws;
    }

    public void Flush()
    {
        if (_dirty.Count == 0)
        {
            return;
        }

        _restartedAt = Now();
        bool budgeted = FlushBudgetMilliseconds > 0 && _chunks.Count > 0;
        Task? build = _buildTask;
        if (build != null)
        {
            if (budgeted && !build.IsCompleted)
            {
                _ = Now() - _restartedAt;
                return;
            }
            JoinBuild();
        }

        _dirtyOrder.Clear();
        _dirtyOrder.AddRange(_dirty);
        _dirtyOrder.Sort();
        if (budgeted)
        {
            StartBuild();
        }
        else
        {
            BuildInline();
        }

        foreach (int chunk in _dirtyOrder)
        {
            if (budgeted && _dirty.Count < _dirtyOrder.Count && Now() - _restartedAt >= FlushBudgetMilliseconds)
            {
                break;
            }

            if (AnyStale(chunk))
            {
                continue;
            }

            Rebuild(chunk);
            _dirty.Remove(chunk);
        }
        _ = Now() - _restartedAt;
    }

    public void Clear()
    {
        JoinBuild();
        _chunks.Clear();
        _dirty.Clear();
        Array.Clear(_drawn, 0, _drawn.Length);
        Array.Clear(_geometry, 0, _geometry.Length);
        Array.Clear(_stale, 0, _stale.Length);
    }

    public void WaitForBuild() => _buildTask?.Wait();

    private void DirtyNeighbours(int tileIndex)
    {
        int x = tileIndex % _width, y = tileIndex / _width, height = _tiles.Length / _width;
        if (x > 0)
        {
            MarkStale(tileIndex - 1);
        }

        if (x < _width - 1)
        {
            MarkStale(tileIndex + 1);
        }

        if (y > 0)
        {
            MarkStale(tileIndex - _width);
        }

        if (y < height - 1)
        {
            MarkStale(tileIndex + _width);
        }
    }

    private void MarkStale(int tileIndex)
    {
        _stale[tileIndex] = true;
        _dirty.Add(ChunkOf(tileIndex));
    }

    private int ChunkOf(int tileIndex) =>
        (tileIndex / _width / ChunkSize * _chunksX) + (tileIndex % _width / ChunkSize);

    private bool AnyStale(int chunk)
    {
        int height = _tiles.Length / _width;
        int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
        for (int y = y0; y < Math.Min(y0 + ChunkSize, height); y++)
        {
            for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
            {
                if (_stale[(y * _width) + x])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void PrepareStale()
    {
        int height = _tiles.Length / _width;
        foreach (int chunk in _dirtyOrder)
        {
            int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
            for (int y = y0; y < Math.Min(y0 + ChunkSize, height); y++)
            {
                for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
                {
                    int index = (y * _width) + x;
                    if (!_stale[index])
                    {
                        continue;
                    }

                    if (!_drawn[index])
                    {
                        _stale[index] = false;
                        continue;
                    }
                    _builder.Prepare(x, y, _tiles, _geometry[index] ??= new TerrainTileGeometry());
                    _building.Add(index);
                }
            }
        }
    }

    private void StartBuild()
    {
        PrepareStale();
        if (_building.Count == 0)
        {
            return;
        }

        _buildTask = Task.Run(() =>
        {
            foreach (int index in _building)
            {
                _builder.Build(_geometry[index]!);
            }
        });
    }

    private void BuildInline()
    {
        PrepareStale();
        foreach (int index in _building)
        {
            _builder.Build(_geometry[index]!);
        }

        foreach (int index in _building)
        {
            _stale[index] = false;
        }

        _building.Clear();
    }

    private void JoinBuild()
    {
        Task? build = _buildTask;
        if (build == null)
        {
            return;
        }

        _buildTask = null;
        try
        {
            build.GetAwaiter().GetResult();
        }
        finally
        {
            foreach (int index in _building)
            {
                _stale[index] = false;
            }

            _building.Clear();
        }
    }

    private void Rebuild(int chunk)
    {
        var upload = new SchedulerLog.Upload(chunk);
        int height = _tiles.Length / _width;
        int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
        for (int y = y0; y < Math.Min(y0 + ChunkSize, height); y++)
        {
            for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
            {
                int index = (y * _width) + x;
                if (_drawn[index] && _geometry[index] is TerrainTileGeometry geometry)
                {
                    upload.Add(index, geometry);
                }
            }
        }

        Uploads.Add(upload);
        if (upload.Vertices == 0)
        {
            _chunks.Remove(chunk);
        }
        else
        {
            _chunks.Add(chunk);
        }

        Uploaded?.Invoke();
    }
}

/// <summary>What a scheduler uploaded, chunk by chunk, with each tile's vertices and triangles as numbers.</summary>
internal sealed class SchedulerLog : ITerrainChunkSink
{
    private Upload? _open;

    public List<Upload> Uploads { get; } = new();

    public Action? Uploaded { get; set; }

    public void BeginChunk(int chunk) => _open = new Upload(chunk);

    public void AddTile(int tileIndex, TerrainTileGeometry geometry) => _open!.Add(tileIndex, geometry);

    public void EndChunk(int chunk)
    {
        Assert.Equal(_open!.Chunk, chunk);
        Uploads.Add(_open);
        _open = null;
        Uploaded?.Invoke();
    }

    internal sealed class Upload(int chunk)
    {
        public int Chunk { get; } = chunk;
        public List<int> Tiles { get; } = new();
        public List<float> Numbers { get; } = new();
        public int Vertices { get; private set; }

        public void Add(int tileIndex, TerrainTileGeometry geometry)
        {
            Tiles.Add(tileIndex);
            Vertices += geometry.VertexCount;
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                Numbers.Add(geometry.Positions[i].X); Numbers.Add(geometry.Positions[i].Y); Numbers.Add(geometry.Positions[i].Z);
                Numbers.Add(geometry.Normals[i].X); Numbers.Add(geometry.Uvs[i].Y);
            }
            for (int s = 0; s < geometry.KeyCount; s++)
            {
                Numbers.Add(geometry.Keys[s].GetHashCode());
                foreach (int index in geometry.Triangles[s].AsSpan())
                {
                    Numbers.Add(index);
                }
            }
        }

        public override string ToString() => $"chunk {Chunk}: {Tiles.Count} tiles, {Vertices} vertices";
    }
}
