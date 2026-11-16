// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The adapter's per-tile light table store, which its terrain shader reads.</summary>
public interface ITileLightSink
{
    void Set(ClassicLightTable table);
    void Remove(TilePos tile);
    void Flush();
}

/// <summary>What a frame's <see cref="ClassicLightPublisher.Prepare"/> found.</summary>
public enum ClassicLightPublication
{
    /// <summary>The viewport is empty: nothing was prepared and nothing is flushed.</summary>
    Skipped,
    /// <summary>The visible tables are the ones already published: flush the sink if it exists.</summary>
    Unchanged,
    /// <summary>The tables changed: publish them to the sink (<see cref="ClassicLightPublisher.Publish"/>).</summary>
    Changed,
}

/// <summary>A scene's classic light frame over its <see cref="ClassicWorldLightCache"/>: the visible tiles of the camera
/// (and of a second camera, the full map's preview), the cache's frame, and the tables published to the adapter's
/// <see cref="ITileLightSink"/> as a diff against what it already holds. The adapter turns its cameras into poses.
/// Reads no world state, only what the scene pushed into the cache (ADR-0458).</summary>
public sealed class ClassicLightPublisher
{
    /// <summary>The native view distance the second camera's tiles are found with.</summary>
    public const float SecondCameraViewDistance = 10;

    private readonly ClassicWorldLightCache _cache;
    private readonly PortalCentreLights _portals;
    private readonly int _width, _height;
    private int _portalRequestsPublished;
    private bool _tablesNeedPublication;
    private TilePos[] _visibleTiles = Array.Empty<TilePos>();
    private int _visibleCount;
    private readonly HashSet<TilePos> _published = new();
    private ClassicCameraPose? _pose;
    private CameraViewport _viewport;
    private float _viewDistance;
    private ClassicCameraPose? _secondPose;
    private CameraViewport _secondViewport;
    private TilePos[] _secondTiles = Array.Empty<TilePos>();
    private readonly HashSet<TilePos> _joined = new();
    private TilePos[] _joinedBuffer = Array.Empty<TilePos>();

    /// <param name="torchFlags">The scene's torch flags (<see cref="ClassicLightFlagsRules.Torch"/>), whose cheap test the
    /// entrance lights take.</param>
    public ClassicLightPublisher(ClassicWorldLightCache cache, int width, int height, PortalCentreLights portals,
        ClassicLightFlags torchFlags)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _portals = portals ?? throw new ArgumentNullException(nameof(portals));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _width = width;
        _height = height;
        TorchFlags = torchFlags;
    }

    public ClassicLightFlags TorchFlags { get; }

    /// <summary>Creates the entrance lights for the requests not seen before (the list only grows), then advances them
    /// all to <paramref name="now"/> and hands them to the cache.</summary>
    public void PublishPortalLights(IReadOnlyList<TilePos> requests, EffectTime now)
    {
        while (_portalRequestsPublished < requests.Count)
            _portals.Create(requests[_portalRequestsPublished++]);
        _cache.UpdatePortalLights(_portals.Advance(now, TorchFlags.IsCheapTorch()));
    }

    /// <summary>A table was written to the sink outside the frame, so the next frame publishes even when the cache's
    /// tables are unchanged.</summary>
    public void MarkTablesWritten() => _tablesNeedPublication = true;

    /// <summary>Prepares the cache's frame for the tiles <paramref name="pose"/> sees, joined with those
    /// <paramref name="second"/> sees at <see cref="SecondCameraViewDistance"/>. Each camera's tiles are found again only
    /// when its pose, viewport or (the first camera's) distance changes.</summary>
    public ClassicLightPublication Prepare(in ClassicCameraPose pose, CameraViewport viewport, float viewDistance,
        in ClassicCameraPose? second = null, CameraViewport secondViewport = default)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0) return ClassicLightPublication.Skipped;
        if (!_pose.HasValue || !_pose.Value.Equals(pose) || !_viewport.Equals(viewport) || _viewDistance != viewDistance)
        {
            TilePos[] polygon = ClassicLightVisibility.BuildPolygon(pose, viewport, viewDistance);
            _visibleCount = ClassicLightVisibility.EnumerateTiles(polygon, ref _visibleTiles);
            _pose = pose;
            _viewport = viewport;
            _viewDistance = viewDistance;
        }
        var visible = new ReadOnlySpan<TilePos>(_visibleTiles, 0, _visibleCount);
        if (second is ClassicCameraPose secondPose)
        {
            if (!_secondPose.HasValue || !_secondPose.Value.Equals(secondPose) || !secondViewport.Equals(_secondViewport))
            {
                _secondTiles = ClassicLightVisibility.EnumerateTiles(
                    ClassicLightVisibility.BuildPolygon(secondPose, secondViewport, SecondCameraViewDistance));
                _secondPose = secondPose;
                _secondViewport = secondViewport;
            }
            // Joined once per frame into buffers kept across frames.
            _joined.Clear();
            for (int i = 0; i < _visibleCount; i++) _joined.Add(_visibleTiles[i]);
            foreach (TilePos tile in _secondTiles) _joined.Add(tile);
            if (_joinedBuffer.Length < _joined.Count) _joinedBuffer = new TilePos[_joined.Count * 2];
            _joined.CopyTo(_joinedBuffer);
            visible = new ReadOnlySpan<TilePos>(_joinedBuffer, 0, _joined.Count);
        }
        _cache.PrepareFrame(visible);
        return _cache.TablesUnchanged && !_tablesNeedPublication
            ? ClassicLightPublication.Unchanged : ClassicLightPublication.Changed;
    }

    /// <summary>Publishes a <see cref="ClassicLightPublication.Changed"/> frame: removes the tiles published before and
    /// no longer visible, sets every visible on-map tile's table, then flushes.</summary>
    public void Publish(ITileLightSink sink)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));
        foreach (TilePos previous in _published)
            if (!_cache.IsVisible(previous)) sink.Remove(previous);
        _published.Clear();
        foreach (ClassicLightTable table in _cache.VisibleTables)
        {
            if ((uint)table.Tile.X >= (uint)_width || (uint)table.Tile.Y >= (uint)_height) continue;
            sink.Set(table);
            _published.Add(table.Tile);
        }
        sink.Flush();
        _tablesNeedPublication = false;
    }

    /// <summary>Forgets the cache's sources, the published tiles and the first camera's tiles, as a scene clear does.
    /// The entrance request count, the second camera and a pending publication are kept.</summary>
    public void Clear()
    {
        _cache.Clear();
        _published.Clear();
        _visibleTiles = Array.Empty<TilePos>();
        _visibleCount = 0;
        _pose = null;
    }
}
