// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using DK2.Core;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    public sealed class ClassicTileLightBuffer : IDisposable
    {
        public const string Keyword = "DK2_TILE_LIGHT_TABLES";
        [StructLayout(LayoutKind.Sequential)]
        private struct Entry
        {
            public Vector4 PositionRadiusSquared;
            public Vector3 Colour;
            public uint IntensityFlags;
        }

        private static readonly int SourcesId = Shader.PropertyToID("_DK2TileLightSources");
        private static readonly int CountsId = Shader.PropertyToID("_DK2TileLightCounts");
        private static readonly int WidthId = Shader.PropertyToID("_DK2TileLightGridWidth");
        private static readonly int GridCountId = Shader.PropertyToID("_DK2TileLightGridCount");
        private readonly int _width, _height, _tileCount, _entryCount;
        private readonly Entry[] _entries;
        private readonly uint[] _counts;
        private readonly GraphicsBuffer _sources, _tableCounts;
        private int _firstDirty, _lastDirty = -1;
        private bool _disposed;

        // One owning scene publishes authoritative tables, including empty tiles; see ADR-0484.
        public ClassicTileLightBuffer(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            _width = width; _height = height;
            _tileCount = checked(width * height);
            _entryCount = checked(_tileCount * ClassicLightTable.MaximumSources);
            _entries = ArrayPool<Entry>.Shared.Rent(_entryCount);
            _counts = ArrayPool<uint>.Shared.Rent(_tileCount);
            try
            {
                Array.Clear(_entries, 0, _entryCount);
                Array.Clear(_counts, 0, _tileCount);
                _sources = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _entryCount, Marshal.SizeOf<Entry>());
                _tableCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _tileCount, sizeof(uint));
                _sources.SetData(_entries, 0, 0, _entryCount);
                _tableCounts.SetData(_counts, 0, 0, _tileCount);
                Publish();
            }
            catch
            {
                _sources?.Dispose(); _tableCounts?.Dispose();
                ArrayPool<Entry>.Shared.Return(_entries);
                ArrayPool<uint>.Shared.Return(_counts);
                throw;
            }
        }

        public void Set(ClassicLightTable table)
        {
            ThrowIfDisposed();
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (table.Tile.X < 0 || table.Tile.X >= _width || table.Tile.Y < 0 || table.Tile.Y >= _height)
                throw new ArgumentOutOfRangeException(nameof(table), "Light table tile is outside this buffer's grid.");
            int tile = table.Tile.Y * _width + table.Tile.X;
            int offset = tile * ClassicLightTable.MaximumSources;
            bool changed = _counts[tile] != (uint)table.Count;
            ReadOnlySpan<ClassicPointLight> lights = table.Lights;
            for (int index = 0; index < ClassicLightTable.MaximumSources; index++)
            {
                Entry entry = default;
                if (index < lights.Length)
                {
                    ClassicPointLight light = lights[index];
                    entry.PositionRadiusSquared = new Vector4(light.Position.X, light.Position.Y, light.Position.Z,
                        light.Radius * light.Radius);
                    entry.Colour = new Vector3(light.Colour.X, light.Colour.Y, light.Colour.Z);
                    entry.IntensityFlags = (uint)Math.Round(light.Intensity * 128f) | ((uint)light.Flags << 8);
                }
                changed |= !entry.PositionRadiusSquared.Equals(_entries[offset + index].PositionRadiusSquared)
                    || !entry.Colour.Equals(_entries[offset + index].Colour)
                    || entry.IntensityFlags != _entries[offset + index].IntensityFlags;
                _entries[offset + index] = entry;
            }
            _counts[tile] = (uint)table.Count;
            if (!changed) return;
            _firstDirty = _lastDirty < 0 ? tile : Math.Min(_firstDirty, tile);
            _lastDirty = Math.Max(_lastDirty, tile);
        }

        public void Flush()
        {
            ThrowIfDisposed();
            if (_lastDirty >= 0)
            {
                int count = _lastDirty - _firstDirty + 1;
                int sourceOffset = _firstDirty * ClassicLightTable.MaximumSources;
                _sources.SetData(_entries, sourceOffset, sourceOffset, count * ClassicLightTable.MaximumSources);
                _tableCounts.SetData(_counts, _firstDirty, _firstDirty, count);
                _lastDirty = -1;
            }
            Publish();
        }

        public void Remove(TilePos position)
        {
            ThrowIfDisposed();
            if (position.X < 0 || position.X >= _width || position.Y < 0 || position.Y >= _height)
                throw new ArgumentOutOfRangeException(nameof(position));
            int tile = position.Y * _width + position.X;
            if (_counts[tile] == 0) return;
            _counts[tile] = 0;
            Array.Clear(_entries, tile * ClassicLightTable.MaximumSources, ClassicLightTable.MaximumSources);
            _firstDirty = _lastDirty < 0 ? tile : Math.Min(_firstDirty, tile);
            _lastDirty = Math.Max(_lastDirty, tile);
        }

        public void Clear()
        {
            ThrowIfDisposed();
            Array.Clear(_entries, 0, _entryCount);
            Array.Clear(_counts, 0, _tileCount);
            _firstDirty = 0; _lastDirty = _tileCount - 1;
            Flush();
        }

        private void Publish()
        {
            Shader.SetGlobalBuffer(SourcesId, _sources);
            Shader.SetGlobalBuffer(CountsId, _tableCounts);
            Shader.SetGlobalInt(WidthId, _width);
            Shader.SetGlobalInt(GridCountId, _tileCount);
            Shader.EnableKeyword(Keyword);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ClassicTileLightBuffer));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Shader.SetGlobalInt(GridCountId, 0);
            Shader.SetGlobalInt(WidthId, 0);
            Shader.DisableKeyword(Keyword);
            _sources.Dispose(); _tableCounts.Dispose();
            ArrayPool<Entry>.Shared.Return(_entries);
            ArrayPool<uint>.Shared.Return(_counts);
        }
    }
}
